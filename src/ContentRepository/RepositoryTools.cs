using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Schema;
using SenseNet.ContentRepository.Storage.Schema;
using SenseNet.ContentRepository.Versioning;
using System.Globalization;
using System.Linq;
using SenseNet.Diagnostics;
using System.Security.Cryptography;
using SenseNet.ApplicationModel;
using SenseNet.ContentRepository.Storage.Security;
using Newtonsoft.Json;
using SenseNet.Security;
using SenseNet.Search;
using System.Diagnostics;
using System.Drawing.Text;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.ContentRepository.Storage.Search;
using SenseNet.Search.Indexing;
using SenseNet.TaskManagement.Core;
using STT = System.Threading.Tasks;
using SenseNet.ContentRepository.i18n;

namespace SenseNet.ContentRepository
{
    public static class RepositoryTools
    {
        //TODO: [async] move this method to the Tools package
        // Remove the original CombineCancellationToken method from the Common project as well.
        internal static CancellationToken AddTimeout(this CancellationToken cancellationToken, TimeSpan timeout)
        {
            if (timeout == default)
                return cancellationToken;

            var timeoutToken = new CancellationTokenSource(timeout).Token;
            return cancellationToken == CancellationToken.None
                ? timeoutToken
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutToken).Token;
        }

        public static string GetStreamString(Stream stream)
        {
            StreamReader sr = new StreamReader(stream);
            stream.Position = 0;
            return sr.ReadToEnd();
        }
        public static Stream GetStreamFromString(string textData)
        {
            var stream = new MemoryStream();

            // Write to the stream only if the text is not empty, because writing an empty
            // string in UTF-8 format would result in a 3 bytes length stream.
            if (!string.IsNullOrEmpty(textData))
            {
                var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.Write(textData);
                writer.Flush();

                stream.Position = 0;
            }

            return stream;
        }

        public static CultureInfo GetUICultureByNameOrDefault(string cultureName)
        {
            CultureInfo cultureInfo = null;

            if (!String.IsNullOrEmpty(cultureName))
            {
                cultureInfo = (from c in CultureInfo.GetCultures(CultureTypes.AllCultures)
                               where c.Name == cultureName
                               select c).FirstOrDefault();
            }
            if (cultureInfo == null)
                cultureInfo = CultureInfo.CurrentUICulture;

            return cultureInfo;
        }

        public static string GetVersionString(Node node)
        {
            string extraText = string.Empty;
            switch (node.Version.Status)
            {
                case VersionStatus.Pending: extraText = SR.GetString("Portal", "Approving"); break;
                case VersionStatus.Draft: extraText = SR.GetString("Portal", "Draft"); break;
                case VersionStatus.Locked:
                    var lockedByName = node.Lock.LockedBy == null ? "" : node.Lock.LockedBy.Name;
                    extraText = string.Concat(SR.GetString("Portal", "CheckedOutBy"), " ", lockedByName);
                    break;
                case VersionStatus.Approved: extraText = SR.GetString("Portal", "Public"); break;
                case VersionStatus.Rejected: extraText = SR.GetString("Portal", "Reject"); break;
            }

            var content = node as GenericContent;
            var vmode = VersioningType.None;
            if (content != null)
                vmode = content.VersioningMode;

            if (vmode == VersioningType.None)
                return extraText;
            if (vmode == VersioningType.MajorOnly)
                return string.Concat(node.Version.Major, " ", extraText);
            return string.Concat(node.Version.Major, ".", node.Version.Minor, " ", extraText);
        }

        public static string CalculateMD5(string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);

            using (var stream = new MemoryStream(bytes))
            {
                return CalculateMD5(stream, 64 * 1024);
            }
        }

        public static string CalculateMD5(Stream stream, int bufferSize)
        {
            MD5 md5Hasher = MD5.Create();

            byte[] buffer = new byte[bufferSize];
            int readBytes;

            while ((readBytes = stream.Read(buffer, 0, bufferSize)) > 0)
            {
                md5Hasher.TransformBlock(buffer, 0, readBytes, buffer, 0);
            }

            md5Hasher.TransformFinalBlock(new byte[0], 0, 0);

            var result = md5Hasher.Hash.Aggregate(string.Empty, (full, next) => full + next.ToString("x2"));
            return result;
        }

        private static readonly char[] _availableRandomChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz".ToCharArray();

        /// <summary>
        /// Generates a random string consisting of <paramref name="length">length</paramref> number of characters, using RNGCryptoServiceProvider.
        /// </summary>
        /// <param name="length">The length of the generated string.</param>
        /// <returns>A string consisting of random characters.</returns>
        public static string GetRandomString(int length)
        {
            return GetRandomString(length, _availableRandomChars);
        }

        /// <summary>
        /// Generates a random string consisting of <paramref name="length">length</paramref> number of characters, using RNGCryptoServiceProvider.
        /// </summary>
        /// <param name="length">The length of the generated string.</param>
        /// <param name="availableCharacters">Characters that can be used in the random string.</param>
        /// <returns>A string consisting of random characters.</returns>
        public static string GetRandomString(int length, char[] availableCharacters)
        {
            if (availableCharacters == null)
                throw new ArgumentNullException("availableCharacters");
            if (availableCharacters.Length == 0)
                throw new ArgumentException("Available characters array must contain at least one character.");

            var rng = new RNGCryptoServiceProvider();
            var random = new byte[length];
            rng.GetNonZeroBytes(random);

            var buffer = new char[length];
            var characterTableLength = availableCharacters.Length;

            for (var index = 0; index < length; index++)
            {
                buffer[index] = availableCharacters[random[index] % characterTableLength];
            }

            return new string(buffer);
        }

        /// <summary>
        /// Generates a random string using RNGCryptoServiceProvider. The length of the string will be bigger
        /// than <paramref name="byteLength">byteLength</paramref> because the result bytes will be converted to string using Base64 conversion.
        /// </summary>
        /// <param name="byteLength">The length of the randomly generated byte array that will be converted to string.</param>
        /// <returns>A string consisting of random characters.</returns>
        public static string GetRandomStringBase64(int byteLength)
        {
            var randomBytes = new byte[byteLength];
            var rng = new RNGCryptoServiceProvider();
            rng.GetNonZeroBytes(randomBytes);

            return Convert.ToBase64String(randomBytes);
        }

        /// <summary>
        /// Converts the given datetime to a datetime in UTC format. If it is already in UTC, there will be 
        /// no conversion. Undefined datetime will be considered as UTC. A duplicate of this method exists 
        /// in the Storage layer.
        /// </summary>
        /// <param name="dateTime"></param>
        /// <returns></returns>
        internal static DateTime ConvertToUtcDateTime(DateTime dateTime)
        {
            switch (dateTime.Kind)
            {
                case DateTimeKind.Local:
                    return dateTime.ToUniversalTime();
                case DateTimeKind.Utc:
                    return dateTime;
                case DateTimeKind.Unspecified:
                    return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                default:
                    throw new InvalidOperationException("Unknown datetime kind: " + dateTime.Kind);
            }
        }

        public static bool IsExecutableExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return false;

            return Repository.ExecutableExtensions.Any(e => string.Compare(e, extension.Trim('.'), StringComparison.InvariantCultureIgnoreCase) == 0);
        }

        public static bool IsExecutableType(NodeType nodeType)
        {
            if (nodeType == null)
                return false;

            return Repository.ExecutableFileTypeNames.Any(tn => string.Compare(tn, nodeType.Name, StringComparison.InvariantCultureIgnoreCase) == 0);
        }

        public static void AssertArgumentNull(object value, string name)
        {
            if (value == null)
                throw new ArgumentNullException(name);
        }

        [Obsolete("Use ServiceTools.GetClientIpAddress instead.", true)]
        public static string GetClientIpAddress()
        {
            return string.Empty;
        }

        // Structure building ==================================================================

        public static Content CreateStructure(string path)
        {
            return CreateStructure(path, "Folder");
        }

        public static Content CreateStructure(string path, string containerTypeName)
        {
            // check path validity before calling the recursive method
            if (string.IsNullOrEmpty(path))
                return null;

            RepositoryPath.CheckValidPath(path);

            return EnsureContainer(path, containerTypeName);
        }

        private static Content EnsureContainer(string path, string containerTypeName)
        {
            if (Node.Exists(path))
                return null;

            var name = RepositoryPath.GetFileName(path);
            var parentPath = RepositoryPath.GetParentPath(path);

            // recursive call to create parent containers
            EnsureContainer(parentPath, containerTypeName);

            return CreateContent(parentPath, name, containerTypeName);
        }

        private static Content CreateContent(string parentPath, string name, string typeName)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException("name");
            if (string.IsNullOrEmpty(typeName))
                throw new ArgumentNullException("typeName");

            var parent = Node.LoadNode(parentPath);

            if (parent == null)
                throw new ApplicationException("Parent does not exist: " + parentPath);

            // don't use admin account here, that should be 
            // done in the calling 'client' code if needed
            var content = Content.CreateNew(typeName, parent, name);
            content.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();

            return content;
        }

        // Diagnostics =========================================================================

        public static string CollectExceptionMessages(Exception ex)
        {
            var sb = new StringBuilder();
            var e = ex;
            while (e != null)
            {
                sb.AppendLine(e.Message).AppendLine(e.StackTrace).AppendLine("-----------------");
                e = e.InnerException;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Checks all containers in the requested subtree and returns all paths where AllowedChildTypes is empty.
        /// </summary>
        /// <remarks>
        /// The response is a list of content paths where AllowedChildTypes is empty categorized by content type names.
        /// Here is an annotated example:
        /// <code>
        /// {
        ///   "Domain": [              // ContentType name
        ///     "/Root/...",           // Path1
        ///     "/Root/...",           // Path2
        ///   ],
        ///   "OrganizationalUnit": [  // ContentType name
        ///     "/Root/..."            // Path1
        ///   ]
        /// }
        /// </code>
        /// </remarks>
        /// <param name="root"></param>
        /// <returns>A dictionary where the ContentType name is the key and a path list is the value.</returns>
        [ODataFunction(Category = "Content Types")]
        [AllowedRoles(N.R.Administrators, N.R.PublicAdministrators, N.R.Developers)]
        public static Dictionary<string, List<string>> CheckAllowedChildTypesOfFolders(Content root)
        {
            var result = new Dictionary<string, List<string>>();
            var rootPath = root != null ? root.Path : Identifiers.RootPath;
            foreach (var node in NodeEnumerator.GetNodes(rootPath))
            {
                if (!(node is IFolder))
                    continue;

                var gc = node as GenericContent;
                if (gc == null)
                    continue;

                if (gc.ContentType.IsTransitiveForAllowedTypes)
                    continue;

                var t = node.NodeType.Name;

                if (gc.GetAllowedChildTypeNames().Count() > 0)
                    continue;

                if (!result.ContainsKey(t))
                    result.Add(t, new List<string> { gc.Path });
                else
                    result[t].Add(gc.Path);
            }
            return result;
        }

        /// <summary>
        /// Returns all content types.
        /// </summary>
        /// <param name="content"></param>
        /// <returns>Content list of all content types.</returns>
        [ODataFunction("GetAllContentTypes", Category = "Content Types")]
        [ContentTypes(N.CT.PortalRoot)]
        [AllowedRoles(N.R.Everyone)]
        public static IEnumerable<Content> GetListOfAllContentTypes(Content content)
        {
            return ContentType.GetContentTypes().Select(ct => Content.Create(ct));
        }

        /// <summary>
        /// Returns a path list of Contents that cannot be deleted.
        /// </summary>
        /// <remarks>
        /// The default is the following:
        /// <code>
        /// [
        ///   "/Root",
        ///   "/Root/IMS",
        ///   "/Root/IMS/BuiltIn",
        ///   "/Root/IMS/BuiltIn/Portal",
        ///   "/Root/IMS/BuiltIn/Portal/Admin",
        ///   "/Root/IMS/BuiltIn/Portal/Administrators",
        ///   "/Root/IMS/BuiltIn/Portal/Visitor",
        ///   "/Root/IMS/BuiltIn/Portal/Everyone",
        ///   "/Root/IMS/Public",
        ///   "/Root/System",
        ///   "/Root/System/Schema",
        ///   "/Root/System/Schema/ContentTypes",
        ///   "/Root/System/Schema/ContentTypes/GenericContent",
        ///   "/Root/System/Schema/ContentTypes/GenericContent/Folder",
        ///   "/Root/System/Schema/ContentTypes/GenericContent/File",
        ///   "/Root/System/Schema/ContentTypes/GenericContent/User",
        ///   "/Root/System/Schema/ContentTypes/GenericContent/Group"
        /// ]
        /// </code>
        /// </remarks>
        /// <param name="content"></param>
        /// <returns>A string array as a path list.</returns>
        [ODataFunction(operationName: "ProtectedPaths", Category = "Security")]
        [ContentTypes(N.CT.PortalRoot)]
        [AllowedRoles(N.R.Everyone)]
        public static string[] GetProtectedPaths(Content content)
        {
            var permitted = Providers.Instance.ContentProtector.GetProtectedPaths()
                .Where(x =>
                {
                    var head = NodeHead.Get(x);
                    return head != null && Providers.Instance.SecurityHandler.HasPermission(
                               User.Current, head.Id, PermissionType.See);
                })
                .ToArray();
            return permitted;
        }

        /// <summary>
        /// Returns the list of content types that are allowed in the content type of the requested content.
        /// </summary>
        /// <param name="content"></param>
        /// <returns>Content list of content types.</returns>
        [ODataFunction(Category = "Content Types")]
        [AllowedRoles(N.R.Everyone)]
        // ReSharper disable once InconsistentNaming
        public static IEnumerable<Content> GetAllowedChildTypesFromCTD(Content content)
        {
            return content.ContentType.AllowedChildTypes.Select(Content.Create);
        }

        /// <summary>
        /// Returns a path list in the subtree of the requested content
        /// containing items that have explicit security entry for the Everyone group but
        /// do not have an explicit security entry for the Visitor user.
        /// </summary>
        /// <remarks>
        /// Result example:
        /// <code>
        /// [
        ///   "/Root/(apps)/GenericContent/Versions",
        ///   "/Root/(apps)/User/Logout",
        ///   "/Root/Content",
        ///   "/Root/Trash"
        /// ]
        /// </code>
        /// </remarks>
        /// <param name="root"></param>
        /// <returns>Path list.</returns>
        [ODataFunction(Category = "Security")]
        [AllowedRoles(N.R.Administrators, N.R.PublicAdministrators, N.R.Developers)]
        public static IEnumerable<string> MissingExplicitEntriesOfVisitorComparedToEveryone(Content root)
        {
            var result = new List<string>();
            foreach (var node in NodeEnumerator.GetNodes(root.Path))
            {
                var hasEveryoneEntry = false;
                var hasVisitorEntry = false;
                foreach (var entry in node.Security.GetExplicitEntries(EntryType.Normal))
                {
                    if (entry.IdentityId == Identifiers.EveryoneGroupId)
                        hasEveryoneEntry = true;
                    if (entry.IdentityId == Identifiers.VisitorUserId)
                        hasVisitorEntry = true;
                }
                if (hasEveryoneEntry && !hasVisitorEntry)
                    result.Add(node.Path);
            }
            return result;
        }

        /// <summary>
        /// Returns the requested content's ancestor chain. The first element is the parent,
        /// the last is the Root or the closest permitted content towards the Root.
        /// </summary>
        /// <param name="content"></param>
        /// <returns>Content list of the ancestors of the requested content.</returns>
        [ODataFunction(Category = "Tools")]
        [ContentTypes(N.CT.GenericContent, N.CT.ContentType)]
        [AllowedRoles(N.R.Everyone, N.R.Visitor)]
        public static IEnumerable<Content> Ancestors(Content content)
        {
            var ancestors = new List<Content>();
            try
            {
                // parent walk
                var ancestor = content;
                while ((ancestor = Content.Load(ancestor.ContentHandler.ParentId)) != null)
                    ancestors.Add(ancestor);
            }
            catch (SenseNetSecurityException)
            {
                // This is not a real error: just stop the parent walk 
                // when a user does not have permission to an ancestor.
            }
            return ancestors;
        }

        /// <summary>
        /// Copies all explicit permission entries of the Everyone group for the Visitor user.
        /// The copy operation is executed on all content in the subtree of the requested content
        /// that are not in the <paramref name="exceptList"/>.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="exceptList">White list of untouched Contents.</param>
        /// <returns><c>Ok</c> if the operation is successfully executed.</returns>
        [ODataAction(Category = "Security")]
        [AllowedRoles(N.R.Administrators, N.R.Developers)]
        public static string CopyExplicitEntriesOfEveryoneToVisitor(Content root, string[] exceptList)
        {
            var visitorId = User.Visitor.Id;
            var everyoneId = Group.Everyone.Id;
            var except = exceptList.Select(p => p.ToLower()).ToList();
            var ctx = Providers.Instance.SecurityHandler.SecurityContext;
            var aclEd = Providers.Instance.SecurityHandler.CreateAclEditor(ctx);
            foreach (var path in MissingExplicitEntriesOfVisitorComparedToEveryone(root))
            {
                if (!except.Contains(path.ToLower()))
                {
                    var node = Node.LoadNode(path);
                    var aces = ctx.GetExplicitEntries(node.Id, new[] { everyoneId });
                    foreach (var ace in aces)
                    {
                        aclEd.Set(node.Id, visitorId, ace.LocalOnly, ace.AllowBits, ace.DenyBits);
                    }
                }
            }
            aclEd.ApplyAsync(CancellationToken.None).GetAwaiter().GetResult();
            return "Ok";
        }

        // Index backup =========================================================================

        /// <summary>
        /// Takes a snapshot of the index and copies it to the given target.
        /// The target is typically a directory in the filesystem.
        /// The backup is an exclusive operation that can be started only once.
        /// </summary>
        /// <remarks>
        /// The response contains a state and the current backup descriptor. The history is always null.
        /// 
        /// An example if the backup is started successfully:
        /// <code>
        /// {
        ///   "State": "Started",
        ///   "Current": {
        ///     "StartedAt": "0001-01-01T00:00:00",
        ///     "FinishedAt": "0001-01-01T00:00:00",
        ///     "TotalBytes": 0,
        ///     "CopiedBytes": 0,
        ///     "CountOfFiles": 0,
        ///     "CopiedFiles": 0,
        ///     "CurrentlyCopiedFile": null,
        ///     "Message": null
        ///   },
        ///   "History": null
        /// }
        /// </code>
        /// Another example if the backup is already executing:
        /// <code>
        /// {
        ///   "State": "Executing",
        ///   "Current": {
        ///     "StartedAt": "2020-08-26T22:46:29.4516539Z",
        ///     "FinishedAt": "0001-01-01T00:00:00",
        ///     "TotalBytes": 126,
        ///     "CopiedBytes": 42,
        ///     "CountOfFiles": 3,
        ///     "CopiedFiles": 1,
        ///     "CurrentlyCopiedFile": "File2",
        ///     "Message": null
        ///   },
        ///   "History": null
        /// }
        /// </code>
        /// </remarks>
        /// <param name="content"></param>
        /// <param name="target">Target of the copy operation.</param>
        /// <returns>A Task that represents the asynchronous operation and wraps the <see cref="BackupResponse"/>.
        /// </returns>
        [ODataAction(Category = "Indexing")]
        [ContentTypes(N.CT.PortalRoot)]
        [AllowedRoles(N.R.Administrators, N.R.Developers)]
        public static async STT.Task<BackupResponse> BackupIndex(Content content, string target = null)
        {
            var engine = Providers.Instance.SearchEngine.IndexingEngine;
            var response = await engine.BackupAsync(target, CancellationToken.None)
                .ConfigureAwait(false);
            return response;
        }

        /// <summary>
        /// Queries the index backup state in the system.
        /// </summary>
        /// <remarks>
        /// The response contains a state, the current backup descriptor (if the backup is running) and a history of
        /// backup operations that happened since the application has started.
        /// For example:
        /// <code>
        /// {
        ///   "State": "Executing",
        ///   "Current": {
        ///     "StartedAt": "2020-08-26T22:46:29.4516539Z",
        ///     "FinishedAt": "0001-01-01T00:00:00",
        ///     "TotalBytes": 126,
        ///     "CopiedBytes": 42,
        ///     "CountOfFiles": 3,
        ///     "CopiedFiles": 1,
        ///     "CurrentlyCopiedFile": "File2",
        ///     "Message": null
        ///   },
        ///   "History": []
        /// }
        /// </code>
        /// The available states:
        /// 
        /// | State     | Description                                                 |
        /// | --------- | ----------------------------------------------------------- |
        /// | Initial   | there has been no backup since the application was launched |
        /// | Executing | the backup is currently running                             |
        /// | Canceled  | the last backup operation was canceled without error        |
        /// | Faulted   | an error occured during the last backup operation           |
        /// | Finished  | the last backup is successfully finished                    |
        /// </remarks>
        /// <param name="content"></param>
        /// <returns>A Task that represents the asynchronous operation and wraps the <see cref="BackupResponse"/>.</returns>
        [ODataFunction(Category = "Indexing")]
        [ContentTypes(N.CT.PortalRoot)]
        [AllowedRoles(N.R.Administrators, N.R.Developers)]
        public static async STT.Task<BackupResponse> QueryIndexBackup(Content content)
        {
            var engine = Providers.Instance.SearchEngine.IndexingEngine;
            var response = await engine.QueryBackupAsync(CancellationToken.None)
                .ConfigureAwait(false);
            return response;
        }

        /// <summary>
        /// Requests the termination of the currently running backup operation.
        /// </summary>
        /// <remarks>
        /// The response contains a state, the current backup descriptor (if the backup is running) and a history of
        /// backup operations that happened since the application has started.
        /// For example:
        /// <code>
        /// {
        ///   "State": "CancelRequested",
        ///   "Current": {
        ///     "StartedAt": "2020-08-26T22:46:29.4516539Z",
        ///     "FinishedAt": "0001-01-01T00:00:00",
        ///     "TotalBytes": 126,
        ///     "CopiedBytes": 42,
        ///     "CountOfFiles": 3,
        ///     "CopiedFiles": 1,
        ///     "CurrentlyCopiedFile": "File2",
        ///     "Message": null
        ///   },
        ///   "History": []
        /// }
        /// </code>
        /// </remarks>
        /// <param name="content"></param>
        /// <returns>A Task that represents the asynchronous operation and wraps the <see cref="BackupResponse"/>.</returns>
        [ODataAction(Category = "Indexing")]
        [ContentTypes(N.CT.PortalRoot)]
        [AllowedRoles(N.R.Administrators, N.R.Developers)]
        public static async STT.Task<BackupResponse> CancelIndexBackup(Content content)
        {
            var engine = Providers.Instance.SearchEngine.IndexingEngine;
            var response = await engine.CancelBackupAsync(CancellationToken.None)
                .ConfigureAwait(false);
            return response;
        }

        // Save Index =========================================================================

        /// <summary>
        /// Gets summary information about the index.
        /// Contains the activity status, field info and a versionId list.
        /// Useful in debugging scenarios.
        /// </summary>
        /// <remarks>
        /// A shortened example:
        /// <code>
        /// {
        ///   "IndexingActivityStatus": {
        ///     "LastActivityId": 194,
        ///     "Gaps": []
        ///     },
        ///   "FieldInfo": {
        ///     "_Text": 948,
        ///     "ActionTypeName": 1,
        ///     /*....*/
        ///     "Width": 8,
        ///     "Workspace": 11,
        ///     "WorkspaceSkin": 1
        ///   },
        ///   "VersionIds": [ 1, 2, /*....*/ 157, 158, 163 ]
        ///}
        /// </code>
        /// <para>The properties are:
        /// - IndexingActivityStatus: information about the progress of the indexing process (local index only).
        /// - FieldInfo: sorted list of indexed fields. Every item is a key-value pair with the field name and the count of terms.
        /// - VersionIds: sorted list of all indexed versionIds. Note that the versionId is the primary key of index documents.
        /// </para>
        /// </remarks>
        /// <param name="content"></param>
        /// <returns></returns>
        [ODataFunction(Category = "Indexing")]
        [ContentTypes(N.CT.PortalRoot)]
        [AllowedRoles(N.R.Administrators, N.R.PublicAdministrators, N.R.Developers)]
        public static IndexProperties GetIndexProperties(Content content)
        {
            var engine = Providers.Instance.SearchEngine.IndexingEngine;
            var response = engine.GetIndexProperties();
            return response;
        }

		/// <summary>
		/// Shows the whole inverted index in a raw format with some transformations for easier readability.
		/// WARNING! The index may contain sensitive information.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Note that some index providers do not support this feature because of the size of the index.
		/// </para>
		/// <para>
		/// The response does not appear all at once because it is generated using a streaming technique.
		/// This may affect browser add-ons (e.g. json validator or formatter, etc.).
		/// </para>
		/// An annotated example:
		/// <code>
		/// {
		///   "ActionTypeName": {          // level-1: field
		///     "clientaction":            // level-2: term
		///         "0 1 2 3 7 12 19 ..."  // level-3: sorted documentId list as a single string
		///   },
		///   /* ... */
		///   "Description": {
		///     /* ... */
		///     "browser": "147",
		///     "calendarevent": "132",
		///     "can": "143 144 147 148 151 152",
		///     "case": "143",
		///     /* ... */
		/// </code>
		/// </remarks>
		/// <param name="content"></param>
		/// <param name="httpContext"></param>
		/// <returns>The whole raw index.</returns>
		[ODataFunction(Category = "Indexing")]
        [ContentTypes(N.CT.PortalRoot)]
        [AllowedRoles(N.R.Administrators, N.R.PublicAdministrators, N.R.Developers)]
        public static async STT.Task GetWholeInvertedIndex(Content content, HttpContext httpContext)
        {
            var httpResponse = httpContext.Response;
            STT.Task WriteAsync(string text)
            {
                return httpResponse.WriteAsync(text, Encoding.UTF8, httpContext.RequestAborted);
            }

            var engine = Providers.Instance.SearchEngine.IndexingEngine;
            var response = await engine.GetInvertedIndexAsync(httpContext.RequestAborted);
            
            using (var op = SnTrace.System.StartOperation("GetWholeInvertedIndex"))
            {
                httpContext.Response.ContentType = "application/json;odata=verbose;charset=utf-8";

                await httpContext.Response.WriteAsync("{");
                var fieldLines = 0;
                foreach (var fieldData in response)
                {
                    var fieldLine = "  \"" + fieldData.Key + "\": {";
                    if (fieldLines++ == 0)
                        await WriteAsync("\n" + fieldLine);
                    else
                        await WriteAsync(",\n" + fieldLine);

                    var termLines = 0;
                    foreach (var termData in fieldData.Value)
                    {
                        var termLine = "    \"" +
                                       termData.Key.Replace("\\", "\\\\")
                                           .Replace("\"", "\\\"") + "\": \"" +
                                       string.Join(" ", termData.Value.Select(x => x.ToString())) + "\"";
                        if (termLines++ == 0)
                            await WriteAsync("\n" + termLine);
                        else
                            await WriteAsync(",\n" + termLine);
                    }
                    await WriteAsync("\n  }");
                }
                await WriteAsync("\n}");

                op.Successful = true;
            }
        }


        /// <summary>
        /// Shows the inverted index of the requested field in a raw format with some transformations for easier readability.
        /// WARNING! The index may contain sensitive information.
        /// </summary>
        /// <remarks>
        /// A shortened example where the fieldName is "Description":
        /// <code>
        /// {
        ///   /* ... */
        ///   "browser": "147",
        ///   "calendarevent": "132",
        ///   "can": "143 144 147 148 151 152",
        ///   "case": "143",
        ///   /* ... */
        /// </code>
        /// </remarks>
        /// <param name="content"></param>
        /// <param name="httpContext"></param>
        /// <param name="fieldName">The field name that identifies the requested sub-index.</param>
        /// <returns>Key-value pairs of the term and a sorted documentId list.</returns>
        [ODataFunction(Category = "Indexing")]
        [ContentTypes(N.CT.PortalRoot)]
        [AllowedRoles(N.R.Administrators, N.R.PublicAdministrators, N.R.Developers)]
        public static async STT.Task<IDictionary<string, object>> GetInvertedIndex(Content content, HttpContext httpContext, string fieldName)
        {
            var engine = Providers.Instance.SearchEngine.IndexingEngine;
            var response = await engine.GetInvertedIndexAsync(fieldName, httpContext.RequestAborted);
            if (response == null || response.Count == 0)
                return EmptyInvertedIndex;
            return response.ToDictionary(x => x.Key, x => (object)string.Join(",", x.Value.Select(y => y.ToString())));
        }

        private static readonly IDictionary<string, object> EmptyInvertedIndex = new Dictionary<string, object> {{"", ""}};

        /// <summary>
        /// Gets the index document (not-inverted index) of the current version of the requested resource.
        /// WARNING! The index may contain sensitive information.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The version of the requested resource depends on the logged in user's permissions but can be tailored by the
        /// general parameter "version".
        /// This parameter format is ((['V'|'v'])?[majornumber][.][minornumber]([.] [*]+)?)|'lastmajor'|'lastminor'
        /// Valid examples: V1.0, 2.3, v12.3456, lastmajor, lastminor
        /// Note that the logged-in user needs enough permission for the requested version that can be
        /// Open, OpenMinor, RecallOldVersions.
        /// </para>
        /// <para>
        /// The response contains key-value pairs where the key is the field name and the value is a list of ordered term values.
        /// A shortened example:
        /// </para>
        /// <code>
        /// {
        ///   /* ... */
        ///   "CreatedBy": "12",
        ///   "CreatedById": "12",
        ///   "CreationDate": "2022-07-20 05:59",
        ///   "Depth": "3",
        ///   "Description": "behavior, can, case, customize, different, example, extractor, file, indexing, settings, system, text, types, used, you",
        ///   "DisplayName": "indexing.settings",
        ///   "EnableLifespan": "no",
        ///   /* ... */
        /// </code>
        /// Note that the original text cannot be reproduced from the term values in some cases.
        /// </remarks>
        /// <param name="content"></param>
        /// <param name="versionId">Optional versionId if it is different from the versionId of the requested resource.</param>
        [ODataFunction(Category = "Indexing")]
        [ContentTypes(N.CT.GenericContent, N.CT.ContentType)]
        [AllowedRoles(N.R.Administrators, N.R.PublicAdministrators, N.R.Developers)]
        public static IDictionary<string, object> GetIndexDocument(Content content, int versionId = 0)
        {
            if (versionId == 0)
                versionId = content.ContentHandler.VersionId;

            var engine = Providers.Instance.SearchEngine.IndexingEngine;
            IDictionary<string, string> response = null;
            try
            {
                response = engine.GetIndexDocumentByVersionId(versionId);
            }
            catch
            {
                return new Dictionary<string, object>();
            }

            if (response == null)
                return new Dictionary<string, object>();

            return response
                .OrderBy(x => x.Key)
                .ToDictionary(x => x.Key, x => (object)x.Value);
        }

        /// <summary>
        /// Gets the index document (not-inverted index) of the requested documentId.
        /// WARNING! The index may contain sensitive information.
        /// </summary>
        /// <remarks>
        /// The documentId depends on the index provider and comes from the inverted index
        /// (see the <see cref="GetInvertedIndex"/> function).
        /// The response contains key-value pairs where the key is the field name and the value is a list of ordered term values.
        /// A shortened example:
        /// <code>
        /// {
        ///   /* ... */
        ///   "CreatedBy": "12",
        ///   "CreatedById": "12",
        ///   "CreationDate": "2022-07-20 05:59",
        ///   "Depth": "3",
        ///   "Description": "behavior, can, case, customize, different, example, extractor, file, indexing, settings, system, text, types, used, you",
        ///   "DisplayName": "indexing.settings",
        ///   "EnableLifespan": "no",
        ///   /* ... */
        /// </code>
        /// Note that the original text cannot be reproduced from the term values in some cases.
        /// </remarks>
        /// <param name="content"></param>
        /// <param name="documentId">The documentId from the inverted index.</param>
        [ODataFunction(Category = "Indexing")]
        [ContentTypes(N.CT.PortalRoot)]
        [AllowedRoles(N.R.Administrators, N.R.PublicAdministrators, N.R.Developers)]
        public static IDictionary<string, object> GetIndexDocumentByDocumentId(Content content, int documentId)
        {
            var engine = Providers.Instance.SearchEngine.IndexingEngine;
            IDictionary<string, string> response = null;
            try
            {
                response = engine.GetIndexDocumentByDocumentId(documentId);
            }
            catch
            {
                return new Dictionary<string, object>();
            }

            if (response == null)
                return new Dictionary<string, object>();

            return response
                .OrderBy(x => x.Key)
                .ToDictionary(x => x.Key, x => (object)x.Value);
        }

		// ======================================================================================

		/// <summary>
		/// Returns a JSON object that contains a resource class with the given language.
		/// </summary>
		/// <param name="content"></param>
		/// <param name="className">Name of existing resource class</param>
		/// <param name="langCode">Two character identifier of the culture</param>
		/// <exception cref="ApplicationException">Exception thrown when there's no resouce data 
		/// with the given className or langCode.</exception>
		[ODataFunction(Category = "Tools")]
		[ContentTypes(N.CT.PortalRoot)]
		[AllowedRoles(N.R.Everyone, N.R.Visitor)]
		public static Dictionary<string, object> GetResourceClass(Content content, string className, string langCode)
		{
			var localizationData = SenseNetResourceManager.Current.GetClassItems(className, new CultureInfo(langCode));

			if (localizationData == null)
				throw new ApplicationException($"Localization resource with classname \'{className}\' and language \'{langCode}\' not found");
			else
				return localizationData;
		}

		/// <summary>
		/// Sets the provided <paramref name="userOrGroup"/> as the owner of the requested content.
		/// If the <paramref name="userOrGroup"/> is null, the current user will be the owner.
		/// The operation requires <c>TakeOwnership</c> permission.
		/// </summary>
		/// <param name="content"></param>
		/// <param name="userOrGroup">Path or id of the desired owner.</param>
		/// <exception cref="ArgumentException">Thrown if the <paramref name="userOrGroup"/> parameter cannot be recognized
		/// as a path or id. The method also throws this exception if the identified content is not a User or a Group.</exception>
		[ODataAction(OperationName = "TakeOwnership", Category = "Permissions")]
        [AllowedRoles(N.R.Everyone)]
        [RequiredPermissions(N.P.TakeOwnership)]
        public static async System.Threading.Tasks.Task TakeOwnershipAsync(Content content, HttpContext httpContext, string userOrGroup)
        {
            if (content == null)
                throw new ArgumentNullException("content");

            Content target = null;
            if (!String.IsNullOrEmpty(userOrGroup))
            {
                target = Content.LoadByIdOrPath(userOrGroup);
                if (target == null)
                    throw new ArgumentException("The parameter cannot be recognized as a path or an Id: " + userOrGroup);
            }

            if (Providers.Instance.SecurityHandler.HasPermission(content.Id, PermissionType.TakeOwnership))
            {
                if (target == null)
                {
                    // if the input string was null or empty
                    content["Owner"] = User.Current;
                }
                else
                {
                    if (target.ContentHandler is Group)
                        content["Owner"] = target.ContentHandler as Group;
                    else if (target.ContentHandler is User)
                        content["Owner"] = target.ContentHandler as User;
                    else
                        throw new ArgumentException("The parameter cannot be recognized as a User or a Group: " + userOrGroup);
                }

                await content.SaveAsync(httpContext.RequestAborted).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Transfers the lock on the requested content to the target <paramref name="user"/>.
        /// If the target <paramref name="user"/> is null, the target will be the current user.
        /// Current user must have <c>ForceCheckin</c> permission on the requested content.
        /// </summary>
        /// <param name="content"></param>
        /// <param name="user">Path or id of the desired lock owner User.</param>
        /// <returns><c>Ok</c> if the operation is executed successfully.</returns>
        /// <exception cref="ArgumentException">Thrown if the content is not checked out (unlocked).
        /// Also thrown if the <paramref name="user"/> cannot be recognized as a path or id of an existing
        /// <c>User</c>.</exception>
        [ODataAction(Category = "Permissions")]
        [AllowedRoles(N.R.Everyone)]
        [RequiredPermissions(N.P.ForceCheckin)]
        public static string TakeLockOver(Content content, string user)
        {
            content.ContentHandler.Lock.TakeLockOver(GetUserFromString(user));

            return "Ok";
        }

        private static User GetUserFromString(string user)
        {
            User targetUser = null;
            if (!String.IsNullOrEmpty(user))
            {
                int userId;
                if (Int32.TryParse(user, out userId))
                    targetUser = Node.LoadNode(userId) as User;
                else
                    if (RepositoryPath.IsValidPath(user) == RepositoryPath.PathResult.Correct)
                        targetUser = Node.LoadNode(user) as User;
                else
                    throw new ArgumentException("The 'user' parameter cannot be recognized as a path or an Id: " + user);
                if (targetUser == null)
                    throw new ArgumentException("User not found by the parameter: " + user);
            }
            return targetUser;
        }

        public static class OData
        {
            public static string CreateSingleContentUrl(Content content, string operationName = null)
            {
                return string.Format("/" + Configuration.Services.ODataServiceToken + "{0}('{1}'){2}",
                    content.ContentHandler.ParentPath,
                    content.Name,
                    string.IsNullOrEmpty(operationName) ? string.Empty : RepositoryPath.PathSeparator + operationName);
            }
        }

        /// <summary>
        /// A developer tool that returns an object that contains information about the execution of the last
        /// few security activities.
        /// </summary>
        /// <remarks>
        /// Example response (truncated):
        /// <code>
        /// {
        ///   "State": {
        ///     "Serializer": {
        ///       "LastQueued": 154,
        ///       "QueueLength": 0,
        ///       "Queue": []
        ///     },
        ///     "DependencyManager": {
        ///       "WaitingSetLength": 0,
        ///       "WaitingSet": []
        ///     },
        ///     "Termination": {
        ///       "LastActivityId": 154,
        ///       "GapsLength": 0,
        ///       "Gaps": []
        ///     }
        ///   },
        ///   "Message": null,
        ///   "RecentLength": 154,
        ///   "Recent": [
        ///     {
        ///       "Id": 1,
        ///       "TypeName": "CreateSecurityEntityActivity",
        ///       "FromReceiver": false,
        ///       "FromDb": false,
        ///       "IsStartup": false,
        ///       "Error": null,
        ///       "WaitedFor": null,
        ///       "ArrivedAt": "2020-08-27T08:46:16.0132362Z",
        ///       "StartedAt": "2020-08-27T08:46:16.0150841Z",
        ///       "FinishedAt": "2020-08-27T08:46:16.0294322Z",
        ///       "WaitTime": "00:00:00.0018479",
        ///       "ExecTime": "00:00:00.0143481",
        ///       "FullTime": "00:00:00.0161960"
        ///     },
        ///     {
        ///       "Id": 2,
        ///       "TypeName": "CreateSecurityEntityActivity",
        ///       "FromReceiver": false,
        ///       "FromDb": false,
        ///       "IsStartup": false,
        ///       "Error": null,
        ///       "WaitedFor": [
        ///         1
        ///       ],
        ///       "ArrivedAt": "2020-08-27T08:46:16.019736Z",
        ///       "StartedAt": "2020-08-27T08:46:16.0300987Z",
        ///       "FinishedAt": "2020-08-27T08:46:16.031381Z",
        ///       "WaitTime": "00:00:00.0103627",
        ///       "ExecTime": "00:00:00.0012823",
        ///       "FullTime": "00:00:00.0116450"
        ///     }
        ///     ...
        /// </code>
        /// </remarks>
        /// <param name="content"></param>
        /// <returns>A <see cref="SenseNet.Security.Messaging.SecurityActivityHistory"/> instance.</returns>
        [ODataFunction(Category = "Security")]
        [ContentTypes(N.CT.PortalRoot)]
        [AllowedRoles(N.R.Administrators, N.R.PublicAdministrators, N.R.Developers)]
        public static SenseNet.Security.Messaging.SecurityActivityHistory GetRecentSecurityActivities(Content content)
        {
            return Providers.Instance.SecurityHandler.SecurityContext.GetRecentActivities();
        }

        /// <summary>
        /// A developer tool that returns an object that contains information about the execution of the last
        /// few indexing activities.
        /// </summary>
        /// <remarks>
        /// A possible response:
        /// <code>
        /// {
        ///   "State": {
        ///     "Serializer": {
        ///       "LastQueued": 1,
        ///       "QueueLength": 0,
        ///       "Queue": []
        ///     },
        ///     "DependencyManager": {
        ///       "WaitingSetLength": 0,
        ///       "WaitingSet": []
        ///     },
        ///     "Termination": {
        ///       "LastActivityId": 1,
        ///       "Gaps": []
        ///     }
        ///   },
        ///   "Message": null,
        ///   "RecentLength": 1,
        ///   "Recent": [
        ///     {
        ///       "Id": 1,
        ///       "TypeName": "AddDocument",
        ///       "FromReceiver": false,
        ///       "FromDb": false,
        ///       "IsStartup": false,
        ///       "Error": null,
        ///       "WaitedFor": null,
        ///       "ArrivedAt": "2020-08-27T08:46:16.3838978Z",
        ///       "StartedAt": "2020-08-27T08:46:16.3855456Z",
        ///       "FinishedAt": "2020-08-27T08:46:16.3969588Z",
        ///       "WaitTime": "00:00:00.0016478",
        ///       "ExecTime": "00:00:00.0114132",
        ///       "FullTime": "00:00:00.0130610"
        ///     }
        ///   ]
        /// }
        /// </code>
        /// </remarks>
        /// <param name="content"></param>
        /// <returns>An <see cref="IndexingActivityHistory"/> instance.</returns>
        [ODataFunction(Category = "Indexing")]
        [ContentTypes(N.CT.PortalRoot)]
        [AllowedRoles(N.R.Administrators, N.R.PublicAdministrators, N.R.Developers)]
        public static IndexingActivityHistory GetRecentIndexingActivities(Content content)
        {
            return ((IndexManager)Providers.Instance.IndexManager).DistributedIndexingActivityQueue.GetIndexingActivityHistory();
        }

        /// <summary>
        /// A developer tool that resets the indexing activity history.
        /// WARNING: Do not use it in a production environment.
        /// </summary>
        /// <remarks>
        /// A possible response:
        /// <code>
        /// {
        ///   "State": {
        ///     "Serializer": {
        ///       "LastQueued": 0,
        ///       "QueueLength": 0,
        ///       "Queue": []
        ///     },
        ///     "DependencyManager": {
        ///       "WaitingSetLength": 0,
        ///       "WaitingSet": []
        ///     },
        ///     "Termination": {
        ///       "LastActivityId": 0,
        ///       "Gaps": []
        ///     }
        ///   },
        ///   "Message": null,
        ///   "RecentLength": 0,
        ///   "Recent": []
        /// }
        /// </code>
        /// </remarks>
        /// <param name="content"></param>
        /// <returns>An <see cref="IndexingActivityHistory"/> instance.</returns>
        [ODataAction(Category = "Indexing")]
        [ContentTypes(N.CT.PortalRoot)]
        [AllowedRoles(N.R.Administrators, N.R.Developers)]
        public static IndexingActivityHistory ResetRecentIndexingActivities(Content content)
        {
            return ((IndexManager)Providers.Instance.IndexManager).DistributedIndexingActivityQueue.ResetIndexingActivityHistory();
        }

        /// <summary>
        /// DEPRECATED. Checking index integrity online is not supported anymore. Use an offline solution instead.
        /// </summary>
        /// <param name="recurse">Irrelevant because throws SnNotSupportedException.</param>
        /// <returns>Throws SnNotSupportedException.</returns>
        /// <exception cref="SnNotSupportedException"></exception>
        [Obsolete("Use an offline solution instead.", true)]
        [ODataFunction(Category = "Deprecated")]
        public static object CheckIndexIntegrity(Content content, bool recurse)
        {
            throw new SnNotSupportedException("Checking index integrity online is not supported anymore.");
        }

        /// <summary>
        /// Checks the security consistency in the subtree of the requested content.
        /// WARNING! The operation can be slow so use it only in justified cases and with a scope as small as possible.
        /// </summary>
        /// <remarks>Compares the security cache and the main database. the investigation covers the
        /// parallelism of the entity vs content structure, membership vs content-references,
        /// and entity-identity existence in security entries. If the security data is consistent,
        /// the response is the following (the comments are not part of the response):
        /// <code>
        /// {
        ///   "IsConsistent": true,                    // aggregated all categories validity
        ///   "IsMembershipConsistent": true,          // aggregated membership category validity
        ///   "IsEntityStructureConsistent": true,     // aggregated entity structure category validity
        ///   "IsAcesConsistent": true,                // aggregated ACE category validity
        ///   "ElapsedTime": "00:00:00.0087222",       // time of investigation
        ///   "MissingEntitiesFromRepository": [],     // entity structure category (SecurityEntityInfo[])
        ///   "MissingEntitiesFromSecurityDb": [],     // entity structure category (SecurityEntityInfo[])
        ///   "MissingEntitiesFromSecurityCache": [],  // entity structure category (SecurityEntityInfo[])
        ///   "MissingMembershipsFromCache": [],       // membership category (SecurityMembershipInfo[])
        ///   "UnknownMembershipInSecurityDb": [],     // membership category (SecurityMembershipInfo[])
        ///   "MissingMembershipsFromSecurityDb": [],  // membership category (SecurityMembershipInfo[])
        ///   "UnknownMembershipInCache": [],          // membership category (SecurityMembershipInfo[])
        ///   "MissingRelationFromFlattenedUsers": [], // membership category (SecurityMembershipInfo[])
        ///   "UnknownRelationInFlattenedUsers": [],   // membership category (SecurityMembershipInfo[])
        ///   "InvalidACE_MissingEntity": [],          // ACE category (StoredAceDebugInfo[])
        ///   "InvalidACE_MissingIdentity": []         // ACE category (StoredAceDebugInfo[])
        /// }
        /// </code>
        /// In case of invalid state, at least one of the categories contains one or more sub-items.
        /// Here is an example for every item type.
        /// <para>
        /// <c>SecurityEntityInfo</c> example (an item in MissingEntitiesFromRepository, MissingEntitiesFromSecurityDb, MissingEntitiesFromSecurityCache)
        /// <code>
        /// {
        ///   "Id": 1,
        ///   "ParentId": 5,
        ///   "OwnerId": 1,
        ///   "Path": "/Root/IMS/BuiltIn/Portal/Admin"
        /// }
        /// </code>
        /// </para>
        /// <para>
        /// <c>SecurityMembershipInfo</c> example (an item in MissingMembershipsFromCache, UnknownMembershipInSecurityDb, MissingMembershipsFromSecurityDb, UnknownMembershipInCache, MissingRelationFromFlattenedUsers, UnknownRelationInFlattenedUsers)
        /// <code>
        /// {
        ///   "GroupId": 5,
        ///   "MemberId": 9,
        ///   "GroupPath": "/Root/IMS/BuiltIn/Portal",
        ///   "MemberPath": "/Root/IMS/BuiltIn/Portal/Owners"
        /// }
        /// </code>
        /// </para>
        /// <para>
        /// <c>StoredAceDebugInfo</c> example (an item in InvalidACE_MissingEntity, InvalidACE_MissingIdentity)
        /// <code>
        /// {
        ///   "EntityId": 2,
        ///   "IdentityId": 7,
        ///   "LocalOnly": false,
        ///   "AllowBits": 524287,
        ///   "DenyBits": 0,
        ///   "StringView": "(2)|Normal|+(7):_____________________________________________+++++++++++++++++++"
        /// }
        /// </code>
        /// </para>
        /// </remarks>
        /// <param name="content"></param>
        /// <returns>The SecurityConsistencyResult instance.</returns>
        [ODataFunction(Category = "Security")]
        [ContentTypes(N.CT.GenericContent, N.CT.ContentType)]
        [AllowedRoles(N.R.Administrators, N.R.Developers)]
        public static SecurityConsistencyResult CheckSecurityConsistency(Content content)
        {
            var groups = NodeQuery.QueryNodesByType(NodeType.GetByName("Group"), false).Identifiers;
            var ous = NodeQuery.QueryNodesByType(NodeType.GetByName("OrganizationalUnit"), false).Identifiers;
            var allGroups = groups.Union(ous).ToArray();
            var allIds = NodeQuery.QueryNodesByPath("/", false).Identifiers;

            return CheckSecurityConsistency(allIds, allGroups);
        }

        /// <summary>
        /// Slow method for mapping all the possible inconsistencies in the 
        /// repository and the security component's stored and cached values.
        /// </summary>
        /// <param name="contentIds">List of all content ids in the repository.</param>
        /// <param name="groupIds">List of all the security containers in the repository. It will be enumerated once.</param>
        private static SecurityConsistencyResult CheckSecurityConsistency(IEnumerable<int> contentIds, IEnumerable<int> groupIds)
        {
            var result = new SecurityConsistencyResult();
            result.StartTimer();

            var secCachedEntities = Providers.Instance.SecurityHandler.GetCachedEntities();

            CheckSecurityEntityConsistency(contentIds, secCachedEntities, result);
            CheckMembershipConsistency(groupIds, result);
            CheckAceConsistency(result, secCachedEntities);

            result.StopTimer();

            return result;
        }
        private static void CheckSecurityEntityConsistency(IEnumerable<int> contentIds, IDictionary<int, SecurityEntity> secCachedEntities, SecurityConsistencyResult result)
        {
            var secDbEntities = Providers.Instance.SecurityHandler
                .SecurityContext.SecuritySystem.DataProvider.LoadSecurityEntities().ToList(); // convert to list, because we will modify this collection
            var foundEntities = new List<StoredSecurityEntity>();

            foreach (var contentId in contentIds)
            {
                var nh = NodeHead.Get(contentId);

                // content exists in the index but not in the db (deleted manually from the db)
                if (nh == null)
                {
                    result.AddMissingEntityFromRepository(contentId);
                    continue;
                }

                var secEntity = secDbEntities.FirstOrDefault(se => se.Id == contentId);
                if (secEntity == null || secEntity.ParentId != nh.ParentId || secEntity.OwnerId != nh.OwnerId)
                {
                    // not found in the security db, or found it but with different properties
                    result.AddMissingEntityFromSecurityDb(nh);
                    continue;
                }

                // move correctly found entities to a temp list
                foundEntities.Add(secEntity);
                secDbEntities.Remove(secEntity);
            }

            // the remaining ones are not in SN repo
            foreach (var secEntity in secDbEntities)
            {
                result.AddMissingEntityFromRepository(secEntity.Id);
            }

            // find entities that are in db but not in memory
            foreach (var secDbEntityId in secDbEntities.Concat(foundEntities).Select(dbe => dbe.Id).Except(secCachedEntities.Keys))
            {
                result.AddMissingEntityFromSecurityCache(secDbEntityId);
            }

            // find entities that are in memory but not in db
            foreach (var cachedEntityId in secCachedEntities.Keys.Except(secDbEntities.Concat(foundEntities).Select(dbe => dbe.Id)))
            {
                result.AddMissingEntityFromSecurityDb(cachedEntityId);
            }
        }
        private static void CheckMembershipConsistency(IEnumerable<int> groupIds, SecurityConsistencyResult result)
        {
            var secuCache = Providers.Instance.SecurityHandler
                .SecurityContext.GetCachedMembershipForConsistencyCheck();
            var secuDb = Providers.Instance.SecurityHandler
                .SecurityContext.SecuritySystem.DataProvider.GetMembershipForConsistencyCheck();

            var repo = new List<long>();
            foreach (var head in groupIds.Select(NodeHead.Get).Where(h => h != null))
            {
                var groupIdBase = Convert.ToInt64(head.Id) << 32;
                var userMembers = new List<int>();
                var groupMembers = new List<int>();

                CollectSecurityIdentityChildren(head, userMembers, groupMembers);

                foreach (var userId in userMembers)
                    repo.Add(groupIdBase + userId);

                foreach (var groupId in groupMembers)
                    repo.Add(groupIdBase + groupId);
            }

            // ---------------------------------------------------------

            var missingInSecuCache = repo.Except(secuCache);
            foreach (var relation in missingInSecuCache)
                result.AddMissingMembershipFromCache(unchecked((int)(relation >> 32)), unchecked((int)(relation & 0xFFFFFFFF)));

            var missingInSecuDb = repo.Except(secuDb);
            foreach (var relation in missingInSecuDb)
                result.AddMissingMembershipFromSecurityDb(unchecked((int)(relation >> 32)), unchecked((int)(relation & 0xFFFFFFFF)));

            var unknownInSecuCache = secuCache.Except(repo);
            foreach (var relation in unknownInSecuCache)
                result.AddUnknownMembershipInCache(unchecked((int)(relation >> 32)), unchecked((int)(relation & 0xFFFFFFFF)));

            var unknownInSecuDb = secuDb.Except(repo);
            foreach (var relation in unknownInSecuDb)
                result.AddUnknownMembershipInSecurityDb(unchecked((int)(relation >> 32)), unchecked((int)(relation & 0xFFFFFFFF)));

            // ---------------------------------------------------------

            IEnumerable<long> missingInFlattening, unknownInFlattening;
            Providers.Instance.SecurityHandler.SecurityContext
                .GetFlatteningForConsistencyCheck(out missingInFlattening, out unknownInFlattening);

            foreach (var relation in missingInFlattening)
                result.AddMissingRelationFromFlattenedUsers(unchecked((int)(relation >> 32)), unchecked((int)(relation & 0xFFFFFFFF)));

            foreach (var relation in unknownInFlattening)
                result.AddMissingRelationFromFlattenedUsers(unchecked((int)(relation >> 32)), unchecked((int)(relation & 0xFFFFFFFF)));
        }
        private static void CollectSecurityIdentityChildren(NodeHead head, ICollection<int> userIds, ICollection<int> groupIds)
        {
            // collect physical children (applies for orgunits)
            foreach (var childHead in ContentQuery.QueryAsync(SafeQueries.InFolder, QuerySettings.AdminSettings,
                             CancellationToken.None, head.Path)
                         .ConfigureAwait(false).GetAwaiter().GetResult()
                         .Identifiers.Select(NodeHead.Get).Where(h => h != null))
            {
                // in case of identity types: simply add them to the appropriate collection and move on
                if (childHead.GetNodeType().IsInstaceOfOrDerivedFrom("User"))
                {
                    if (!userIds.Contains(childHead.Id))
                        userIds.Add(childHead.Id);
                }
                else if (childHead.GetNodeType().IsInstaceOfOrDerivedFrom("Group") ||
                         childHead.GetNodeType().IsInstaceOfOrDerivedFrom("OrganizationalUnit"))
                {
                    if (!groupIds.Contains(childHead.Id))
                        groupIds.Add(childHead.Id);
                }
                else
                {
                    // collect identities recursively (if we haven't visited this group yet)
                    if (!groupIds.Contains(childHead.Id))
                        CollectSecurityIdentityChildren(childHead, userIds, groupIds);
                }
            }

            // collect group members
            if (head.GetNodeType().IsInstaceOfOrDerivedFrom("Group"))
            {
                var group = Node.Load<Group>(head.Id);
                foreach (var memberGroup in group.GetMemberGroups())
                {
                    if (!groupIds.Contains(memberGroup.Id))
                        groupIds.Add(memberGroup.Id);
                }
                foreach (var memberUser in group.GetMemberUsers())
                {
                    if (!userIds.Contains(memberUser.Id))
                        userIds.Add(memberUser.Id);
                }
            }
        }
        private static void CheckAceConsistency(SecurityConsistencyResult result, IDictionary<int, SecurityEntity> secCachedEntities)
        {
            // Checks whether every ACE in the security db is valid for the repository: EntityId and IdentityId are 
            // exist as SecurityEntity.
            var storedAces = Providers.Instance.SecurityHandler.SecurityContext
                .SecuritySystem.DataProvider.LoadAllPermissionEntries();
            foreach (var storedAce in storedAces)
            {
                if (!secCachedEntities.ContainsKey(storedAce.EntityId))
                    result.AddInvalidAceMissingEntity(storedAce);
                if (!secCachedEntities.ContainsKey(storedAce.IdentityId))
                    result.AddInvalidAceMissingIdentity(storedAce);
            }
        }

        /// <summary>Finalizes an AD sync task for a user, group or organizational unit.
        /// This action is intended for internal use by the Task Management module.</summary>
        /// <param name="content"></param>
        /// <param name="context"></param>
        /// <param name="result">Result of the AD sync task.</param>
        [ODataAction(Category = "AdSync")]
        public static async STT.Task Ad2PortalSyncFinalizer(Content content, HttpContext context, SnTaskResult result)
        {
            await(context.RequestServices.GetRequiredService<ITaskManager>())
                .OnTaskFinishedAsync(result, context.RequestAborted).ConfigureAwait(false);

            // not enough information
            if (result.Task == null)
                return;

            try
            {
                if (!string.IsNullOrEmpty(result.ResultData))
                {
                    dynamic resultData = JsonConvert.DeserializeObject(result.ResultData);

                    SnLog.WriteInformation("AD sync finished. See details below.", EventId.DirectoryServices,
                        properties: new Dictionary<string, object>
                        {
                            {"SyncedObjects", resultData.SyncedObjects},
                            {"ObjectErrorCount", resultData.ObjectErrorCount},
                            {"ErrorCount", resultData.ErrorCount},
                            {"ElapsedTime", resultData.ElapsedTime}
                        });
                }
                else
                {
                    SnLog.WriteWarning("AD sync finished with no results.", EventId.DirectoryServices);
                }
            }
            catch (Exception ex)
            {
                SnLog.WriteException(ex, "Error during AD sync finalizer.", EventId.DirectoryServices);
            }

            // the task was executed successfully without an error message
            if (result.Successful && result.Error == null)
                return;

            SnLog.WriteError("Error during AD sync. " + result.Error);
        }
    }

    public static class StringExtensions
    {
        /// <summary>
        /// Trims the schema and trailing slashes from a url.
        /// </summary>
        public static string RemoveUrlSchema(this string url)
        {
            if (url == null)
                return null;

            var schIndex = url.IndexOf("://", StringComparison.OrdinalIgnoreCase);

            return (schIndex >= 0 ? url.Substring(schIndex + 3) : url).Trim('/', ' ');
        }

        /// <summary>
        /// Appends an 'https://' prefix to a url if it is missing.
        /// </summary>
        public static string AddUrlSchema(this string url)
        {
            if (string.IsNullOrEmpty(url) || url.StartsWith("http"))
                return url;

            return "https://" + url;
        }

        public static string AddUrlParameter(this string url, string name, string value)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));

            url ??= string.Empty;
            url += url.Contains("?") ? "&" : "?";

            return url + $"{name}={value}";
        }
    }

    public struct SecurityMembershipInfo
    {
        public int GroupId { get; private set; }
        public int MemberId { get; private set; }
        public string GroupPath { get; private set; }
        public string MemberPath { get; private set; }

        public SecurityMembershipInfo(int groupId, int userId)
            : this()
        {
            GroupId = groupId;
            MemberId = userId;

            var gnh = NodeHead.Get(GroupId);
            var mnh = NodeHead.Get(MemberId);

            if (gnh != null)
                GroupPath = gnh.Path;
            if (mnh != null)
                MemberPath = mnh.Path;
        }

        // ====================================================================================== Equality implementation

        public override bool Equals(Object obj)
        {
            return obj is SecurityMembershipInfo && this == (SecurityMembershipInfo)obj;
        }
        public override int GetHashCode()
        {
            return GroupId.GetHashCode() ^ MemberId.GetHashCode();
        }
        public static bool operator ==(SecurityMembershipInfo x, SecurityMembershipInfo y)
        {
            return x.GroupId == y.GroupId && x.MemberId == y.MemberId;
        }
        public static bool operator !=(SecurityMembershipInfo x, SecurityMembershipInfo y)
        {
            return !(x == y);
        }
    }

    public struct SecurityEntityInfo
    {
        public int Id { get; private set; }
        public int ParentId { get; private set; }
        public int OwnerId { get; private set; }
        public string Path { get; private set; }

        private NodeHead _nodeHead;

        // ====================================================================================== Constructors

        public SecurityEntityInfo(int contentId)
            : this()
        {
            Id = contentId;

            var head = NodeHead.Get(Id);
            if (head != null)
            {
                Path = head.Path;
                ParentId = head.ParentId;
                OwnerId = head.OwnerId;

                _nodeHead = head;
            }
        }

        public SecurityEntityInfo(NodeHead nodeHead)
            : this()
        {
            Id = nodeHead.Id;
            Path = nodeHead.Path;
            ParentId = nodeHead.ParentId;
            OwnerId = nodeHead.OwnerId;

            _nodeHead = nodeHead;
        }

        // ====================================================================================== Helper API

        public bool IsSkippableContent()
        {
            if (_nodeHead == null)
                return false;

            return SenseNet.Preview.DocumentPreviewProvider.Current.IsPreviewOrThumbnailImage(_nodeHead);
        }

        // ====================================================================================== Equality implementation

        public override bool Equals(Object obj)
        {
            return obj is SecurityEntityInfo && this == (SecurityEntityInfo)obj;
        }
        public override int GetHashCode()
        {
            return Id.GetHashCode() ^ ParentId.GetHashCode() ^ OwnerId.GetHashCode();
        }
        public static bool operator ==(SecurityEntityInfo x, SecurityEntityInfo y)
        {
            return x.Id == y.Id && x.ParentId == y.ParentId && x.OwnerId == y.OwnerId;
        }
        public static bool operator !=(SecurityEntityInfo x, SecurityEntityInfo y)
        {
            return !(x == y);
        }
    }

    public class StoredAceDebugInfo
    {
        public int EntityId { get; set; }
        public int IdentityId { get; set; }
        public bool LocalOnly { get; set; }
        public ulong AllowBits { get; set; }
        public ulong DenyBits { get; set; }
        public string StringView { get; set; }

        public StoredAceDebugInfo(StoredAce ace)
        {
            this.EntityId = ace.EntityId;
            this.IdentityId = ace.IdentityId;
            this.LocalOnly = ace.LocalOnly;
            this.AllowBits = ace.AllowBits;
            this.DenyBits = ace.DenyBits;
            this.StringView = ace.ToString();
        }
    }

    public class SecurityConsistencyResult
    {
        public bool IsConsistent
        {
            get { return IsMembershipConsistent && IsEntityStructureConsistent && IsAcesConsistent; }
        }

        public bool IsMembershipConsistent
        {
            get
            {
                return MissingMembershipsFromCache.Count == 0 && UnknownMembershipInCache.Count == 0 &&
                    MissingMembershipsFromSecurityDb.Count == 0 && UnknownMembershipInSecurityDb.Count == 0 &&
                    MissingRelationFromFlattenedUsers.Count == 0 && UnknownRelationInFlattenedUsers.Count == 0;
            }
        }

        public bool IsEntityStructureConsistent
        {
            get
            {
                return MissingEntitiesFromRepository.Count == 0 &&
                    MissingEntitiesFromSecurityCache.Count == 0 &&
                    MissingEntitiesFromSecurityDb.Count == 0;
            }
        }

        public bool IsAcesConsistent
        {
            get
            {
                return InvalidACE_MissingEntity.Count == 0 &&
                    InvalidACE_MissingIdentity.Count == 0;
            }
        }

        public TimeSpan ElapsedTime
        {
            get { return _consistencyStopper.Elapsed; }
        }

        private Stopwatch _consistencyStopper;

        public List<SecurityEntityInfo> MissingEntitiesFromRepository { get; private set; }
        public List<SecurityEntityInfo> MissingEntitiesFromSecurityDb { get; private set; }
        public List<SecurityEntityInfo> MissingEntitiesFromSecurityCache { get; private set; }

        public List<SecurityMembershipInfo> MissingMembershipsFromCache { get; private set; }
        public List<SecurityMembershipInfo> UnknownMembershipInSecurityDb { get; private set; }
        public List<SecurityMembershipInfo> MissingMembershipsFromSecurityDb { get; private set; }
        public List<SecurityMembershipInfo> UnknownMembershipInCache { get; private set; }
        public List<SecurityMembershipInfo> MissingRelationFromFlattenedUsers { get; private set; }
        public List<SecurityMembershipInfo> UnknownRelationInFlattenedUsers { get; private set; }

        public List<StoredAceDebugInfo> InvalidACE_MissingEntity { get; private set; }
        public List<StoredAceDebugInfo> InvalidACE_MissingIdentity { get; private set; }

        public SecurityConsistencyResult()
        {
            MissingEntitiesFromRepository = new List<SecurityEntityInfo>();
            MissingEntitiesFromSecurityDb = new List<SecurityEntityInfo>();
            MissingEntitiesFromSecurityCache = new List<SecurityEntityInfo>();

            MissingMembershipsFromCache = new List<SecurityMembershipInfo>();
            UnknownMembershipInCache = new List<SecurityMembershipInfo>();
            MissingMembershipsFromSecurityDb = new List<SecurityMembershipInfo>();
            UnknownMembershipInSecurityDb = new List<SecurityMembershipInfo>();
            MissingRelationFromFlattenedUsers = new List<SecurityMembershipInfo>();
            UnknownRelationInFlattenedUsers = new List<SecurityMembershipInfo>();

            InvalidACE_MissingEntity = new List<StoredAceDebugInfo>();
            InvalidACE_MissingIdentity = new List<StoredAceDebugInfo>();
        }

        public void AddMissingMembershipFromCache(int groupId, int memberId)
        {
            AddMembershipInfoToList(groupId, memberId, MissingMembershipsFromCache);
        }
        public void AddUnknownMembershipInCache(int groupId, int memberId)
        {
            AddMembershipInfoToList(groupId, memberId, UnknownMembershipInCache);
        }
        public void AddMissingMembershipFromSecurityDb(int groupId, int memberId)
        {
            AddMembershipInfoToList(groupId, memberId, MissingMembershipsFromSecurityDb);
        }
        public void AddUnknownMembershipInSecurityDb(int groupId, int memberId)
        {
            AddMembershipInfoToList(groupId, memberId, UnknownMembershipInSecurityDb);
        }
        public void AddMissingRelationFromFlattenedUsers(int groupId, int memberId)
        {
            AddMembershipInfoToList(groupId, memberId, MissingRelationFromFlattenedUsers);
        }
        public void AddUnknownRelationInFlattenedUsers(int groupId, int memberId)
        {
            AddMembershipInfoToList(groupId, memberId, UnknownRelationInFlattenedUsers);
        }

        public void AddMissingEntityFromSecurityDb(NodeHead head)
        {
            AddMissingEntityToList(head, MissingEntitiesFromSecurityDb);
        }
        public void AddMissingEntityFromSecurityDb(int contentId)
        {
            AddMissingEntityToList(contentId, MissingEntitiesFromSecurityDb);
        }
        public void AddMissingEntityFromSecurityCache(int contentId)
        {
            AddMissingEntityToList(contentId, MissingEntitiesFromSecurityCache);
        }
        public void AddMissingEntityFromRepository(int contentId)
        {
            var sei = new SecurityEntityInfo(contentId);

            // workaround for non-indexed content (preview images): skip those items
            if (sei.IsSkippableContent())
                return;

            AddMissingEntityToList(sei, MissingEntitiesFromRepository);
        }

        private static void AddMembershipInfoToList(int groupId, int memberId, IList<SecurityMembershipInfo> membershipInfoList)
        {
            var smi = new SecurityMembershipInfo(groupId, memberId);

            if (membershipInfoList.All(c => c != smi))
                membershipInfoList.Add(smi);
        }
        private void AddMissingEntityToList(int contentId, IList<SecurityEntityInfo> entityInfoList)
        {
            AddMissingEntityToList(new SecurityEntityInfo(contentId), entityInfoList);
        }
        private void AddMissingEntityToList(SecurityEntityInfo entity, IList<SecurityEntityInfo> entityInfoList)
        {
            if (entity != null && !entityInfoList.Any(c => c == entity))
                entityInfoList.Add(entity);
        }
        private void AddMissingEntityToList(NodeHead head, IList<SecurityEntityInfo> entityInfoList)
        {
            var sei = new SecurityEntityInfo(head);

            if (!entityInfoList.Any(c => c == sei))
                entityInfoList.Add(sei);
        }

        public void AddInvalidAceMissingEntity(StoredAce storedAce)
        {
            InvalidACE_MissingEntity.Add(new StoredAceDebugInfo(storedAce));
        }
        public void AddInvalidAceMissingIdentity(StoredAce storedAce)
        {
            InvalidACE_MissingIdentity.Add(new StoredAceDebugInfo(storedAce));
        }

        public void StartTimer()
        {
            _consistencyStopper = Stopwatch.StartNew();
        }

        public void StopTimer()
        {
            _consistencyStopper.Stop();
        }

    }
}
