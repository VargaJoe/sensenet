﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SenseNet.ApplicationModel;
using SenseNet.ContentRepository.Fields;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Security;
using SenseNet.Diagnostics;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Schema;
using SenseNet.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SenseNet.ContentRepository;
using SenseNet.OData.Writers;
using SenseNet.Security;
using SenseNet.Services.Core.Configuration;
using SenseNet.Services.Core.Diagnostics;
using File = SenseNet.ContentRepository.File;
using Task = System.Threading.Tasks.Task;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Retrier = SenseNet.Tools.Retrier;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable UnusedMember.Global
// ReSharper disable CommentTypo
// ReSharper disable ArrangeThisQualifier

namespace SenseNet.OData
{
    /// <summary>
    /// AN ASP.NET Core middleware to process the OData requests.
    /// </summary>
    public class ODataMiddleware
    {
        public class ContentCreationResult
        {
            public Content Content { get; set; }
            public List<string> BrokenReferenceFieldNames { get; set; } = new();
        }

        public static readonly string ODataRequestHttpContextKey = "SenseNet.OData.ODataRequest";

        private static readonly IActionResolver DefaultActionResolver = new DefaultActionResolver();
        internal static IActionResolver ActionResolver => Providers.Instance.GetProvider<IActionResolver>() ?? DefaultActionResolver;

        internal static readonly string[] HeadFieldNames = new[] { "Id", "Name", "DisplayName", "Icon", "CreationDate", "ModificationDate", "CreatedBy", "ModifiedBy" };
        internal static readonly List<string> DisabledFieldNames = new List<string>(new[] { "TypeIs", "InTree", "InFolder", "NodeType", "IsRateable", "RateStr", "RateAvg", "RateCount", "Rate"/*, "VersioningMode", "ApprovingMode"*/ });
        internal static readonly List<string> DeferredFieldNames = new List<string>(new[] { "AllowedChildTypes", "EffectiveAllowedChildTypes" });
        internal static readonly List<string> AllowedMethodNamesWithoutContent = new List<string>(new[] { "PATCH", "PUT", "POST", "DELETE" });

        internal static List<JsonConverter> JsonConverters { get; }
        internal static List<FieldConverter> FieldConverters { get; }

        static ODataMiddleware()
        {
            JsonConverters = new List<JsonConverter> {new Newtonsoft.Json.Converters.VersionConverter()};

            FieldConverters = new List<FieldConverter>();
            var fieldConverterTypes = TypeResolver.GetTypesByBaseType(typeof(FieldConverter));
            foreach (var fieldConverterType in fieldConverterTypes)
            {
                var fieldConverter = (FieldConverter)Activator.CreateInstance(fieldConverterType);
                JsonConverters.Add(fieldConverter);
                FieldConverters.Add(fieldConverter);
            }

            //UNDONE:<?:do not call discovery and providers setting in the static ctor of ODataMiddleware
            OperationCenter.Discover();
        }

        internal static readonly DateTime BaseDate = new DateTime(1970, 1, 1);
        internal const string ModelRequestKeyName = "models";
        internal const string ActionsPropertyName = "Actions";
        internal const string ChildrenPropertyName = "Children";
        internal const string BinaryPropertyName = "Binary";
        internal const int ExpansionLimit = int.MaxValue - 1;

        private readonly RequestDelegate _next;
        private readonly IConfiguration _appConfig;
        private readonly ILogger<ODataMiddleware> _logger;
        private readonly SenseNet.Services.Core.Configuration.HttpRequestOptions _requestOptions;

        // Must have constructor with this signature, otherwise exception at run time
        public ODataMiddleware(RequestDelegate next, IConfiguration config, 
            IOptions<SenseNet.Services.Core.Configuration.HttpRequestOptions> requestOptions,
            ILogger<ODataMiddleware> logger)
        {
            _next = next;
            _appConfig = config;
            _logger = logger;
            _requestOptions = requestOptions?.Value ?? new SenseNet.Services.Core.Configuration.HttpRequestOptions();
        }

        private bool _delayLogged;
        public async Task InvokeAsync(HttpContext httpContext, WebTransferRegistrator statistics)
        {
            while (!Providers.Instance.RepositoryStatus.IsRunning)
            {
                if (!_delayLogged)
                {
                    _delayLogged = true;
                    var msg = "One or more requests delayed because the repository is starting. The RepositoryStatus is " +
                              Providers.Instance.RepositoryStatus.Current;
                    SnTrace.Web.Write(msg);
                    _logger.LogDebug(msg);
                }
                await Task.Delay(50).ConfigureAwait(false);
            }

            var req = httpContext.Request;
            using (var op = SnTrace.Web.StartOperation($"{req.Method} {req.GetDisplayUrl()}"))
            {
                var statData = statistics?.RegisterWebRequest(httpContext);

                // set request size limit if configured
                if (_requestOptions?.MaxRequestBodySize > 0)
                    httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize = 
                        _requestOptions.MaxRequestBodySize;

                // Create OData-response strategy
                var odataRequest = ODataRequest.Parse(httpContext);

                // Write headers and body of the HttpResponse
                try
                {
                    await ProcessRequestAsync(httpContext, odataRequest).ConfigureAwait(false);
                    statistics?.RegisterWebResponse(statData, httpContext, odataRequest.ResponseSize);
                    
                    op.Successful = true;
                }
                catch (OperationCanceledException)
                {
                    // Log the cancellation. There is no point in returning anything to the client,
                    // because the client is not listening anymore.
                    _logger.LogInformation("The operation was canceled. Request: {Request}", 
                        httpContext.Request.GetDisplayUrl());
                }
            }

            // Call next in the chain if exists
            if (_next != null)
                await _next(httpContext).ConfigureAwait(false);
        }

        [SuppressMessage("ReSharper", "PossibleNullReferenceException")]
        internal async Task ProcessRequestAsync(HttpContext httpContext, ODataRequest odataRequest)
        {
            httpContext.SetODataRequest(odataRequest);

            var request = httpContext.Request;
            var httpMethod = request.Method;
            var inputStream = request.Body;
            ODataWriter odataWriter = null;
            try
            {
                Content content;
                if (odataRequest == null)
                {
                    odataWriter = new ODataJsonWriter();
                    throw new ODataException("The Request is not an OData request.", ODataExceptionCode.RequestError);
                }

                odataWriter = ODataWriter.Create(httpContext, odataRequest);
                if (odataWriter == null)
                {
                    odataWriter = new ODataJsonWriter();
                    odataWriter.Initialize(odataRequest);
                    throw new ODataException(ODataExceptionCode.InvalidFormatParameter);
                }

                odataWriter.Initialize(odataRequest);

                var requestError = odataRequest.RequestError;
                if (requestError != null)
                {
                    var innerOdataError = requestError as ODataException;
                    var message = "An error occured during request parsing. " + requestError.Message +
                                  " See inner exception for details.";
                    var code = innerOdataError?.ODataExceptionCode ?? ODataExceptionCode.RequestError;
                    throw new ODataException(message, code, requestError);
                }

                odataRequest.Format = odataWriter.FormatName;

                var requestedContent = LoadContentByVersionRequest(odataRequest.RepositoryPath, httpContext);

                var exists = requestedContent != null;
                if (!exists && !odataRequest.IsServiceDocumentRequest && !odataRequest.IsMetadataRequest &&
                    !AllowedMethodNamesWithoutContent.Contains(httpMethod))
                {
                    ContentNotFound(httpContext);
                    return;
                }

                JObject model;
                switch (httpMethod)
                {
                    case "GET":
                        if (odataRequest.IsServiceDocumentRequest)
                        {
                            await odataWriter.WriteServiceDocumentAsync(httpContext, odataRequest)
                                .ConfigureAwait(false);
                        }
                        else if (odataRequest.IsMetadataRequest)
                        {
                            await odataWriter.WriteMetadataAsync(httpContext, odataRequest)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            if (!Node.Exists(odataRequest.RepositoryPath))
                                ContentNotFound(httpContext);
                            else if (odataRequest.IsCollection)
                                await odataWriter.WriteChildrenCollectionAsync(odataRequest.RepositoryPath, httpContext,
                                        odataRequest)
                                    .ConfigureAwait(false);
                            else if (odataRequest.IsControllerRequest)
                                await odataWriter.WriteContentPropertyAsync(
                                        odataRequest.RepositoryPath, odataRequest.PropertyName,
                                        odataRequest.IsRawValueRequest, httpContext, odataRequest, _appConfig)
                                    .ConfigureAwait(false);
                            else if (odataRequest.IsMemberRequest)
                                await odataWriter.WriteContentPropertyAsync(
                                        odataRequest.RepositoryPath, odataRequest.PropertyName,
                                        odataRequest.IsRawValueRequest, httpContext, odataRequest, _appConfig)
                                    .ConfigureAwait(false);
                            else
                                await odataWriter.WriteSingleContentAsync(requestedContent, httpContext, odataRequest)
                                    .ConfigureAwait(false);
                        }

                        break;
                    case "PUT": // update
                        if (odataRequest.IsControllerRequest)
                        {
                            throw new ODataException("Cannot access a controller with HTTP PUT.",
                                ODataExceptionCode.IllegalInvoke);
                        }
                        else if (odataRequest.IsMemberRequest)
                        {
                            throw new ODataException("Cannot access a member with HTTP PUT.",
                                ODataExceptionCode.IllegalInvoke);
                        }
                        else
                        {
                            model = await ReadToJsonAsync(httpContext).ConfigureAwait(false);
                            content = LoadContentOrVirtualChild(odataRequest);
                            if (content == null)
                            {
                                ContentNotFound(httpContext);
                                return;
                            }

                            ResetContent(content);
                            await UpdateContentAsync(content, model, odataRequest, httpContext.RequestAborted).ConfigureAwait(false);
                            await odataWriter.WriteSingleContentAsync(content, httpContext, odataRequest)
                                .ConfigureAwait(false);
                        }

                        break;
                    case "MERGE":
                    case "PATCH": // update
                        if (odataRequest.IsControllerRequest)
                        {
                            throw new ODataException(
                                String.Concat("Cannot access a controller with HTTP ", httpMethod, "."),
                                ODataExceptionCode.IllegalInvoke);
                        }
                        else if (odataRequest.IsMemberRequest)
                        {
                            throw new ODataException(
                                String.Concat("Cannot access a member with HTTP ", httpMethod, "."),
                                ODataExceptionCode.IllegalInvoke);
                        }
                        else
                        {
                            model = await ReadToJsonAsync(httpContext).ConfigureAwait(false);
                            content = LoadContentOrVirtualChild(odataRequest);
                            if (content == null)
                            {
                                ContentNotFound(httpContext);
                                return;
                            }

                            await UpdateContentAsync(content, model, odataRequest, httpContext.RequestAborted).ConfigureAwait(false);
                            await odataWriter.WriteSingleContentAsync(content, httpContext, odataRequest)
                                .ConfigureAwait(false);
                        }

                        break;
                    case "POST": // invoke an action, create content
                        if (odataRequest.IsControllerRequest)
                        {
                            // CONTROLLER REQUEST
                            await odataWriter.WritePostOperationResultAsync(httpContext, odataRequest, _appConfig)
                                .ConfigureAwait(false);
                        }
                        else if (odataRequest.IsMemberRequest)
                        {
                            // MEMBER REQUEST
                            await odataWriter.WritePostOperationResultAsync(httpContext, odataRequest, _appConfig)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            // CREATION
                            if (!Node.Exists(odataRequest.RepositoryPath))
                            {
                                // parent does not exist
                                ContentNotFound(httpContext);
                                return;
                            }

                            model = await ReadToJsonAsync(httpContext).ConfigureAwait(false);
                            var newContent = await CreateNewContentAsync(model, odataRequest, httpContext.RequestAborted).ConfigureAwait(false);
                            await odataWriter.WriteSingleContentAsync(newContent, httpContext, odataRequest)
                                .ConfigureAwait(false);
                        }

                        break;
                    case "DELETE":
                        if (odataRequest.IsControllerRequest)
                        {
                            throw new ODataException(
                                String.Concat("Cannot access a controller with HTTP ", httpMethod, "."),
                                ODataExceptionCode.IllegalInvoke);
                        }
                        else if (odataRequest.IsMemberRequest)
                        {
                            throw new ODataException(
                                String.Concat("Cannot access a member with HTTP ", httpMethod, "."),
                                ODataExceptionCode.IllegalInvoke);
                        }
                        else
                        {
                            content = LoadContentOrVirtualChild(odataRequest);
                            if (content != null)
                            {
                                var x = httpContext.Request.Query["permanent"].ToString();
                                if (x.Equals("true", StringComparison.OrdinalIgnoreCase))
                                    await content.ForceDeleteAsync(httpContext.RequestAborted);
                                else
                                    await content.DeleteAsync(httpContext.RequestAborted);
                            }
                        }

                        break;
                }
            }
            catch (Exception ex)
            {
                var oe = HandleException(ex, odataRequest, httpContext);
                if (oe == null)
                    return;
                await odataWriter.WriteErrorResponseAsync(httpContext, odataRequest, oe, _appConfig)
                    .ConfigureAwait(false);
            }
        }

        private ODataException HandleException(Exception e, ODataRequest odataRequest, HttpContext httpContext)
        {
            switch (e)
            {
                case TargetInvocationException targetInvocationException:
                    return targetInvocationException.InnerException == null
                        ? new ODataException(ODataExceptionCode.NotSpecified, targetInvocationException)
                        : HandleException(targetInvocationException.InnerException, odataRequest, httpContext);
                case ODataException oDataException:
                {
                    if (oDataException.HttpStatusCode == 500)
                        SnLog.WriteException(e);
                    return oDataException;
                }
                case ContentNotFoundException _:
                    return new ODataException(ODataExceptionCode.ResourceNotFound, e);
                case AccessDeniedException ade:
                {
                    SnTrace.Security.WriteError(ade.ToString);
                    return new ODataException("Access denied.", ODataExceptionCode.Forbidden, ade);
                }
                case UnauthorizedAccessException _:
                    return new ODataException(ODataExceptionCode.Unauthorized, e);
                case ContentRepository.Storage.Data.NodeAlreadyExistsException nodeAlreadyExistsException:
                    if (e.Message.StartsWith("Cannot copy the content") || e.Message.StartsWith("Cannot move the content"))
                        return new ODataException(e.Message, ODataExceptionCode.ContentAlreadyExists, e);
                    return new ODataException("The content was not saved because it already exists.",
                        ODataExceptionCode.ContentAlreadyExists, e);
                case SenseNetSecurityException sse:
                {
                    // If the current user (visitor or the logged-in user) has not See permission on the requested content,
                    // return 404 (content not found) instead of any security related error.
                    if (odataRequest != null)
                    {
                        var head = NodeHead.Get(odataRequest.RepositoryPath);
                        if (head != null && !Providers.Instance.SecurityHandler.HasPermission(head, PermissionType.See))
                        {
                            ContentNotFound(httpContext);
                            SnTrace.Security.Write(sse.Data["FormattedMessage"].ToString());
                            return null;
                        }
                    }

                    var oe = new ODataException(ODataExceptionCode.Forbidden, e);
                    SnTrace.Security.WriteError(e.Message);
                    return oe;
                }
                case InvalidContentActionException invalidContentActionException:
                {
                    var oe = new ODataException(ODataExceptionCode.NotSpecified, e);
                    if (invalidContentActionException.Reason != InvalidContentActionReason.NotSpecified)
                        oe.ErrorCode = Enum.GetName(typeof(InvalidContentActionReason), invalidContentActionException.Reason);

                    // it is unnecessary to log this exception as this is not a real error
                    return oe;
                }
                case ApplicationException:
                {
					// it is unnecessary to log this exception as this is not a real error
					return new ODataException(ODataExceptionCode.RequestError, e);
                }
                case OperationCanceledException operationCanceledException:
                {
                    return new ODataException(
                        $"The operation was canceled. Request url: {httpContext?.Request?.GetDisplayUrl()}",
                        ODataExceptionCode.NotSpecified, operationCanceledException);
                }
            }

            // General error handling
            var generalError = new ODataException(ODataExceptionCode.NotSpecified, e);
            _logger.LogError(generalError, generalError.Message);
            return generalError;
        }

        /* =================================================================================== */

        internal static async Task<JObject> ReadToJsonAsync(HttpContext context)
        {
            var inputStream = context?.Request.Body;
            string models;
          
            // In case of a multipart Upload request we have to use the Form
            // property to load post fields automatically, including file fragments. 
            // In this case we cannot read the response manually, because 
            // the stream can be read only once.
            if ((context?.Request?.ContentType?.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
            {
                var dict = new Dictionary<string, string>();
                foreach (var formItem in context.Request.Form.Where(fi => !string.IsNullOrEmpty(fi.Key)))
                {
                    dict[formItem.Key] = formItem.Value.FirstOrDefault();
                }

                models = JsonConvert.SerializeObject(dict);

                return ReadToJson(models);
            }

            if (inputStream == null)
                return null;
            if (inputStream == Stream.Null)
                return null;
            using (var reader = new StreamReader(inputStream))
                models = await reader.ReadToEndAsync().ConfigureAwait(false);

            return ReadToJson(models);
        }
        /// <summary>
        /// Helper method for deserializing the given string representation.
        /// </summary>
        /// <param name="models">JSON object that will be deserialized.</param>
        /// <returns>Deserialized JObject instance.</returns>
        internal static JObject ReadToJson(string models)
        {
            if (string.IsNullOrEmpty(models))
                return null;

            static bool IsJson(string postData)
            {
                if (string.IsNullOrEmpty(postData))
                    return false;
                return postData.StartsWith("{") && postData.EndsWith("}") ||
                       postData.StartsWith("[{") && postData.EndsWith("}]");
            }
            
            // determine the starting and closing bracket type
            var firstChar = models.Last() == ']' ? '[' : '{';
            var p = models.IndexOf(firstChar);
            if (p > 0)
                models = models.Substring(p);

            // if the data is formatted as a forms-encoded collection, convert it to json
            if (!IsJson(models))
            {
                var json = new StringBuilder("{");
                var pairs = models.Split('&');
                foreach (var pair in pairs)
                {
                    var items = pair.Split('=');
                    if (items.Length != 2)
                    {
                        json.Clear();
                        break;
                    }
                    if (json.Length > 1)
                        json.Append(",");
                    json.Append($"\"{items[0]}\":\"{items[1]}\"");
                }

                if (json.Length > 0)
                {
                    json.Append("}");
                    models = json.ToString();
                }
            }

            var settings = new JsonSerializerSettings {DateFormatHandling = DateFormatHandling.IsoDateFormat};
            var serializer = JsonSerializer.Create(settings);
            var jReader = new JsonTextReader(new StringReader(models));
            var deserialized = serializer.Deserialize(jReader);

            if (deserialized is JObject jObject)
                return jObject;
            if (deserialized is JArray jArray)
                return jArray[0] as JObject;

            throw new SnNotSupportedException();
        }
        internal static object Read(Stream inputStream, Type type)
        {
            string models;
            using (var reader = new StreamReader(inputStream))
                models = reader.ReadToEnd(); // HttpUtility.UrlDecode(reader.ReadToEnd());

            if (string.IsNullOrEmpty(models))
                return null;

            var firstChar = models.Last() == ']' ? '[' : '{';
            var p = models.IndexOf(firstChar);
            if (p > 0)
                models = models.Substring(p);

            var settings = new JsonSerializerSettings { DateFormatHandling = DateFormatHandling.IsoDateFormat };
            var serializer = JsonSerializer.Create(settings);
            var jReader = new JsonTextReader(new StringReader(models));
            var deserialized = serializer.Deserialize(jReader, type);

            return deserialized;
        }

        internal static string GetEntityUrl(string path)
        {
            path = path.TrimEnd('/');

            var p = path.LastIndexOf('/');
            if (p < 0)
                return string.Concat("(", path, ")");

            return string.Concat(path.Substring(0, p), "('", path.Substring(p + 1), "')");
        }

        internal static void ContentNotFound(HttpContext httpContext)
        {
            httpContext.Response.Clear();
            httpContext.Response.StatusCode = 404;
        }
        internal static void ContentAlreadyExists(string path)
        {
            throw new ODataException(SNSR.GetString(SNSR.Exceptions.OData.ContentAlreadyExists_1, path), ODataExceptionCode.ContentAlreadyExists);
        }
        internal static void ResourceNotFound(Content content, string propertyName)
        {
            throw new ODataException(SNSR.GetString(SNSR.Exceptions.OData.ResourceNotFound_2, content.Path, propertyName), ODataExceptionCode.ResourceNotFound);
        }
        internal static void ResourceNotFound()
        {
            throw new ODataException(SNSR.GetString(SNSR.Exceptions.OData.ResourceNotFound), ODataExceptionCode.ResourceNotFound);
        }

        // ==============================================================================================================

        internal static Content LoadContentByVersionRequest(string path, HttpContext httpContext)
        {
            var versionRequest = httpContext.Request.Query["version"].ToString();
            return !string.IsNullOrEmpty(versionRequest) && VersionNumber.TryParse(versionRequest, out var version)
                ? Content.Load(path, version)
                : Content.Load(path);
        }

        private async Task<Content> CreateNewContentAsync(JObject model, ODataRequest odataRequest, CancellationToken cancel)
        {
            var parentPath = odataRequest.RepositoryPath;
            var contentTypeName = GetPropertyValue<string>("__ContentType", model);
            var templateName = GetPropertyValue<string>("__ContentTemplate", model);
            var contentName = GetPropertyValue<string>("Name", model);
            var displayName = GetPropertyValue<string>("DisplayName", model);
            var isMultiStepSave = odataRequest.MultistepSave;

            var creationResult = await CreateNewContentAsync(parentPath, contentTypeName, templateName, contentName, displayName, 
                isMultiStepSave, model, false, cancel).ConfigureAwait(false);

            return creationResult.Content;
        }
        public static async Task<ContentCreationResult> CreateNewContentAsync(string parentPath, string contentTypeName, string templateName,
            string contentName, string displayName, bool isMultiStepSave, JObject model,
            bool skipBrokenReferences, CancellationToken cancel, bool importing = false)
        {
            contentName = ContentNamingProvider.GetNameFromDisplayName(string.IsNullOrEmpty(contentName) ? displayName : contentName);

            var parent = await Retrier.RetryAsync(5, 2000,
                () => Node.LoadAsync<GenericContent>(parentPath, cancel),
                (node, _, _) => node != null).ConfigureAwait(false);

            if (parent == null)
                throw new InvalidOperationException($"Cannot create content {contentName}, parent not found: {parentPath}");

            if (string.IsNullOrEmpty(contentTypeName))
            {
                var allowedChildTypeNames = parent.GetAllowedChildTypeNames();

                if (allowedChildTypeNames is AllContentTypeNames)
                {
                    contentTypeName = nameof(File);
                }
                else
                {
                    var allowedContentTypeNames = parent.GetAllowedChildTypeNames().ToArray();
                    contentTypeName = allowedContentTypeNames.FirstOrDefault();
                    if (string.IsNullOrEmpty(contentTypeName))
                        contentTypeName = nameof(File);
                }
            }

            Content content;
            Node template = null;
            if (templateName != null)
                template = ContentTemplate.GetNamedTemplate(contentTypeName, templateName);

            if (template == null)
            {
                content = Content.CreateNew(contentTypeName, parent, contentName);
            }
            else
            {
                var node = ContentTemplate.CreateFromTemplate(parent, template, contentName);
                content = Content.Create(node);
            }

            content.Importing = importing;

            var brokenReferenceFieldNames = await UpdateFieldsAsync(content, model, skipBrokenReferences, cancel)
                .ConfigureAwait(false);

            if (isMultiStepSave)
                await content.SaveAsync(SavingMode.StartMultistepSave, cancel).ConfigureAwait(false);
            else
                await content.SaveAsync(cancel).ConfigureAwait(false);

            return new ContentCreationResult
            {
                Content = content,
                BrokenReferenceFieldNames = brokenReferenceFieldNames
            };
        }

        private static readonly List<string> SafeFieldsInReset = new List<string>(new[] {
            "Name",
            "CreatedBy", "CreatedById", "CreationDate",
            "ModifiedBy", "ModifiedById", "ModificationDate" });

        private static Content LoadContentOrVirtualChild(ODataRequest odataReq)
        {
            var content = Content.Load(odataReq.RepositoryPath);

            if (content == null)
            {
                // try to load a virtual content
                var parentPath = RepositoryPath.GetParentPath(odataReq.RepositoryPath);
                var name = RepositoryPath.GetFileName(odataReq.RepositoryPath);
                if (Node.LoadNode(parentPath) is ISupportsVirtualChildren vp)
                    content = vp.GetChild(name);
            }

            return content;
        }

        private void ResetContent(Content content)
        {
            // Create "dummy" content
            var newContent = SystemAccount.Execute(() => Content.CreateNew(content.ContentType.Name, content.ContentHandler.Parent, null));

            Aspect[] aspects = null;
            if (content.ContentHandler.HasProperty(GenericContent.ASPECTS))
            {
                // Get aspects
                aspects = content.ContentHandler.GetReferences(GenericContent.ASPECTS).Cast<Aspect>().ToArray();

                // Reset aspect fields
                if (content.ContentHandler is GenericContent gc)
                {
                    content.RemoveAllAspects();
                    gc.AspectData = null;
                    gc.ClearReference(GenericContent.ASPECTS);
                }
            }

            // Reset regular fields
            foreach (var field in content.Fields.Values)
            {
                var fieldName = field.Name;
                if (newContent.Fields.Any(f => f.Value.Name == fieldName) && !field.ReadOnly && !SafeFieldsInReset.Contains(fieldName))
                    content[fieldName] = newContent[fieldName];
            }

            if (content.ContentHandler.HasProperty(GenericContent.ASPECTS))
            {
                // Re-add all the aspects
                content.AddAspects(aspects);
            }
        }

        private async Task UpdateContentAsync(Content content, JObject model, ODataRequest odataRequest, CancellationToken cancel)
        {
            await UpdateFieldsAsync(content, model, false, cancel).ConfigureAwait(false);

            if (odataRequest.MultistepSave)
                await content.SaveAsync(SavingMode.StartMultistepSave, cancel).ConfigureAwait(false);
            else
                await content.SaveAsync(cancel).ConfigureAwait(false);
        }

        /// <summary>
        /// Helper method for updating the given <see cref="Content"/> with a model represented by JObject.
        /// The <see cref="Content"/> will not be saved.
        /// </summary>
        /// <param name="content">The <see cref="Content"/> that will be modified. Cannot be null.</param>
        /// <param name="model">The modifier JObject instance. Cannot be null.</param>
        /// <param name="skipBrokenReferences">If true, the broken reference fields will not updated to null value.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A Task that represents the asynchronous operation containing a list of field names
        /// that have broken references.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ODataException"></exception>
        public static async Task<List<string>> UpdateFieldsAsync(Content content, JObject model, bool skipBrokenReferences,
            CancellationToken cancel)
        {
            var brokenReferenceFieldNames = new List<string>();
            if (content == null)
                throw new ArgumentNullException(nameof(content));
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            var readonlyFields = new List<string>();

            var isNew = content.Id == 0;
            foreach (var prop in model.Properties())
            {
                if (string.IsNullOrEmpty(prop.Name) || prop.Name is "__ContentType" or "__ContentTemplate" or "Type" or "ContentType")
                    continue;

                // readonly properties: skip if not enough permissions
                if (prop.Name is "CreationDate" or "VersionCreationDate")
                {
                    if (!User.Current.IsOperator)
                    {
                        readonlyFields.Add(prop.Name);
                        continue;
                    }
                }
                
                try
                {
                    var hasField = content.Fields.TryGetValue(prop.Name, out var field);
                    if (!hasField && content.SupportsAddingFieldsOnTheFly && (prop.Value as JValue)?.Value != null)
                    {
                        var value = ((JValue)prop.Value).Value;
                        var fieldSetting = FieldSetting.InferFieldSettingFromType(value.GetType(), prop.Name);
                        var meta = new FieldMetadata(true, true, prop.Name, prop.Name, fieldSetting);
                        hasField = content.AddFieldsOnTheFly(new[] { meta }) &&
                                   content.Fields.TryGetValue(prop.Name, out field);
                    }

                    if (hasField)
                    {
                        if (!field.ReadOnly)
                        {
                            if (prop.Value is JValue jValue)
                            {
                                if (field is IntegerField)
                                {
                                    field.SetData(Convert.ToInt32(jValue.Value));
                                    continue;
                                }

                                if (field is DateTimeField && jValue.Value == null)
                                    continue;
                                if (isNew && field is ReferenceField && jValue.Value == null)
                                {
                                    if (field.Name is "CreatedBy" or "ModifiedBy" or "Owner")
                                        continue;
                                }

                                if (field is ReferenceField && jValue.Value != null)
                                {
                                    var refNode = await LoadReferenceSafeAsync(jValue, cancel)
                                        .ConfigureAwait(false);

                                    // skip setting Somebody
                                    if (prop.Name is "Owner" && refNode?.Id == Identifiers.SomebodyUserId)
                                    {
                                        readonlyFields.Add(prop.Name);
                                        continue;
                                    }

                                    if (refNode == null)
                                        brokenReferenceFieldNames.Add(field.Name);
                                    if (refNode != null || !skipBrokenReferences)
                                        field.SetData(refNode);

                                    continue;
                                }

                                if (isNew && field.Name == "Name" && jValue.Value != null)
                                {
                                    field.SetData(
                                        ContentNamingProvider.GetNameFromDisplayName(jValue.Value.ToString()));
                                    continue;
                                }

                                field.SetData(jValue.Value);
                                continue;
                            }

                            if (prop.Value is JObject jObject)
                            {
                                if (field is BinaryField)
                                    continue;
                                if (field is ImageField)
                                {
                                    // the field supports int, long or string values
                                    var url = jObject["Url"].Value<string>();
                                    if (url.Length == 0)
                                        continue;
                                }
                                if (field is PasswordField && content.ContentHandler is User user)
                                {
                                    var text = jObject["Text"]?.Value<string>();
                                    var hash = jObject["Hash"]?.Value<string>();
                                    if (!string.IsNullOrEmpty(hash))
                                        user.PasswordHash = hash;
                                    else if (!string.IsNullOrEmpty(text))
                                        user.Password = text;
                                    continue;
                                }

                                field.SetData(prop.Value);
                                continue;
                            }

                            if (prop.Value is JArray aValue)
                            {
                                if (field is ReferenceField)
                                {
                                    var refValues = aValue.Values().ToList();
                                    if (refValues.Count == 0)
                                    {
                                        field.SetData(null);
                                        continue;
                                    }

                                    var nodesTasks = refValues
                                        .Select(async rv =>
                                        {
                                            Node refNode = null;
                                            if(rv is JValue jv)
                                                refNode = await LoadReferenceSafeAsync(jv, cancel)
                                                    .ConfigureAwait(false);
                                            if (refNode == null)
                                                if(!brokenReferenceFieldNames.Contains(field.Name))
                                                    brokenReferenceFieldNames.Add(field.Name);
                                            return refNode;
                                        });
                                    var nodes = (await nodesTasks.WhenAll())
                                        .Where(x => x != null) // filter unknown or invisible items
                                        .ToArray();

                                    // skip setting Somebody
                                    if (prop.Name is "Owner" && nodes?.FirstOrDefault()?.Id == Identifiers.SomebodyUserId)
                                    {
                                        readonlyFields.Add(prop.Name);
                                        continue;
                                    }

                                    var fieldSetting = field.FieldSetting as ReferenceFieldSetting;
                                    if (fieldSetting?.AllowMultiple != null && fieldSetting.AllowMultiple.Value)
                                        field.SetData(nodes);
                                    else
                                        field.SetData(nodes.FirstOrDefault());

                                }
                                else if (field is ChoiceField)
                                {
                                    // ChoiceField expects the value to be of type List<string>
                                    var list = new List<string>();
                                    foreach (var token in aValue)
                                    {
                                        if (token is JValue value)
                                            list.Add(value.Value.ToString());
                                        else
                                            throw new Exception(
                                                $"Token type {token.GetType().Name} for field {field.Name} (type {field.GetType().Name}) is not supported.");
                                    }

                                    field.SetData(list);
                                }
                                else if (field is AllowedChildTypesField &&
                                         field.Name == "AllowedChildTypes" &&
                                         content.ContentHandler is GenericContent gc)
                                {
                                    var types = aValue.Values().Select(rv =>
                                    {
                                        switch (rv.Type)
                                        {
                                            case JTokenType.Integer:
                                                return Node.LoadNode(Convert.ToInt32(rv.ToString())) as ContentType;
                                            default:
                                                var typeId = rv.ToString();
                                                if (RepositoryPath.IsValidPath(typeId) ==
                                                    RepositoryPath.PathResult.Correct)
                                                    return Node.LoadNode(typeId) as ContentType;
                                                return ContentType.GetByName(typeId);
                                        }
                                    }).Where(ct => ct != null).ToArray();

                                    var ctName = content.ContentType.Name;
                                    if (!content.ContentType.IsTransitiveForAllowedTypes)
                                        gc.SetAllowedChildTypes(types);
                                }

                                continue;
                            }

                            throw new SnNotSupportedException();
                        }
                    }
                }
                catch (Exception ex)
                {
                    SnTrace.Repository.WriteError($"Error updating property {prop.Name} of {content.Path}. Error: {ex.Message}");
                    throw new ODataException($"Error updating property {prop.Name} of {content.Name}.", ODataExceptionCode.RequestError, ex);
                }
            }

            if (readonlyFields.Any())
                SnTrace.Repository.Write($"User {User.Current.Name} cannot update the following " +
                                         $"readonly fields of {content.Path}: {string.Join(", ", readonlyFields)}");

            return brokenReferenceFieldNames;
        }

        private static async Task<Node> LoadReferenceSafeAsync(JValue identifier, CancellationToken cancel)
        {
            if (identifier == null)
                return null;
            if (identifier.Value == null)
                return null;
            if (identifier.Type == JTokenType.Null)
                return null;

            var pathOrId = identifier.Value.ToString();

            Node refNode = null;
            try
            {
                refNode = await Node.LoadNodeByIdOrPathAsync(pathOrId, cancel).ConfigureAwait(false);
            }
            catch (AccessDeniedException) { /* do nothing, result is null */ }
            catch (SenseNetSecurityException) { /* do nothing, result is null */ }
            return refNode;
        }

        private T GetPropertyValue<T>(string name, JObject model)
        {
            if (model[name] is JValue jValue)
                return (T)jValue.Value;
            return default;
        }

        /// <summary>
        /// Returns an OData path that can request the entity identified by the given path. This path is part of the OData entity request. For example
        /// "/Root/MyFolder/MyDocument.doc" will be transformed to "/Root/MyFolder('MyDocument.doc')"
        /// </summary>
        /// <param name="path">This path will be transformed.</param>
        /// <returns>An OData path.</returns>
        public static string GetODataPath(string path)
        {
            if (string.Compare(path, Identifiers.RootPath, StringComparison.OrdinalIgnoreCase) == 0)
                return string.Empty;

            return GetODataPath(RepositoryPath.GetParentPath(path), RepositoryPath.GetFileName(path));
        }
        /// <summary>
        /// Returns an OData path that can request the entity identified by the given path plus name. This path is part of the OData entity request. For example
        /// path = "/Root/MyFolder" and name = "MyDocument.doc" will be transformed to "/Root/MyFolder('MyDocument.doc')".
        /// </summary>
        /// <param name="parentPath">A container path.</param>
        /// <param name="name">Content's name in the given container.</param>
        /// <returns>An OData path.</returns>
        public static string GetODataPath(string parentPath, string name)
        {
            return $"{parentPath}('{name}')";
        }
    }

    internal interface IActionResolver
    {
        GenericScenario GetScenario(string name, string parameters, HttpContext httpContext);
        IEnumerable<ActionBase> GetActions(Content context, string scenario, string backUri, HttpContext httpContext);
        ActionBase GetAction(Content context, string scenario, string actionName, string backUri, object parameters, HttpContext httpContext, IConfiguration appConfig);
    }
    internal class DefaultActionResolver : IActionResolver
    {
        public GenericScenario GetScenario(string name, string parameters, HttpContext httpContext)
        {
            return ScenarioManager.GetScenario(name, httpContext.Request.QueryString.ToString());
        }
        public IEnumerable<ActionBase> GetActions(Content context, string scenario, string backUri, HttpContext httpContext)
        {
            return ActionFramework.GetActions(context, scenario, null, backUri, httpContext);
        }
        public ActionBase GetAction(Content context, string scenario, string actionName, string backUri, object parameters, HttpContext httpContext, IConfiguration appConfig)
        {
            return backUri == null
                ? ActionFramework.GetAction(actionName, context, parameters, GetMethodBasedAction, (httpContext, appConfig))
                : ActionFramework.GetAction(actionName, context, backUri, parameters, GetMethodBasedAction, (httpContext, appConfig));
        }

        private ActionBase GetMethodBasedAction(string name, Content content, object state)
        {
            var (httpContext, config) = ((HttpContext, IConfiguration))state;

            //var odataRequest = (ODataRequest) httpContext.Items[ODataMiddleware.ODataRequestHttpContextKey];
            OperationCallingContext method;
            try
            {
                method = OperationCenter.GetMethodByRequest(content, name,
                    ODataMiddleware.ReadToJsonAsync(httpContext)
                        .GetAwaiter().GetResult(),
                    httpContext.Request.Query);
            }
            catch (OperationNotFoundException e)
            {
                SnTrace.System.WriteError($"Operation {name} not found. " +
                                          $"Content: {content.Path}, User: {User.Current.Username}");

                throw new InvalidContentActionException(e, InvalidContentActionReason.UnknownAction, content.Path,
                    e.Message, name);
            }
            catch (AmbiguousMatchException e)
            {
                SnTrace.System.WriteError($"Operation {name} is ambiguous. " +
                                          $"Content: {content.Path}, User: {User.Current.Username}");

                throw new InvalidContentActionException(e, InvalidContentActionReason.UnknownAction, content.Path,
                    e.Message, name);
            }
            catch(Exception ex)
            {
                SnTrace.System.WriteError($"Error during discovery of method {name}. {ex.Message} " +
                                          $"Content: {content.Path}, User: {User.Current.Username}");
                throw;
            }

            method.HttpContext = httpContext;
            method.ApplicationConfiguration = config;
            return new ODataOperationMethodExecutor(method);
        }
    }
}
