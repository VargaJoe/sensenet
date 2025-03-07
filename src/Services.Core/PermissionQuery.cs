﻿using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SenseNet.ApplicationModel;
using SenseNet.Configuration;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Security;

namespace SenseNet.Services.Core
{
    public class PermissionQuery
    {
        private class AggregatedPermission
        {
            [JsonProperty("name")]
            public string Name;
            [JsonProperty("index")]
            public int Index;
            [JsonProperty("type")]
            public string Type
            {
                get
                {
                    if (AllowedFrom.Count * DeniedFrom.Count > 0)
                        return "deniedandallowed";
                    if (AllowedFrom.Count > 0)
                        return "allowed";
                    if (AllowedFrom.Count > 0)
                        return "denied";
                    return null;
                }
            }
            [JsonProperty("allowedFrom")]
            public readonly List<PermissionSource> AllowedFrom = new List<PermissionSource>();
            [JsonProperty("deniedFrom")]
            public readonly List<PermissionSource> DeniedFrom = new List<PermissionSource>();
        }
        private class PermissionSource
        {
            [JsonProperty("path")]
            public string Path;
            [JsonProperty("identity")]
            public object Identity;
        }

        /// <summary>Gets aggregated permission information on the content
        /// for the specified user.</summary>
        /// <param name="content"></param>
        /// <param name="identity">User content path.</param>
        /// <returns>An array of permission entries relevant for the provided
        /// user and their groups.</returns>
        /// <example>
        /// <code>
        /// [
        ///      {
        ///          "identity": {
        ///              "id": 1234,
        ///              "path": "/Root/IMS/Public/user1",
        ///              "name": "user1",
        ///              "displayName": "User 1",
        ///              "kind": "User",
        ///              "domain": "public"
        ///          },
        ///         "permissions": [
        ///             {
        ///                 "name": "See",
        ///                 "index": 0,
        ///                 "type": "allowed",
        ///                 "allowfrom": [
        ///                     "path": "/Root/Content/SampleWorkspace",
        ///                     "identity": "..../IdentitfiedUsers"
        ///                 ],
        ///                 "denyfrom": [
        ///                     "path": "/Root/Content",
        ///                     "identity": "..../alba"
        ///                 ]
        ///             }
        ///      }
        /// ]
        /// </code>
        /// </example>
        [ODataFunction(Category = "Permissions")]
        public static object GetPermissionOverview(Content content, string identity)
        {
            if (string.IsNullOrEmpty(identity))
                throw new ArgumentException("Please provide an identity path");

            var user = Node.Load<User>(identity);
            if (user == null)
                throw new ArgumentException("Identity must be an existing user.");

            if (!content.Security.HasPermission(PermissionType.SeePermissions))
            {
                return new[]
                {
                    new Dictionary<string, object>
                    {
                        {"identity", GetIdentity(user)},
                        {"permissions", new AggregatedPermission[0]}
                    }
                };
            }

            return GetOverviewAce(content, user);
        }


        internal static Dictionary<string, object>[] GetOverviewAce(Content content, User user)
        {
            var relatedIdentities = Providers.Instance.SecurityHandler.GetGroupsWithOwnership(content.Id, user).ToList();
            relatedIdentities.Add(user.Id);

            var acl = SnAccessControlList.GetAcl(content.Id);
            var relatedEntries = acl.Entries.Where(e => relatedIdentities.Contains(e.Identity.NodeId)).ToArray();
            return CreateOverviewAce(user, relatedEntries);
        }
        private static Dictionary<string, object>[] CreateOverviewAce(User user, SnAccessControlEntry[] relatedEntries)
        {
            return new[]
            {
                new Dictionary<string, object>
                {
                    { "identity", GetIdentity(user) },
                    // { "propagates", entry.Propagates },
                    { "permissions", GetAggregatedPermissions(relatedEntries) }
                }
            };
        }
        private static object[] GetAggregatedPermissions(SnAccessControlEntry[] relatedEntries)
        {
            //    "permissions": [
            //        {
            //            "name": "See",
            //            "index": 0,
            //            "type": "allowed" // "denied" | "deniedandallowed"
            //            "allowfrom": [
            //                "path": "/Root/Sites/Default_Site/workspaces"
            //                "identity": "..../IdentitfiedUsers"
            //            ]
            //            "denyfrom": [
            //                "path": "/Root/Sites/Default_Site"
            //                "identity": "..../alba"
            //            ]
            //        },


            var aggregatedPermissions = new List<AggregatedPermission>();
            for (var i = 0; i < relatedEntries.Length; i++)
            {
                var entry = relatedEntries[i];
                if (i == 0)
                {
                    aggregatedPermissions.AddRange(entry.Permissions.Select(permission => new AggregatedPermission
                    {
                        Name = permission.Name, Index = PermissionType.GetByName(permission.Name).Index
                    }));
                }
                foreach (var perm in entry.Permissions)
                {
                    var aggregatedPermission = aggregatedPermissions.First(x => x.Name == perm.Name);
                    if (perm.Allow)
                    {
                        aggregatedPermission.AllowedFrom.Add(new PermissionSource
                        {
                            Path = perm.AllowFrom,
                            Identity = GetIdentity(entry)
                        });
                    }
                    else if (perm.Deny)
                    {
                        aggregatedPermission.DeniedFrom.Add(new PermissionSource
                        {
                            Path = perm.DenyFrom,
                            Identity = GetIdentity(entry)
                        });
                    }
                }
            }

            if (!aggregatedPermissions.Any())
            {
                aggregatedPermissions.AddRange(PermissionType.PermissionTypes.Select(permissionType => new AggregatedPermission
                {
                    Name = permissionType.Name,
                    Index = permissionType.Index
                }));
            }

            return aggregatedPermissions.ToArray();
        }

        internal static object GetIdentity(SnAccessControlEntry entry)
        {
            return GetIdentity(Node.LoadNode(entry.Identity.Path));
        }
        internal static object GetIdentity(Node node)
        {
            if (node == null)
                throw new ArgumentException("Identity not found");

            var seeOnly = !node.Security.HasPermission(PermissionType.Open);

            string domain = null;
            string avatar = null;
            SnIdentityKind kind;
            if (node is User userNode)
            {
                kind = SnIdentityKind.User;
                domain = seeOnly ? null : userNode.Domain;
                avatar = seeOnly ? null : userNode.AvatarUrl;
            }
            else if (node is Group groupNode)
            {
                kind = SnIdentityKind.Group;
                domain = seeOnly ? null : groupNode.Domain?.Name;
            }
            else
            {
                kind = SnIdentityKind.OrganizationalUnit;
            }

            return new Dictionary<string, object>
            {
                { "id", node.Id },
                { "path", node.Path },
                { "name", node.Name },
                { "displayName", seeOnly ? node.Name : SNSR.GetString(node.DisplayName) },
                { "domain", domain },
                { "kind", kind.ToString().ToLower() },
                { "avatar", avatar }
            };
        }
    }
}
