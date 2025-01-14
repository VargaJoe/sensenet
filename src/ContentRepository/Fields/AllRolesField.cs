﻿using System.Linq;
using System.Xml;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Schema;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Security;

namespace SenseNet.ContentRepository.Fields
{
    [ShortName("AllRoles")]
    [DefaultFieldSetting(typeof(NullFieldSetting))]
    public class AllRolesField : ReferenceField
    {
        public override bool ReadOnly => true;

        protected override void ImportData(XmlNode fieldNode, ImportContext context)
        {
            throw new SnNotSupportedException();
        }

        public override object GetData()
        {
            // this method will load only containers that the current user has permissions for
            return Node.LoadNodes(Providers.Instance.SecurityHandler.SecurityContext
                .GetParentGroups(this.Content.Id, false)).ToArray();
        }
    }
}
