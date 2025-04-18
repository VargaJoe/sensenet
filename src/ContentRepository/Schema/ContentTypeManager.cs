using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.XPath;
using SenseNet.Communication.Messaging;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Schema;
using SenseNet.ContentRepository.Storage.Search;
using SnCS = SenseNet.ContentRepository.Storage;
using SenseNet.Search.Indexing;
using SenseNet.Tools;
using STT=System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BinaryData = SenseNet.ContentRepository.Storage.BinaryData;

namespace SenseNet.ContentRepository.Schema
{
    internal sealed class ContentTypeManager
    {
        [Serializable]
        internal sealed class ContentTypeManagerResetDistributedAction : DistributedAction
        {
            public override string TraceMessage => null;

            public override STT.Task DoActionAsync(bool onRemote, bool isFromMe, CancellationToken cancellationToken)
            {
                // Local echo of my action: Return without doing anything
                if (onRemote && isFromMe)
                    return STT.Task.CompletedTask;

                ContentTypeManager.ResetPrivate();

                return STT.Task.CompletedTask;
            }
        }

        // ======================================================================= Static interface

        private static object _syncRoot = new Object();

        private static bool _initializing = false;

        private const string ContentTypeManagerProviderKey = "ContentTypeManager";
        private ILogger<ContentTypeManager> _logger;

        [Obsolete("Use Instance instead.", true)]
        public static ContentTypeManager Current => Instance;

        public static ContentTypeManager Instance
        {
            get
            {
                var contentTypeManager = Providers.Instance.GetProvider<ContentTypeManager>(ContentTypeManagerProviderKey);
                if (contentTypeManager == null)
                {
                    lock (_syncRoot)
                    {
                        contentTypeManager = Providers.Instance.GetProvider<ContentTypeManager>(ContentTypeManagerProviderKey);
                        if (contentTypeManager == null)
                        {
                            _initializing = true;
                            var ctm = new ContentTypeManager();
                            ctm.Initialize();
                            contentTypeManager = ctm;
                            _initializing = false;
                            Providers.Instance.SetProvider(ContentTypeManagerProviderKey, ctm);
                            contentTypeManager._logger.LogInformation("ContentTypeManager created. Content types: " + ctm._contentTypes.Count);
                        }
                    }
                }

                return contentTypeManager;
            }
        }

        internal static ContentTypeManager CreateForTests()
        {
            var result = new ContentTypeManager();

            result._contentPaths = new Dictionary<string, string>();
            result._contentTypes = new Dictionary<string, ContentType>();
            result.AllFieldNames = new List<string>();

            return result;
        }

        // =======================================================================

        private Dictionary<string, string> _contentPaths;
        private Dictionary<string, ContentType> _contentTypes;

        internal Dictionary<string, ContentType> ContentTypes
        {
            get { return _contentTypes; }
        }

        #region GetContentTypeNameByType
        private Dictionary<Type, NodeType> _contentTypeNamesByType;
        public static string GetContentTypeNameByType(Type t)
        {
            if (Instance._contentTypeNamesByType == null)
            {
                var contentTypeNamesByType = new Dictionary<Type, NodeType>();
                foreach (var nt in Providers.Instance.StorageSchema.NodeTypes)
                {
                    var type = TypeResolver.GetType(nt.ClassName, false);
                    if (type == null)
                        continue;

                    if (type == typeof(GenericContent))
                    {
                        if (nt.Name == "GenericContent")
                            contentTypeNamesByType.Add(type, nt);
                    }
                    else if (!contentTypeNamesByType.TryGetValue(type, out var prevNt))
                        contentTypeNamesByType.Add(type, nt);
                    else
                        if (prevNt.IsInstaceOfOrDerivedFrom(nt))
                        contentTypeNamesByType[type] = nt;
                }
                Instance._contentTypeNamesByType = contentTypeNamesByType;
            }

            if (Instance._contentTypeNamesByType.TryGetValue(t, out var nodeType))
                return nodeType.Name;

            return null;
        }
        #endregion

        private ContentTypeManager()
        {
        }
        static ContentTypeManager()
        {
            Node.AnyContentListDeleted += new EventHandler(Node_AnyContentListDeleted);
        }
        private static void Node_AnyContentListDeleted(object sender, EventArgs e)
        {
            Reset();
        }

        private void Initialize()
        {
            _logger = Providers.Instance.Services.GetRequiredService<ILogger<ContentTypeManager>>();

            using (new SenseNet.ContentRepository.Storage.Security.SystemAccount())
            {
                _contentPaths = new Dictionary<string, string>();
                _contentTypes = new Dictionary<string, ContentType>();

                // temporary save: read enumerator only once
                var contentTypes = new List<ContentType>();

                var result = NodeQuery.QueryNodesByTypeAndPath(
                    Providers.Instance.StorageSchema.NodeTypes["ContentType"], false, 
                    string.Concat(Repository.ContentTypesFolderPath, SnCS.RepositoryPath.PathSeparator), true);

                foreach (ContentType contentType in result.Nodes)
                {
                    contentTypes.Add(contentType);
                    _contentPaths.Add(contentType.Name, contentType.Path);
                    _contentTypes.Add(contentType.Name, contentType);
                }
                foreach (ContentType contentType in contentTypes)
                {
                    if (contentType.ParentTypeName == null)
                        contentType.SetParentContentType(null);
                    else
                        contentType.SetParentContentType(_contentTypes[contentType.ParentTypeName]);
                }
                AllFieldNames = contentTypes.SelectMany(t => t.FieldSettings.Select(f => f.Name)).Distinct().ToList();

                _fsInfoTable = CreateFsInfoTable();

                FinalizeAllowedChildTypes(AllFieldNames);
                FinalizeIndexingInfo();
            }
        }

        internal static void Start()
        {
            if (_initializing)
                return;
            var m = Instance;
        }

        private static ILogger<ContentTypeManager> GetLogger()
            => Providers.Instance.Services.GetRequiredService<ILogger<ContentTypeManager>>();

        internal static void Reset()
        {
            GetLogger().LogTrace("ContentTypeManager.Reset called.");
            new ContentTypeManagerResetDistributedAction().ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        private static void ResetPrivate()
        {
            lock (_syncRoot)
            {
                Providers.Instance.SetProvider(ContentTypeManagerProviderKey, null);
                _indexingInfoTable = new Dictionary<string, IPerFieldIndexingInfo>();
                ContentType.OnTypeSystemRestarted();

                GetLogger().LogInformation("ContentTypeManager.Reset executed.");
            }
        }

        internal static void Reload()
        {
            ResetPrivate();
            var c = Instance;
        }

        internal ContentType GetContentTypeByHandler(Node contentHandler)
        {
            var nodeType = contentHandler.NodeType;
            if (nodeType == null)
                return null;
            return GetContentTypeByName(nodeType.Name);
        }
        internal ContentType GetContentTypeByName(string contentTypeName)
        {
            ContentType contentType;
            if (_contentTypes.TryGetValue(contentTypeName, out contentType))
                return contentType;

            lock (_syncRoot)
            {
                if (_contentTypes.TryGetValue(contentTypeName, out contentType))
                    return contentType;

                string path;
                if (_contentPaths.TryGetValue(contentTypeName, out path))
                {
                    contentType = ContentType.LoadAndInitialize(path);
                    if (contentType != null)
                    {
                        _contentTypes.Add(contentTypeName, contentType);
                        _contentPaths.Add(contentTypeName, contentType.Path);
                    }
                }
            }
            return contentType;
        }

        internal void RemoveContentType(string name)
        {
            // Caller: ContentType.Delete()
            lock (_syncRoot)
            {
                ContentType contentType;
                if (_contentTypes.TryGetValue(name, out contentType))
                {
                    SchemaEditor editor = new SchemaEditor();
                    editor.Load();
                    RemoveContentType(contentType, editor);
                    editor.Register();
                }
            }
        }
        private void RemoveContentType(ContentType contentType, SchemaEditor editor)
        {
            // Remove recursive
            foreach (FieldSetting fieldSetting in contentType.FieldSettings)
                if (fieldSetting.Owner == contentType)
                    fieldSetting.ParentFieldSetting = null;
            foreach (ContentType childType in contentType.ChildTypes)
                RemoveContentType(childType, editor);
            NodeType nodeType = editor.NodeTypes[contentType.Name];
            if (nodeType != null)
                editor.DeleteNodeType(nodeType);
            _contentTypes.Remove(contentType.Name);
            _contentPaths.Remove(contentType.Name);
        }

        // ====================================================================== Registration interface

        private const BindingFlags _publicPropertyBindingFlags = BindingFlags.Instance | BindingFlags.Public;
        private const BindingFlags _nonPublicPropertyBindingFlags = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static ContentType LoadOrCreateNew(string contentTypeDefinitionXml)
        {
            return LoadOrCreateNew(new XPathDocument(new StringReader(contentTypeDefinitionXml)));
        }
        internal static ContentType LoadOrCreateNew(IXPathNavigable contentTypeDefinitionXml)
        {
            // ==== saves and puts the holder

            // #1 Determine name and parent's name
            XPathNavigator nav = contentTypeDefinitionXml.CreateNavigator().SelectSingleNode("/*[1]");
            string name = nav.GetAttribute("name", "");
            string parentTypeName = nav.GetAttribute("parentType", "");

            // #2 Load ContentType
            ContentType contentType = Instance.GetContentTypeByName(name);

            // #3 Parent Node: if it is loaded yet use it (ReferenceEquals)
            Node parentNode;
            if (string.IsNullOrEmpty(parentTypeName))
            {
                if(name != "ContentType" && name != "GenericContent")
                    throw new ContentRegistrationException(SR.Exceptions.Registration.Msg_MissingParentContentType, name);
                parentNode = (Folder)Node.LoadNode(Repository.ContentTypesFolderPath);
            }
            else if (parentTypeName == "ContentType")
            {
                throw new ContentRegistrationException(SR.Exceptions.Registration.Msg_ForbiddenParentContentType, name);
            }
            else
            {
                parentNode = Instance.GetContentTypeByName(parentTypeName);
                if (parentNode == null)
                    throw new ApplicationException(String.Concat(SR.Exceptions.Content.Msg_UnknownContentType, ": ", parentTypeName));
            }

            // #4 Create ContentType if it does not exist
            if (contentType == null)
            {
                contentType = new ContentType(parentNode);
                contentType.Name = name;
            }

            // #5 Update hierarchy if parent is changed
            if (contentType.ParentId != parentNode.Id)
            {
                throw new SnNotSupportedException("Change ContentType hierarchy is not supported");
            }

            // #6 Set Binary data
            BinaryData binaryData = new BinaryData();
            binaryData.FileName = new BinaryFileName(name, ContentType.ContentTypeFileNameExtension);
            binaryData.SetStream(RepositoryTools.GetStreamFromString(contentTypeDefinitionXml.CreateNavigator().OuterXml));
            contentType.Binary = binaryData;

            return contentType;
        }

        internal void AddContentType(ContentType contentType)
        {
            lock (_syncRoot)
            {
                var parentContentTypeName = contentType.ParentName;

                ContentType parentContentType;
                _contentTypes.TryGetValue(parentContentTypeName, out parentContentType);

                string name = contentType.Name;
                if (!_contentTypes.ContainsKey(name))
                    _contentTypes.Add(name, contentType);
                if (!_contentPaths.ContainsKey(name))
                    _contentPaths.Add(name, contentType.Path);
                contentType.SetParentContentType(parentContentType);
            }
        }

        internal static void ApplyChanges(ContentType settings, bool reset)
        {
            SchemaEditor editor = new SchemaEditor();
            editor.Load();
            ApplyChangesInEditor(settings, editor);
            editor.Register();

            // The ContentTypeManager distributes its reset, no custom DistributedAction call needed
            if (reset)
                ContentTypeManager.Reset(); // necessary (ApplyChanges) calls ContentType.Save
        }
        internal static void ApplyChangesInEditor(ContentType contentType, SchemaEditor editor)
        {
            // Find ContentHandler
            var handlerType = TypeResolver.GetType(contentType.HandlerName, false);
            if (handlerType == null)
                throw new RegistrationException(string.Concat(
                    SR.Exceptions.Registration.Msg_ContentHandlerNotFound, ": ", contentType.HandlerName));

            // parent type
            NodeType parentNodeType = null;
            if (contentType.ParentTypeName != null)
            {
                parentNodeType = editor.NodeTypes[contentType.ParentTypeName];
                if (parentNodeType == null)
                    throw new ContentRegistrationException(SR.Exceptions.Registration.Msg_UnknownParentContentType, contentType.Name);

                // make sure that all content handlers defined on the parent chain exist
                var pnt = parentNodeType;
                while (pnt != null)
                {
                    var ht = TypeResolver.GetType(pnt.ClassName, false);
                    if (ht == null)
                        throw new RegistrationException($"Unknown content handler: {pnt.ClassName}");

                    pnt = pnt.Parent;
                }
            }

            // handler type
            NodeType nodeType = editor.NodeTypes[contentType.Name];
            if (nodeType == null)
                nodeType = editor.CreateNodeType(parentNodeType, contentType.Name, contentType.HandlerName);
            if (nodeType.ClassName != contentType.HandlerName)
                editor.ModifyNodeType(nodeType, contentType.HandlerName);
            if (nodeType.Parent != parentNodeType)
                editor.ModifyNodeType(nodeType, parentNodeType);

            // 1: ContentHandler properties
            NodeTypeRegistration ntReg = ParseAttributes(handlerType);
            if (ntReg == null)
                throw new ContentRegistrationException(
                    SR.Exceptions.Registration.Msg_DefinedHandlerIsNotAContentHandler, contentType.Name);

            // 2: Field properties
            foreach (FieldSetting fieldSetting in contentType.FieldSettings)
            {
                Instance.AssertFieldSettingIsValid(fieldSetting);

                Type[][] slots = fieldSetting.HandlerSlots;
                int fieldSlotCount = slots.GetLength(0);

                if (fieldSetting.Bindings.Count != fieldSlotCount)
                    throw new ContentRegistrationException(String.Format(CultureInfo.InvariantCulture,
                        SR.Exceptions.Registration.Msg_FieldBindingsCount_1, fieldSlotCount), contentType.Name, fieldSetting.Name);
                for (int i = 0; i < fieldSetting.Bindings.Count; i++)
                {
                    string propName = fieldSetting.Bindings[i];
                    var dataType = fieldSetting.DataTypes[i];
                    CheckDataType(propName, dataType, contentType.Name, editor);
                    PropertyInfo propInfo = handlerType.GetProperty(propName);
                    if (propInfo != null)
                    {
                        // #1: there is a property under the slot:
                        bool ok = false;
                        for (int j = 0; j < slots[i].Length; j++)
                        {
                            if (slots[i][j].IsAssignableFrom(propInfo.PropertyType))
                            {
                                PropertyTypeRegistration propReg = ntReg.PropertyTypeRegistrationByName(propName);
                                if (propInfo.DeclaringType != handlerType)
                                {
                                    if (propReg == null)
                                    {
                                        object[] attrs = propInfo.GetCustomAttributes(typeof(RepositoryPropertyAttribute), false);
                                        if (attrs.Length > 0)
                                        {
                                            propReg = new PropertyTypeRegistration(propInfo, (RepositoryPropertyAttribute)attrs[0]);
                                            ntReg.PropertyTypeRegistrations.Add(propReg);
                                        }
                                    }
                                }
                                if (propReg != null && propReg.DataType != fieldSetting.DataTypes[i])
                                    throw new ContentRegistrationException(String.Concat(
                                        "The data type of the field in the content type definition does not match the data type of its content handler's property. ",
                                        "Please modify the field type in the content type definition. ",
                                        "ContentTypeDefinition: '", contentType.Name,
                                        "', FieldName: '", fieldSetting.Name,
                                        "', DataType of Field's binding: '", fieldSetting.DataTypes[i],
                                        "', ContentHandler: '", handlerType.FullName,
                                        "', PropertyName: '", propReg.Name,
                                        "', DataType of property: '", propReg.DataType,
                                        "'"));

                                ok = true;
                                fieldSetting.HandlerSlotIndices[i] = j;
                                fieldSetting.PropertyIsReadOnly = !PropertyHasPublicSetter(propInfo);
                                break;
                            }
                        }
                        if (!ok)
                        {
                            if (fieldSetting.ShortName == "Reference" || fieldSetting.DataTypes[i] == RepositoryDataType.Reference)
                                CheckReference(propInfo, slots[i], contentType, fieldSetting);
                            else
                                throw new ContentRegistrationException(SR.Exceptions.Registration.Msg_PropertyAndFieldAreNotConnectable,
                                    contentType.Name, fieldSetting.Name);
                        }
                    }
                    else
                    {
                        // #2: there is not a property under the slot:
                        PropertyTypeRegistration propReg = new PropertyTypeRegistration(propName, dataType);
                        ntReg.PropertyTypeRegistrations.Add(propReg);
                    }
                }
            }

            // Collect deletables. Check equals
            foreach (PropertyType propType in nodeType.PropertyTypes.ToArray())
            {
                PropertyTypeRegistration propReg = ntReg.PropertyTypeRegistrationByName(propType.Name);
                if (propReg == null)
                {
                    editor.RemovePropertyTypeFromPropertySet(propType, nodeType);
                }
            }


            // Register
            foreach (PropertyTypeRegistration ptReg in ntReg.PropertyTypeRegistrations)
            {
                PropertyType propType = nodeType.PropertyTypes[ptReg.Name];
                if (propType == null)
                {
                    propType = editor.PropertyTypes[ptReg.Name];
                    if (propType == null)
                        propType = editor.CreatePropertyType(ptReg.Name, ConvertDataType(ptReg.DataType));
                    editor.AddPropertyTypeToPropertySet(propType, nodeType);
                }
            }
        }

        private static void CheckDataType(string propName, RepositoryDataType dataType, string nodeTypeName, SchemaEditor editor)
        {
            var propType = editor.PropertyTypes[propName];

            if (propType == null)
                return;
            if (dataType == (RepositoryDataType)propType.DataType)
                return;

            // "DataType collision in two properties. NodeType = '{0}', PropertyType = '{1}', original DataType = {2}, passed DataType = {3}.";
            throw new RegistrationException(String.Format(SR.Exceptions.Registration.Msg_DataTypeCollisionInTwoProperties_4,
                nodeTypeName, propName, propType.DataType, dataType));
        }

        private static DataType ConvertDataType(RepositoryDataType source)
        {
            if (source == RepositoryDataType.NotDefined)
                throw new ArgumentOutOfRangeException("source", "Source DataType cannot be NotDefined");
            return (DataType)source;
        }
        private static void CheckReference(PropertyInfo propInfo, Type[] type, ContentType cts, FieldSetting fs)
        {
            if (propInfo.PropertyType == (typeof(Node)))
                return;
            if (propInfo.PropertyType.IsSubclassOf(typeof(Node)))
                return;
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(propInfo.PropertyType))
                return;
            throw new NotSupportedException(String.Format(CultureInfo.InvariantCulture,
                SR.Exceptions.Registration.Msg_InvalidReferenceField_2, cts.Name, fs.Name));
        }

        /* ---------------------------------------------------------------------- FieldSetting validation */

        [DebuggerDisplay("{Name}: {FieldType} ({Binding})")]
        private class FieldSettingInfo
        {
            public string Name;
            public string Binding;
            public string FieldType;
        }
        private class FieldSettingInfoEqualityComparer : IEqualityComparer<FieldSettingInfo>
        {
            public bool Equals(FieldSettingInfo x, FieldSettingInfo y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return x.Name == y.Name;
            }

            public int GetHashCode(FieldSettingInfo obj)
            {
                return (obj.Name != null ? obj.Name.GetHashCode() : 0);
            }
        }

        private readonly object _fsInfoTableLock = new object();
        private List<FieldSettingInfo> _fsInfoTable;
        private void AssertFieldSettingIsValid(FieldSetting fieldSetting)
        {
            var contentTypeName = fieldSetting.Owner.Name;
            var fieldName = fieldSetting.Name;

            if(fieldSetting.Name.Equals("Actions", StringComparison.OrdinalIgnoreCase) ||
               fieldSetting.Name.Equals("Children", StringComparison.OrdinalIgnoreCase))
                throw new ContentRegistrationException(
                    $"The '{fieldName}' field cannot be used in any content type definition. ContentType: {contentTypeName}",
                    null, contentTypeName, fieldName);

            FieldSettingInfo fs = null;
            lock (_fsInfoTableLock)
            {
                fs = _fsInfoTable.FirstOrDefault(x => x.Name == fieldSetting.Name);
                if (fs == null)
                {
                    _fsInfoTable.Add(CreateFsInfo(fieldSetting));
                    return;
                }
            }

            if (fs.FieldType != fieldSetting.FieldClassName)
                throw new ContentRegistrationException(
                    $"Field type violation in the {contentTypeName} content type definition. " +
                    $"The expected 'type' of the '{fieldName}' field is {fs.FieldType}.",
                    null, contentTypeName, fieldName);

            var actualBinding = string.Join(", ", fieldSetting.Bindings);
            if (fs.Binding != actualBinding)
                throw new ContentRegistrationException(
                    $"Field 'Binding' violation in the {contentTypeName}.{fieldName}. " +
                    $"Expected: {fs.Binding}. Actual: {actualBinding}.",
                    null, contentTypeName, fieldName);
        }

        private List<FieldSettingInfo> CreateFsInfoTable()
        {
            var all = ContentTypes.Values.SelectMany(x => x.FieldSettings.Where(y => y.Owner == x))
                .Select(CreateFsInfo)
                .OrderBy(x => x.Name)
                .ThenBy(x => x.FieldType)
                .Distinct(new FieldSettingInfoEqualityComparer())
                .ToList();

            return all;
        }

        private FieldSettingInfo CreateFsInfo(FieldSetting fs)
        {
            return new FieldSettingInfo
            {
                Name = fs.Name,
                Binding = string.Join(", ", fs.Bindings),
                FieldType = fs.FieldClassName
            };
        }

        // ---------------------------------------------------------------------- Attribute parsing

        private static NodeTypeRegistration ParseAttributes(Type type)
        {
            NodeTypeRegistration ntReg = null;
            ContentHandlerAttribute contentHandlerAttribute = null;

            foreach (object attrObject in type.GetCustomAttributes(false))
                if ((contentHandlerAttribute = attrObject as ContentHandlerAttribute) != null)
                    break;

            // Finish if there is not a ContentHandlerAttribute
            if (contentHandlerAttribute == null)
                return ntReg;

            // Must inherit from Node.
            if (!IsInheritedFromNode(type))
                throw new ContentRegistrationException(String.Format(CultureInfo.CurrentCulture,
                    SR.Exceptions.Registration.Msg_NodeTypeMustBeInheritedFromNode_1,
                    type.FullName));

            // Property checks
            RepositoryPropertyAttribute propertyAttribute = null;
            List<PropertyTypeRegistration> propertyTypeRegistrations = new List<PropertyTypeRegistration>();
            Dictionary<string, RepositoryPropertyAttribute> propertyAttributes = new Dictionary<string, RepositoryPropertyAttribute>();

            List<PropertyInfo> props = new List<PropertyInfo>(type.GetProperties(_publicPropertyBindingFlags));
            props.AddRange(type.GetProperties(_nonPublicPropertyBindingFlags));

            foreach (PropertyInfo propInfo in props)
            {
                string propName = propInfo.Name;

                propertyAttribute = null;
                foreach (object attrObject in propInfo.GetCustomAttributes(false))
                    if ((propertyAttribute = attrObject as RepositoryPropertyAttribute) != null)
                        break;

                if (propertyAttribute == null)
                    continue;

                if (propertyAttributes.ContainsKey(propName))
                    throw new RegistrationException(String.Format(CultureInfo.CurrentCulture,
                        SR.Exceptions.Registration.Msg_PropertyTypeAttributesWithTheSameName_2,
                        type.FullName, propInfo.Name));
                propertyAttributes.Add(propName, propertyAttribute);

                // Override default name with passed name
                if (propertyAttribute.PropertyName != null)
                    propName = propertyAttribute.PropertyName;

                // Build PropertyTypeRegistration
                PropertyTypeRegistration propReg = new PropertyTypeRegistration(propInfo, propertyAttribute);
                propertyTypeRegistrations.Add(propReg);
            }

            // Build NodeTypeRegistration
            ntReg = new NodeTypeRegistration(type, null, propertyTypeRegistrations);

            return ntReg;
        }
        private static bool IsInheritedFromNode(Type type)
        {
            Type t = type;
            while (t != typeof(Object))
            {
                if (t == typeof(Node))
                    return true;
                t = t.BaseType;
            }
            return false;
        }

        // ---------------------------------------------------------------------- Information methods

        internal ContentType[] GetContentTypes()
        {
            ContentType[] array = new ContentType[_contentTypes.Count];
            _contentTypes.Values.CopyTo(array, 0);
            return array;
        }
        internal string[] GetContentTypeNames()
        {
            string[] array = new string[_contentTypes.Count];
            _contentTypes.Keys.CopyTo(array, 0);
            return array;
        }

        internal ContentType[] GetRootTypes()
        {
            List<ContentType> list = new List<ContentType>();
            foreach (ContentType ct in GetContentTypes())
                if (ct.ParentType == null)
                    list.Add(ct);
            return list.ToArray();
        }
        internal string[] GetRootTypeNames()
        {
            List<string> list = new List<string>();
            foreach (ContentType ct in GetRootTypes())
                list.Add(ct.Name);
            return list.ToArray();
        }

        internal List<string> AllFieldNames { get; private set; }

        internal string TraceContentSchema()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            bool first = true;
            foreach (ContentType ct in GetRootTypes())
            {
                if (first)
                    first = false;
                else
                    sb.Append(", ");
                TraceContentSchema(sb, ct);
            }
            sb.Append("}");
            return sb.ToString();
        }
        private void TraceContentSchema(StringBuilder sb, ContentType root)
        {
            sb.Append(root.Name);
            if (root.ChildTypes.Count > 0)
                sb.Append("{");
            bool first = true;
            foreach (ContentType child in root.ChildTypes)
            {
                if (first)
                    first = false;
                else
                    sb.Append(", ");
                TraceContentSchema(sb, child);
            }
            if (root.ChildTypes.Count > 0)
                sb.Append("}");
        }

        internal static bool PropertyHasPublicSetter(PropertyInfo prop)
        {
            return prop.GetSetMethod() != null;
        }

        // ====================================================================== Indexing

        private static IDictionary<string, IPerFieldIndexingInfo> _indexingInfoTable = new Dictionary<string, IPerFieldIndexingInfo>();
        internal IDictionary<string, IPerFieldIndexingInfo> IndexingInfo { get { return _indexingInfoTable; } }

        internal static IDictionary<string, IPerFieldIndexingInfo> GetPerFieldIndexingInfo()
        {
            return Instance.IndexingInfo;
        }
        internal static IPerFieldIndexingInfo GetPerFieldIndexingInfo(string fieldName)
        {
            var ensureStart = Instance;

            IPerFieldIndexingInfo info = null;
            if (fieldName.Contains('.'))
                info = Aspect.GetPerFieldIndexingInfo(fieldName);

            if (info != null || Instance.IndexingInfo.TryGetValue(fieldName, out info))
                return info;

            return null;
        }
        internal static void SetPerFieldIndexingInfo(string fieldName, string contentTypeName, IPerFieldIndexingInfo indexingInfo)
        {
            IPerFieldIndexingInfo origInfo;

            if (!_indexingInfoTable.TryGetValue(fieldName, out origInfo))
            {
                lock (_syncRoot)
                {
                    if (!_indexingInfoTable.TryGetValue(fieldName, out origInfo))
                    {
                        _indexingInfoTable.Add(fieldName, indexingInfo);
                        return;
                    }
                }
            }

            if (origInfo.IndexingMode == IndexingMode.Default)
                origInfo.IndexingMode = indexingInfo.IndexingMode;
            else if (indexingInfo.IndexingMode != IndexingMode.Default && indexingInfo.IndexingMode != origInfo.IndexingMode)
                throw new ContentRegistrationException("Cannot override IndexingMode", contentTypeName, fieldName);

            if (origInfo.IndexStoringMode == IndexStoringMode.Default)
                origInfo.IndexStoringMode = indexingInfo.IndexStoringMode;
            else if (indexingInfo.IndexStoringMode != IndexStoringMode.Default && indexingInfo.IndexStoringMode != origInfo.IndexStoringMode)
                throw new ContentRegistrationException("Cannot override IndexStoringMode", contentTypeName, fieldName);

            if (origInfo.TermVectorStoringMode == IndexTermVector.Default)
                origInfo.TermVectorStoringMode = indexingInfo.TermVectorStoringMode;
            else if (indexingInfo.TermVectorStoringMode != IndexTermVector.Default && indexingInfo.TermVectorStoringMode != origInfo.TermVectorStoringMode)
                throw new ContentRegistrationException("Cannot override TermVectorStoringMode", contentTypeName, fieldName);

            if (origInfo.Analyzer == IndexFieldAnalyzer.Default)
                origInfo.Analyzer = indexingInfo.Analyzer;
            else if (indexingInfo.Analyzer != IndexFieldAnalyzer.Default && indexingInfo.Analyzer != origInfo.Analyzer)
                throw new ContentRegistrationException("Cannot override Analyzer", contentTypeName, fieldName);
        }

        internal static Exception AnalyzerViolationExceptionHelper(string contentTypeName, string fieldSettingName)
        {
            return new ContentRegistrationException(
                String.Concat("Change analyzer in a field is not allowed. ContentType: ", contentTypeName, ", Field: ", fieldSettingName)
                , null, contentTypeName, fieldSettingName);
        }
        internal static Exception ParserViolationExceptionHelper(string contentTypeName, string fieldSettingName)
        {
            return new ContentRegistrationException(
                String.Concat("Change FieldIndexHandler in a field is not allowed. ContentType: ", contentTypeName, ", Field: ", fieldSettingName)
                , null, contentTypeName, fieldSettingName);
        }

        private void FinalizeAllowedChildTypes(List<string> allFieldNames)
        {
            foreach (var ct in this.ContentTypes.Values)
                ct.FinalizeAllowedChildTypes(this.ContentTypes, allFieldNames);
        }

        private void FinalizeIndexingInfo()
        {
            if (!Providers.Instance.SearchManager.SearchEngine.IndexingEngine.Running)
                return;

            Providers.Instance.SearchManager.SearchEngine.SetIndexingInfo(_indexingInfoTable);
        }

        public static long _GetTimestamp()
        {
            if (Providers.Instance.GetProvider<ContentTypeManager>(ContentTypeManagerProviderKey) == null)
                return 0L;
            ContentType ct = null;
            Instance.ContentTypes.TryGetValue("Automobile", out ct);
            if (ct == null)
                return -1;
            return ct.NodeTimestamp;
        }

        // ======================================================================

        /// <summary>
        /// Gets the name of every field in the system.
        /// </summary>
        /// <param name="includeNonIndexedFields">Whether or not to include non-indexed fields. Default is true.</param>
        /// <returns>A list which contains the name of every field in the system which meets the specificed criteria.</returns>
        public static IEnumerable<string> GetAllFieldNames(bool includeNonIndexedFields = true)
        {
            if (includeNonIndexedFields)
                return Instance.AllFieldNames;

            return Instance.AllFieldNames.Where(x => ContentTypeManager.Instance.IndexingInfo.ContainsKey(x)
                                                     && Instance.IndexingInfo[x].IsInIndex);
        }

        /// <summary>
        /// Gets detailed indexing information about all fields in the repository.
        /// </summary>
        /// <param name="includeNonIndexedFields">Whether to include non-indexed fields.</param>
        /// <returns>Detailed indexing information about all fields in the repository.</returns>
        public static IEnumerable<ExplicitPerFieldIndexingInfo> GetExplicitPerFieldIndexingInfo(bool includeNonIndexedFields)
        {
            var infoArray = new List<ExplicitPerFieldIndexingInfo>(ContentTypeManager.Instance.ContentTypes.Count * 5);

            foreach (var contentType in ContentTypeManager.Instance.ContentTypes.Values)
            {
                var xml = new System.Xml.XmlDocument();
                var nsmgr = new System.Xml.XmlNamespaceManager(xml.NameTable);
                var fieldCount = 0;

                nsmgr.AddNamespace("x", ContentType.ContentDefinitionXmlNamespace);
                xml.Load(contentType.Binary.GetStream());
                var fieldNodes = xml.SelectNodes("/x:ContentType/x:Fields/x:Field", nsmgr);
                if (fieldNodes != null)
                {
                    foreach (System.Xml.XmlElement fieldElement in fieldNodes)
                    {
                        var typeAttr = fieldElement.Attributes["type"] ?? fieldElement.Attributes["handler"];

                        var info = new ExplicitPerFieldIndexingInfo
                        {
                            ContentTypeName = contentType.Name,
                            ContentTypePath =
                                contentType.Path.Replace(Repository.ContentTypesFolderPath + "/", String.Empty),
                            FieldName = fieldElement.Attributes["name"].Value,
                            FieldType = typeAttr.Value
                        };

                        var fieldTitleElement = fieldElement.SelectSingleNode("x:DisplayName", nsmgr);
                        if (fieldTitleElement != null)
                            info.FieldTitle = fieldTitleElement.InnerText;

                        var fieldDescElement = fieldElement.SelectSingleNode("x:Description", nsmgr);
                        if (fieldDescElement != null)
                            info.FieldDescription = fieldDescElement.InnerText;

                        var hasIndexing = false;
                        var indexingNodes = fieldElement.SelectNodes("x:Indexing/*", nsmgr);
                        if (indexingNodes != null)
                        {
                            foreach (System.Xml.XmlElement element in indexingNodes)
                            {
                                if (!Enum.TryParse(element.InnerText, out IndexFieldAnalyzer analyzer))
                                    analyzer = IndexFieldAnalyzer.Default;
                                hasIndexing = true;
                                switch (element.LocalName)
                                {
                                    case "Analyzer":
                                        info.Analyzer = analyzer;
                                        break;
                                    case "IndexHandler":
                                        info.IndexHandler = element.InnerText.Replace("SenseNet.Search", ".");
                                        break;
                                    case "Mode":
                                        info.IndexingMode = element.InnerText;
                                        break;
                                    case "Store":
                                        info.IndexStoringMode = element.InnerText;
                                        break;
                                    case "TermVector":
                                        info.TermVectorStoringMode = element.InnerText;
                                        break;
                                }
                            }
                        }

                        fieldCount++;

                        if (hasIndexing || includeNonIndexedFields)
                            infoArray.Add(info);
                    }
                }

                // content type without fields
                if (fieldCount == 0 && includeNonIndexedFields)
                {
                    var info = new ExplicitPerFieldIndexingInfo
                    {
                        ContentTypeName = contentType.Name,
                        ContentTypePath = contentType.Path.Replace(Repository.ContentTypesFolderPath + "/", String.Empty),
                    };

                    infoArray.Add(info);
                }
            }

            return infoArray;
        }

        /// <summary>
        /// Gets explicit per-field indexing information collected into a table.
        /// </summary>
        /// <param name="fullTable">Whether or not to include non-indexed fields.</param>
        /// <returns>A table containing detailed indexing information.</returns>
        public static string GetExplicitIndexingInfo(bool fullTable)
        {
            var infoArray = GetExplicitPerFieldIndexingInfo(fullTable);

            var sb = new StringBuilder();
            sb.AppendLine("TypePath\tType\tField\tFieldTitle\tFieldDescription\tFieldType\tMode\tStore\tTVect\tHandler\tAnalyzer");
            foreach (var info in infoArray)
            {
                sb.AppendFormat("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}\t{10}",
                    info.ContentTypePath,
                    info.ContentTypeName,
                    info.FieldName,
                    info.FieldTitle,
                    info.FieldDescription,
                    info.FieldType,
                    info.IndexingMode,
                    info.IndexStoringMode,
                    info.TermVectorStoringMode,
                    info.IndexHandler,
                    info.Analyzer);
                sb.AppendLine();
            }
            return sb.ToString();
        }

    }
}
