﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Schema;
using SenseNet.ContentRepository.InMemory;
using SenseNet.Search.Indexing;
using SenseNet.Testing;
using SenseNet.Tests.Core;
using BinaryData = SenseNet.ContentRepository.Storage.BinaryData;

namespace SenseNet.ContentRepository.Tests
{
    [TestClass]
    public class ContentTypeTests : TestBase
    {
        [TestMethod]
        [TestCategory("ContentType")]
        public void ContentType_HeadElementOrder()
        {
            Test(() =>
            {
                var ctd = @"<ContentType name=""MyType1"" parentType=""GenericContent"" handler=""SenseNet.ContentRepository.GenericContent"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
    <DisplayName>displayName1</DisplayName>
    <Description>description1</Description>
    <Icon>icon1</Icon>
    <Preview>true</Preview>
    <Extension>extension1</Extension>
    <AllowIncrementalNaming>true</AllowIncrementalNaming>
    <AppInfo>appInfo</AppInfo>
    <AllowedChildTypes>User,Group</AllowedChildTypes>
    <Categories>cat1 cat2</Categories>
    <SystemType>true</SystemType>
    <AllowIndexing>true</AllowIndexing>
    <Fields>
        <Field name=""MyField"" type=""ShortText"">
            <DisplayName>$Ctd-GenericContent,Name-DisplayName</DisplayName>
            <Description>$Ctd-GenericContent,Name-Description</Description>
            <Icon>Field-icon</Icon>
            <AppInfo>Any well-formed xml</AppInfo>
            <Bind property=""Name"" />
            <Indexing>
                <Store>Yes</Store>
            </Indexing>
            <Categories>Category1 Category2</Categories>
            <Configuration />
        </Field>
    </Fields>
</ContentType>";

                ContentTypeInstaller.InstallContentType(ctd);
                var contentType = ContentType.GetByName("MyType1");
                Assert.AreEqual("displayName1", contentType.DisplayName);
                Assert.AreEqual("description1", contentType.Description);
                Assert.AreEqual("icon1", contentType.Icon);
                Assert.AreEqual(true, contentType.Preview);
                Assert.AreEqual("extension1", contentType.Extension);
                Assert.AreEqual(true, contentType.AllowIncrementalNaming);
                AssertSequenceEqual(new[] { "User", "Group" }, contentType.AllowedChildTypeNames);
                AssertSequenceEqual(new[] { "cat1", "cat2" }, contentType.Categories);
                Assert.AreEqual(true, contentType.IsSystemType);
                Assert.AreEqual(true, contentType.IsIndexingEnabled);

                var field = contentType.FieldSettings.First(f => f.Name == "MyField");
                Assert.AreEqual("Any well-formed xml", field.AppInfo);
                Assert.AreEqual(IndexStoringMode.Yes, field.IndexingInfo.IndexStoringMode);
                AssertSequenceEqual(new[] { "Category1", "Category2" }, field.Categories);
            });
        }

        /* =========================================================================== Categories */

        [TestMethod]
        [TestCategory("ContentType")]
        public void ContentType_Categories()
        {
            Test(() =>
            {
                var prefix = @"<ContentType name=""MyType1"" parentType=""GenericContent"" handler=""SenseNet.ContentRepository.GenericContent"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
";
                var suffix = @"
  <Fields />
</ContentType>";

                ContentTypeInstaller.InstallContentType($@"{prefix}{suffix}");
                var contentType = ContentType.GetByName("MyType1");
                Assert.IsNotNull(contentType.Categories);
                Assert.AreEqual(0, contentType.Categories.Count());

                ContentTypeInstaller.InstallContentType($@"{prefix}<Categories />{suffix}");
                contentType = ContentType.GetByName("MyType1");
                Assert.IsNotNull(contentType.Categories);
                Assert.AreEqual(0, contentType.Categories.Count());

                ContentTypeInstaller.InstallContentType($@"{prefix}<Categories>Cat1, Cat2 ; Cat3 Cat4</Categories>{suffix}");
                contentType = ContentType.GetByName("MyType1");
                Assert.IsNotNull(contentType.Categories);
                var categories = contentType.Categories.ToArray();
                AssertSequenceEqual(new[] {"Cat1", "Cat2", "Cat3", "Cat4"}, categories);
            });
        }

        [TestMethod]
        [TestCategory("ContentType")]
        public void ContentType_Field_Categories()
        {
            Test(() =>
            {
                var prefix = @"<ContentType name=""MyType1"" parentType=""GenericContent"" handler=""SenseNet.ContentRepository.GenericContent"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
  <Fields>
    <Field name=""MyField"" type=""ShortText"">
";
                var suffix = @"
    </Field>
  </Fields>
</ContentType>";

                ContentTypeInstaller.InstallContentType($@"{prefix}{suffix}");
                var contentType = ContentType.GetByName("MyType1");
                var field = contentType.FieldSettings.First(f => f.Name == "MyField");
                Assert.IsNotNull(field.Categories);
                Assert.AreEqual(0, field.Categories.Count());

                ContentTypeInstaller.InstallContentType($@"{prefix}<Categories />{suffix}");
                contentType = ContentType.GetByName("MyType1");
                field = contentType.FieldSettings.First(f => f.Name == "MyField");
                Assert.IsNotNull(field.Categories);
                Assert.AreEqual(0, field.Categories.Count());

                ContentTypeInstaller.InstallContentType($@"{prefix}<Categories>Cat1, Cat2 ; Cat3 Cat4</Categories>{suffix}");
                contentType = ContentType.GetByName("MyType1");
                field = contentType.FieldSettings.First(f => f.Name == "MyField");
                Assert.IsNotNull(field.Categories);
                var categories = field.Categories.ToArray();
                AssertSequenceEqual(new[] { "Cat1", "Cat2", "Cat3", "Cat4" }, categories);
            });
        }

        /* =========================================================================== MORE PERMISSIVE LOAD TESTS */

        [TestMethod]
        [TestCategory("ContentType")]
        public void ContentType_MorePermissiveLoad_MissingBinding_Load()
        {
            Test(() =>
            {
                var ctd0 = @"<ContentType name=""MyType1"" parentType=""GenericContent"" handler=""SenseNet.ContentRepository.GenericContent"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
  <Fields>
    <Field name=""ImageRef"" type=""Reference""></Field>
    <Field name=""ImageData"" type=""Binary""></Field>
    <Field name=""Image2"" type=""Image"">
      <Bind property=""ImageRef"" />
      <Bind property=""ImageData"" />
    </Field>
  </Fields>
</ContentType>";
                var ctd1 = @"<ContentType name=""MyType1"" parentType=""GenericContent"" handler=""SenseNet.ContentRepository.GenericContent"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
  <Fields>
    <Field name=""ImageRef"" type=""Reference""></Field>
    <Field name=""ImageData"" type=""Binary""></Field>
    <Field name=""Image2"" type=""Image""></Field>
  </Fields>
</ContentType>";

                // ARRANGE
                ContentTypeInstaller.InstallContentType(ctd0);

                var myType = ContentType.GetByName("MyType1");
                Assert.IsNotNull(myType);
                Assert.IsNotNull(myType.FieldSettings.FirstOrDefault(x => x.Name == "Image2"));

                HackDatabaseForMorePermissiveLoadTest("MyType1", ctd0, ctd1);

                // ACTION: Reload schema from the database.
                Cache.Reset();
                ContentTypeManager.Reload();

                // ASSERT: the ContentType is loaded but invalid.
                var myType1 = ContentType.GetByName("MyType1");
                Assert.AreEqual(ctd1, myType1.ToXml());
                Assert.IsTrue(myType1.IsInvalid);
            });

        }
        [TestMethod]
        [TestCategory("ContentType")]
        public void ContentType_MorePermissiveLoad_MissingBinding_Create()
        {
            Test(() =>
            {
                var ctd0 = @"<ContentType name=""MyType1"" parentType=""GenericContent"" handler=""SenseNet.ContentRepository.GenericContent"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
  <Fields>
    <Field name=""Image2"" type=""Image""></Field>
  </Fields>
</ContentType>";
                var ctd1 = @"<ContentType name=""MyType1"" parentType=""GenericContent"" handler=""SenseNet.ContentRepository.GenericContent"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
  <Fields>
    <Field name=""ImageRef"" type=""Reference""></Field>
    <Field name=""ImageData"" type=""Binary""></Field>
    <Field name=""Image2"" type=""Image"">
      <Bind property=""ImageRef"" />
      <Bind property=""ImageData"" />
    </Field>
  </Fields>
</ContentType>";

                // ACTION-1: Try install an invalid CTD.
                try
                {
                    //ContentTypeInstaller.InstallContentType(ctd0);
                    var binaryData = new BinaryData();
                    binaryData.FileName = "MyType1";
                    binaryData.SetStream(RepositoryTools.GetStreamFromString(ctd0));
                    var contentType = new ContentType(ContentType.GetByName("GenericContent"))
                    {
                        Name = "MyType1",
                        Binary = binaryData
                    };
                    contentType.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Fail("The expected exception was not thrown.");
                }
                catch (ContentRegistrationException e)
                {
                    // do nothing
                }

                // ACTION-2: reinstall without any problem.
                ContentTypeInstaller.InstallContentType(ctd1);


                // ACTION-3: Reload schema from the database.
                Cache.Reset();
                ContentTypeManager.Reload();

                // ASSERT: the ContentType is loaded and valid.
                var myType1 = ContentType.GetByName("MyType1");
                Assert.AreEqual(ctd1, myType1.ToXml());
                Assert.IsFalse(myType1.IsInvalid);
                Assert.IsNotNull(myType1.FieldSettings.FirstOrDefault(x => x.Name == "Image2"));
            });

        }


        [TestMethod]
        [TestCategory("ContentType")]
        public void ContentType_MorePermissiveLoad_MissingFieldHandler_Load()
        {
            Test(() =>
            {
                var ctd0 = @"<ContentType name=""MyType1"" parentType=""GenericContent"" handler=""SenseNet.ContentRepository.GenericContent"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
  <Fields>
    <Field name=""Field1"" type=""ShortText""></Field>
  </Fields>
</ContentType>";
                var ctd1 = @"<ContentType name=""MyType1"" parentType=""GenericContent"" handler=""SenseNet.ContentRepository.GenericContent"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
  <Fields>
    <Field name=""Field1"" type=""ShortText2""></Field>
  </Fields>
</ContentType>";

                // ARRANGE
                ContentTypeInstaller.InstallContentType(ctd0);

                var myType = ContentType.GetByName("MyType1");
                Assert.IsNotNull(myType);
                Assert.IsNotNull(myType.FieldSettings.FirstOrDefault(x => x.Name == "Field1"));

                HackDatabaseForMorePermissiveLoadTest("MyType1", ctd0, ctd1);

                // ACTION: Reload schema from the database.
                Cache.Reset();
                ContentTypeManager.Reload();

                // ASSERT: the ContentType is loaded but invalid.
                var myType1 = ContentType.GetByName("MyType1");
                Assert.AreEqual(ctd1, myType1.ToXml());
                Assert.IsTrue(myType1.IsInvalid);
            });

        }
        [TestMethod]
        [TestCategory("ContentType")]
        public void ContentType_MorePermissiveLoad_MissingFieldHandler_Create()
        {
            Test(() =>
            {
                var ctd0 = @"<ContentType name=""MyType1"" parentType=""GenericContent"" handler=""SenseNet.ContentRepository.GenericContent"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
  <Fields>
    <Field name=""Field1"" type=""ShortText2""></Field>
  </Fields>
</ContentType>";
                var ctd1 = @"<ContentType name=""MyType1"" parentType=""GenericContent"" handler=""SenseNet.ContentRepository.GenericContent"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
  <Fields>
    <Field name=""Field1"" type=""ShortText""></Field>
  </Fields>
</ContentType>";

                // ACTION-1: Try install an invalid CTD.
                try
                {
                    //ContentTypeInstaller.InstallContentType(ctd0);
                    var binaryData = new BinaryData();
                    binaryData.FileName = "MyType1";
                    binaryData.SetStream(RepositoryTools.GetStreamFromString(ctd0));
                    var contentType = new ContentType(ContentType.GetByName("GenericContent"))
                    {
                        Name = "MyType1",
                        Binary = binaryData
                    };
                    contentType.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Fail("The expected exception was not thrown.");
                }
                catch (ContentRegistrationException e)
                {
                    // do nothing
                }

                // ACTION-2: reinstall without any problem.
                ContentTypeInstaller.InstallContentType(ctd1);


                // ACTION-3: Reload schema from the database.
                Cache.Reset();
                ContentTypeManager.Reload();

                // ASSERT: the ContentType is loaded and valid.
                var myType1 = ContentType.GetByName("MyType1");
                Assert.AreEqual(ctd1, myType1.ToXml());
                Assert.IsFalse(myType1.IsInvalid);
                Assert.IsNotNull(myType1.FieldSettings.FirstOrDefault(x => x.Name == "Field1"));
            });

        }


        [TestMethod]
        [TestCategory("ContentType")]
        public void ContentType_MorePermissiveLoad_MissingContentHandler_Load()
        {
            Test(() =>
            {
                var ctd0 = @"<ContentType name=""MyType1"" parentType=""GenericContent"" handler=""SenseNet.ContentRepository.GenericContent"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
  <Fields>
    <Field name=""Field1"" type=""ShortText""></Field>
  </Fields>
</ContentType>";
                var ctd1 = @"<ContentType name=""MyType1"" parentType=""GenericContent"" handler=""MyType"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
  <Fields>
    <Field name=""Field1"" type=""ShortText""></Field>
  </Fields>
</ContentType>";

                // ARRANGE
                ContentTypeInstaller.InstallContentType(ctd0);

                var myType = ContentType.GetByName("MyType1");
                Assert.IsNotNull(myType);
                Assert.IsNotNull(myType.FieldSettings.FirstOrDefault(x => x.Name == "Field1"));

                HackDatabaseForMorePermissiveLoadTest("MyType1", ctd0, ctd1);

                // ACTION: Reload schema from the database.
                Cache.Reset();
                ContentTypeManager.Reload();

                // ASSERT: the ContentType is loaded but invalid.
                var myType1 = ContentType.GetByName("MyType1");
                Assert.AreEqual(ctd1, myType1.ToXml());
                Assert.IsTrue(myType1.IsInvalid);
            });

        }
        [TestMethod]
        [TestCategory("ContentType")]
        public void ContentType_MorePermissiveLoad_MissingContentHandler_Create()
        {
            Test(() =>
            {
                var ctd0 = @"<ContentType name=""MyType1"" parentType=""GenericContent"" handler=""MyType"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
  <Fields>
    <Field name=""Field1"" type=""ShortText2""></Field>
  </Fields>
</ContentType>";
                var ctd1 = @"<ContentType name=""MyType1"" parentType=""GenericContent"" handler=""SenseNet.ContentRepository.GenericContent"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
  <Fields>
    <Field name=""Field1"" type=""ShortText""></Field>
  </Fields>
</ContentType>";

                // ACTION-1: Try install an invalid CTD.
                try
                {
                    //ContentTypeInstaller.InstallContentType(ctd0);
                    var binaryData = new BinaryData();
                    binaryData.FileName = "MyType1";
                    binaryData.SetStream(RepositoryTools.GetStreamFromString(ctd0));
                    var contentType = new ContentType(ContentType.GetByName("GenericContent"))
                    {
                        Name = "MyType1",
                        Binary = binaryData
                    };
                    contentType.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Fail("The expected exception was not thrown.");
                }
                catch (ContentRegistrationException e)
                {
                    // do nothing
                }

                // ACTION-2: reinstall without any problem.
                ContentTypeInstaller.InstallContentType(ctd1);


                // ACTION-3: Reload schema from the database.
                Cache.Reset();
                ContentTypeManager.Reload();

                // ASSERT: the ContentType is loaded and valid.
                var myType1 = ContentType.GetByName("MyType1");
                Assert.AreEqual(ctd1, myType1.ToXml());
                Assert.IsFalse(myType1.IsInvalid);
                Assert.IsNotNull(myType1.FieldSettings.FirstOrDefault(x => x.Name == "Field1"));
            });

        }

        private void HackDatabaseForMorePermissiveLoadTest(string contentTypeName, string oldCtd, string newCtd)
        {
            // Get related data row (FileDoc of the InMemoryDatabase)
            var dataProvider = (InMemoryDataProvider)Providers.Instance.DataProvider;
            var db = dataProvider.DB;
            var fileRow = db.Files.First(x => x.FileNameWithoutExtension == contentTypeName);

            // Get blob id
            var bp = (InMemoryBlobProvider)Providers.Instance.BlobStorage.GetProvider(0);
            var data = (InMemoryBlobProviderData)bp.ParseData(fileRow.BlobProviderData);

            // Get and check the old CTD from the related blob
            var bpAcc = new ObjectAccessor(bp);
            var blobs = (Dictionary<Guid, byte[]>)bpAcc.GetField("_blobStorage");
            var oldBuffer = blobs[data.BlobId];
            Assert.AreEqual(oldCtd, RepositoryTools.GetStreamString(new MemoryStream(oldBuffer)));

            // Change the related blob to the invalid CTD
            var stream = (MemoryStream)RepositoryTools.GetStreamFromString(newCtd);
            var newBuffer = new byte[stream.Length];
            var newStream = new MemoryStream(newBuffer);
            stream.CopyTo(newStream);
            fileRow.Buffer = newBuffer;
            fileRow.Size = newBuffer.Length;
        }

        /* =================================================================================== */

        [TestMethod]
        [TestCategory("ContentType")]
        public void ContentType_Analyzers()
        {
            var contentTypeName = System.Reflection.MethodBase.GetCurrentMethod().Name;
            var fieldName1 = "Field1";
            var fieldName2 = "Field2";
            var analyzerValue1 = IndexFieldAnalyzer.Whitespace;
            var analyzerValue2 = IndexFieldAnalyzer.Standard;

            Test(() =>
            {
                var analyzersBefore = Providers.Instance.SearchManager.SearchEngine.GetAnalyzers();

                /**/ContentTypeInstaller.InstallContentType($@"<?xml version='1.0' encoding='utf-8'?>
<ContentType name='{contentTypeName}' parentType='GenericContent'
         handler='{typeof(GenericContent).FullName}' xmlns='http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition'>
    <Fields>
        <Field name='{fieldName1}' type='ShortText'><Indexing><Analyzer>{analyzerValue1}</Analyzer></Indexing></Field>
        <Field name='{fieldName2}' type='ShortText'><Indexing><Analyzer>{analyzerValue2}</Analyzer></Indexing></Field>
    </Fields>
</ContentType>
");
                ContentType.GetByName(contentTypeName); // starts the contenttype system

                var analyzersAfter = Providers.Instance.SearchManager.SearchEngine.GetAnalyzers();

                Assert.IsFalse(analyzersBefore.ContainsKey(fieldName1));
                Assert.IsFalse(analyzersBefore.ContainsKey(fieldName2));

                Assert.IsTrue(analyzersAfter.ContainsKey(fieldName1));
                Assert.IsTrue(analyzersAfter[fieldName1] == analyzerValue1);
                Assert.IsTrue(analyzersAfter.ContainsKey(fieldName2));
                Assert.IsTrue(analyzersAfter[fieldName2] == analyzerValue2);
            });
        }

        [TestMethod]
        [TestCategory("ContentType")]
        [ExpectedException(typeof(ContentRegistrationException))]
        public void ContentType_WrongAnalyzer()
        {
            var contentTypeName = System.Reflection.MethodBase.GetCurrentMethod().Name;
            var fieldName = "Field1";
            var analyzerValue = "Lucene.Net.Analysis.KeywordAnalyzer";
            Test(() =>
            {
                var searchEngine = Providers.Instance.SearchManager.SearchEngine as InMemorySearchEngine;
                Assert.IsNotNull(searchEngine);

                ContentTypeInstaller.InstallContentType($@"<?xml version='1.0' encoding='utf-8'?>
<ContentType name='{contentTypeName}' parentType='GenericContent'
         handler='{typeof(GenericContent).FullName}' xmlns='http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition'>
    <Fields>
        <Field name='{fieldName}' type='ShortText'>
            <Indexing>
                <Analyzer>{analyzerValue}</Analyzer>
            </Indexing>
        </Field>
    </Fields>
</ContentType>
");
            });
        }

        private enum CheckFieldResult { CtdError, /*SchemaError, FieldExists,*/ NoError }

        [TestMethod]
        [TestCategory("ContentType")]
        public void ContentType_FieldNameBlackList()
        {
            var contentTypeName = System.Reflection.MethodBase.GetCurrentMethod().Name;

            var fieldNames = new[] {"Actions", "Type", "TypeIs", "Children", "InFolder", "InTree",
                "IsSystemContent", "SharedWith", "SharedBy","SharingMode", "SharingLevel"};

            var ctdErrors = new List<string>();
            var noErrors = new List<string>();
            Test(() =>
            {
                foreach (var fieldName in fieldNames)
                {
                    switch (CheckField(contentTypeName, fieldName))
                    {
                        case CheckFieldResult.CtdError: ctdErrors.Add(fieldName); break;
                        case CheckFieldResult.NoError: noErrors.Add(fieldName); break;
                        default: throw new ArgumentOutOfRangeException();
                    }
                }

                Assert.AreEqual(fieldNames.Length, ctdErrors.Count);
            });
        }

        private CheckFieldResult CheckField(string contentTypeName, string fieldName)
        {
            var result = CheckField(contentTypeName, fieldName, "ShortText");
            if (result == CheckFieldResult.NoError)
                result = CheckField(contentTypeName, fieldName, "Integer");
            return result;
        }
        private CheckFieldResult CheckField(string contentTypeName, string fieldName, string fieldType)
        {
            var result = CheckFieldResult.NoError;

            try
            {
                ContentTypeInstaller.InstallContentType($@"<?xml version='1.0' encoding='utf-8'?>
<ContentType name='{contentTypeName}' parentType='GenericContent'
         handler='{typeof(GenericContent).FullName}' xmlns='http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition'>
    <Fields>
        <Field name='{fieldName}' type='{fieldType}'/>
    </Fields>
</ContentType>
");
            }
            catch (Exception e)
            {
                result = CheckFieldResult.CtdError;
            }

            var contentType = ContentType.GetByName(contentTypeName);
            if(contentType != null)
                ContentTypeInstaller.RemoveContentTypeAsync(contentTypeName, CancellationToken.None).GetAwaiter().GetResult();

            return result;
        }
    }
}
