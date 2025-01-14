﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SenseNet.ApplicationModel;
using SenseNet.Configuration;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Security;
using SenseNet.ContentRepository.Versioning;
using SenseNet.Extensions.DependencyInjection;
using SenseNet.OData;
using SenseNet.ODataTests.Responses;
using SenseNet.Services.Wopi;
using Task = System.Threading.Tasks.Task;
// ReSharper disable UnusedVariable
// ReSharper disable NotAccessedVariable
// ReSharper disable RedundantAssignment
// ReSharper disable UnusedAutoPropertyAccessor.Local
// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo

namespace SenseNet.ODataTests
{
    [TestClass]
    public class ODataOperationMethodTests : ODataTestBase
    {
        /* ====================================================================== OPERATION INFO TESTS */

        [TestMethod]
        public void OD_MBO_GetInfo_0Prm()
        {
            ODataTest(() =>
            {
                var method = new TestMethodInfo("fv1", null, null);

                var info = AddMethod(method);

                Assert.IsNull(info);
            });
        }
        [TestMethod]
        public void OD_MBO_GetInfo_1Prm_Invalid()
        {
            ODataTest(() =>
            {
                var method = new TestMethodInfo("fv1", "string a", null);

                var info = AddMethod(method);

                Assert.IsNull(info);
            });
        }
        [TestMethod]
        public void OD_MBO_GetInfo_1Prm_Optional()
        {
            ODataTest(() =>
            {
                var method = new TestMethodInfo("fv1", null, "Content content");

                var info = AddMethod(method);

                Assert.IsNull(info);
            });
        }
        [TestMethod]
        public void OD_MBO_GetInfo_1Prm()
        {
            ODataTest(() =>
            {
                var method = new TestMethodInfo("fv1", "Content content", null);

                var info = AddMethod(method);

                Assert.AreEqual(0, info.RequiredParameterNames.Length);
                Assert.AreEqual(0, info.RequiredParameterTypes.Length);
                Assert.AreEqual(0, info.OptionalParameterNames.Length);
                Assert.AreEqual(0, info.OptionalParameterTypes.Length);
            });
        }

        [TestMethod]
        public void OD_MBO_GetInfo_5Prm2()
        {
            ODataTest(() =>
            {
                var method = new TestMethodInfo("fv1",
                    "Content content, string a, int b",
                    "string c, object d");

                // ACTION
                var info = AddMethod(method);

                // ASSERT
                Assert.AreEqual(method, info.Method);
                Assert.AreEqual(2, info.OptionalParameterNames.Length);
                Assert.AreEqual(
                    "a,b",
                    string.Join(",", info.RequiredParameterNames));
                Assert.AreEqual(
                    "String,Int32",
                    string.Join(",", info.RequiredParameterTypes.Select(t => t.Name).ToArray()));
                Assert.AreEqual(
                    "c,d",
                    string.Join(",", info.OptionalParameterNames));
                Assert.AreEqual(
                    "String,Object",
                    string.Join(",", info.OptionalParameterTypes.Select(t => t.Name).ToArray()));
            });
        }

        [TestMethod]
        public void OD_MBO_GetInfo_AllowedArrays()
        {
            ODataTest(() =>
            {
                var method = new TestMethodInfo("fv1",
                    "Content content, object[] a, string[] b, int[] c, long[] d, bool[] e, float[] f, double[] g, decimal[] h",
                    null);

                // ACTION
                var info = AddMethod(method);

                // ASSERT
                Assert.AreEqual(8, info.RequiredParameterNames.Length);
                Assert.AreEqual(8, info.RequiredParameterTypes.Length);
                Assert.AreEqual(0, info.OptionalParameterNames.Length);
                Assert.AreEqual(0, info.OptionalParameterTypes.Length);
                Assert.AreEqual(typeof(object[]), info.RequiredParameterTypes[0]);
                Assert.AreEqual(typeof(string[]), info.RequiredParameterTypes[1]);
                Assert.AreEqual(typeof(int[]), info.RequiredParameterTypes[2]);
                Assert.AreEqual(typeof(long[]), info.RequiredParameterTypes[3]);
                Assert.AreEqual(typeof(bool[]), info.RequiredParameterTypes[4]);
                Assert.AreEqual(typeof(float[]), info.RequiredParameterTypes[5]);
                Assert.AreEqual(typeof(double[]), info.RequiredParameterTypes[6]);
                Assert.AreEqual(typeof(decimal[]), info.RequiredParameterTypes[7]);
            });
        }

        [TestMethod]
        public void OD_MBO_GetInfo_DisallowedArrays()
        {
            ODataTest(() =>
            {
                Assert.IsNull(AddMethod(new TestMethodInfo(
                    "fv1", "Content content, DateTime[] a", null)));
                Assert.IsNull(AddMethod(new TestMethodInfo(
                    "fv1", "Content content, Elephant[] a", null)));
                // etc.
            });
        }

        [TestMethod]
        public void OD_MBO_GetInfo_Attributes_ContentTypes()
        {
            ODataTest(() =>
            {
                var info = AddMethod(typeof(TestOperations).GetMethod("Op1"));
                Assert.AreEqual("Group,OrgUnit,User", ArrayToString(info.ContentTypes, true));
            });
        }
        [TestMethod]
        public void OD_MBO_GetInfo_Attributes_AllContentTypes()
        {
            ODataTest(() =>
            {
                var info = AddMethod(typeof(TestOperations).GetMethod("Op2"));
                Assert.AreEqual("", ArrayToString(info.ContentTypes, true));
            });
        }
        [TestMethod]
        public void OD_MBO_GetInfo_Attributes_DefaultContentTypes()
        {
            ODataTest(() =>
            {
                var info = AddMethod(typeof(TestOperations).GetMethod("Op3"));
                Assert.AreEqual("GenericContent", ArrayToString(info.ContentTypes, true));
            });
        }
        [TestMethod]
        public void OD_MBO_GetInfo_Attributes_Scenario()
        {
            ODataTest(() =>
            {
                var info = AddMethod(typeof(TestOperations).GetMethod("Op1"));
                Assert.AreEqual("Scenario1,Scenario2,Scenario3,Scenario4", ArrayToString(info.Scenarios, true));
            });
        }
        [TestMethod]
        public void OD_MBO_GetInfo_Attributes_SnAuthorize()
        {
            ODataTest(() =>
            {
                var info = AddMethod(typeof(TestOperations).GetMethod("Op2"));
                Assert.AreEqual("/Root/IMS/BuiltIn/Portal/Administrators,path/Editors,path/Visitor",
                    ArrayToString(info.Roles, true));
                Assert.AreEqual("P1,P2,P3", ArrayToString(info.Permissions, true));
                Assert.AreEqual("Policy1,Policy2,Policy3", ArrayToString(info.Policies, true));
            });
        }

        [TestMethod]
        public void OD_MBO_GetInfo_Attributes_DescriptionIcon()
        {
            ODataTest(() =>
            {
                // [ODataFunction(Description = "Lorem ipsum ...")]
                var info = AddMethod(typeof(TestOperations).GetMethod("Op6"));
                Assert.AreEqual("Lorem ipsum ...", info.Description);
                Assert.AreEqual("Application", info.Icon);

                // [ODataFunction(Icon = "icon42")]
                info = AddMethod(typeof(TestOperations).GetMethod("Op7"));
                Assert.AreEqual(null, info.Description);
                Assert.AreEqual("icon42", info.Icon);

                // [ODataFunction(Description = "Lorem ipsum ...", Icon = "icon94")]
                info = AddMethod(typeof(TestOperations).GetMethod("Op8"));
                Assert.AreEqual("Lorem ipsum ...", info.Description);
                Assert.AreEqual("Application", info.Icon);

                //[ODataFunction(DisplayName = "Operation-Nine")]
                info = AddMethod(typeof(TestOperations).GetMethod("Op9"));
                Assert.AreEqual("Operation-Nine", info.DisplayName);

                // [ODataFunction("Op10_Renamed", Description = "Lorem ipsum ...", Icon = "icon94")]
                info = AddMethod(typeof(TestOperations).GetMethod("Op10"));
                Assert.AreEqual("Op10_Renamed", info.Name);
                Assert.AreEqual("Lorem ipsum ...", info.Description);
                Assert.AreEqual("icon94", info.Icon);
            });
        }

        [TestMethod]
        public void OD_MBO_GetInfo_SyncAsync()
        {
            ODataTest(() =>
            {
                const string n = "_syncasync";
                const string p = "Content content";

                var info1 = AddMethod(new TestMethodInfo($"{n}1", p, null, typeof(void)));
                var info2 = AddMethod(new TestMethodInfo($"{n}1", p, null, typeof(object)));
                var info3 = AddMethod(new TestMethodInfo($"{n}2", p, null, typeof(string)));
                var info4 = AddMethod(new TestMethodInfo($"{n}3", p, null, typeof(Task)));
                var info5 = AddMethod(new TestMethodInfo($"{n}4", p, null, typeof(Task<string>)));

                Assert.IsFalse(info1.IsAsync);
                Assert.IsFalse(info2.IsAsync);
                Assert.IsFalse(info3.IsAsync);
                Assert.IsTrue(info4.IsAsync);
                Assert.IsTrue(info5.IsAsync);
            });
        }
        
        /* ====================================================================== SEARCH METHOD TESTS */

        [TestMethod]
        public void OD_MBO_Discover()
        {
            ODataTest(() =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var countBefore = OperationCenter.Operations.Count;
                    Assert.AreEqual(0, countBefore);

                    // ACTION
                    OperationCenter.Discover();

                    // ASSERT
                    var countAfter = OperationCenter.Operations.Count;
                    Assert.IsTrue(countAfter > countBefore);
                }
            });
        }

        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Strict_1()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m0 = AddMethod(new TestMethodInfo("fv0", "Content content, string a", "int x"));
                    var m1 = AddMethod(new TestMethodInfo("fv1", "Content content, string a", "int x"));
                    var m2 = AddMethod(new TestMethodInfo("fv1", "Content content, string a", "string x"));
                    var m3 = AddMethod(new TestMethodInfo("fv2", "Content content, string a", "int x"));

                    // ACTION
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1",
                        @"{""a"":""asdf"",""b"":""qwer"",""y"":12,""x"":42}");
                    // ASSERT
                    Assert.AreEqual(m1, context.Operation);
                    Assert.AreEqual(2, context.Parameters.Count);
                    Assert.AreEqual("asdf", context.Parameters["a"]);
                    Assert.AreEqual(42, context.Parameters["x"]);
                }
            });
        }

        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Strict_2()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m0 = AddMethod(new TestMethodInfo("fv0", "Content content, string a", "int x"));
                    var m1 = AddMethod(new TestMethodInfo("fv1", "Content content, string a", "int x"));
                    var m2 = AddMethod(new TestMethodInfo("fv1", "Content content, string a", "string x"));
                    var m3 = AddMethod(new TestMethodInfo("fv2", "Content content, string a", "int x"));

                    // ACTION
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1",
                        @"{""a"":""asdf"",""b"":""qwer"",""y"":12,""x"":""42"" }");
                    // ASSERT
                    Assert.AreEqual(m2, context.Operation);
                    Assert.AreEqual(2, context.Parameters.Count);
                    Assert.AreEqual("asdf", context.Parameters["a"]);
                    Assert.AreEqual("42", context.Parameters["x"]);
                }
            });
        }

        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Bool()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m = AddMethod(new TestMethodInfo("fv1", "Content content, bool a", null));

                    // ACTION-1 strict
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":true}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(true, context.Parameters["a"]);

                    // ACTION not strict
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""true""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(true, context.Parameters["a"]);
                }
            });
        }
        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Bool_Nullable()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m = AddMethod(new TestMethodInfo("fv1", "Content content, bool? a", null));

                    // ACTION strict
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":true}");

                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(true, context.Parameters["a"]);

                    // ACTION null
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":null}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(null, context.Parameters["a"]);

                    // ACTION not strict
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""true""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(true, context.Parameters["a"]);
                }
            });
        }

        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Int_Nullable()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m = AddMethod(new TestMethodInfo("fv1", "Content content, int? a", null));

                    // ACTION strict
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":12345678}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(12345678, context.Parameters["a"]);

                    // ACTION null
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":null}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(null, context.Parameters["a"]);

                    // ACTION not strict
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""12345678""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(12345678, context.Parameters["a"]);
                }
            });
        }

        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Long()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m = AddMethod(new TestMethodInfo("fv1", "Content content, long a", null));

                    // ACTION-1 strict
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":123456789}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(123456789L, context.Parameters["a"]);

                    // ACTION not strict
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""123456789""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(123456789L, context.Parameters["a"]);
                }
            });
        }
        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Long_Nullable()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m = AddMethod(new TestMethodInfo("fv1", "Content content, long? a", null));

                    // ACTION strict
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":123456789}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(123456789L, context.Parameters["a"]);

                    // ACTION null
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":null}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(null, context.Parameters["a"]);

                    // ACTION not strict
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""123456789""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(123456789L, context.Parameters["a"]);
                }
            });
        }

        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Byte()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m = AddMethod(new TestMethodInfo("fv1", "Content content, byte a", null));

                    // ACTION-1 strict
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":142}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual((byte)142, context.Parameters["a"]);

                    // ACTION not strict
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""142""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual((byte)142, context.Parameters["a"]);
                }
            });
        }
        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Byte_Nullable()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m = AddMethod(new TestMethodInfo("fv1", "Content content, byte? a", null));

                    // ACTION strict
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":142}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual((byte)142, context.Parameters["a"]);

                    // ACTION null
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":null}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(null, context.Parameters["a"]);

                    // ACTION not strict
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""142""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual((byte)142, context.Parameters["a"]);
                }
            });
        }

        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Decimal()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m = AddMethod(new TestMethodInfo("fv1", "Content content, decimal a", null));

                    // ACTION-1 strict
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":0.123456789}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789m, context.Parameters["a"]);

                    // ACTION not strict, localized
                    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("hu-hu");
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""0,123456789""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789m, context.Parameters["a"]);

                    // ACTION not strict, globalized
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""0.123456789""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789m, context.Parameters["a"]);
                }
            });
        }
        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Decimal_Nullable()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m = AddMethod(new TestMethodInfo("fv1", "Content content, decimal? a", null));

                    // ACTION strict
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":0.123456789}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789m, context.Parameters["a"]);

                    // ACTION null
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":null}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(null, context.Parameters["a"]);

                    // ACTION not strict, localized
                    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("hu-hu");
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""0,123456789""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789m, context.Parameters["a"]);

                    // ACTION not strict, globalized
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""0.123456789""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789m, context.Parameters["a"]);
                }
            });
        }

        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Float()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m = AddMethod(new TestMethodInfo("fv1", "Content content, float a", null));

                    // ACTION-1 strict
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":0.123456789}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789f, context.Parameters["a"]);

                    // ACTION not strict, localized
                    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("hu-hu");
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""0,123456789""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789f, context.Parameters["a"]);

                    // ACTION not strict, globalized
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""0.123456789""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789f, context.Parameters["a"]);
                }
            });
        }
        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Float_Nullable()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m = AddMethod(new TestMethodInfo("fv1", "Content content, float? a", null));

                    // ACTION-1 strict
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":0.123456789}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789f, context.Parameters["a"]);

                    // ACTION null
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":null}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(null, context.Parameters["a"]);

                    // ACTION not strict, localized
                    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("hu-hu");
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""0,123456789""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789f, context.Parameters["a"]);

                    // ACTION not strict, globalized
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""0.123456789""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789f, context.Parameters["a"]);
                }
            });
        }

        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Double()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m = AddMethod(new TestMethodInfo("fv1", "Content content, double a", null));

                    // ACTION-1 strict
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":0.123456789}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789d, context.Parameters["a"]);

                    // ACTION not strict, localized
                    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("hu-hu");
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""0,123456789""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789d, context.Parameters["a"]);

                    // ACTION not strict, globalized
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""0.123456789""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789d, context.Parameters["a"]);
                }
            });
        }
        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Double_Nullable()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m = AddMethod(new TestMethodInfo("fv1", "Content content, double? a", null));

                    // ACTION-1 strict
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":0.123456789}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789d, context.Parameters["a"]);

                    // ACTION null
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":null}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(null, context.Parameters["a"]);

                    // ACTION not strict, localized
                    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("hu-hu");
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""0,123456789""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789d, context.Parameters["a"]);

                    // ACTION not strict, globalized
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""0.123456789""}");
                    // ASSERT
                    Assert.AreEqual(m, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(0.123456789d, context.Parameters["a"]);
                }
            });
        }

        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Spaceship()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m1 = AddMethod(new TestMethodInfo("fv1", "Content content, Spaceship a", null));
                    var m2 = AddMethod(new TestMethodInfo("fv1", "Content content, Elephant a", null));

                    // ACTION-1
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1",
                        @"{""a"":{""Name"":""Space Bender 8"", ""Class"":""Big F Vehicle"", ""Length"":444}}");

                    // ASSERT
                    Assert.AreEqual(m1, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(typeof(Spaceship), context.Parameters["a"].GetType());
                    var spaceship = (Spaceship)context.Parameters["a"];
                    Assert.AreEqual("Space Bender 8", spaceship.Name);
                    Assert.AreEqual("Big F Vehicle", spaceship.Class);
                    Assert.AreEqual(444, spaceship.Length);
                }

            });
        }

        [TestMethod]
        public void OD_MBO_GetMethodByRequest_Elephant()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m1 = AddMethod(new TestMethodInfo("fv1", "Content content, Spaceship a", null));
                    var m2 = AddMethod(new TestMethodInfo("fv1", "Content content, Elephant a", null));

                    // ACTION-1
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1",
                        @"{""a"":{""Snout"":42, ""Height"":44}}");

                    // ASSERT
                    Assert.AreEqual(m2, context.Operation);
                    Assert.AreEqual(1, context.Parameters.Count);
                    Assert.AreEqual(typeof(Elephant), context.Parameters["a"].GetType());
                    var elephant = (Elephant)context.Parameters["a"];
                    Assert.AreEqual(42, elephant.Snout);
                    Assert.AreEqual(44, elephant.Height);
                }
            });
        }


        [TestMethod]
        public void OD_MBO_GetMethodByRequest_NotStrict_1()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m0 = AddMethod(new TestMethodInfo("fv0", "Content content, string a", "int x"));
                    var m1 = AddMethod(new TestMethodInfo("fv1", "Content content, string a", "int x"));
                    var m2 = AddMethod(new TestMethodInfo("fv1", "Content content, string a", "bool x"));
                    var m3 = AddMethod(new TestMethodInfo("fv2", "Content content, string a", "int x"));

                    // ACTION-1
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1",
                        @"{""a"":""asdf"",""b"":""qwer"",""y"":12,""x"":""42""}");

                    // ASSERT-1
                    Assert.AreEqual(m1, context.Operation);
                    Assert.AreEqual(2, context.Parameters.Count);
                    Assert.AreEqual("asdf", context.Parameters["a"]);
                    Assert.AreEqual(42, context.Parameters["x"]);

                    // ACTION-2
                    context = OperationCenter.GetMethodByRequest(GetContent(), "fv1",
                        @"{ ""a"":""asdf"",""b"":""qwer"",""y"":12,""x"":""true""}");

                    // ASSERT-2
                    Assert.AreEqual(m2, context.Operation);
                    Assert.AreEqual(2, context.Parameters.Count);
                    Assert.AreEqual("asdf", context.Parameters["a"]);
                    Assert.AreEqual(true, context.Parameters["x"]);
                }
            });
        }

        [TestMethod]
        [ExpectedException(typeof(OperationNotFoundException))]
        public void OD_MBO_GetMethodByRequest_NotFound_ByName()
        {
            ODataTest(() =>
            {
                using (new CleanOperationCenterBlock())
                {
                    AddMethod(new TestMethodInfo("fv0", "Content content, string a", "int x"));

                    // ACTION
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""asdf""}");
                }
            });
        }

        [TestMethod]
        [ExpectedException(typeof(OperationNotFoundException))]
        public void OD_MBO_GetMethodByRequest_NotFound_ByRequiredParamName()
        {
            ODataTest(() =>
            {
                using (new CleanOperationCenterBlock())
                {
                    AddMethod(new TestMethodInfo("fv0", "Content content, string a, string b", null));

                    // ACTION
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv0", @"{""a"":""asdf""}");
                }
            });
        }

        [TestMethod]
        [ExpectedException(typeof(OperationNotFoundException))]
        public void OD_MBO_GetMethodByRequest_NotFound_ByRequiredParamType()
        {
            ODataTest(() =>
            {
                using (new CleanOperationCenterBlock())
                {
                    AddMethod(new TestMethodInfo("fv0", "Content content, string a, string b", null));

                    // ACTION
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv0", @"{""a"":""asdf"",""b"":42}");
                }
            });
        }

        [TestMethod]
        [ExpectedException(typeof(AmbiguousMatchException))]
        public void OD_MBO_GetMethodByRequest_AmbiguousMatch()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m0 = AddMethod(new TestMethodInfo("fv0", "Content content, string a", "int x"));
                    var m1 = AddMethod(new TestMethodInfo("fv1", "Content content, string a", "int x"));
                    var m2 = AddMethod(new TestMethodInfo("fv1", "Content content, string a", "string x"));
                    var m3 = AddMethod(new TestMethodInfo("fv2", "Content content, string a", "int x"));

                    // ACTION
                    var context = OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{""a"":""asdf""}");
                }
            });
        }

        [TestMethod]
        [ExpectedException(typeof(OperationNotFoundException))]
        public void OD_MBO_GetMethodByRequest_UnmatchedOptional()
        {
            ODataTest(() =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m0 = AddMethod(new TestMethodInfo("fv0", "Content content, string a", "int x"));
                    var m1 = AddMethod(new TestMethodInfo("fv1", "Content content, string a", "int x"));
                    var m2 = AddMethod(new TestMethodInfo("fv1", "Content content, string a", "bool x"));
                    var m3 = AddMethod(new TestMethodInfo("fv2", "Content content, string a", "int x"));

                    // ACTION
                    var context =
                        OperationCenter.GetMethodByRequest(GetContent(), "fv1", @"{ ""a"":""asdf"",""x"":""asdf""}");
                }
            });
        }

        [TestMethod]
        public void OD_MBO_GetMethodByRequest_CaseInsensitiveParamNames()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m0 = AddMethod(new TestMethodInfo("method0", "Content content, string param1", null));
                    var m1 = AddMethod(new TestMethodInfo("method1", "Content content", "string param1"));

                    var ctx = OperationCenter.GetMethodByRequest(GetContent(),
                        "method0", @"models=[{ param1:""asdf""}]");
                    Assert.AreEqual(m0, ctx.Operation);

                    ctx = OperationCenter.GetMethodByRequest(GetContent(),
                        "method0", @"models=[{ Param1:""asdf""}]");
                    Assert.AreEqual(m0, ctx.Operation);

                    ctx = OperationCenter.GetMethodByRequest(GetContent(),
                        "method0", @"models=[{ pArAm1:""asdf""}]");
                    Assert.AreEqual(m0, ctx.Operation);

                    ctx = OperationCenter.GetMethodByRequest(GetContent(),
                        "method1", @"models=[{ param1:""asdf""}]");
                    Assert.AreEqual(m1, ctx.Operation);

                    ctx = OperationCenter.GetMethodByRequest(GetContent(),
                        "method1", @"models=[{ Param1:""asdf""}]");
                    Assert.AreEqual(m1, ctx.Operation);

                    ctx = OperationCenter.GetMethodByRequest(GetContent(),
                        "method1", @"models=[{ pArAm1:""asdf""}]");
                    Assert.AreEqual(m1, ctx.Operation);
                }
            });
        }

        /* ====================================================================== REQUEST PARSING TESTS */

        [TestMethod]
        [ExpectedException(typeof(JsonReaderException))]
        public void OD_MBO_Request_InvalidJson()
        {
            var request = ODataMiddleware.ReadToJson("asdf");
        }
        [TestMethod]
        public void OD_MBO_Request_Invalid()
        {
            var request = ODataMiddleware.ReadToJson("");
            Assert.IsNull(request);

            request = ODataMiddleware.ReadToJson("['asdf']");
            Assert.IsNull(request);
        }
        [TestMethod]
        public void OD_MBO_Request_Object()
        {
            var request = ODataMiddleware.ReadToJson("{'a':'asdf'}");
            Assert.AreEqual(JTokenType.String, request["a"].Type);
            Assert.AreEqual("{\"a\":\"asdf\"}", request.ToString()
                .Replace("\r", "").Replace("\n", "").Replace(" ", ""));
        }
        [TestMethod]
        public void OD_MBO_Request_Models()
        {
            var request = ODataMiddleware.ReadToJson("models=[{'a':'asdf'}]");
            Assert.AreEqual(JTokenType.String, request["a"].Type);
            Assert.AreEqual("{\"a\":\"asdf\"}", request.ToString()
                .Replace("\r", "").Replace("\n", "").Replace(" ", ""));
        }
        [TestMethod]
        public void OD_MBO_Request_Properties()
        {
            var request = ODataMiddleware.ReadToJson("models=[{'a':42, 'b':false, 'c':'asdf', 'd':[], 'e':{a:12}}]");
            Assert.AreEqual(JTokenType.Integer, request["a"].Type);
            Assert.AreEqual(JTokenType.Boolean, request["b"].Type);
            Assert.AreEqual(JTokenType.String, request["c"].Type);
            Assert.AreEqual(JTokenType.Array, request["d"].Type);
            Assert.AreEqual(JTokenType.Object, request["e"].Type);
        }
        [TestMethod]
        public void OD_MBO_Request_SpecialChars()
        {
            var request = ODataMiddleware.ReadToJson("models=[{'a':'b=c&d=e'}]");

            Assert.IsTrue(request.ContainsKey("a"), "Property 'a' not found.");
            Assert.AreEqual(1, request.Count, "Incorrect number of child elements.");

            Assert.AreEqual(JTokenType.String, request["a"].Type);
            Assert.AreEqual("b=c&d=e", request["a"].Value<string>());

            request = ODataMiddleware.ReadToJson("models=[{'a':'b=c&d=e&$f=g&#h=i'}]");
            Assert.AreEqual(JTokenType.String, request["a"].Type);
            Assert.AreEqual("b=c&d=e&$f=g&#h=i", request["a"].Value<string>());
        }
        [TestMethod]
        public void OD_MBO_Request_FormsEncoded()
        {
            var request = ODataMiddleware.ReadToJson("a=b&c=d");

            Assert.IsTrue(request.ContainsKey("a"), "Property 'a' not found.");
            Assert.AreEqual(2, request.Count, "Incorrect number of child elements.");

            Assert.AreEqual(JTokenType.String, request["a"].Type);
            Assert.AreEqual("b", request["a"].Value<string>());
            Assert.AreEqual("d", request["c"].Value<string>());
        }

        [TestMethod]
        public void OD_MBO_Request_Float()
        {
            var request = ODataMiddleware.ReadToJson("models=[{'a':4.2}]");
            Assert.AreEqual(JTokenType.Float, request["a"].Type);
            Assert.AreEqual(4.2f, request["a"].Value<float>());
            Assert.AreEqual(4.2d, request["a"].Value<double>());
            Assert.AreEqual(4.2m, request["a"].Value<decimal>());
        }
        [TestMethod]
        public void OD_MBO_Request_StringArray()
        {
            var request = ODataMiddleware.ReadToJson("models=[{'a':['xxx','yyy','zzz']}]");

            var array = (JArray)request["a"];
            Assert.AreEqual(JTokenType.Array, array.Type);
            var stringArray = array.Select(x => x.Value<string>()).ToArray();
            var actual = string.Join(",", stringArray);
            Assert.AreEqual("xxx,yyy,zzz", actual);
        }
        [TestMethod]
        public void OD_MBO_Request_ObjectArray()
        {
            var request = ODataMiddleware.ReadToJson("models=[{'a':[1,'xxx',false,42]}]");

            var array = (JArray)request["a"];
            Assert.AreEqual(JTokenType.Array, array.Type);
            var objectArray = array.Cast<object>().ToArray();
            var actual = string.Join(",", objectArray.Select(x=>x.ToString()));
            Assert.AreEqual("1,xxx,False,42", actual);
        }

        /* ====================================================================== CALLING TESTS */

        [TestMethod]
        public void OD_MBO_Call_RequiredPrimitives()
        {
            ODataTest(() =>
            {
                var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), "Op1",
                    @"{""a"":""asdf"", ""b"":42, ""c"":true, ""d"":0.12, ""e"":0.13, ""f"":0.14}");

                // ACTION
                var result = OperationCenter.Invoke(context);

                // ASSERT
                var objects = (object[]) result;
                Assert.AreEqual("asdf", objects[0]);
                Assert.AreEqual(42, objects[1]);
                Assert.AreEqual(true, objects[2]);
                Assert.AreEqual(0.12f, objects[3]);
                Assert.AreEqual(0.13m, objects[4]);
                Assert.AreEqual(0.14d, objects[5]);
            });
        }
        [TestMethod]
        public void OD_MBO_Call_Primitives_QueryString()
        {
            ODataTest(() =>
            {
                var _ = ODataMiddleware.ODataRequestHttpContextKey; // need to touch ODataMiddleware
                var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), "Op1",
                    null, new TestQueryCollection(@"?a=asdf&b=42&c=true&d=0.12&e=0.13&f=0.14"));

                // ACTION
                var result = OperationCenter.Invoke(context);

                // ASSERT
                var objects = (object[])result;
                Assert.AreEqual("asdf", objects[0]);
                Assert.AreEqual(42, objects[1]);
                Assert.AreEqual(true, objects[2]);
                Assert.AreEqual(0.12f, objects[3]);
                Assert.AreEqual(0.13m, objects[4]);
                Assert.AreEqual(0.14d, objects[5]);
            });
        }
        [TestMethod]
        public void OD_MBO_Call_Primitives_QueryString_AposAndQuot()
        {
            ODataTest(() =>
            {
                var _ = ODataMiddleware.ODataRequestHttpContextKey; // need to touch ODataMiddleware
                var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), "Op2",
                    null, new TestQueryCollection("?a=(')(\")"));

                // ACTION
                var result = OperationCenter.Invoke(context);

                // ASSERT
                var objects = (object[])result;
                Assert.AreEqual("(')(\")", objects[0]);
            });
        }

        [TestMethod]
        public void OD_MBO_Call_OptionalPrimitives()
        {
            ODataTest(() =>
            {
                // ACTION
                var context = OperationCenter.GetMethodByRequest(GetContent(), "Op2", @"{""dummy"":0}");
                var result = OperationCenter.Invoke(context);

                // ASSERT
                var objects = (object[]) result;
                Assert.AreEqual(null, objects[0]);
                Assert.AreEqual(0, objects[1]);
                Assert.AreEqual(false, objects[2]);
                Assert.AreEqual(0.0f, objects[3]);
                Assert.AreEqual(0.0m, objects[4]);
                Assert.AreEqual(0.0d, objects[5]);

                // ACTION
                context = OperationCenter.GetMethodByRequest(GetContent(), "Op2", @"{""a"":""testvalue""}");
                result = OperationCenter.Invoke(context);

                // ASSERT
                objects = (object[]) result;
                Assert.AreEqual("testvalue", objects[0]);
                Assert.AreEqual(0, objects[1]);
                Assert.AreEqual(false, objects[2]);
                Assert.AreEqual(0.0f, objects[3]);
                Assert.AreEqual(0.0m, objects[4]);
                Assert.AreEqual(0.0d, objects[5]);

                // ACTION
                context = OperationCenter.GetMethodByRequest(GetContent(), "Op2", @"{""b"":42}");
                result = OperationCenter.Invoke(context);

                // ASSERT
                objects = (object[]) result;
                Assert.AreEqual(null, objects[0]);
                Assert.AreEqual(42, objects[1]);
                Assert.AreEqual(false, objects[2]);
                Assert.AreEqual(0.0f, objects[3]);
                Assert.AreEqual(0.0m, objects[4]);
                Assert.AreEqual(0.0d, objects[5]);

                // ACTION
                context = OperationCenter.GetMethodByRequest(GetContent(), "Op2", @"{""c"":true}");
                result = OperationCenter.Invoke(context);

                // ASSERT
                objects = (object[]) result;
                Assert.AreEqual(null, objects[0]);
                Assert.AreEqual(0, objects[1]);
                Assert.AreEqual(true, objects[2]);
                Assert.AreEqual(0.0f, objects[3]);
                Assert.AreEqual(0.0m, objects[4]);
                Assert.AreEqual(0.0d, objects[5]);

                // ACTION
                context = OperationCenter.GetMethodByRequest(GetContent(), "Op2", @"{""d"":12.345}");
                result = OperationCenter.Invoke(context);

                // ASSERT
                objects = (object[]) result;
                Assert.AreEqual(null, objects[0]);
                Assert.AreEqual(0, objects[1]);
                Assert.AreEqual(false, objects[2]);
                Assert.AreEqual(12.345f, objects[3]);
                Assert.AreEqual(0.0m, objects[4]);
                Assert.AreEqual(0.0d, objects[5]);

                // ACTION
                context = OperationCenter.GetMethodByRequest(GetContent(), "Op2", @"{""e"":12.345}");
                result = OperationCenter.Invoke(context);

                // ASSERT
                objects = (object[]) result;
                Assert.AreEqual(null, objects[0]);
                Assert.AreEqual(0, objects[1]);
                Assert.AreEqual(false, objects[2]);
                Assert.AreEqual(0.0f, objects[3]);
                Assert.AreEqual(12.345m, objects[4]);
                Assert.AreEqual(0.0d, objects[5]);

                // ACTION
                context = OperationCenter.GetMethodByRequest(GetContent(), "Op2", @"{""f"":12.345}");
                result = OperationCenter.Invoke(context);

                // ASSERT
                objects = (object[]) result;
                Assert.AreEqual(null, objects[0]);
                Assert.AreEqual(0, objects[1]);
                Assert.AreEqual(false, objects[2]);
                Assert.AreEqual(0.0f, objects[3]);
                Assert.AreEqual(0.0m, objects[4]);
                Assert.AreEqual(12.345d, objects[5]);
            });
        }

        [TestMethod]
        public void OD_MBO_Call_MinimalParameters()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                // ACTION
                var context = OperationCenter.GetMethodByRequest(GetContent(), "Op3", @"{""dummy"":1}");
                var result = OperationCenter.Invoke(context);
                // ASSERT
                Assert.AreEqual("Called", result);
            });
        }

        [TestMethod]
        public void OD_MBO_Call_NullAndDefault()
        {
            ODataTest(() =>
            {
                var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), "Op1",
                    @"{""a"":null, ""b"":null, ""c"":null, ""d"":null, ""e"":null, ""f"":null}");

                // ACTION
                var result = OperationCenter.Invoke(context);

                // ASSERT
                var objects = (object[]) result;
                Assert.AreEqual(null, objects[0]);
                Assert.AreEqual(0, objects[1]);
                Assert.AreEqual(false, objects[2]);
                Assert.AreEqual(0.0f, objects[3]);
                Assert.AreEqual(0.0m, objects[4]);
                Assert.AreEqual(0.0d, objects[5]);
            });
        }

        [TestMethod]
        public void OD_MBO_Call_UndefinedAndDefault()
        {
            ODataTest(() =>
            {
                var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), "Op1",
                    @"{""a"":undefined, ""b"":undefined, ""c"":undefined, ""d"":undefined, ""e"":undefined, ""f"":undefined}");

                // ACTION
                var result = OperationCenter.Invoke(context);

                // ASSERT
                var objects = (object[]) result;
                Assert.AreEqual(null, objects[0]);
                Assert.AreEqual(0, objects[1]);
                Assert.AreEqual(false, objects[2]);
                Assert.AreEqual(0.0f, objects[3]);
                Assert.AreEqual(0.0m, objects[4]);
                Assert.AreEqual(0.0d, objects[5]);
            });
        }

        [TestMethod]
        public void OD_MBO_Call_Inspection()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                var inspector = (AllowEverything)Providers.Instance.Services.GetRequiredService<OperationInspector>();
                var content = GetContent(null, "User");

                // ACTION
                var context = OperationCenter.GetMethodByRequest(content, "Op1",
                    @"{""a"":""asdf"", ""b"":42, ""c"":true, ""d"":0.12, ""e"":0.13, ""f"":0.14}");
                var result = OperationCenter.Invoke(context);

                // ASSERT
                var lines = inspector.Log.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
                Assert.AreEqual(4, lines.Length);
                Assert.AreEqual("CheckContentType: User ==>, User,Group,OrgUnit", lines[0]);
                Assert.AreEqual("CheckByRoles: 1, /Root/IMS/BuiltIn/Portal/Administrators,path/Editors", lines[1]);
                Assert.AreEqual("CheckByPermissions: 0, 1, See,Run", lines[2]);
                Assert.AreEqual("CheckPolicies: 1, Policy1", lines[3]);
            });
        }

        [TestMethod]
        public void OD_MBO_Call_Renamed()
        {
            ODataTest(() =>
            {
                var inspector = new AllowEverything();
                var content = Content.Load("/Root/IMS");

                // ACTION
                var context = OperationCenter.GetMethodByRequest(content, TestOperations.GoodMethodName,
                    @"{""a"":""paramValue""}");
                var result = OperationCenter.Invoke(context);

                // ASSERT
                Assert.AreEqual("WrongMethodName-paramValue", result.ToString());
            });
        }

        [TestMethod]
        public void OD_MBO_Call_InsensitiveMethodName()
        {
            // Do not execute if the case insensivity is disabled
            if (!OperationCenter.IsCaseInsensitiveOperationNameEnabled)
                return;

            ODataTest(() =>
            {
                var inspector = new AllowEverything();
                var content = Content.Load("/Root/IMS");

                // ACTION
                var context = OperationCenter.GetMethodByRequest(content, 
                    nameof(TestOperations.SensitiveMethodName).ToLowerInvariant(),
                    @"{""a"":""paramValue""}");
                var result = OperationCenter.Invoke(context);

                // ASSERT
                Assert.AreEqual("SensitiveMethodName-paramValue", result.ToString());
            });
        }

        [TestMethod]
        public void OD_MBO_Call_InsensitiveMethodName_Renamed()
        {
            // Do not execute if the case insensivity is disabled
            if (!OperationCenter.IsCaseInsensitiveOperationNameEnabled)
                return;

            ODataTest(() =>
            {
                var inspector = new AllowEverything();
                var content = Content.Load("/Root/IMS");

                // ACTION
                var context = OperationCenter.GetMethodByRequest(content, TestOperations.GoodMethodName.ToLowerInvariant(),
                    @"{""a"":""paramValue""}");
                var result = OperationCenter.Invoke(context);

                // ASSERT
                Assert.AreEqual("WrongMethodName-paramValue", result.ToString());
            });
        }

        [TestMethod]
        public async Task OD_MBO_Call_Async()
        {
            await ODataTestAsync(async () =>
            {
                var inspector = new AllowEverything();
                var content = Content.Load("/Root/IMS");

                // ACTION
                var context = OperationCenter.GetMethodByRequest(content,
                    nameof(TestOperations.AsyncMethod),
                    @"{""a"":""qwer""}");
                var result = await OperationCenter.InvokeAsync(context).ConfigureAwait(false);

                // ASSERT
                Assert.AreEqual("AsyncMethod-qwer", result.ToString());
            }).ConfigureAwait(false);
        }
        [TestMethod]
        public async Task OD_MBO_Call_Async_GET()
        {
            await ODataTestAsync(async () =>
            {
                // ACTION
                var response = await ODataGetAsync($"/OData.svc/Root('IMS')/AsyncMethod",
                    "?param2=value2&a=value1").ConfigureAwait(false);

                // ASSERT
                Assert.AreEqual(200, response.StatusCode);
                Assert.AreEqual("AsyncMethod-value1", response.Result);
            }).ConfigureAwait(false);
        }
        [TestMethod]
        public async Task OD_MBO_Call_Async_POST()
        {
            await ODataTestAsync(async () =>
            {
                // ACTION
                var response = await ODataPostAsync($"/OData.svc/Root('IMS')/AsyncMethod", "?param2=value2",
                    $"{{a:\"paramValue\"}}").ConfigureAwait(false);

                // ASSERT
                Assert.AreEqual(200, response.StatusCode);
                Assert.AreEqual("AsyncMethod-paramValue", response.Result);
            }).ConfigureAwait(false);
        }

        /* ================================================================ REAL INSPECTION */

        [TestMethod]
        public void OD_MBO_Call_RealInspection_WithoutAuthorization()
        {
            ODataTest(() =>
            {
                RealInspectionTest(nameof(TestOperations.Authorization_None), null, 200);
                RealInspectionTest(nameof(TestOperations.Authorization_None), User.Administrator, 401, ODataExceptionCode.Unauthorized);
                RealInspectionTest(nameof(TestOperations.Authorization_None), User.Visitor, 404);
            });
        }
        [TestMethod]
        public void OD_MBO_Call_RealInspection_AuthorizationByRole()
        {
            ODataTest(() =>
            {
                RealInspectionTest(nameof(TestOperations.AuthorizedByRole_Administrators), null, 200);
                RealInspectionTest(nameof(TestOperations.AuthorizedByRole_Administrators), User.Administrator, 200);
                RealInspectionTest(nameof(TestOperations.AuthorizedByRole_Administrators), User.Visitor, 404);
            });
        }
        [TestMethod]
        public void OD_MBO_Call_RealInspection_AuthorizationByRole_Visitor()
        {
            ODataTest(() =>
            {
                using (new AllowPermissionBlock(Identifiers.PortalRootId, Identifiers.VisitorUserId,
                    false, PermissionType.See))
                {
                    RealInspectionTest(nameof(TestOperations.AuthorizedByRole_Visitor), null, 200);
                    RealInspectionTest(nameof(TestOperations.AuthorizedByRole_Visitor), User.Administrator, 403, ODataExceptionCode.Forbidden);
                    RealInspectionTest(nameof(TestOperations.AuthorizedByRole_Visitor), User.Visitor, 200);
                }
            });
        }
        [TestMethod]
        public void OD_MBO_Call_RealInspection_AuthorizationByRole_All()
        {
            ODataTest(() =>
            {
                using (new AllowPermissionBlock(Identifiers.PortalRootId, Identifiers.VisitorUserId,
                    false, PermissionType.See))
                {
                    RealInspectionTest(nameof(TestOperations.AuthorizedByRole_All), null, 200);
                    RealInspectionTest(nameof(TestOperations.AuthorizedByRole_All), User.Administrator, 200);
                    RealInspectionTest(nameof(TestOperations.AuthorizedByRole_All), User.Visitor, 200);
                }
            });
        }
        [TestMethod]
        public void OD_MBO_Call_RealInspection_AuthorizationByRole_All2()
        {
            ODataTest(() =>
            {
                using (new AllowPermissionBlock(Identifiers.PortalRootId, Identifiers.VisitorUserId,
                    false, PermissionType.See))
                {
                    RealInspectionTest(nameof(TestOperations.AuthorizedByRole_All2), null, 200);
                    RealInspectionTest(nameof(TestOperations.AuthorizedByRole_All2), User.Administrator, 200);
                    RealInspectionTest(nameof(TestOperations.AuthorizedByRole_All2), User.Visitor, 200);
                }
            });
        }
        [TestMethod]
        public void OD_MBO_Call_RealInspection_AuthorizationByPermission()
        {
            ODataTest(() =>
            {
                using (new AllowPermissionBlock(Identifiers.PortalRootId, Identifiers.VisitorUserId,
                    false, PermissionType.See))
                {
                    RealInspectionTest(nameof(TestOperations.AuthorizedByPermission), null, 200);
                    RealInspectionTest(nameof(TestOperations.AuthorizedByPermission), User.Administrator, 200);
                    RealInspectionTest(nameof(TestOperations.AuthorizedByPermission), User.Visitor, 403, ODataExceptionCode.Forbidden);
                }
            });
        }
        [TestMethod]
        public void OD_MBO_Call_RealInspection_AuthorizationByPolicy()
        {
            using (new PolicyStoreSwindler())
            {
                ODataTest(builder =>
                {
                    OperationCenter.Policies.Clear();

                    builder
                        // Register an inline policy
                        .UseOperationMethodExecutionPolicy("VisitorAllowed", (user, context) => 
                            user.Id == Identifiers.VisitorUserId ? OperationMethodVisibility.Enabled : OperationMethodVisibility.Disabled)
                        // Register a test policy class
                        .UseOperationMethodExecutionPolicy("AdminDenied",
                            new DeniedUsersOperationMethodPolicy(new []{Identifiers.AdministratorUserId}));

                }, () =>
                {
                    // TEST-1: Unknown policy (see TestOperations.AuthorizedByPolicy_Error method)
                    RealInspectionTest(nameof(TestOperations.AuthorizedByPolicy_Error), User.Administrator, 403,
                        ODataExceptionCode.Forbidden, "Policy not found: UnknownPolicy");

                    // TEST-2: System is allowed by both policies
                    RealInspectionTest(nameof(TestOperations.AuthorizedByPolicy), null, 200);
                    // TEST-3: Admin is denied by a policy
                    RealInspectionTest(nameof(TestOperations.AuthorizedByPolicy), User.Administrator, 403, ODataExceptionCode.Forbidden);
                    // TEST-4: Visitor is allowed by both policy but the content is not visible for her.
                    RealInspectionTest(nameof(TestOperations.AuthorizedByPolicy), User.Visitor, 404);

                    // Enable the content's visibility for the Visitor.
                    using (new AllowPermissionBlock(Identifiers.PortalRootId, Identifiers.VisitorUserId,
                        false, PermissionType.See))
                    {
                        Assert.IsTrue(Providers.Instance.SecurityHandler.HasPermission(User.Visitor, NodeHead.Get("/Root/IMS"), PermissionType.See));
                        // TEST-5: Visitor is allowed by both policy and the content.
                        RealInspectionTest(nameof(TestOperations.AuthorizedByPolicy), User.Visitor, 200);
                    }
                });
            }
        }

        private void RealInspectionTest(string methodName, IUser user, int expectedHttpCode,
            ODataExceptionCode? expectedODataExceptionCode = null, string expectedErrorMessage = null)
        {
            var magicValue = Guid.NewGuid().ToString();
            ODataResponse response;

            // ACTION
            using (user == null ? (IDisposable)new SystemAccount() : new CurrentUserBlock(user))
                response = ODataPostAsync($"/OData.svc/Root('IMS')/{methodName}", "?param2=value2",
                    $"{{a:\"{magicValue}\"}}").ConfigureAwait(false).GetAwaiter().GetResult();

            // ASSERT
            Assert.AreEqual(expectedHttpCode, response.StatusCode);
            if (expectedHttpCode == 200)
            {
                Assert.AreEqual($"{methodName}-{magicValue}", response.Result);
            }
            else if (expectedHttpCode == 404)
            {
                Assert.AreEqual(0, response.Result.Length);
            }
            else
            {
                var error = GetError(response, false);
                Assert.IsNotNull(error);
                if (expectedODataExceptionCode != null)
                    Assert.AreEqual(expectedODataExceptionCode.Value, error.Code);
                if (expectedErrorMessage != null)
                    Assert.AreEqual(expectedErrorMessage, error.Message);
            }
        }

        /* ================================================================ CALL */

        [TestMethod]
        public void OD_MBO_Call_JObject()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), "Op4",
                    @"{'a':{'Snout': 456, 'Height': 654}}");

                // ACTION
                var result = OperationCenter.Invoke(context);

                // ASSERT
                Assert.AreEqual(typeof(JObject), result.GetType());
                Assert.AreEqual("{\"Snout\":456,\"Height\":654}", RemoveWhitespaces(result.ToString()));
            });
        }
        [TestMethod]
        [ExpectedException(typeof(OperationNotFoundException))]
        public void OD_MBO_Call_JObject_QueryString()
        {
            // This version does not support JSON object in the querystring.
            ODataTest(() =>
            {
                var _ = ODataMiddleware.ODataRequestHttpContextKey; // need to touch ODataMiddleware
                var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), "Op4",
                    null, new TestQueryCollection(@"?a={'Snout': 456, 'Height': 654}"));
            });
        }

        [TestMethod]
        public void OD_MBO_Call_ObjectArray()
        {
            ODataTest2(services =>
            {
                services.AddSingleton<OperationInspector, AllowEverything>();
            }, () =>
            {
                var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), "Op5",
                    @"{'a':[42, 'xxx', true, 4.25, [1,2,3], {'Snout': 456, 'Height': 654}]}");

                // ACTION
                var result = OperationCenter.Invoke(context);

                // ASSERT
                var objects = (object[]) result;
                Assert.AreEqual(42L, objects[0]);
                Assert.AreEqual("xxx", objects[1]);
                Assert.AreEqual(true, objects[2]);
                Assert.AreEqual(4.25d, objects[3]);
                Assert.AreEqual(typeof(JArray), objects[4].GetType());
                Assert.AreEqual("[1,2,3]", RemoveWhitespaces(objects[4].ToString()));
                Assert.AreEqual(typeof(JObject), objects[5].GetType());
                Assert.AreEqual("{\"Snout\":456,\"Height\":654}", RemoveWhitespaces(objects[5].ToString()));
            });
        }

        [TestMethod]
        public void OD_MBO_Call_Array()
        {
            #region void Test<T>(string methodName, string request, T[] expectedResult)
            void Test<T>(string methodName, string request, T[] expectedResult)
            {
                ODataTest2(services =>
                {
                    services.AddSingleton<OperationInspector, AllowEverything>();
                }, () =>
                {
                    var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), methodName, request);

                    // ACTION
                    var result = OperationCenter.Invoke(context);

                    // ASSERT
                    var values = (T[]) result;
                    Assert.AreEqual(expectedResult.Length, values.Length);
                    for (var i = 0; i < expectedResult.Length; i++)
                        Assert.AreEqual(expectedResult[i], values[i],
                            $"Assert.AreEqual failed. Expected: {expectedResult[i]}. Actual: {values[i]}");
                });
            }
            #endregion

            Test<string>(nameof(TestOperations.Array_String), @"{'a':['xxx', 'yyy', 'zzz']}", new[] { "xxx", "yyy", "zzz" });
            Test<int>(nameof(TestOperations.Array_Int), @"{'a':[1,2,42]}", new[] { 1, 2, 42 });
            Test<long>(nameof(TestOperations.Array_Long), @"{'a':[1,2,42]}", new[] { 1L, 2L, 42L });
            Test<bool>(nameof(TestOperations.Array_Bool), @"{'a':[true,false,true]}", new[] { true, false, true });
            Test<float>(nameof(TestOperations.Array_Float), @"{'a':[1.1,2.2,42.42]}", new[] { 1.1f, 2.2f, 42.42f });
            Test<double>(nameof(TestOperations.Array_Double), @"{'a':[1.1,2.2,42.42]}", new[] { 1.1d, 2.2d, 42.42d });
            Test<decimal>(nameof(TestOperations.Array_Decimal), @"{'a':[1.1,2.2,42.42]}", new[] { 1.1m, 2.2m, 42.42m });
        }
        [TestMethod]
        public void OD_MBO_Call_List()
        {
            #region void Test<T>(string methodName, string request, T[] expectedResult)
            void Test<T>(string methodName, string request, T[] expectedResult)
            {
                ODataTest2(services =>
                {
                    services.AddSingleton<OperationInspector, AllowEverything>();
                }, () =>
                {
                    var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), methodName, request);

                    // ACTION
                    var result = OperationCenter.Invoke(context);

                    // ASSERT
                    var values = (List<T>)result;
                    Assert.AreEqual(expectedResult.Length, values.Count);
                    for (var i = 0; i < expectedResult.Length; i++)
                        Assert.AreEqual(expectedResult[i], values[i],
                            $"Assert.AreEqual failed. Expected: {expectedResult[i]}. Actual: {values[i]}");
                });
            }
            #endregion

            Test<string>(nameof(TestOperations.List_String), @"{'a':['xxx', 'yyy', 'zzz']}", new[] { "xxx", "yyy", "zzz" });
            Test<int>(nameof(TestOperations.List_Int), @"{'a':[1,2,42]}", new[] { 1, 2, 42 });
            Test<long>(nameof(TestOperations.List_Long), @"{'a':[1,2,42]}", new[] { 1L, 2L, 42L });
            Test<bool>(nameof(TestOperations.List_Bool), @"{'a':[true,false,true]}", new[] { true, false, true });
            Test<float>(nameof(TestOperations.List_Float), @"{'a':[1.1,2.2,42.42]}", new[] { 1.1f, 2.2f, 42.42f });
            Test<double>(nameof(TestOperations.List_Double), @"{'a':[1.1,2.2,42.42]}", new[] { 1.1d, 2.2d, 42.42d });
            Test<decimal>(nameof(TestOperations.List_Decimal), @"{'a':[1.1,2.2,42.42]}", new[] { 1.1m, 2.2m, 42.42m });
        }
        [TestMethod]
        public void OD_MBO_Call_Enumerable()
        {
            #region void Test<T>(string methodName, string request, T[] expectedResult)
            void Test<T>(string methodName, string request, T[] expectedResult)
            {
                ODataTest2(services =>
                {
                    services.AddSingleton<OperationInspector, AllowEverything>();
                }, () =>
                {
                    var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), methodName, request);

                    // ACTION
                    var result = OperationCenter.Invoke(context);

                    // ASSERT
                    var values = ((IEnumerable<T>)result).ToArray();
                    Assert.AreEqual(expectedResult.Length, values.Length);
                    for (var i = 0; i < expectedResult.Length; i++)
                        Assert.AreEqual(expectedResult[i], values[i],
                            $"Assert.AreEqual failed. Expected: {expectedResult[i]}. Actual: {values[i]}");
                });
            }
            #endregion

            Test<string>(nameof(TestOperations.Enumerable_String), @"{'a':['xxx', 'yyy', 'zzz']}", new[] { "xxx", "yyy", "zzz" });
            Test<int>(nameof(TestOperations.Enumerable_Int), @"{'a':[1,2,42]}", new[] { 1, 2, 42 });
            Test<long>(nameof(TestOperations.Enumerable_Long), @"{'a':[1,2,42]}", new[] { 1L, 2L, 42L });
            Test<bool>(nameof(TestOperations.Enumerable_Bool), @"{'a':[true,false,true]}", new[] { true, false, true });
            Test<float>(nameof(TestOperations.Enumerable_Float), @"{'a':[1.1,2.2,42.42]}", new[] { 1.1f, 2.2f, 42.42f });
            Test<double>(nameof(TestOperations.Enumerable_Double), @"{'a':[1.1,2.2,42.42]}", new[] { 1.1d, 2.2d, 42.42d });
            Test<decimal>(nameof(TestOperations.Enumerable_Decimal), @"{'a':[1.1,2.2,42.42]}", new[] { 1.1m, 2.2m, 42.42m });
        }
        [TestMethod]
        public void OD_MBO_Call_ODataArray()
        {
            #region void Test<T>(string methodName, string request, T[] expectedResult)
            void Test<T>(string methodName, string request, T[] expectedResult)
            {
                ODataTest2(services =>
                {
                    services.AddSingleton<OperationInspector, AllowEverything>();
                }, () =>
                {
                    var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), methodName, request);

                    // ACTION
                    var result = OperationCenter.Invoke(context);

                    // ASSERT
                    var values = ((IEnumerable<T>)result).ToArray();
                    Assert.AreEqual(expectedResult.Length, values.Length);
                    for (var i = 0; i < expectedResult.Length; i++)
                        Assert.AreEqual(expectedResult[i], values[i],
                            $"Assert.AreEqual failed. Expected: {expectedResult[i]}. Actual: {values[i]}");
                });
            }
            #endregion

            Test<string>(nameof(TestOperations.ODataArray_String), @"{'a':['xxx', 'yyy', 'zzz']}", new[] { "xxx", "yyy", "zzz" });
            Test<int>(nameof(TestOperations.ODataArray_Int), @"{'a':[1,2,42]}", new[] { 1, 2, 42 });
            Test<long>(nameof(TestOperations.ODataArray_Long), @"{'a':[1,2,42]}", new[] { 1L, 2L, 42L });
            Test<bool>(nameof(TestOperations.ODataArray_Bool), @"{'a':[true,false,true]}", new[] { true, false, true });
            Test<float>(nameof(TestOperations.ODataArray_Float), @"{'a':[1.1,2.2,42.42]}", new[] { 1.1f, 2.2f, 42.42f });
            Test<double>(nameof(TestOperations.ODataArray_Double), @"{'a':[1.1,2.2,42.42]}", new[] { 1.1d, 2.2d, 42.42d });
            Test<decimal>(nameof(TestOperations.ODataArray_Decimal), @"{'a':[1.1,2.2,42.42]}", new[] { 1.1m, 2.2m, 42.42m });

            Test<ODataArrayTests.TestItem>(nameof(TestOperations.ODataArray_TestItemArray), @"{'a':[1.1,2.2,42.42]}",
                new[]
                {
                    new ODataArrayTests.TestItem("11"),
                    new ODataArrayTests.TestItem("22"),
                    new ODataArrayTests.TestItem("4242"),
                });
        }

        [TestMethod]
        public void OD_MBO_Call_Array_QueryString()
        {
            #region void Test<T>(string methodName, string queryString, T[] expectedResult)
            void Test<T>(string methodName, string queryString, T[] expectedResult)
            {
                ODataTest2(services =>
                {
                    services.AddSingleton<OperationInspector, AllowEverything>();
                }, () =>
                {
                    var _ = ODataMiddleware.ODataRequestHttpContextKey; // need to touch ODataMiddleware
                    var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), methodName,
                        null, new TestQueryCollection(queryString));

                    // ACTION
                    var result = OperationCenter.Invoke(context);

                    // ASSERT
                    var values = (T[])result;
                    Assert.AreEqual(expectedResult.Length, values.Length);
                    for (var i = 0; i < expectedResult.Length; i++)
                        Assert.AreEqual(expectedResult[i], values[i],
                            $"Assert.AreEqual failed. Expected: {expectedResult[i]}. Actual: {values[i]}");
                });
            }
            #endregion

            // one item
            Test<string>(nameof(TestOperations.Array_String), @"?a=xxx", new[] { "xxx" });
            Test<int>(nameof(TestOperations.Array_Int), @"?a=42", new[] { 42 });
            Test<long>(nameof(TestOperations.Array_Long), @"?a=42", new[] { 42L });
            Test<bool>(nameof(TestOperations.Array_Bool), @"?a=true", new[] { true });
            Test<float>(nameof(TestOperations.Array_Float), @"?a=42.42", new[] { 42.42f });
            Test<double>(nameof(TestOperations.Array_Double), @"?a=42.42", new[] { 42.42d });
            Test<decimal>(nameof(TestOperations.Array_Decimal), @"?a=42.42", new[] { 42.42m });

            // more items
            Test<string>(nameof(TestOperations.Array_String), @"?a=xxx&a=yyy&a=zzz", new[] { "xxx", "yyy", "zzz" });
            Test<int>(nameof(TestOperations.Array_Int), @"?a=1&a=2&a=42", new[] { 1, 2, 42 });
            Test<long>(nameof(TestOperations.Array_Long), @"?a=1&a=2&a=42", new[] { 1L, 2L, 42L });
            Test<bool>(nameof(TestOperations.Array_Bool), @"?a=true&a=false&a=true", new[] { true, false, true });
            Test<float>(nameof(TestOperations.Array_Float), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1f, 2.2f, 42.42f });
            Test<double>(nameof(TestOperations.Array_Double), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1d, 2.2d, 42.42d });
            Test<decimal>(nameof(TestOperations.Array_Decimal), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1m, 2.2m, 42.42m });
        }
        [TestMethod]
        public void OD_MBO_Call_Array_Optional_QueryString()
        {
            #region void Test<T>(string methodName, string queryString, T[] expectedResult)
            void Test<T>(string methodName, string queryString, T[] expectedResult)
            {
                ODataTest2(services =>
                {
                    services.AddSingleton<OperationInspector, AllowEverything>();
                }, () =>
                {
                    var _ = ODataMiddleware.ODataRequestHttpContextKey; // need to touch ODataMiddleware
                    var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), methodName,
                        null, new TestQueryCollection(queryString));

                    // ACTION
                    var result = OperationCenter.Invoke(context);

                    // ASSERT
                    var values = (T[])result;
                    Assert.AreEqual(expectedResult.Length, values.Length);
                    for (var i = 0; i < expectedResult.Length; i++)
                        Assert.AreEqual(expectedResult[i], values[i],
                            $"Assert.AreEqual failed. Expected: {expectedResult[i]}. Actual: {values[i]}");
                });
            }
            #endregion

            // one item
            Test<string>(nameof(TestOperations.Optional_Array_String), @"?a=xxx", new[] { "xxx" });
            Test<int>(nameof(TestOperations.Optional_Array_Int), @"?a=42", new[] { 42 });
            Test<long>(nameof(TestOperations.Optional_Array_Long), @"?a=42", new[] { 42L });
            Test<bool>(nameof(TestOperations.Optional_Array_Bool), @"?a=true", new[] { true });
            Test<float>(nameof(TestOperations.Optional_Array_Float), @"?a=42.42", new[] { 42.42f });
            Test<double>(nameof(TestOperations.Optional_Array_Double), @"?a=42.42", new[] { 42.42d });
            Test<decimal>(nameof(TestOperations.Optional_Array_Decimal), @"?a=42.42", new[] { 42.42m });

            // more items
            Test<string>(nameof(TestOperations.Optional_Array_String), @"?a=xxx&a=yyy&a=zzz", new[] { "xxx", "yyy", "zzz" });
            Test<int>(nameof(TestOperations.Optional_Array_Int), @"?a=1&a=2&a=42", new[] { 1, 2, 42 });
            Test<long>(nameof(TestOperations.Optional_Array_Long), @"?a=1&a=2&a=42", new[] { 1L, 2L, 42L });
            Test<bool>(nameof(TestOperations.Optional_Array_Bool), @"?a=true&a=false&a=true", new[] { true, false, true });
            Test<float>(nameof(TestOperations.Optional_Array_Float), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1f, 2.2f, 42.42f });
            Test<double>(nameof(TestOperations.Optional_Array_Double), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1d, 2.2d, 42.42d });
            Test<decimal>(nameof(TestOperations.Optional_Array_Decimal), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1m, 2.2m, 42.42m });
        }

        [TestMethod]
        public void OD_MBO_Call_List_QueryString()
        {
            #region void Test<T>(string methodName, string queryString, T[] expectedResult)
            void Test<T>(string methodName, string queryString, T[] expectedResult)
            {
                ODataTest2(services =>
                {
                    services.AddSingleton<OperationInspector, AllowEverything>();
                }, () =>
                {
                    var _ = ODataMiddleware.ODataRequestHttpContextKey; // need to touch ODataMiddleware
                    var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), methodName,
                        null, new TestQueryCollection(queryString));

                    // ACTION
                    var result = OperationCenter.Invoke(context);

                    // ASSERT
                    var values = (List<T>)result;
                    Assert.AreEqual(expectedResult.Length, values.Count);
                    for (var i = 0; i < expectedResult.Length; i++)
                        Assert.AreEqual(expectedResult[i], values[i],
                            $"Assert.AreEqual failed. Expected: {expectedResult[i]}. Actual: {values[i]}");
                });
            }
            #endregion

            // one item
            Test<string>(nameof(TestOperations.List_String), @"?a=xxx", new[] { "xxx" });
            Test<int>(nameof(TestOperations.List_Int), @"?a=42", new[] { 42 });
            Test<long>(nameof(TestOperations.List_Long), @"?a=42", new[] { 42L });
            Test<bool>(nameof(TestOperations.List_Bool), @"?a=true", new[] { true });
            Test<float>(nameof(TestOperations.List_Float), @"?a=42.42", new[] { 42.42f });
            Test<double>(nameof(TestOperations.List_Double), @"?a=42.42", new[] { 42.42d });
            Test<decimal>(nameof(TestOperations.List_Decimal), @"?a=42.42", new[] { 42.42m });

            // more items
            Test<string>(nameof(TestOperations.List_String), @"?a=xxx&a=yyy&a=zzz", new[] { "xxx", "yyy", "zzz" });
            Test<int>(nameof(TestOperations.List_Int), @"?a=1&a=2&a=42", new[] { 1, 2, 42 });
            Test<long>(nameof(TestOperations.List_Long), @"?a=1&a=2&a=42", new[] { 1L, 2L, 42L });
            Test<bool>(nameof(TestOperations.List_Bool), @"?a=true&a=false&a=true", new[] { true, false, true });
            Test<float>(nameof(TestOperations.List_Float), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1f, 2.2f, 42.42f });
            Test<double>(nameof(TestOperations.List_Double), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1d, 2.2d, 42.42d });
            Test<decimal>(nameof(TestOperations.List_Decimal), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1m, 2.2m, 42.42m });
        }
        [TestMethod]
        public void OD_MBO_Call_List_Optional_QueryString()
        {
            #region void Test<T>(string methodName, string queryString, T[] expectedResult)
            void Test<T>(string methodName, string queryString, T[] expectedResult)
            {
                ODataTest2(services =>
                {
                    services.AddSingleton<OperationInspector, AllowEverything>();
                }, () =>
                {
                    var _ = ODataMiddleware.ODataRequestHttpContextKey; // need to touch ODataMiddleware
                    var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), methodName,
                        null, new TestQueryCollection(queryString));

                    // ACTION
                    var result = OperationCenter.Invoke(context);

                    // ASSERT
                    var values = (List<T>)result;
                    Assert.AreEqual(expectedResult.Length, values.Count);
                    for (var i = 0; i < expectedResult.Length; i++)
                        Assert.AreEqual(expectedResult[i], values[i],
                            $"Assert.AreEqual failed. Expected: {expectedResult[i]}. Actual: {values[i]}");
                });
            }
            #endregion

            // one item
            Test<string>(nameof(TestOperations.Optional_List_String), @"?a=xxx", new[] { "xxx" });
            Test<int>(nameof(TestOperations.Optional_List_Int), @"?a=42", new[] { 42 });
            Test<long>(nameof(TestOperations.Optional_List_Long), @"?a=42", new[] { 42L });
            Test<bool>(nameof(TestOperations.Optional_List_Bool), @"?a=true", new[] { true });
            Test<float>(nameof(TestOperations.Optional_List_Float), @"?a=42.42", new[] { 42.42f });
            Test<double>(nameof(TestOperations.Optional_List_Double), @"?a=42.42", new[] { 42.42d });
            Test<decimal>(nameof(TestOperations.Optional_List_Decimal), @"?a=42.42", new[] { 42.42m });

            // more items
            Test<string>(nameof(TestOperations.Optional_List_String), @"?a=xxx&a=yyy&a=zzz", new[] { "xxx", "yyy", "zzz" });
            Test<int>(nameof(TestOperations.Optional_List_Int), @"?a=1&a=2&a=42", new[] { 1, 2, 42 });
            Test<long>(nameof(TestOperations.Optional_List_Long), @"?a=1&a=2&a=42", new[] { 1L, 2L, 42L });
            Test<bool>(nameof(TestOperations.Optional_List_Bool), @"?a=true&a=false&a=true", new[] { true, false, true });
            Test<float>(nameof(TestOperations.Optional_List_Float), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1f, 2.2f, 42.42f });
            Test<double>(nameof(TestOperations.Optional_List_Double), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1d, 2.2d, 42.42d });
            Test<decimal>(nameof(TestOperations.Optional_List_Decimal), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1m, 2.2m, 42.42m });
        }

        [TestMethod]
        public void OD_MBO_Call_Enumerable_QueryString()
        {
            #region void Test<T>(string methodName, string queryString, T[] expectedResult)
            void Test<T>(string methodName, string queryString, T[] expectedResult)
            {
                ODataTest2(services =>
                {
                    services.AddSingleton<OperationInspector, AllowEverything>();
                }, () =>
                {
                    var _ = ODataMiddleware.ODataRequestHttpContextKey; // need to touch ODataMiddleware
                    var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), methodName,
                        null, new TestQueryCollection(queryString));

                    // ACTION
                    var result = OperationCenter.Invoke(context);

                    // ASSERT
                    var values = ((IEnumerable<T>)result).ToArray();
                    Assert.AreEqual(expectedResult.Length, values.Length);
                    for (var i = 0; i < expectedResult.Length; i++)
                        Assert.AreEqual(expectedResult[i], values[i],
                            $"Assert.AreEqual failed. Expected: {expectedResult[i]}. Actual: {values[i]}");
                });
            }
            #endregion

            // one item
            Test<string>(nameof(TestOperations.Enumerable_String), @"?a=xxx", new[] { "xxx" });
            Test<int>(nameof(TestOperations.Enumerable_Int), @"?a=42", new[] { 42 });
            Test<long>(nameof(TestOperations.Enumerable_Long), @"?a=42", new[] { 42L });
            Test<bool>(nameof(TestOperations.Enumerable_Bool), @"?a=true", new[] { true });
            Test<float>(nameof(TestOperations.Enumerable_Float), @"?a=42.42", new[] { 42.42f });
            Test<double>(nameof(TestOperations.Enumerable_Double), @"?a=42.42", new[] { 42.42d });
            Test<decimal>(nameof(TestOperations.Enumerable_Decimal), @"?a=42.42", new[] { 42.42m });

            // more items
            Test<string>(nameof(TestOperations.Enumerable_String), @"?a=xxx&a=yyy&a=zzz", new[] { "xxx", "yyy", "zzz" });
            Test<int>(nameof(TestOperations.Enumerable_Int), @"?a=1&a=2&a=42", new[] { 1, 2, 42 });
            Test<long>(nameof(TestOperations.Enumerable_Long), @"?a=1&a=2&a=42", new[] { 1L, 2L, 42L });
            Test<bool>(nameof(TestOperations.Enumerable_Bool), @"?a=true&a=false&a=true", new[] { true, false, true });
            Test<float>(nameof(TestOperations.Enumerable_Float), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1f, 2.2f, 42.42f });
            Test<double>(nameof(TestOperations.Enumerable_Double), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1d, 2.2d, 42.42d });
            Test<decimal>(nameof(TestOperations.Enumerable_Decimal), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1m, 2.2m, 42.42m });
        }
        [TestMethod]
        public void OD_MBO_Call_Enumerable_Optional_QueryString()
        {
            #region void Test<T>(string methodName, string queryString, T[] expectedResult)
            void Test<T>(string methodName, string queryString, T[] expectedResult)
            {
                ODataTest2(services =>
                {
                    services.AddSingleton<OperationInspector, AllowEverything>();
                }, () =>
                {
                    var _ = ODataMiddleware.ODataRequestHttpContextKey; // need to touch ODataMiddleware
                    var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), methodName,
                        null, new TestQueryCollection(queryString));

                    // ACTION
                    var result = OperationCenter.Invoke(context);

                    // ASSERT
                    var values = ((IEnumerable<T>)result).ToArray();
                    Assert.AreEqual(expectedResult.Length, values.Length);
                    for (var i = 0; i < expectedResult.Length; i++)
                        Assert.AreEqual(expectedResult[i], values[i],
                            $"Assert.AreEqual failed. Expected: {expectedResult[i]}. Actual: {values[i]}");
                });
            }
            #endregion

            // one item
            Test<string>(nameof(TestOperations.Optional_Enumerable_String), @"?a=xxx", new[] { "xxx" });
            Test<int>(nameof(TestOperations.Optional_Enumerable_Int), @"?a=42", new[] { 42 });
            Test<long>(nameof(TestOperations.Optional_Enumerable_Long), @"?a=42", new[] { 42L });
            Test<bool>(nameof(TestOperations.Optional_Enumerable_Bool), @"?a=true", new[] { true });
            Test<float>(nameof(TestOperations.Optional_Enumerable_Float), @"?a=42.42", new[] { 42.42f });
            Test<double>(nameof(TestOperations.Optional_Enumerable_Double), @"?a=42.42", new[] { 42.42d });
            Test<decimal>(nameof(TestOperations.Optional_Enumerable_Decimal), @"?a=42.42", new[] { 42.42m });

            // more items
            Test<string>(nameof(TestOperations.Optional_Enumerable_String), @"?a=xxx&a=yyy&a=zzz", new[] { "xxx", "yyy", "zzz" });
            Test<int>(nameof(TestOperations.Optional_Enumerable_Int), @"?a=1&a=2&a=42", new[] { 1, 2, 42 });
            Test<long>(nameof(TestOperations.Optional_Enumerable_Long), @"?a=1&a=2&a=42", new[] { 1L, 2L, 42L });
            Test<bool>(nameof(TestOperations.Optional_Enumerable_Bool), @"?a=true&a=false&a=true", new[] { true, false, true });
            Test<float>(nameof(TestOperations.Optional_Enumerable_Float), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1f, 2.2f, 42.42f });
            Test<double>(nameof(TestOperations.Optional_Enumerable_Double), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1d, 2.2d, 42.42d });
            Test<decimal>(nameof(TestOperations.Optional_Enumerable_Decimal), @"?a=1.1&a=2.2&a=42.42", new[] { 1.1m, 2.2m, 42.42m });
        }

        [TestMethod]
        public void OD_MBO_Call_OdataArray_QueryString()
        {
            #region void Test<T>(string methodName, string queryString, T[] expectedResult)
            void Test<T>(string methodName, string queryString, T[] expectedResult)
            {
                ODataTest2(services =>
                {
                    services.AddSingleton<OperationInspector, AllowEverything>();
                }, () =>
                {
                    var _ = ODataMiddleware.ODataRequestHttpContextKey; // need to touch ODataMiddleware
                    var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), methodName,
                        null, new TestQueryCollection(queryString));

                    // ACTION
                    var result = OperationCenter.Invoke(context);

                    // ASSERT
                    var values = ((IEnumerable<T>)result).ToArray();
                    Assert.AreEqual(expectedResult.Length, values.Length);
                    for (var i = 0; i < expectedResult.Length; i++)
                        Assert.AreEqual(expectedResult[i], values[i],
                            $"Assert.AreEqual failed. Expected: {expectedResult[i]}. Actual: {values[i]}");
                });
            }
            #endregion

            // one item
            Test<string>(nameof(TestOperations.ODataArray_String), @"?a=xxx", new[] { "xxx" });
            Test<int>(nameof(TestOperations.ODataArray_Int), @"?a=42", new[] { 42 });
            Test<long>(nameof(TestOperations.ODataArray_Long), @"?a=42", new[] { 42L });
            Test<bool>(nameof(TestOperations.ODataArray_Bool), @"?a=true", new[] { true });
            Test<float>(nameof(TestOperations.ODataArray_Float), @"?a=42.42", new[] { 42.42f });
            Test<double>(nameof(TestOperations.ODataArray_Double), @"?a=42.42", new[] { 42.42d });
            Test<decimal>(nameof(TestOperations.ODataArray_Decimal), @"?a=42.42", new[] { 42.42m });

            // more items
            Test<string>(nameof(TestOperations.ODataArray_String), @"?a=xxx,yyy,zzz", new[] { "xxx", "yyy", "zzz" });
            Test<int>(nameof(TestOperations.ODataArray_Int), @"?a=1,2,42", new[] { 1, 2, 42 });
            Test<long>(nameof(TestOperations.ODataArray_Long), @"?a=1,2,42", new[] { 1L, 2L, 42L });
            Test<bool>(nameof(TestOperations.ODataArray_Bool), @"?a=true,false,true", new[] { true, false, true });
            Test<float>(nameof(TestOperations.ODataArray_Float), @"?a=1.1,2.2,42.42", new[] { 1.1f, 2.2f, 42.42f });
            Test<double>(nameof(TestOperations.ODataArray_Double), @"?a=1.1,2.2,42.42", new[] { 1.1d, 2.2d, 42.42d });
            Test<decimal>(nameof(TestOperations.ODataArray_Decimal), @"?a=1.1,2.2,42.42", new[] { 1.1m, 2.2m, 42.42m });
        }
        [TestMethod]
        public void OD_MBO_Call_OdataArray_Optional_QueryString()
        {
            #region void Test<T>(string methodName, string queryString, T[] expectedResult)
            void Test<T>(string methodName, string queryString, T[] expectedResult)
            {
                ODataTest2(services =>
                {
                    services.AddSingleton<OperationInspector, AllowEverything>();
                }, () =>
                {
                    var _ = ODataMiddleware.ODataRequestHttpContextKey; // need to touch ODataMiddleware
                    var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), methodName,
                        null, new TestQueryCollection(queryString));

                    // ACTION
                    var result = OperationCenter.Invoke(context);

                    // ASSERT
                    var values = ((IEnumerable<T>)result).ToArray();
                    Assert.AreEqual(expectedResult.Length, values.Length);
                    for (var i = 0; i < expectedResult.Length; i++)
                        Assert.AreEqual(expectedResult[i], values[i],
                            $"Assert.AreEqual failed. Expected: {expectedResult[i]}. Actual: {values[i]}");
                });
            }
            #endregion

            // one item
            Test<string>(nameof(TestOperations.Optional_ODataArray_String), @"?a=xxx", new[] { "xxx" });
            Test<int>(nameof(TestOperations.Optional_ODataArray_Int), @"?a=42", new[] { 42 });
            Test<long>(nameof(TestOperations.Optional_ODataArray_Long), @"?a=42", new[] { 42L });
            Test<bool>(nameof(TestOperations.Optional_ODataArray_Bool), @"?a=true", new[] { true });
            Test<float>(nameof(TestOperations.Optional_ODataArray_Float), @"?a=42.42", new[] { 42.42f });
            Test<double>(nameof(TestOperations.Optional_ODataArray_Double), @"?a=42.42", new[] { 42.42d });
            Test<decimal>(nameof(TestOperations.Optional_ODataArray_Decimal), @"?a=42.42", new[] { 42.42m });

            // more items
            Test<string>(nameof(TestOperations.Optional_ODataArray_String), @"?a=xxx,yyy,zzz", new[] { "xxx", "yyy", "zzz" });
            Test<int>(nameof(TestOperations.Optional_ODataArray_Int), @"?a=1,2,42", new[] { 1, 2, 42 });
            Test<long>(nameof(TestOperations.Optional_ODataArray_Long), @"?a=1,2,42", new[] { 1L, 2L, 42L });
            Test<bool>(nameof(TestOperations.Optional_ODataArray_Bool), @"?a=true,false,true", new[] { true, false, true });
            Test<float>(nameof(TestOperations.Optional_ODataArray_Float), @"?a=1.1,2.2,42.42", new[] { 1.1f, 2.2f, 42.42f });
            Test<double>(nameof(TestOperations.Optional_ODataArray_Double), @"?a=1.1,2.2,42.42", new[] { 1.1d, 2.2d, 42.42d });
            Test<decimal>(nameof(TestOperations.Optional_ODataArray_Decimal), @"?a=1.1,2.2,42.42", new[] { 1.1m, 2.2m, 42.42m });
        }

        [TestMethod]
        public void OD_MBO_Call_OdataArray_Customized_QueryString()
        {
            #region void Test<T>(string methodName, string queryString, T[] expectedResult)
            void Test<T>(string methodName, string queryString, T[] expectedResult)
            {
                ODataTest2(services =>
                {
                    services.AddSingleton<OperationInspector, AllowEverything>();
                }, () =>
                {
                    var _ = ODataMiddleware.ODataRequestHttpContextKey; // need to touch ODataMiddleware
                    var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), methodName,
                        null, new TestQueryCollection(queryString));

                    // ACTION
                    var result = OperationCenter.Invoke(context);

                    // ASSERT
                    var values = ((IEnumerable<T>)result).ToArray();
                    Assert.AreEqual(expectedResult.Length, values.Length);
                    for (var i = 0; i < expectedResult.Length; i++)
                        Assert.AreEqual(expectedResult[i], values[i],
                            $"Assert.AreEqual failed. Expected: {expectedResult[i]}. Actual: {values[i]}");
                });
            }
            #endregion

            // one item
            Test<ODataArrayTests.TestItem>(nameof(TestOperations.ODataArray_TestItemArray), @"?a=x1x2x", new[]
            {
                new ODataArrayTests.TestItem("12"),
            });

            // more items
            Test<ODataArrayTests.TestItem>(nameof(TestOperations.ODataArray_TestItemArray), @"?a=x1x2x,y3y4y,z5z6z", new[]
            {
                new ODataArrayTests.TestItem("12"),
                new ODataArrayTests.TestItem("34"),
                new ODataArrayTests.TestItem("56"),
            });
        }
        [TestMethod]
        public void OD_MBO_Call_OdataArray_Customized_Optional_QueryString()
        {
            #region void Test<T>(string methodName, string queryString, T[] expectedResult)
            void Test<T>(string methodName, string queryString, T[] expectedResult)
            {
                ODataTest2(services =>
                {
                    services.AddSingleton<OperationInspector, AllowEverything>();
                }, () =>
                {
                    var _ = ODataMiddleware.ODataRequestHttpContextKey; // need to touch ODataMiddleware
                    var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), methodName,
                        null, new TestQueryCollection(queryString));

                    // ACTION
                    var result = OperationCenter.Invoke(context);

                    // ASSERT
                    var values = ((IEnumerable<T>)result).ToArray();
                    Assert.AreEqual(expectedResult.Length, values.Length);
                    for (var i = 0; i < expectedResult.Length; i++)
                        Assert.AreEqual(expectedResult[i], values[i],
                            $"Assert.AreEqual failed. Expected: {expectedResult[i]}. Actual: {values[i]}");
                });
            }
            #endregion

            // one item
            Test<ODataArrayTests.TestItem>(nameof(TestOperations.Optional_ODataArray_TestItemArray), @"?a=x1x2x", new[]
            {
                new ODataArrayTests.TestItem("12"),
            });

            // more items
            Test<ODataArrayTests.TestItem>(nameof(TestOperations.Optional_ODataArray_TestItemArray), @"?a=x1x2x,y3y4y,z5z6z", new[]
            {
                new ODataArrayTests.TestItem("12"),
                new ODataArrayTests.TestItem("34"),
                new ODataArrayTests.TestItem("56"),
            });
        }

        [TestMethod]
        public void OD_MBO_Call_EnumerableError()
        {
            #region void Test<T>(string methodName, string request)
            // ReSharper disable once UnusedTypeParameter
            bool Test<T>(string methodName, string request)
            {
                var testResult = false;
                ODataTest2(services =>
                {
                    services.AddSingleton<OperationInspector, AllowEverything>();
                }, () =>
                {
                    try
                    {
                        var context = OperationCenter.GetMethodByRequest(GetContent(null, "User"), methodName, request);

                        var result = OperationCenter.Invoke(context);

                        testResult = true;
                    }
                    catch
                    {
                        // ignored
                    }
                });
                return testResult;
            }

            #endregion

            Assert.IsTrue(Test<string>(nameof(TestOperations.Enumerable_String), @"{'a':['xxx', true, 'zzz']}"));
            Assert.IsTrue(Test<int>(nameof(TestOperations.Enumerable_Int), @"{'a':[1,2,'42']}"));
            Assert.IsFalse(Test<int>(nameof(TestOperations.Enumerable_Int), @"{'a':[1,2,'xxx']}"));
        }

        /* ====================================================================== ACTION QUERY TESTS */

        [TestMethod]
        public void OD_MBO_Actions_Uri_Root()
        {
            ODataTest(() =>
            {
                var content = Content.Create(Repository.Root);
                var expected = "/odata.svc/('Root')/Operation1";

                var actual = OperationMethodStorage.GenerateUri(content, "Operation1");

                Assert.AreEqual(expected, actual);
            });
        }
        [TestMethod]
        public void OD_MBO_Actions_Uri()
        {
            ODataTest(() =>
            {
                var content = Content.Create(Repository.ImsFolder);
                var expected = "/odata.svc/Root('IMS')/Operation1";

                var actual = OperationMethodStorage.GenerateUri(content, "Operation1");

                Assert.AreEqual(expected, actual);
            });
        }

        [TestMethod]
        public void OD_MBO_Actions_Lists()
        {
            ODataTest(() =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m0 = AddMethod(new TestMethodInfo("op", "Content content, string a", null),
                        new Attribute[]
                        {
                            new ODataAction
                            {
                                DisplayName = "Op title", Description = "Op desc", Icon = "Op icon"
                            }
                        });

                    // ACTION-1: metadata
                    var response = ODataGetAsync("/OData.svc/Root('IMS')", "?$select=Id")
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    // ASSERT-1
                    var entity = GetEntity(response);
                    var operations1 = entity.MetadataActions.Union(entity.MetadataFunctions).Where(x=>x.Name == "op").ToArray();
                    Assert.AreEqual(1, operations1.Length);
                    var op1 = operations1[0];

                    // ACTION-2: Actions expanded field
                    response = ODataGetAsync("/OData.svc/Root('IMS')", "?metadata=no&$expand=Actions&$select=Id,Actions")
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    entity = GetEntity(response);
                    var operations2 = entity.Actions.Where(x => x.Name == "op").ToArray();
                    Assert.AreEqual(1, operations2.Length);
                    var op2 = operations2[0];

                    // ACTION-3: Actions field only
                    response = ODataGetAsync("/OData.svc/Root('IMS')/Actions", "")
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    // ASSERT-3
                    var entities = GetEntities(response);
                    var operations3 = entities.Where(x => x.Name == "op").ToArray();
                    Assert.AreEqual(1, operations3.Length);
                    var op3 = operations3[0];

                    // ASSERT-4 Operation properties
                    Assert.AreEqual("Op title", op3.AllProperties["DisplayName"].ToString());
                    Assert.AreEqual("Op icon", op3.AllProperties["Icon"].ToString());

                    Assert.AreEqual(op1.Title, op2.DisplayName);
                    Assert.AreEqual(op1.Title, op3.AllProperties["DisplayName"].ToString());

                    Assert.AreEqual(op2.Icon, op3.AllProperties["Icon"].ToString());
                }
            });
        }
        [TestMethod, TestCategory("Services")]
        public void OD_MBO_Actions_ActionIds_CSrv()
        {
            ODataTest(() =>
            {
                using (new CleanOperationCenterBlock())
                {
                    AddMethod(new TestMethodInfo("op",
                            "Content content", null),
                        new Attribute[] { new ODataAction(), });
                    AddMethod(new TestMethodInfo("op",
                            "Content content, HttpContext ctx, string a", null),
                        new Attribute[] { new ODataAction(), });
                    AddMethod(new TestMethodInfo("op",
                            "Content content, string a", "int x"),
                        new Attribute[] { new ODataAction(), });
                    AddMethod(new TestMethodInfo("op",
                            "Content content, HttpContext ctx, string b, int c", null),
                        new Attribute[] { new ODataFunction(), });
                    AddMethod(new TestMethodInfo("op",
                            "Content content, string d, int e", "int x"),
                        new Attribute[] { new ODataFunction(), });
                    AddMethod(new TestMethodInfo("op",
                            "Content content, HttpContext ctx, string p, int q", "int x, int y"),
                        new Attribute[] { new ODataFunction(), });

                    // ACTION-1: metadata
                    var response1 = ODataGetAsync("/OData.svc/Root('IMS')", "?$select=Id")
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    // ASSERT-1
                    var entity = GetEntity(response1);
                    var operations1 = entity.MetadataActions.Union(entity.MetadataFunctions).Where(x => x.Name == "op").ToArray();
                    var ids1 = string.Join("|", operations1.Select(x => x.OpId).OrderBy(x => x));
                    Assert.AreEqual(6, operations1.Length);

                    // ACTION-2: Actions expanded field
                    var response2 = ODataGetAsync("/OData.svc/Root('IMS')", "?metadata=no&$expand=Actions&$select=Id,Actions")
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    entity = GetEntity(response2);
                    var operations2 = entity.Actions.Where(x => x.Name == "op").ToArray();
                    var ids2 = string.Join("|", operations1.Select(x => x.OpId).OrderBy(x => x));
                    Assert.AreEqual(6, operations2.Length);

                    // ACTION-3: Actions field only
                    var response3 = ODataGetAsync("/OData.svc/Root('IMS')/Actions", "")
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    // ASSERT-3
                    var entities = GetEntities(response3);
                    var operations3 = entities.Where(x => x.Name == "op").ToArray();
                    var ids3 = string.Join("|", operations1.Select(x => x.OpId).OrderBy(x => x));
                    Assert.AreEqual(6, operations3.Length);

                    // ASSERT-4 OpIds
                    var ids = string.Join("|", new[] { "op()", "op(a)", "op(a,x)", "op(b,c)", "op(d,e,x)", "op(p,q,x,y)"}
                        .OrderBy(x => x));
                    Assert.AreEqual(ids, ids1);
                    Assert.AreEqual(ids1, ids2);
                    Assert.AreEqual(ids1, ids3);
                }
            });
        }

        [TestMethod]
        public void OD_MBO_Actions_ActionProperties_ActionField()
        {
            ODataTest(() =>
            {
                using (new CleanOperationCenterBlock())
                {
                    AddMethod(new TestMethodInfo("op0", "Content content", null),
                        new Attribute[] { new ODataAction(), new RequiredPoliciesAttribute("AllowEverything"), });
                    AddMethod(new TestMethodInfo("op1", "Content content, string a", null),
                        new Attribute[] { new ODataAction(), });
                    AddMethod(new TestMethodInfo("op2", "Content content, string b, int c", null),
                        new Attribute[] { new ODataFunction(), });

                    // ACTION
                    var response = ODataGetAsync("/OData.svc/Root('IMS')/Actions", "")
                        .ConfigureAwait(false).GetAwaiter().GetResult();

                    // ASSERT
                    var entities = GetEntities(response);
                    var actions = GetActions(entities);
                    var operations = actions
                        .Where(x => x.Name.StartsWith("op"))
                        .OrderBy(x => x.Name)
                        .ToArray();

                    Assert.AreEqual(3, operations.Length);

                    Assert.AreEqual("op0", operations[0].Name);
                    Assert.AreEqual("op0()", operations[0].OpId);
                    Assert.AreEqual("", string.Join(",", operations[0].ActionParameters));
                    Assert.AreEqual("/odata.svc/Root('IMS')/op0", operations[0].Url);
                    Assert.AreEqual(true, operations[0].IsODataAction);
                    Assert.AreEqual(false, operations[0].Forbidden);

                    Assert.AreEqual("op1", operations[1].Name);
                    Assert.AreEqual("op1(a)", operations[1].OpId);
                    Assert.AreEqual("a", string.Join(",", operations[1].ActionParameters));
                    Assert.AreEqual("/odata.svc/Root('IMS')/op1", operations[1].Url);
                    Assert.AreEqual(true, operations[1].IsODataAction);
                    Assert.AreEqual(false, operations[1].Forbidden);

                    Assert.AreEqual("op2", operations[2].Name);
                    Assert.AreEqual("op2(b,c)", operations[2].OpId);
                    Assert.AreEqual("b,c", string.Join(",", operations[2].ActionParameters));
                    Assert.AreEqual("/odata.svc/Root('IMS')/op2", operations[2].Url);
                    Assert.AreEqual(true, operations[2].IsODataAction);
                    Assert.AreEqual(false, operations[2].Forbidden);
                }
            });
        }
        [TestMethod]
        public void OD_MBO_Actions_ActionProperties_Metadata()
        {
            ODataTest(() =>
            {
                using (new CleanOperationCenterBlock())
                {
                    AddMethod(new TestMethodInfo("op0", "Content content", null),
                        new Attribute[] { new ODataAction(), });
                    AddMethod(new TestMethodInfo("op1", "Content content, string a", null),
                        new Attribute[] { new ODataFunction(), });
                    AddMethod(new TestMethodInfo("op2", "Content content, string b, int c", null),
                        new Attribute[] { new ODataFunction(), });

                    // ACTION
                    var response = ODataGetAsync("/OData.svc/Root('IMS')", "")
                        .ConfigureAwait(false).GetAwaiter().GetResult();

                    // ASSERT
                    var entity = GetEntity(response);
                    var operations = entity.MetadataActions
                        .Union(entity.MetadataFunctions)
                        .Where(x => x.Name.StartsWith("op"))
                        .OrderBy(x => x.Name)
                        .ToArray();

                    Assert.AreEqual(3, operations.Length);

                    Assert.AreEqual("op0", operations[0].Name);
                    Assert.AreEqual("op0()", operations[0].OpId);
                    Assert.AreEqual("", string.Join(",", operations[0].Parameters.Select(x => x.Name)));
                    Assert.AreEqual("", string.Join(",", operations[0].Parameters.Select(x => x.Type)));
                    Assert.AreEqual("/odata.svc/Root('IMS')/op0", operations[0].Target);
                    Assert.AreEqual(false, operations[0].Forbidden);

                    Assert.AreEqual("op1", operations[1].Name);
                    Assert.AreEqual("op1(a)", operations[1].OpId);
                    Assert.AreEqual("a", string.Join(",", operations[1].Parameters.Select(x => x.Name)));
                    Assert.AreEqual("string", string.Join(",", operations[1].Parameters.Select(x => x.Type)));
                    Assert.AreEqual("/odata.svc/Root('IMS')/op1", operations[1].Target);
                    Assert.AreEqual(false, operations[1].Forbidden);

                    Assert.AreEqual("op2", operations[2].Name);
                    Assert.AreEqual("op2(b,c)", operations[2].OpId);
                    Assert.AreEqual("b,c", string.Join(",", operations[2].Parameters.Select(x => x.Name)));
                    Assert.AreEqual("string,int", string.Join(",", operations[2].Parameters.Select(x => x.Type)));
                    Assert.AreEqual("/odata.svc/Root('IMS')/op2", operations[2].Target);
                    Assert.AreEqual(false, operations[2].Forbidden);
                }
            });
        }

        [TestMethod]
        public void OD_MBO_Actions_FilteredByContentType()
        {
            ODataTest(() =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m0 = AddMethod(new TestMethodInfo("fv0", "Content content, string a", null),
                        new Attribute[] { new ODataAction(), new ContentTypesAttribute("GenericContent"), });
                    var m1 = AddMethod(new TestMethodInfo("fv1", "Content content, string a", null),
                        new Attribute[] { new ODataAction(), new ContentTypesAttribute("Folder"), });
                    var m2 = AddMethod(new TestMethodInfo("fv2", "Content content, string a", null),
                        new Attribute[] { new ODataAction(), new ContentTypesAttribute("Domains"), });
                    var m3 = AddMethod(new TestMethodInfo("fv3", "Content content, string a", null),
                        new Attribute[] { new ODataAction(), new ContentTypesAttribute("File"), });

                    // ACTION
                    var response = ODataGetAsync("/OData.svc/Root('IMS')/Actions", "")
                        .ConfigureAwait(false).GetAwaiter().GetResult();

                    // ASSERT
                    var entities = GetEntities(response);
                    var operationNames = entities
                        .Select(x => x.Name)
                        .OrderBy(x => x)
                        .ToArray();
                    Assert.IsTrue(operationNames.Contains("fv0"));
                    Assert.IsTrue(operationNames.Contains("fv1"));
                    Assert.IsTrue(operationNames.Contains("fv2"));
                    Assert.IsFalse(operationNames.Contains("fv3"));
                }
            });
        }

        [TestMethod]
        public void OD_MBO_Actions_Authorization_Membership()
        {
            ODataTest(() =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m0 = AddMethod(new TestMethodInfo("fv0", "Content content, string a", null),
                        new Attribute[] {new ODataAction(), new AllowedRolesAttribute(N.R.Administrators) });
                    var m1 = AddMethod(new TestMethodInfo("fv1", "Content content, string a", null),
                        new Attribute[] {new ODataAction(), new AllowedRolesAttribute(N.R.Developers) });
                    var m3 = AddMethod(new TestMethodInfo("fv2", "Content content, string a", null),
                        new Attribute[] { new ODataAction(), new AllowedRolesAttribute($"{N.R.Administrators},{N.R.Developers}") });
                    var m4 = AddMethod(new TestMethodInfo("fv3", "Content content, string a", null),
                        new Attribute[] {new ODataAction(), new AllowedRolesAttribute(N.R.Developers, "UnknownGroup42")});

                    using (new CurrentUserBlock(User.Administrator))
                    {
                        // ACTION
                        var response = ODataGetAsync("/OData.svc/Root('IMS')/Actions", "")
                            .ConfigureAwait(false).GetAwaiter().GetResult();

                        // ASSERT
                        AssertNoError(response);

                        var entities = GetEntities(response);
                        var operationNames = entities
                            .Select(x => x.Name)
                            .OrderBy(x => x)
                            .ToArray();
                        Assert.IsTrue(operationNames.Contains("fv0"));
                        Assert.IsFalse(operationNames.Contains("fv1"));
                        Assert.IsTrue(operationNames.Contains("fv2"));
                        Assert.IsFalse(operationNames.Contains("fv3"));
                    }
                }
            });
        }

        [TestMethod]
        public void OD_MBO_Actions_Authorization_Membership_Admin()
        {
            ODataTest(() =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var publicDomain = Node.LoadNode("/Root/IMS/Public");
                    var user1 = new User(publicDomain) {Name = "User1", Email = "user1@example.com", Enabled = true};
                    user1.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();
                    var publicAdmins = Node.Load<Group>("/Root/IMS/Public/Administrators");
                    publicAdmins.AddMember(user1);
                    new SecurityHandler(Providers.Instance.Services.GetService<ILogger<SecurityHandler>>()).CreateAclEditor()
                        .Allow(Repository.ImsFolder.Id, publicAdmins.Id, false, PermissionType.BuiltInPermissionTypes)
                        .ApplyAsync(CancellationToken.None).GetAwaiter().GetResult();

                    var m0 = AddMethod(new TestMethodInfo("fv0", "Content content, string a", null),
                        new Attribute[] { new ODataAction(), new AllowedRolesAttribute(N.R.Administrators) });
                    var m1 = AddMethod(new TestMethodInfo("fv1", "Content content, string a", null),
                        new Attribute[] { new ODataAction(), new AllowedRolesAttribute(publicAdmins.Path) });

                    using (new CurrentUserBlock(User.Administrator))
                    {
                        // ACTION-1
                        var response = ODataGetAsync("/OData.svc/Root('IMS')/Actions", "")
                            .ConfigureAwait(false).GetAwaiter().GetResult();

                        // ASSERT-1
                        AssertNoError(response);
                        var entities = GetEntities(response);
                        var operationNames = entities.Select(x => x.Name).OrderBy(x => x).ToArray();
                        Assert.IsTrue(operationNames.Contains("fv0"));
                        Assert.IsFalse(operationNames.Contains("fv1"));
                    }
                    using (new CurrentUserBlock(user1))
                    {
                        // ACTION-2
                        var response = ODataGetAsync("/OData.svc/Root('IMS')/Actions", "")
                            .ConfigureAwait(false).GetAwaiter().GetResult();

                        // ASSERT-2
                        AssertNoError(response);
                        var entities = GetEntities(response);
                        var operationNames = entities.Select(x => x.Name).OrderBy(x => x).ToArray();
                        Assert.IsFalse(operationNames.Contains("fv0"));
                        Assert.IsTrue(operationNames.Contains("fv1"));
                    }
                }
            });
        }

        [TestMethod]
        public void OD_MBO_Actions_Authorization_Permission()
        {
            string GetAllowedActionNames(string nodeName)
            {
                var response = ODataGetAsync($"/OData.svc/Root('{nodeName}')/Actions", "")
                    .ConfigureAwait(false).GetAwaiter().GetResult();

                if (response.StatusCode == 404)
                    return "";

                var entities = GetEntities(response);
                var actions = GetActions(entities);
                var operationNames = string.Join(", ", actions.Select(x => $"{x.Name}:{!x.Forbidden}").ToArray());
                return operationNames;
            }

            ODataTestAsync(() =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m0 = AddMethod(new TestMethodInfo("fv0", "Content content, string a", null),
                        new Attribute[] { new ODataAction(), new RequiredPermissionsAttribute(N.P.See) });
                    var m1 = AddMethod(new TestMethodInfo("fv1", "Content content, string a", null),
                        new Attribute[] { new ODataAction(), new RequiredPermissionsAttribute(N.P.Open) });
                    var m3 = AddMethod(new TestMethodInfo("fv2", "Content content, string a", null),
                        new Attribute[] { new ODataAction(), new RequiredPermissionsAttribute(N.P.Save) });

                    var user = User.Visitor;

                    var nodes = Enumerable.Range(0, 4).Select(x =>
                    {
                        var folder = new Folder(Repository.Root) {Name = $"Folder{x}"};
                        folder.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();
                        return folder;
                    }).ToArray();

                    Providers.Instance.SecurityHandler.CreateAclEditor()
                        .Allow(nodes[1].Id, user.Id, false, PermissionType.See)
                        .Allow(nodes[2].Id, user.Id, false, PermissionType.Open)
                        .Allow(nodes[3].Id, user.Id, false, PermissionType.Save)
                        .ApplyAsync(CancellationToken.None).GetAwaiter().GetResult();

                    using (new CurrentUserBlock(User.Visitor))
                    {
                        Assert.AreEqual("", GetAllowedActionNames(nodes[0].Name));
                        Assert.AreEqual("fv0:True, fv1:False, fv2:False", GetAllowedActionNames(nodes[1].Name));
                        Assert.AreEqual("fv0:True, fv1:True, fv2:False", GetAllowedActionNames(nodes[2].Name));
                        Assert.AreEqual("fv0:True, fv1:True, fv2:True", GetAllowedActionNames(nodes[3].Name));
                    }

                    return Task.CompletedTask;
                }
            }).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void OD_MBO_Actions_FilteredByScenario()
        {
            string GetResult(ODataResponse response)
            {
                return string.Join(",", GetEntities(response)
                    .Where(x=>x.Name.StartsWith("fv"))
                    .Select(x => x.Name)
                    .OrderBy(x => x)
                    .ToArray());
            }

            ODataTest(() =>
            {
                using (new CleanOperationCenterBlock())
                {
                    var m0 = AddMethod(new TestMethodInfo("fv0", "Content content, string a", null),
                        new Attribute[] { new ODataAction() });
                    var m1 = AddMethod(new TestMethodInfo("fv1", "Content content, string a", null),
                        new Attribute[] { new ODataAction(), new ScenarioAttribute("S1") });
                    var m2 = AddMethod(new TestMethodInfo("fv2", "Content content, string a", null),
                        new Attribute[] { new ODataAction(), new ScenarioAttribute("S1,S2") });
                    var m3 = AddMethod(new TestMethodInfo("fv3", "Content content, string a", null),
                        new Attribute[] { new ODataAction(), new ScenarioAttribute("S2") });
                    var m4 = AddMethod(new TestMethodInfo("fv4", "Content content, string a", null),
                        new Attribute[] { new ODataAction(), new ScenarioAttribute("S3") });

                    // TEST-1: without scenario
                    var response = ODataGetAsync("/OData.svc/Root('IMS')/Actions", "")
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    Assert.AreEqual("fv0,fv1,fv2,fv3,fv4", GetResult(response));

                    // TEST-2: scenario S1
                    response = ODataGetAsync("/OData.svc/Root('IMS')/Actions", "?scenario=S1")
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    Assert.AreEqual("fv1,fv2", GetResult(response));

                    // TEST-3: scenario S2
                    response = ODataGetAsync("/OData.svc/Root('IMS')/Actions", "?scenario=S2")
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    Assert.AreEqual("fv2,fv3", GetResult(response));

                    // TEST-4: scenario s1 (case insensitive)
                    response = ODataGetAsync("/OData.svc/Root('IMS')/Actions", "?scenario=S1")
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    Assert.AreEqual("fv1,fv2", GetResult(response));

                    // TEST-5: scenario s2
                    response = ODataGetAsync("/OData.svc/Root('IMS')/Actions", "?scenario=S2")
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    Assert.AreEqual("fv2,fv3", GetResult(response));
                }
            });
        }

        [TestMethod]
        public void OD_MBO_Actions_FilteredByPolicy()
        {
            IOperationMethodStorage oms = null;
            ODataTest(builder =>
                {
                    builder.AddAllTestPolicies();
                    OperationCenter.Policies["WopiOpenView"] = new WopiOpenViewMethodPolicy();
                    OperationCenter.Policies["WopiOpenEdit"] = new WopiOpenEditMethodPolicy();
                    oms = builder.Services.GetRequiredService<IOperationMethodStorage>();
                },
                () =>
                {
                    OperationCenter.Discover();
                    
                    var original = AccessProvider.Current.GetCurrentUser();

                    try
                    {
                        // set the caller user temporarily
                        AccessProvider.Current.SetCurrentUser(User.Administrator);
                        var httpContext = new DefaultHttpContext();

                        // Root content: action is in the list
                        var content = Content.Create(Repository.Root);
                        Assert.IsTrue(oms.GetActions(new ActionBase[0], content, null, httpContext)
                            .Any(a => a.Name == "Op11"));

                        // other content: action is not in the list
                        content = Content.Create(User.Administrator);
                        Assert.IsFalse(oms.GetActions(new ActionBase[0], content, null, httpContext)
                            .Any(a => a.Name == "Op11"));
                    }
                    finally
                    {
                        AccessProvider.Current.SetCurrentUser(original);
                    }
                });
        }

        [TestMethod]
        public void OD_MBO_Actions_VersioningActions()
        {
            IOperationMethodStorage oms = null;
            ODataTest(
                builder =>
                {
                    builder.AddAllTestPolicies();
                    OperationCenter.Policies["WopiOpenView"] = new WopiOpenViewMethodPolicy();
                    OperationCenter.Policies["WopiOpenEdit"] = new WopiOpenEditMethodPolicy();
                    oms = builder.Services.GetRequiredService<IOperationMethodStorage>();
                },
                () =>
                {
                    // prepare test file
                    const string parentPath = "/Root/MyFiles";
                    var parent = RepositoryTools.CreateStructure(parentPath, "SystemFolder")
                                 ?? Content.Load(parentPath);
                    var file = new File(parent.ContentHandler)
                    {
                        Name = Guid.NewGuid() + ".docx",
                        VersioningMode = VersioningType.MajorAndMinor,
                        ApprovingMode = ApprovingType.True
                    };
                    file.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();

                    var fileContent = Content.Create(file);

                    using (new CleanOperationCenterBlock())
                    {
                        OperationCenter.Discover();

                        // We have to execute the test in the name of a regular
                        // user, not SystemUser - otherwise all policy checks
                        // would be skipped.
                        using var _ = new CurrentUserBlock(User.Administrator);

                        // V0.1.D
                        AssertActions(fileContent, new[] { "checkout", "publish" });

                        file.CheckOutAsync(CancellationToken.None).GetAwaiter().GetResult();

                        // V0.2.L
                        AssertActions(fileContent, new[] { "checkin", "undocheckout", "publish" });

                        file.CheckInAsync(CancellationToken.None).GetAwaiter().GetResult();
                        file.PublishAsync(CancellationToken.None).GetAwaiter().GetResult();

                        // V0.2.P
                        AssertActions(fileContent, new[] { "approve", "reject", "checkout" });

                        file.RejectAsync(CancellationToken.None).GetAwaiter().GetResult();

                        // V0.2.R
                        AssertActions(fileContent, new[] { "checkout", "publish" });

                        file.PublishAsync(CancellationToken.None).GetAwaiter().GetResult();
                        file.ApproveAsync(CancellationToken.None).GetAwaiter().GetResult();

                        // V1.0.A
                        AssertActions(fileContent, new[] { "checkout" });
                    }
                });

            void AssertActions(Content content, string[] visible)
            {
                var actions = oms.GetActions(
                    ActionFramework.GetActionsFromContentRepository(content, null, null),
                    content, null, null).ToArray();
                
                foreach (var actionName in SavingAction.VERSIONING_ACTIONS)
                {
                    var action = actions.SingleOrDefault(a =>
                        string.Equals(a.Name, actionName, StringComparison.InvariantCultureIgnoreCase));

                    if (visible.Contains(actionName))
                    {
                        // the actions list MUST contain this
                        Assert.IsNotNull(action, $"Action {actionName} is missing from the list.");
                    }
                    else
                    {
                        // the actions list must NOT contain this
                        Assert.IsNull(action, $"Action {actionName} should NOT be in the list.");
                    }
                }
            }
        }

        [TestMethod]
        public void OD_MBO_Actions_TrashBag()
        {
            IOperationMethodStorage oms = null;
            ODataTest(builder =>
            {
                oms = builder.Services.GetRequiredService<IOperationMethodStorage>();
            }, () =>
            {
                using var _ = new CleanOperationCenterBlock();
                OperationCenter.Discover();

                // prepare test file
                const string parentPath = "/Root/MyFiles";
                var parent = RepositoryTools.CreateStructure(parentPath, "SystemFolder")
                             ?? Content.Load(parentPath);
                
                // workaround for trash disabled default value (true for Folders)
                parent["TrashDisabled"] = false;
                parent.SaveSameVersionAsync(CancellationToken.None).GetAwaiter().GetResult();

                var file = new File(parent.ContentHandler) { Name = Guid.NewGuid() + ".docx" };
                file.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();

                // delete to the Trash
                file.DeleteAsync(CancellationToken.None).GetAwaiter().GetResult();

                // reload to get the new trashbag parent
                file = Node.Load<File>(file.Id);
                if (!(file.Parent is TrashBag bag))
                    throw new InvalidOperationException("File is not in the Trash.");

                var bagContent = Content.Create(bag);
                var actions = oms.GetActions(
                    ActionFramework.GetActionsFromContentRepository(bagContent, null, null),
                    bagContent, null, null).ToArray();

                Assert.IsNotNull(actions.SingleOrDefault(a => a.Name == "Restore"), "Restore action not available.");
            });
        }

        /* ====================================================================== TOOLS */

        private readonly Attribute[] _defaultAttributes = new Attribute[] { new ODataFunction() };

        private OperationInfo AddMethod(MethodInfo method)
        {
            return OperationCenter.AddMethod(method);
        }
        private OperationInfo AddMethod(TestMethodInfo method, Attribute[] attributes = null)
        {
            return OperationCenter.AddMethod(method, attributes ?? _defaultAttributes);
        }

        #region Nested classes

        private class CleanOperationCenterBlock : IDisposable
        {
            public CleanOperationCenterBlock()
            {
                var _ = new ODataMiddleware(null, null, null); // Ensure running the first-touch discover
                OperationCenter.Operations.Clear();
            }
            public void Dispose()
            {
                OperationCenter.Operations.Clear();
                OperationCenter.Discover();
            }
        }

        private class TestContentHandler
        {
            public static readonly string CTD = @"<?xml version='1.0' encoding='utf-8'?>
<ContentType name='{0}' handler='{1}' xmlns='http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition'>
  <Fields>
    <Field name='Value' type='ShortText'/>
  </Fields>
</ContentType>
";
            public string Value { get; set; }
        }
        private Content GetContent(string value = null, string contentTypeName = null)
        {
            return Content.Create(
                new TestContentHandler {Value = value},
                string.Format(TestContentHandler.CTD, contentTypeName ?? "TestContent", typeof(TestContentHandler).FullName));
        }

        internal class PolicyStoreSwindler : IDisposable
        {
            private readonly Dictionary<string, IOperationMethodPolicy> _backup =
                new Dictionary<string, IOperationMethodPolicy>();

            public PolicyStoreSwindler()
            {
                foreach (var item in OperationCenter.Policies)
                    _backup.Add(item.Key, item.Value);
            }

            public void Dispose()
            {
                OperationCenter.Policies.Clear();
                foreach (var item in _backup)
                    OperationCenter.Policies.Add(item.Key, item.Value);
            }
        }
        internal class AllowEverything : OperationInspector
        {
            private StringBuilder _sb = new StringBuilder();
            public string Log { get { return _sb.ToString(); } }

            public override bool CheckByContentType(Content content, string[] contentTypes)
            {
                _sb.AppendLine($"CheckContentType: {content.ContentType.Name} ==>, {string.Join(",", contentTypes)}");
                return true;
            }
            public override OperationMethodVisibility CheckPolicies(string[] policies, OperationCallingContext context)
            {
                _sb.AppendLine($"CheckPolicies: {GetRealUserId(User.Current)}, {string.Join(",", policies)}");
                return OperationMethodVisibility.Enabled;
            }
            public override bool CheckByPermissions(Content content, string[] permissions)
            {
                _sb.AppendLine($"CheckByPermissions: {content.Id}, {GetRealUserId(User.Current)}, {string.Join(",", permissions)}");
                return true;
            }
            public override bool CheckByRoles(string[] expectedRoles, IEnumerable<string> actualRoles = null)
            {
                _sb.AppendLine($"CheckByRoles: {GetRealUserId(User.Current)}, {string.Join(",", expectedRoles)}");
                return true;
            }

            private int GetRealUserId(IUser user)
            {
                if (user is SystemUser sysUser)
                    return sysUser.OriginalUser.Id;
                return user.Id;
            }
        }

        internal class TestParameterInfo : ParameterInfo
        {
            public TestParameterInfo(int position, Type parameterType, string name, bool isOptional)
            {
                PositionImpl = position;
                NameImpl = name;
                ClassImpl = parameterType;
                if (isOptional)
                    AttrsImpl |= ParameterAttributes.Optional;
            }
        }

        public class TestMethodInfo : MethodInfo
        {
            private ParameterInfo[] _parameters;
            private Type _returnType;
            public TestMethodInfo(string name, string requiredParameters, string optionalParameters, Type returnType = null)
            {
                Name = name;
                _parameters = ParseParameters(requiredParameters, optionalParameters);
                _returnType = returnType ?? typeof(string);
            }
            private ParameterInfo[] ParseParameters(string requiredParameters, string optionalParameters)
            {
                var p = 0;
                var parameters =
                    requiredParameters?.Split(',').Select(x => ParseParameter(p++, x, true)).ToArray() ?? new ParameterInfo[0];
                if (optionalParameters != null)
                {
                    var optionals = optionalParameters.Split(',').Select(x => ParseParameter(p++, x, false)).ToArray();
                    parameters = parameters.Union(optionals).ToArray();
                }
                return parameters;
            }
            private ParameterInfo ParseParameter(int position, string src, bool required)
            {
                var terms = src.Trim().Split(' ');
                var type = ParseType(terms[0].Trim());
                return new TestParameterInfo(position, type, terms[1].Trim(), !required);
            }
            private Type ParseType(string src)
            {
                switch (src)
                {
                    case "Content": return typeof(Content);
                    case "string": return typeof(string);
                    case "object": return typeof(object);

                    case "int": return typeof(int);
                    case "long": return typeof(long);
                    case "byte": return typeof(byte);
                    case "double": return typeof(double);
                    case "decimal": return typeof(decimal);
                    case "float": return typeof(float);
                    case "bool": return typeof(bool);

                    case "int?": return typeof(int?);
                    case "long?": return typeof(long?);
                    case "byte?": return typeof(byte?);
                    case "double?": return typeof(double?);
                    case "decimal?": return typeof(decimal?);
                    case "float?": return typeof(float?);
                    case "bool?": return typeof(bool?);

                    case "string[]": return typeof(string[]);
                    case "object[]": return typeof(object[]);

                    case "int[]": return typeof(int[]);
                    case "byte[]": return typeof(byte[]);
                    case "long[]": return typeof(long[]);
                    case "double[]": return typeof(double[]);
                    case "decimal[]": return typeof(decimal[]);
                    case "float[]": return typeof(float[]);
                    case "bool[]": return typeof(bool[]);

                    case "int?[]": return typeof(int?[]);
                    case "byte?[]": return typeof(byte?[]);
                    case "long?[]": return typeof(long?[]);
                    case "double?[]": return typeof(double?[]);
                    case "decimal?[]": return typeof(decimal?[]);
                    case "float?[]": return typeof(float?[]);
                    case "bool?[]": return typeof(bool?[]);

                    // disallowed types
                    case "DateTime": return typeof(DateTime);
                    case "DateTime[]": return typeof(DateTime[]);
                    case "Elephant": return typeof(Elephant);
                    case "Elephant[]": return typeof(Elephant[]);
                    case "Spaceship": return typeof(Spaceship);
                    case "Spaceship[]": return typeof(Spaceship[]);

                    case "IEnumerable<int>": return typeof(IEnumerable<int>);
                    case "List<int>": return typeof(List<int>);
                    case "Stack<int>": return typeof(Stack<int>);
                    case "Dictionary<int-int>": return typeof(Dictionary<int, int>);

                    case "HttpContext": return typeof(HttpContext);

                    default:
                        throw new ApplicationException("Unknown type: " + src);
                }
            }

            /* ======================================================================================= */

            public override ParameterInfo[] GetParameters() => _parameters;

            public override Type ReturnType => _returnType;

            public override MethodAttributes Attributes => throw new NotImplementedException();

            public override RuntimeMethodHandle MethodHandle => throw new NotImplementedException();

            public override Type DeclaringType => throw new NotImplementedException();

            public override MethodInfo GetBaseDefinition()
            {
                throw new NotImplementedException();
            }

            public override MemberTypes MemberType => throw new NotImplementedException();

            public override ICustomAttributeProvider ReturnTypeCustomAttributes => throw new NotImplementedException();

            public override string Name { get; }

            public override Type ReflectedType => throw new NotImplementedException();

            public override object[] GetCustomAttributes(bool inherit)
            {
                throw new NotImplementedException();
            }

            public override object[] GetCustomAttributes(Type attributeType, bool inherit)
            {
                throw new NotImplementedException();
            }

            public override MethodImplAttributes GetMethodImplementationFlags()
            {
                throw new NotImplementedException();
            }

            public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
            {
                throw new NotImplementedException();
            }

            public override bool IsDefined(Type attributeType, bool inherit)
            {
                throw new NotImplementedException();
            }
        }

        public class DeniedUsersOperationMethodPolicy : IOperationMethodPolicy
        {
            private readonly int[] _deniedUsers;

            public string Name { get; } = "UserDenied";

            public DeniedUsersOperationMethodPolicy(int[] deniedUserIds)
            {
                _deniedUsers = deniedUserIds;
            }
            public OperationMethodVisibility GetMethodVisibility(IUser user, OperationCallingContext context)
            {
                return _deniedUsers.Contains(user.Id) ? OperationMethodVisibility.Invisible : OperationMethodVisibility.Enabled;
            }
        }

        public class AllowEverythingPolicy : IOperationMethodPolicy
        {
            public string Name { get; } = "AllowEverything";
            public OperationMethodVisibility GetMethodVisibility(IUser user, OperationCallingContext context)
            {
                return OperationMethodVisibility.Enabled;
            }
        }

        private class TestQueryCollection : IQueryCollection
        {
            readonly Dictionary<string, StringValues> _collection;

            public TestQueryCollection(string queryString)
            {
                var a = queryString
                    .TrimStart('?')
                    .Split('&')
                    .Select(x => x.Split('='))
                    .GroupBy(x => x[0], x => x[1],
                    (key, group) => new KeyValuePair<string, StringValues>(
                        key, new StringValues(group.ToArray())));
                _collection = new Dictionary<string, StringValues>(a);
            }

            public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() => _collection.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public bool ContainsKey(string key) => _collection.ContainsKey(key);
            public bool TryGetValue(string key, out StringValues value) => _collection.TryGetValue(key, out value);
            public int Count => _collection.Count;
            public ICollection<string> Keys => _collection.Keys;
            public StringValues this[string key] => _collection[key];
        }

        #endregion
    }
}
