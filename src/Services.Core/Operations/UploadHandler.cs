﻿using System.IO;
using System.Linq;
using SenseNet.ContentRepository;
using SenseNet.Portal.Handlers;
using System.Web;
using System;
using SenseNet.ContentRepository.Fields;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Schema;
using SenseNet.ContentRepository.i18n;
using SenseNet.Preview;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Threading;
using System.Xml.XPath;
using BinaryData = SenseNet.ContentRepository.Storage.BinaryData;

namespace SenseNet.Services.Core.Operations
{
    //TODO: let developers override this class/feature
    // Review the potential virtual methods in this class
    // and insert a hook in actions that uses it.
    internal class UploadHandler
    {
        private readonly HttpContext _httpContext;
        private Content Content { get; }

        // ======================================================================== Constructor

        public UploadHandler(Content content, HttpContext context)
        {
            Content = content;
            _httpContext = context;
        }

        // ======================================================================== Properties

        private bool? _useChunk;
        protected bool UseChunk
        {
            get
            {
                if (!_useChunk.HasValue)
                    _useChunk = TryParseRangeHeader(out _chunkStart, out _chunkLength, out _fileLength);

                return _useChunk.Value;
            }
        }

        private long _chunkStart;
        protected long ChunkStart
        {
            get
            {
                if (!_useChunk.HasValue)
                    _useChunk = TryParseRangeHeader(out _chunkStart, out _chunkLength, out _fileLength);

                return _chunkStart;
            }
        }

        private int _chunkLength;
        protected int ChunkLength
        {
            get
            {
                if (!_useChunk.HasValue)
                    _useChunk = TryParseRangeHeader(out _chunkStart, out _chunkLength, out _fileLength);

                return _chunkLength;
            }
        }

        private long _fileLength;
        protected internal long FileLength
        {
            get
            {
                if (!_useChunk.HasValue)
                {
                    _useChunk = TryParseRangeHeader(out _chunkStart, out _chunkLength, out var tempFileLength);

                    // In case of the first request, the length comes from a manual parameter 
                    // instead of the range header. So overwrite it only if the header 
                    // contains a real value;
                    if (tempFileLength > 0)
                    {
                        _fileLength = tempFileLength;
                    }
                }

                return _fileLength;
            }
            set 
            {
                _fileLength = value;
            }
        }

        protected internal string ContentTypeName { get; set; }       

        private string _propertyName;
        protected internal string PropertyName
        {
            get
            {
                return _propertyName ?? "Binary";
            }
            set 
            {
                _propertyName = value;
            }
        }

        protected internal string FileText { get; set; }
        protected internal bool Overwrite { get; set; } = true;
        protected internal int? ContentId { get; set; }
        protected internal string FileName { get; set; }
        protected internal string ChunkToken { get; set; }        
        protected internal bool UseChunkRequestValue { get; set; }        
        protected internal bool? Create { get; set; }      
        
        private IFormFile FormFile { get; set; }

        // ======================================================================== POTENTIAL Virtual methods

        protected async Task<Content> GetContentAsync(Content parent, string fileName, string contentTypeName, bool overwrite,
            CancellationToken cancellationToken)
        {
            var contentName = ContentNamingProvider.GetNameFromDisplayName(fileName);
            var path = RepositoryPath.Combine(parent.Path, contentName);

            Content content;

            if (overwrite)
            {
                // check if content exists
                content = await Content.LoadAsync(path, cancellationToken).ConfigureAwait(false);
                if (content != null)
                {
                    SetPreviewGenerationPriority(content);

                    return content;
                }
            }

            // create new content
            content = Content.CreateNew(contentTypeName, parent.ContentHandler, contentName);

            // prevent autonaming feature in case of preview images
            if (string.Compare(contentTypeName, DocumentPreviewProvider.PREVIEWIMAGE_CONTENTTYPE, StringComparison.InvariantCultureIgnoreCase) != 0)
                content.ContentHandler.AllowIncrementalNaming = true;

            SetPreviewGenerationPriority(content);

            return content;
        }

        protected async System.Threading.Tasks.Task SaveFileToRepositoryAsync(Content uploadedContent, Content parent, string token, bool mustFinalize, 
            bool mustCheckIn, IFormFile file, CancellationToken cancellationToken)
        {
            if (uploadedContent.ContentHandler.Locked && uploadedContent.ContentHandler.LockedBy.Id != User.Current.Id)
                throw new Exception(SenseNetResourceManager.Current.GetString("Action", "UploadExceptionLocked"));

            if (UseChunk)
            {
                // get bytes from the uploaded stream
                byte[] chunkData;
                using (var br = new BinaryReader(file.OpenReadStream()))
                {
                    chunkData = br.ReadBytes(ChunkLength);
                }

                // save chunk
                BinaryData.WriteChunk(uploadedContent.Id, token, FileLength, chunkData, ChunkStart, PropertyName);

                // last chunk should commit the process
                if (ChunkStart + ChunkLength == FileLength)
                {
                    BinaryData.CommitChunk(uploadedContent.Id, token, FileLength, PropertyName, CreateBinaryData(file, false));
                    await uploadedContent.ContentHandler.UploadFinishedAsync(PropertyName, cancellationToken).ConfigureAwait(false);

                    // finalize only if the multistep save was started by this process
                    if (mustFinalize || mustCheckIn)
                    {
                        uploadedContent = await Content.LoadAsync(uploadedContent.Id, cancellationToken).ConfigureAwait(false);

                        SetPreviewGenerationPriority(uploadedContent);

                        await uploadedContent.FinalizeContentAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                if (uploadedContent.IsNew || uploadedContent.ContentHandler.SavingState == ContentSavingState.Finalized)
                {
                    var binData = CreateBinaryData(file);
                    uploadedContent[PropertyName] = binData;
                    await uploadedContent.SaveAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // Workaround for small existing content, in case the user started
                    // a multistep saving process manually: save the whole binary in one chunk
                    // (we cannot execute a real content Save here to avoid messing with saving state).

                    string chunkToken;
                    byte[] chunkData;

                    using (var inputStream = file.OpenReadStream())
                    {
                        var length = inputStream.Length;
                        chunkToken = BinaryData.StartChunk(uploadedContent.Id, length, PropertyName);
                        
                        using (var br = new BinaryReader(inputStream))
                        {
                            chunkData = br.ReadBytes(Convert.ToInt32(length));
                        }
                    }

                    // save everything in one chunk and commit the process
                    BinaryData.WriteChunk(uploadedContent.Id, chunkToken, chunkData.Length, chunkData, 0, PropertyName);
                    BinaryData.CommitChunk(uploadedContent.Id, chunkToken, chunkData.Length, PropertyName, CreateBinaryData(file, false));

                    if (mustFinalize && uploadedContent.ContentHandler.SavingState != ContentSavingState.Finalized)
                        await uploadedContent.FinalizeContentAsync(cancellationToken).ConfigureAwait(false);
                }

                // checkin only if the content was created or checked out by this process
                if (uploadedContent.ContentHandler.Locked && mustCheckIn)
                    await uploadedContent.CheckInAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        
        // ======================================================================== Public API

        public async Task<object> ExecuteAsync(CancellationToken cancellationToken)
        {
            // 1st allowed types check: if allowed content types list is empty, no upload is allowed
            if (!AllowCreationForEmptyAllowedContentTypes(Content.ContentHandler))
                throw new Exception(SenseNetResourceManager.Current.GetString("Action","UploadExceptionEmptyAllowedChildTypes"));

            // Only ContentType is allowed under the System/Schema/ContentTypes
            if (this.Content.Path.StartsWith(Repository.ContentTypesFolderPath + "/", StringComparison.InvariantCultureIgnoreCase))
                if (this.ContentTypeName != "ContentType")
                    throw new Exception(SenseNetResourceManager.Current.GetString("Action", "UploadExceptionInvalidContentType"));

            // the create parameter is sent in the url
            if (Create.HasValue)
            {
                var uploadedContent = await GetContentAsync(Content, cancellationToken).ConfigureAwait(false);

                // check if the content is locked by someone else
                if (uploadedContent.ContentHandler.Locked && uploadedContent.ContentHandler.LockedBy.Id != User.Current.Id)
                    throw new Exception(SenseNetResourceManager.Current.GetString("Action", "UploadExceptionLocked"));

                var chunkToken = string.Empty;

                // If the content is not locked at the start of this process, it will be checked out by the multistep saving mechanism below
                // and it will be checked in at the end (either manually or by the finalizer method).
                var mustCheckIn = uploadedContent.IsNew || !uploadedContent.ContentHandler.Locked;

                // At the end we will finalize only if we started the multistep save.
                var mustFinalize = uploadedContent.ContentHandler.SavingState == ContentSavingState.Finalized;

                if (UseChunkRequestValue)
                {
                    // Start the multistep saving process only if it was not started by 
                    // somebody else before (e.g. with an initial POST request through OData).
                    if (mustFinalize)
                        await uploadedContent.SaveAsync(SavingMode.StartMultistepSave, cancellationToken).ConfigureAwait(false);

                    chunkToken = BinaryData.StartChunk(uploadedContent.Id, FileLength, PropertyName);
                }

                return string.Format("{0}*{1}*{2}*{3}", uploadedContent.Id, chunkToken, mustFinalize, mustCheckIn);
            }
            else
            {
                // handle uploaded chunks/stream/text
                if(string.IsNullOrEmpty(FileText))
                    FormFile = _httpContext.Request.Form.Files.Count > 0 ? _httpContext.Request.Form.Files[0] : null;

                // The request has arrived to the parent represented by the Content property,
                // but in some cases (for example content types) the parent may be different.
                var parent = GetRealParent();

                if (FormFile != null && FormFile.Length == 0)
                {
                    // create content for an empty file if necessary
                    var emptyFile = await GetContentAsync(parent, cancellationToken).ConfigureAwait(false);
                    if (emptyFile != null)
                    {
                        if (!emptyFile.IsNew)
                        {
                            var binaryData = (BinaryData)emptyFile[PropertyName];
                            binaryData.Reset();
                            binaryData.SetStream(new MemoryStream());
                        }
                        await emptyFile.SaveAsync(cancellationToken).ConfigureAwait(false);
                        return GetJsonFromContent(emptyFile, FormFile);
                    }

                    return null;
                }

                if (FormFile == null && string.IsNullOrEmpty(FileText))
                    return null;

                var contentId = 0;
                var chunkToken = string.Empty;
                var mustFinalize = false;
                var mustCheckIn = false;

                // collect data only if this is a real file, not a text
                if (FormFile != null)
                    CollectUploadData(out contentId, out chunkToken, out mustFinalize, out mustCheckIn);

                // load the content using the posted chunk token or create a new one
                // (in case of a small file, when no chunk upload is used)
                var uploadedContent = UseChunk 
                    ? await Content.LoadAsync(contentId, cancellationToken).ConfigureAwait(false) 
                    : await GetContentAsync(parent, cancellationToken).ConfigureAwait(false);

                // in case we just loaded this content
                SetPreviewGenerationPriority(uploadedContent);

                var isContentType = uploadedContent.Path.StartsWith(Repository.ContentTypesFolderPath + "/",
                    StringComparison.OrdinalIgnoreCase);

                if (FormFile != null)
                {
                    await SaveFileToRepositoryAsync(uploadedContent, Content, chunkToken, 
                        mustFinalize, mustCheckIn, FormFile, cancellationToken);
                }
                else
                {
                    // handle text data
                    var binData = isContentType
                        ? new BinaryData {FileName = new BinaryFileName(uploadedContent.Name + ".ContentType"), ContentType = "text/xml"}
                        : new BinaryData {FileName = new BinaryFileName(uploadedContent.Name) };

                    // set content type only if we were unable to recognize it
                    if (string.IsNullOrEmpty(binData.ContentType))
                        binData.ContentType = "text/plain";

                    binData.SetStream(RepositoryTools.GetStreamFromString(FileText));

                    uploadedContent[PropertyName] = binData;
                    await uploadedContent.SaveAsync(cancellationToken).ConfigureAwait(false);
                }

                return GetJsonFromContent(uploadedContent, FormFile);
            }
        }


        public async Task<string> FinalizeContentAsync(Content content, CancellationToken cancel)
        {
            SetPreviewGenerationPriority(content);
            await content.FinalizeContentAsync(cancel).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> StartBlobUploadToParentAsync(string name, string contentType, 
            long fullSize, CancellationToken cancellationToken, string fieldName = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));

            // load or create the content
            var file = await Content.LoadAsync(RepositoryPath.Combine(Content.Path, name), 
                cancellationToken).ConfigureAwait(false);

            if (file == null)
            {
                if (string.IsNullOrEmpty(contentType))
                    contentType = GetContentTypeName(Content, name);

                // we have to create it in a multistep saving state because chunk upload needs that
                file = Content.CreateNew(contentType, Content.ContentHandler, name);
                await file.SaveAsync(SavingMode.StartMultistepSave, cancellationToken).ConfigureAwait(false);
            }

            return StartBlobUpload(file, fullSize, fieldName);
        }
        public string StartBlobUpload(long fullSize, string fieldName = null)
        {
            return StartBlobUpload(Content, fullSize, fieldName);
        }
        protected string StartBlobUpload(Content content, long fullSize, string fieldName = null)
        {
            // we have to put the content into a state that enables chunk write operations
            if (content.ContentHandler.SavingState == ContentSavingState.Finalized)
                content.SaveAsync(SavingMode.StartMultistepSave, CancellationToken.None).GetAwaiter().GetResult();

            var token = BinaryData.StartChunk(content.Id, fullSize, fieldName);

            return $"{{ id: '{content.Id}', token: '{token}', versionId: {content.ContentHandler.VersionId} }}";
        }

        public async Task<string> FinalizeBlobUploadAsync(string token, long fullSize, CancellationToken cancellationToken,
            string fieldName = null, string fileName = null)
        {
            if (string.IsNullOrEmpty(token))
                throw new ArgumentNullException(nameof(token));

            // In most cases this will be the file name, but in case of custom
            // binary fields it is possible to provide a different name.
            if (string.IsNullOrEmpty(fileName))
                fileName = Content.Name;

            BinaryData.CommitChunk(Content.Id, token, fullSize, fieldName, new BinaryData
            {
                FileName = new BinaryFileName(fileName)
            });

            // reload the content to have a fresh object after commit chunk
            var content = await Content.LoadAsync(Content.Id, cancellationToken).ConfigureAwait(false);
            return await FinalizeContentAsync(content, cancellationToken).ConfigureAwait(false);
        }

        public string GetBinaryToken(string fieldName = null)
        {
            // workaround for empty string (not null, so an optional argument is not enough)
            if (string.IsNullOrEmpty(fieldName))
                fieldName = "Binary";

            if (!Content.Fields.TryGetValue(fieldName, out Field field) || !(field is BinaryField))
                throw new InvalidOperationException("Unknown binary field: " + fieldName);

            if (!(Content[fieldName] is BinaryData binaryData))
                throw new InvalidOperationException("Empty binary value: " + fieldName);

            return $"{{ token: '{binaryData.GetToken()}', versionId: {Content.ContentHandler.VersionId} }}";
        }

        // ======================================================================== Helper methods

        protected string GetContentTypeName(Content parent, string fileName)
        {
            // 1. if contenttype post parameter is defined we will use that without respect to allowed types
            // 2. otherwise check configured upload types (by extension) and use it if it is allowed
            // 3. otherwise get the first allowed type that is or is derived from file

            string contentTypeName = null;

            if (!string.IsNullOrEmpty(ContentTypeName))
            {
                // try resolving provided type
                var ct = ContentType.GetByName(ContentTypeName);
                if (ct != null)
                    contentTypeName = ContentTypeName;
            }
            else
            {
                if (!(parent.ContentHandler is GenericContent gc))
                    return null;

                var allowedTypes = gc.GetAllowedChildTypes().ToArray();

                // check configured upload types (by extension) and use it if it is allowed
                var fileContentType = UploadHelper.GetContentType(fileName, parent.Path);
                if (!string.IsNullOrEmpty(fileContentType))
                {
                    if (allowedTypes.Select(ct => ct.Name).Contains(fileContentType))
                        contentTypeName = fileContentType;
                }

                if (string.IsNullOrEmpty(contentTypeName))
                {
                    // get the first allowed type that is or is derived from file
                    if (allowedTypes.Any(ct => ct.Name == "File"))
                    {
                        contentTypeName = "File";
                    }
                    else
                    {
                        var fileDescendant = allowedTypes.FirstOrDefault(ct => ct.IsInstaceOfOrDerivedFrom("File"));
                        if (fileDescendant != null)
                            contentTypeName = fileDescendant.Name;
                    }
                }
            }

            return contentTypeName;
        }

        private const string contentDispHeaderPrefix = "filename=";
        protected string GetFileName(IFormFile file)
        {
            if (UseChunk)
            {
                // Content-Disposition: attachment; filename="x.png"
                var contentDispHeader = _httpContext.Request.Headers["Content-Disposition"].FirstOrDefault() ?? string.Empty;
                var idx = contentDispHeader.IndexOf(contentDispHeaderPrefix, StringComparison.InvariantCultureIgnoreCase);
                var fileInQuotes = contentDispHeader.Substring(idx + contentDispHeaderPrefix.Length);
                var fileName = fileInQuotes.Replace("\"", "");
                return HttpUtility.UrlDecode(fileName);
            }
            else
            {
                var fileNames = file.FileName.Split(new char[] { '\\' });
                var fileName = fileNames[fileNames.Length - 1];
                return fileName;
            }
        }
        
        protected bool TryParseRangeHeader(out long chunkStart, out int chunkLength, out long fullLength)
        {
            // parse chunk information
            chunkStart = 0;
            chunkLength = 0;
            fullLength = 0;
            var rangeHeader = _httpContext.Request.Headers["Content-Range"].FirstOrDefault() ?? string.Empty;
            if (!string.IsNullOrEmpty(rangeHeader))
            {
                var fullinfo = rangeHeader.Substring("bytes ".Length).Split('/');
                fullLength = Int64.Parse(fullinfo[1]);
                var chunkinfo = fullinfo[0].Split('-');
                chunkStart = Int64.Parse(chunkinfo[0]);
                var chunkEnd = Int64.Parse(chunkinfo[1]);
                chunkLength = Convert.ToInt32(chunkEnd - chunkStart + 1);
                return true;
            }
            return false;
        }

        protected async Task<Content> GetContentAsync(Content parent, CancellationToken cancellationToken)
        {
            if (Overwrite && ContentId.HasValue)
            {
                var content = await Content.LoadAsync(ContentId.Value, cancellationToken).ConfigureAwait(false);
                if (content != null)
                {
                    SetPreviewGenerationPriority(content);

                    return content;
                }
            }

            var contentTypeName = GetContentTypeName(parent, FileName);
            if (contentTypeName == null)
                throw new Exception(SenseNetResourceManager.Current.GetString("Action", "UploadExceptionInvalidContentType"));

            return await GetContentAsync(parent, FileName, contentTypeName, Overwrite, cancellationToken).ConfigureAwait(false);
        }

        protected BinaryData CreateBinaryData(IFormFile file, bool setStream = true)
        {
            var fileName = UseChunk ? GetFileName(file) : file?.FileName;

            return UploadHelper.CreateBinaryData(fileName, setStream ? file?.OpenReadStream() : null, file?.ContentType);
        }

        private Content GetRealParent()
        {
            if (!string.Equals(ContentTypeName, "ContentType", StringComparison.InvariantCultureIgnoreCase))
                return Content;

            // Workaround for content types. The client does not know where to put
            // the new type in the hierarchy. The real parent is determined by
            // looking into the content type xml.
            string ctd;
            if (FormFile != null)
            {
                using var stream = FormFile.OpenReadStream();
                ctd = RepositoryTools.GetStreamString(stream);

                // request stream can be loaded only once, we have to save the value
                FileText = ctd;
                FormFile = null;
            }
            else
            {
                ctd = FileText;
            }

            var (parentName, name) = GetContentTypeInfo(ctd);
            var parentType = ContentType.GetByName(parentName);
            if (parentType != null)
            {
                FileName = name;
                return Content.Load(parentType.Id);
            }

            return Content;
        }

        protected static bool AllowCreationForEmptyAllowedContentTypes(Node node)
        {
            if (node is GenericContent parent)
            {
                if (!parent.GetAllowedChildTypes().Any())
                    return false;
            }
            return true;
        }

        protected string GetJsonFromContent(Content content, IFormFile file)
        {
            if (content == null)
                return string.Empty;

            var result = new
            {
                Url = content.Path,
                Thumbnail_url = content.Path,
                Name = content.Name,
                Length = UseChunk ? FileLength : (file != null ? file.Length : FileText.Length),
                Type = content.ContentType.Name,
                Id = content.Id
            };

            return JsonConvert.SerializeObject(result);
        }

        protected void CollectUploadData(out int contentId, out string token, out bool mustFinalize, out bool mustCheckIn)
        {
            var uploadDataArray = (ChunkToken ?? string.Empty).Split(new[] { '*' });

            if (uploadDataArray.Length != 4)
                throw new Exception(SenseNetResourceManager.Current.GetString("Action", "UploadExceptionInvalidRequest"));

            if (!int.TryParse(uploadDataArray[0], out contentId))
                throw new Exception(SenseNetResourceManager.Current.GetString("Action", "UploadExceptionInvalidRequest"));

            token = uploadDataArray[1];

            if (!bool.TryParse(uploadDataArray[2], out mustFinalize))
                throw new Exception(SenseNetResourceManager.Current.GetString("Action", "UploadExceptionInvalidRequest"));
            if (!bool.TryParse(uploadDataArray[3], out mustCheckIn))
                throw new Exception(SenseNetResourceManager.Current.GetString("Action", "UploadExceptionInvalidRequest"));
        }

        protected internal static void SetPreviewGenerationPriority(Content content)
        {
            if (content?.ContentHandler is ContentRepository.File file)
            {
                file.PreviewGenerationPriority = TaskManagement.Core.TaskPriority.Important;
                file.PageCount = (int)PreviewStatus.Postponed;
            }
        }

        private static (string ParentName, string Name) GetContentTypeInfo(string ctdXml)
        {
            // load and parse a CTD xml
            var ctd = new XPathDocument(new StringReader(ctdXml));
            var nav = ctd.CreateNavigator().SelectSingleNode("/*[1]");

            var name = nav.GetAttribute("name", "");
            var parentName = nav.GetAttribute("parentType", "");

            return (parentName, name);
        }
    }
}
