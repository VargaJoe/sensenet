﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SenseNet.ApplicationModel;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Fields;
using SenseNet.ContentRepository.Schema;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.ContentRepository.Storage.Security;
using SenseNet.Packaging;
using SenseNet.Packaging.Tools;
using SenseNet.Portal.Handlers;
using SenseNet.Security;

namespace SenseNet.ContentRepository
{
    internal class ServicesComponent : SnComponent
    {
        public override string ComponentId => "SenseNet.Services";

        //TODO: Set SupportedVersion before release.
        // This value has to change if there were database, content
        // or configuration changes since the last release that
        // should be enforced using an upgrade patch.
        public override Version SupportedVersion => new Version(7, 8, 0);

        public override void AddPatches(PatchBuilder builder)
        {
            builder.Patch("7.7.11", "7.7.12", "2020-09-7", "Upgrades sensenet content repository.")
                .Action(context =>
                {
                    #region CTD changes

                    var cb = new ContentTypeBuilder(context.GetService<ILogger<ContentTypeBuilder>>());

                    cb.Type("Settings")
                        .Field("Description", "LongText")
                        .VisibleBrowse(FieldVisibility.Hide)
                        .VisibleEdit(FieldVisibility.Show)
                        .VisibleNew(FieldVisibility.Show);

                    cb.Apply();

                    #endregion

                    #region Settings Description field changes

                    SetSettingsDescription("Indexing", "In this Settings file you can customize the indexing behavior (for example the text extractor used in case of different file types) of the system.");
                    SetSettingsDescription("Logging", "Contains logging-related settings, for example which events are sent to the trace. You can control tracing by category: switch on or off writing messages in certain categories to the trace channel.");
                    SetSettingsDescription("MailProcessor", "The content list Inbox feature requires an Exchange or POP3 server configuration and other settings related to connecting libraries to a mailbox.");
                    SetSettingsDescription("OAuth", "When users log in using one of the configured OAuth providers (like Google or Facebook), these settings control the type and place of the newly created users.");
                    SetSettingsDescription("OfficeOnline", "To open or edit Office documents in the browser, the system needs to know the address of the Office Online Server that provides the user interface for the feature. In this section you can configure that and other OOS-related settings.");
                    SetSettingsDescription("Sharing", "Content sharing related options.");
                    SetSettingsDescription("TaskManagement", "When the Task Management module is installed, this is the place where you can configure the connection to the central task management service.");
                    SetSettingsDescription("UserProfile", "When a user is created, and the profile feature is enabled (in the app configuration), they automatically get a profile – a workspace dedicated to the user’s personal documents and tasks. In this setting section you can customize the content type and the place of this profile.");

                    void SetSettingsDescription(string name, string description)
                    {
                        var settings = Content.Load("/Root/System/Settings/" + name + ".settings");
                        if (settings == null)
                            return;
                        settings["Description"] = description;
                        settings.SaveSameVersionAsync(CancellationToken.None).GetAwaiter().GetResult();
                    }

                    #endregion

                    #region App changes

                    var app1 = Content.Load("/Root/(apps)/Folder/Add");
                    if (app1 != null)
                    {
                        app1["Scenario"] = string.Empty;
                        app1.SaveSameVersionAsync(CancellationToken.None).GetAwaiter().GetResult();
                    }

                    var app2 = Content.Load("/Root/(apps)/ContentType/Edit");
                    if (app2 == null)
                    {
                        var parent = RepositoryTools.CreateStructure("/Root/(apps)/ContentType") ??
                                     Content.Load("/Root/(apps)/ContentType");

                        app2 = Content.CreateNew("ClientApplication", parent.ContentHandler, "Edit");
                        app2["DisplayName"] = "$Action,Edit";
                        app2["Scenario"] = "ContextMenu";
                        app2["Icon"] = "edit";
                        app2["RequiredPermissions"] = "See;Open;OpenMinor;Save";
                        app2.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();

                        // set app permissions
                        var developersGroupId = NodeHead.Get("/Root/IMS/BuiltIn/Portal/Developers")?.Id ?? 0;
                        var aclEditor = Providers.Instance.SecurityHandler.SecurityContext.CreateAclEditor();
                        aclEditor
                            .Allow(app2.Id, Identifiers.AdministratorsGroupId,
                                false, PermissionType.RunApplication);
                        if (developersGroupId > 0)
                            aclEditor.Allow(app2.Id, developersGroupId,
                                false, PermissionType.RunApplication);

                        aclEditor.ApplyAsync(CancellationToken.None).GetAwaiter().GetResult();
                    }

                    #endregion
                });

            builder.Patch("7.7.12", "7.7.13", "2020-09-23", "Upgrades sensenet content repository.")
                .Action(context =>
                {
                    #region String resources

                    var rb = new ResourceBuilder();

                    rb.Content("CtdResourcesAB.xml")
                        .Class("Ctd-BinaryFieldSetting")
                        .Culture("en")
                        .AddResource("DisplayName", "Binary field")
                        .Culture("hu")
                        .AddResource("DisplayName", "Bináris mező");

                    rb.Content("CtdResourcesCD.xml")
                        .Class("Ctd-ChoiceFieldSetting")
                        .Culture("en")
                        .AddResource("DisplayName", "Choice field")
                        .Culture("hu")
                        .AddResource("DisplayName", "Választó mező")
                        .Class("Ctd-CurrencyFieldSetting")
                        .Culture("en")
                        .AddResource("DisplayName", "Currency field")
                        .Culture("hu")
                        .AddResource("DisplayName", "Pénzérték mező")
                        .Class("Ctd-DateTimeFieldSetting")
                        .Culture("en")
                        .AddResource("DisplayName", "DateTime field")
                        .Culture("hu")
                        .AddResource("DisplayName", "Dátum mező");

                    rb.Content("CtdResourcesEF.xml")
                        .Class("Ctd-FieldControlTemplate")
                        .Culture("en")
                        .AddResource("DisplayName", "FieldControlTemplate")
                        .AddResource("Description", "A type for FieldControl templates.")
                        .Culture("hu")
                        .AddResource("DisplayName", "Mező vezérlő sablon")
                        .AddResource("Description", "Mező vezérlő sablont tároló fájl")
                        .Class("Ctd-FieldControlTemplates")
                        .Culture("en")
                        .AddResource("DisplayName", "FieldControlTemplates")
                        .AddResource("Description", "This is the container type for ContentViews. Instances are allowed only at /Root/Global/fieldcontroltemplates.")
                        .Culture("hu")
                        .AddResource("DisplayName", "Mező vezérlő sablonok")
                        .AddResource("Description", "Mező vezérlő sablonokat tároló mappa. Csak egy lehet, itt: /Root/Global/fieldcontroltemplates.");
                    
                    rb.Content("CtdResourcesGH.xml")
                        .Class("Ctd-HyperLinkFieldSetting")
                        .Culture("en")
                        .AddResource("DisplayName", "Hyperlink field")
                        .Culture("hu")
                        .AddResource("DisplayName", "Hivatkozás mező");


                    rb.Content("CtdResourcesIJK.xml")
                        .Class("Ctd-Image")
                        .Culture("en")
                        .AddResource("DisplayName", "Image")
                        .AddResource("Name-DisplayName", "Name")
                        .Culture("hu")
                        .AddResource("DisplayName", "Kép")
                        .AddResource("Name-DisplayName", "Név")
                        .Class("Ctd-IntegerFieldSetting")
                        .Culture("en")
                        .AddResource("DisplayName", "Integer field")
                        .Culture("hu")
                        .AddResource("DisplayName", "Egész szám mező");

                    rb.Content("CtdResourcesLM.xml")
                        .Class("Ctd-LongTextFieldSetting")
                        .Culture("en")
                        .AddResource("DisplayName", "Longtext field")
                        .Culture("hu")
                        .AddResource("DisplayName", "Hosszú szöveges mező");

                    rb.Content("CtdResourcesNOP.xml")
                        .Class("Ctd-NullFieldSetting")
                        .Culture("en")
                        .AddResource("DisplayName", "Null field")
                        .Culture("hu")
                        .AddResource("DisplayName", "Null mező")
                        .Class("Ctd-NumberFieldSetting")
                        .Culture("en")
                        .AddResource("DisplayName", "Number field")
                        .Culture("hu")
                        .AddResource("DisplayName", "Szám mező")
                        .Class("Ctd-PasswordFieldSetting")
                        .Culture("en")
                        .AddResource("DisplayName", "Password field")
                        .Culture("hu")
                        .AddResource("DisplayName", "Jelszó mező")
                        .Class("Ctd-PermissionChoiceFieldSetting")
                        .Culture("en")
                        .AddResource("DisplayName", "Permission choice field")
                        .Culture("hu")
                        .AddResource("DisplayName", "Jogosultság választó mező");

                    rb.Content("CtdResourcesRS.xml")
                        .Class("Ctd-ReferenceFieldSetting")
                        .Culture("en")
                        .AddResource("DisplayName", "Reference field")
                        .Culture("hu")
                        .AddResource("DisplayName", "Referencia mező")
                        .Class("Ctd-ShortTextFieldSetting")
                        .Culture("en")
                        .AddResource("DisplayName", "ShortText field")
                        .Culture("hu")
                        .AddResource("DisplayName", "Rövid szöveges mező");

                    rb.Content("CtdResourcesTZ.xml")
                        .Class("Ctd-Workspace")
                        .Culture("en")
                        .AddResource("Name-DisplayName", "Name")
                        .Culture("hu")
                        .AddResource("Name-DisplayName", "Név")
                        .Class("Ctd-TextFieldSetting")
                        .Culture("en")
                        .AddResource("DisplayName", "Text field")
                        .Culture("hu")
                        .AddResource("DisplayName", "Szöveges mező")
                        .Class("Ctd-UserControl")
                        .Culture("en")
                        .AddResource("DisplayName", "User control")
                        .AddResource("Description", "A type for storing ASP.NET user controls.")
                        .Culture("hu")
                        .AddResource("DisplayName", "Egyéni vezérlőelem")
                        .AddResource("Description", "ASP.NET user control tárolására.")
                        .Class("Ctd-ViewBase")
                        .Culture("en")
                        .AddResource("DisplayName", "View base")
                        .AddResource("Description", "An abstract type for ContentList views.")
                        .AddResource("IsDefault-DisplayName", "Default")
                        .AddResource("IsDefault-Description",
                            "Whether this is the default view on the parent ContentList.")
                        .AddResource("Template-DisplayName", "Markup template")
                        .AddResource("Template-Description", "The Xslt template used to generate the view.")
                        .AddResource("FilterXml-DisplayName", "Filtering")
                        .AddResource("FilterXml-Description", "Define filtering rules for the view.")
                        .AddResource("EnableAutofilters-DisplayName", "Enable autofilters")
                        .AddResource("EnableAutofilters-Description",
                            "If autofilters are enabled system content will be filtered from the query.")
                        .AddResource("EnableLifespanFilter-DisplayName", "Enable lifespan filter")
                        .AddResource("EnableLifespanFilter-Description",
                            "If lifespan filter is enabled only valid content will be in the result.")
                        .AddResource("Hidden-Description",
                            "The view won't show in the selector menu if checked. (If unsure, leave unchecked).")
                        .AddResource("QueryTop-DisplayName", "Top")
                        .AddResource("QueryTop-Description",
                            "If you do not want to display all content please specify here a value greater than 0.")
                        .AddResource("QuerySkip-DisplayName", "Skip")
                        .AddResource("QuerySkip-Description",
                            "If you do not want to display the first several content please specify here a value greater than 0.")
                        .AddResource("Icon-DisplayName", "Icon identifier")
                        .AddResource("Icon-Description", "The string identifier of the View's icon.")
                        .Culture("hu")
                        .AddResource("DisplayName", "View base")
                        .AddResource("Description", "Minden view (listanézet) őse.")
                        .AddResource("IsDefault-DisplayName", "Alapértelmezett")
                        .AddResource("IsDefault-Description", "Legyen ez az alapértelmezett listanézet..")
                        .AddResource("Template-DisplayName", "Sablon")
                        .AddResource("Template-Description", "A listát generáló xslt sablon.")
                        .AddResource("FilterXml-DisplayName", "Szűrés")
                        .AddResource("FilterXml-Description", "A listanézet szűrési feltételei.")
                        .AddResource("EnableAutofilters-DisplayName", "Automata szűrések bekapcsolása")
                        .AddResource("EnableAutofilters-Description",
                            "Ha be van kapcsolva, a rendszer fájlok kiszűrésre kerülnek.")
                        .AddResource("EnableLifespanFilter-DisplayName", "Élettartam szűrés")
                        .AddResource("EnableLifespanFilter-Description",
                            "Ha be van kapcsolva, akkor csak az időben aktuális elemek jelennek meg.")
                        .AddResource("Hidden-Description",
                            "Ha be van pipálva, akkor a listanézet nem jelenik meg a választhatók között a felületen.")
                        .AddResource("QueryTop-DisplayName", "Elemszám")
                        .AddResource("QueryTop-Description",
                            "Ha nem akarja az összes elemet megjeleníteni, írjon be egy nullánál nagyobb számot.")
                        .AddResource("QuerySkip-DisplayName", "Kihagyott elemek")
                        .AddResource("QuerySkip-Description",
                            "Ha a lista első valahány elemét ki szeretné hagyni a megjelenítésből, írja be az elhagyni kívánt elemek számát.")
                        .AddResource("Icon-DisplayName", "Ikon azonosító")
                        .AddResource("Icon-Description", "Név amely a listanézet ikonját azonosítja.")
                        .Class("Ctd-WebContent")
                        .Culture("en")
                        .AddResource("DisplayName", "Web Content (structured web content)")
                        .AddResource("Description", "Web Content is the base type for structured web content.")
                        .AddResource("ReviewDate-DisplayName", "Review date")
                        .AddResource("ReviewDate-Description", "")
                        .AddResource("ArchiveDate-DisplayName", "Archive date")
                        .AddResource("ArchiveDate-Description", "")
                        .Culture("hu")
                        .AddResource("DisplayName", "Webes tartalom")
                        .AddResource("Description", "")
                        .AddResource("ReviewDate-DisplayName", "Ellenőrzés dátuma")
                        .AddResource("ReviewDate-Description", "")
                        .AddResource("ArchiveDate-DisplayName", "Archiválás dátuma")
                        .AddResource("ArchiveDate-Description", "")
                        .Class("Ctd-XmlFieldSetting")
                        .Culture("en")
                        .AddResource("DisplayName", "Xml field")
                        .Culture("hu")
                        .AddResource("DisplayName", "Xml mező")
                        .Class("Ctd-XsltApplication")
                        .Culture("en")
                        .AddResource("DisplayName", "Xslt application")
                        .AddResource("Description", "Xslt rendering application.")
                        .AddResource("Binary-DisplayName", "Xslt template")
                        .AddResource("Binary-Description", "Upload or enter the Xslt template to be used in rendering.")
                        .AddResource("MimeType-DisplayName", "MIME type")
                        .AddResource("MimeType-Description",
                            "Sets HTTP MIME type of the output stream. Default value: application/xml.")
                        .AddResource("OmitXmlDeclaration-DisplayName", "OmitXmlDeclaration")
                        .AddResource("OmitXmlDeclaration-Description",
                            "Sets a value indicating whether to write XML declaration.")
                        .AddResource("ResponseEncoding-DisplayName", "Response encoding")
                        .AddResource("ResponseEncoding-Description",
                            "Sets the text encoding to use. Default value: UTF-8.")
                        .AddResource("WithChildren-DisplayName", "With children")
                        .AddResource("WithChildren-Description",
                            "Sets a value indicating whether to render content with all children Default value: true.")
                        .AddResource("Cacheable-DisplayName", "Application is cached")
                        .AddResource("Cacheable-Description",
                            "If set the output of the application will be cached. <div class='ui-helper-clearfix sn-dialog-editportlet-warning'><img class='sn-icon sn-icon16 sn-floatleft' src='/Root/Global/images/icons/16/warning.png' /><i>Switching off application cache may cause performance issues!</i></div>")
                        .AddResource("CacheableForLoggedInUser-DisplayName",
                            "Application is cached for logged in users")
                        .AddResource("CacheableForLoggedInUser-Description",
                            "If set the output of the application will be cached for logged in users. <div class='ui-helper-clearfix sn-dialog-editportlet-warning'><img class='sn-icon sn-icon16 sn-floatleft' src='/Root/Global/images/icons/16/warning.png' /><i>Switching off application cache may cause performance issues!</i></div>")
                        .AddResource("CacheByPath-DisplayName", "Request path influences caching")
                        .AddResource("CacheByPath-Description",
                            "Defines whether the requested content path is included in the cache key. When unchecked application output is preserved regardless of the page's current context content or request path. Check it if you want to cache application output depending on the requested context content.")
                        .AddResource("CacheByParams-DisplayName", "Url query params influence caching")
                        .AddResource("CacheByParams-Description",
                            "Defines whether the url query params are also included in the cache key. When unchecked application output is preserved regardless of changing url params.")
                        .AddResource("CacheByLanguage-DisplayName", "Language influences caching")
                        .AddResource("CacheByLanguage-Description",
                            "Defines whether the language code is also included in the cache key. When unchecked application output is preserved regardless of the language that the users use to browse the site.")
                        .AddResource("CacheByHost-DisplayName", "Host influences caching")
                        .AddResource("CacheByHost-Description",
                            "Defines whether the URL-host (e.g. 'example.com') is also included in the cache key. When unchecked application output is preserved regardless of the host that the users use to browse the site.")
                        .AddResource("AbsoluteExpiration-DisplayName", "Absolute expiration")
                        .AddResource("AbsoluteExpiration-Description",
                            "Given in seconds. The application will be refreshed periodically with the given time period. -1 means that the value is defined by 'AbsoluteExpirationSeconds' setting in the web.config.")
                        .AddResource("SlidingExpirationMinutes-DisplayName", "Sliding expiration")
                        .AddResource("SlidingExpirationMinutes-Description",
                            "Given in seconds. The application is refreshed when it has not been accessed for the given seconds. -1 means that the value is defined by 'SlidingExpirationSeconds' setting in the web.config.")
                        .AddResource("CustomCacheKey-DisplayName", "Custom cache key")
                        .AddResource("CustomCacheKey-Description",
                            "Defines a custom cache key independent of requested path and query params. Useful when the same static output is rendered at various pages. <div class='ui-helper-clearfix sn-dialog-editportlet-warning'><img class='sn-icon sn-icon16 sn-floatleft' src='/Root/Global/images/icons/16/warning.png' /><i>For experts only! Leave empty if unsure.</i></div>")
                        .Culture("hu")
                        .AddResource("DisplayName", "Xslt alkalmazás")
                        .AddResource("Description",
                            "Alkalmazás, amely XSLT segítségével jeleníti meg az aktuális tartalmat.")
                        .AddResource("Binary-DisplayName", "Xslt sablon")
                        .AddResource("Binary-Description", "Adja meg az xslt sablont a megjelenítéshez.")
                        .AddResource("MimeType-DisplayName", "MIME type")
                        .AddResource("MimeType-Description",
                            "Beállítja a kimeneti stream HTTP MIME type-ját. Alapértelmezett érték: <i>alkalmazás/xml</i>.")
                        .AddResource("OmitXmlDeclaration-DisplayName", "Xml deklaráció kihagyása")
                        .AddResource("OmitXmlDeclaration-Description",
                            "Megadhatja, hogy kihagyjuk-e az xml deklarációt.")
                        .AddResource("ResponseEncoding-DisplayName", "Kimeneti stream kódolás")
                        .AddResource("ResponseEncoding-Description",
                            "Beállítja a szöveg kódolását. Alapértelmezett érték: <i>UTF-8</i>.")
                        .AddResource("WithChildren-DisplayName", "Gyermek tartalmak")
                        .AddResource("WithChildren-Description",
                            "Ha be van állítva, a gyermek elemek is szerepelni fognak az oldalon.")
                        .AddResource("Cacheable-DisplayName", "Az oldal kerüljön be a gyorsítótárba")
                        .AddResource("Cacheable-Description",
                            "Ha be van állítva, az oldal kimenete bekerül a gyorsítótárba<div class='ui-helper-clearfix sn-dialog-editportlet-warning'><img class='sn-icon sn-icon16 sn-floatleft' src='/Root/Global/images/icons/16/warning.png' /><i>Ennek kikapcsolása nagy terhelés alatt sebesség-problémákat okozhat!</i></div>")
                        .AddResource("CacheableForLoggedInUser-DisplayName",
                            "Az oldal kerüljön be a gyorsítótárba belépett felhasználók számára.")
                        .AddResource("CacheableForLoggedInUser-Description",
                            "Ha be van állítva, az oldal kimenete bekerül a gyorsítótárba belépett felhasználók számára. <div class='ui-helper-clearfix sn-dialog-editportlet-warning'><img class='sn-icon sn-icon16 sn-floatleft' src='/Root/Global/images/icons/16/warning.png' /><i>Ennek kikapcsolása nagy terhelés alatt sebesség-problémákat okozhat!</i></div>")
                        .AddResource("CacheByPath-DisplayName", "Az aktuális tartalom befolyásolja a cache-elést")
                        .AddResource("CacheByPath-Description",
                            "Ha be van állítva, a kért tartalom útvonala (Path) befolyásolja a gyorsítótárat. Ha nincs, az oldal kimenete ugyanaz lesz, függetlenül az aktuális tartalomtól.")
                        .AddResource("CacheByParams-DisplayName", "URL paraméterek befolyásolják a gyorsítótárat")
                        .AddResource("CacheByParams-Description",
                            "Ha nincs bekapcsolva, az oldal kimenete ugyanaz lesz, függetlenül az URL paraméterektől.")
                        .AddResource("CacheByLanguage-DisplayName", "A nyelv befolyásolja a gyorsítótárat")
                        .AddResource("CacheByLanguage-Description",
                            "Ha nincs bekapcsolva, az oldal kimenete ugyanaz lesz, függetlenül attól, hogy a felhasználó milyen nyelven nézi az oldalt.")
                        .AddResource("CacheByHost-DisplayName", "A host befolyásolja a gyorsítótárat")
                        .AddResource("CacheByHost-Description",
                            "Ha nincs bekapcsolva, az oldal kimenete ugyanaz lesz, függetlenül attól, hogy a felhasználó milyen url-host-ról nézi az oldalt.")
                        .AddResource("AbsoluteExpiration-DisplayName", "Abszolút gyorsítótár lejárat")
                        .AddResource("AbsoluteExpiration-Description",
                            "Másodpercben megadott lejárat. Az oldal rendszeresen frissülni fog a megadott idő után. <br />-1 azt jelenti, hogy az értéket az <i>AbsoluteExpirationSeconds</i> web.config beállítás határozza meg.")
                        .AddResource("SlidingExpirationMinutes-DisplayName", "Csúszó gyorsítótár lejárat")
                        .AddResource("SlidingExpirationMinutes-Description",
                            "Másodpercben megadott lejárat. Az oldal frissülni fog, ha nem érkezett rá kérés a megadott időn belül. <br />-1 azt jelenti, hogy az értéket az <i>SlidingExpirationSeconds</i> web.config beállítás határozza meg.")
                        .AddResource("CustomCacheKey-DisplayName", "Egyedi gyorsítótár (cache) kulcs")
                        .AddResource("CustomCacheKey-Description",
                            "Megadhat egyedi gyorsítótár kulcsot, függetlenül az aktuális tartalomtól és URL paraméterektől. Akkor érdemes használni, ha ugyanazt a statikus tartalmat szeretné megjeleníteni több különböző oldalon. <div class='ui-helper-clearfix sn-dialog-editportlet-warning'><img class='sn-icon sn-icon16 sn-floatleft' src='/Root/Global/images/icons/16/warning.png' /><i>Csak adminisztrátoroknak. Hagyja üresen, ha nem biztos a dologban.</i></div>")
                        .Class("Ctd-YesNoFieldSetting")
                        .Culture("en")
                        .AddResource("DisplayName", "Yes/No field")
                        .Culture("hu")
                        .AddResource("DisplayName", "Igen/nem mező");

                    rb.Apply();

                    #endregion

                    #region CTD changes

                    var cb = new ContentTypeBuilder(context.GetService<ILogger<ContentTypeBuilder>>());

                    cb.Type("CalendarEvent")
                        .Field("StartDate")
                        .DefaultValue("@@currenttime@@")
                        .Field("EndDate")
                        .DefaultValue("@@currenttime@@");
                    cb.Type("GenericContent")
                        .Field("ValidFrom")
                        .DefaultValue("@@currenttime@@")
                        .Field("ValidTill")
                        .DefaultValue("@@currenttime@@");

                    cb.Type("Link")
                        .Field("Url")
                        .DefaultValue("https://");

                    cb.Type("User")
                        .Field("LoginName")
                        .VisibleEdit(FieldVisibility.Hide)
                        .Field("Email")
                        .VisibleBrowse(FieldVisibility.Show)
                        .VisibleEdit(FieldVisibility.Hide)
                        .VisibleNew(FieldVisibility.Show)
                        .Field("BirthDate")
                        .DefaultValue("@@currenttime@@");

                    cb.Type("Group")
                        .Field("AllRoles")
                        .VisibleBrowse(FieldVisibility.Hide)
                        .VisibleEdit(FieldVisibility.Hide)
                        .VisibleNew(FieldVisibility.Hide)
                        .Field("DirectRoles")
                        .VisibleBrowse(FieldVisibility.Hide)
                        .VisibleEdit(FieldVisibility.Hide)
                        .VisibleNew(FieldVisibility.Hide);

                    cb.Apply();

                    #endregion

                    #region Permission changes

                    Providers.Instance.SecurityHandler.SecurityContext.CreateAclEditor()
                        .Allow(NodeHead.Get("/Root/Localization").Id, Identifiers.OwnersGroupId, false, 
                            PermissionType.Save, PermissionType.Delete)
                        .ApplyAsync(CancellationToken.None).GetAwaiter().GetResult();

                    #endregion
                });

            builder.Patch("7.7.13", "7.7.14", "2020-11-19", "Upgrades sensenet content repository.")
                .Action(context =>
                {
                    #region CTD changes

                    var cb = new ContentTypeBuilder(context.GetService<ILogger<ContentTypeBuilder>>());

                    cb.Type("ListItem")
                        .Field("ModifiedBy")
                        .VisibleEdit(FieldVisibility.Hide);

                    cb.Type("User")
                        .Field("BirthDate")
                        .DefaultValue("");

                    cb.Apply();

                    #endregion
                });

            builder.Patch("7.7.14", "7.7.16", "2020-12-08", "Upgrades sensenet content repository.")
                .Action(context =>
                {
                    #region CTD changes

                    var cb = new ContentTypeBuilder(context.GetService<ILogger<ContentTypeBuilder>>());

                    // We can set the new regex only if the current regex is the old default
                    // (otherwise we do not want to overwrite a custom regex).
                    // NOTE: in the old regex below the & character appears as is (in the middle
                    // of the first line), but in the new const we had to replace it with &amp;
                    // to let the patch algorithm set the correct value in the XML.

                    const string oldUrlRegex = "^(http|https)\\://([a-zA-Z0-9\\.\\-]+(\\:[a-zA-Z0-9\\.&%\\$\\-]+)*@)*((25[0-5]|" +
                                               "2[0-4][0-9]|[0-1]{1}[0-9]{2}|[1-9]{1}[0-9]{1}|[1-9])\\.(25[0-5]|2[0-4][0-9]|[0-1]{1}" +
                                               "[0-9]{2}|[1-9]{1}[0-9]{1}|[1-9]|0)\\.(25[0-5]|2[0-4][0-9]|[0-1]{1}[0-9]{2}|[1-9]{1}" +
                                               "[0-9]{1}|[1-9]|0)\\.(25[0-5]|2[0-4][0-9]|[0-1]{1}[0-9]{2}|[1-9]{1}[0-9]{1}|[0-9])|" +
                                               "localhost|([a-zA-Z0-9\\-]+\\.)*[a-zA-Z0-9\\-]+(\\.(com|edu|gov|int|mil|net|org|biz|" +
                                               "arpa|info|name|pro|aero|coop|museum|hu|[a-zA-Z]{2})){0,1})(\\:[0-9]+)*((\\#|/)($|" +
                                               "[a-zA-Z0-9\\.\\,\\?\\'\\\\\\+&%\\$#\\=~_\\-]+))*$";
                    const string newUrlRegex = "^(https?|ftp)\\://([a-zA-Z0-9\\.\\-]+(\\:[a-zA-Z0-9\\.&amp;%\\$\\-]+)*@)*((25[0-5]|" +
                                               "2[0-4][0-9]|[0-1]{1}[0-9]{2}|[1-9]{1}[0-9]{1}|[1-9])\\.(25[0-5]|2[0-4][0-9]|[0-1]{1}" +
                                               "[0-9]{2}|[1-9]{1}[0-9]{1}|[1-9]|0)\\.(25[0-5]|2[0-4][0-9]|[0-1]{1}[0-9]{2}|[1-9]{1}" +
                                               "[0-9]{1}|[1-9]|0)\\.(25[0-5]|2[0-4][0-9]|[0-1]{1}[0-9]{2}|[1-9]{1}[0-9]{1}|[0-9])|" +
                                               "localhost|([a-zA-Z0-9\\-]+\\.)*[a-zA-Z0-9\\-]+(\\.(com|edu|gov|int|mil|net|org|biz|" +
                                               "arpa|info|name|pro|aero|coop|museum|hu|[a-zA-Z]{2})){0,1})(\\:[0-9]+)*" +
                                               "(\\/[\\w\\-\\@\\/\\(\\)]*){0,1}((\\?|\\#)(($|[\\w\\.\\,\\'\\\\\\+&amp;" +
                                               "%\\$#\\=~_\\-\\(\\)]+)*)){0,1}$";

                    var currentRegex =
                        ((ShortTextFieldSetting) ContentType.GetByName("Link").GetFieldSettingByName("Url")).Regex;

                    // replace the regex only if it was the original default
                    if (string.Equals(oldUrlRegex, currentRegex, StringComparison.Ordinal))
                    {
                        cb.Type("Link")
                            .Field("Url")
                            .Configure("Regex", newUrlRegex);
                    }

                    cb.Apply();

                    #endregion

                    #region Content changes

                    // create the new public admin user
                    if (User.PublicAdministrator == null)
                    {
                        var publicAdmin = Content.CreateNew("User", OrganizationalUnit.Portal, "PublicAdmin");
                        publicAdmin["Enabled"] = true;
                        publicAdmin["FullName"] = "PublicAdmin";
                        publicAdmin["LoginName"] = "PublicAdmin";
                        publicAdmin.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();
                    }

                    #endregion
                });

            builder.Patch("7.7.16", "7.7.17", "2021-01-25", "Upgrades sensenet content repository.")
                .Action(context =>
                {
                    #region CTD changes

                    const string ctDefaultValue = @"&lt;?xml version=""1.0"" encoding=""utf-8""?&gt;
&lt;ContentType name=""MyType"" parentType=""GenericContent"" handler=""SenseNet.ContentRepository.GenericContent"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition""&gt;
  &lt;DisplayName&gt;MyType&lt;/DisplayName&gt;
  &lt;Description&gt;&lt;/Description&gt;
  &lt;Icon&gt;Content&lt;/Icon&gt;
  &lt;AllowIncrementalNaming&gt;true&lt;/AllowIncrementalNaming&gt;
  &lt;AllowedChildTypes&gt;ContentTypeName1,ContentTypeName2&lt;/AllowedChildTypes&gt;
  &lt;Fields&gt;
    &lt;Field name=""ShortTextField"" type=""ShortText""&gt;
      &lt;DisplayName&gt;ShortTextField&lt;/DisplayName&gt;
      &lt;Description&gt;&lt;/Description&gt;
      &lt;Configuration&gt;
        &lt;MaxLength&gt;100&lt;/MaxLength&gt;
        &lt;MinLength&gt;0&lt;/MinLength&gt;
        &lt;Regex&gt;[a-zA-Z0-9]*$&lt;/Regex&gt;
        &lt;ReadOnly&gt;false&lt;/ReadOnly&gt;
        &lt;Compulsory&gt;false&lt;/Compulsory&gt;
        &lt;DefaultValue&gt;&lt;/DefaultValue&gt;
        &lt;VisibleBrowse&gt;Show|Hide&lt;/VisibleBrowse&gt;
        &lt;VisibleEdit&gt;Show|Hide&lt;/VisibleEdit&gt;
        &lt;VisibleNew&gt;Show|Hide&lt;/VisibleNew&gt;
      &lt;/Configuration&gt;
    &lt;/Field&gt;
    &lt;Field name=""LongTextField"" type=""LongText""&gt;
      &lt;DisplayName&gt;LongTextField&lt;/DisplayName&gt;
      &lt;Description&gt;&lt;/Description&gt;
      &lt;Configuration&gt;
        &lt;MaxLength&gt;100&lt;/MaxLength&gt;
        &lt;MinLength&gt;0&lt;/MinLength&gt;
        &lt;TextType&gt;LongText|RichText&lt;/TextType&gt;
        &lt;ReadOnly&gt;false&lt;/ReadOnly&gt;
        &lt;Compulsory&gt;false&lt;/Compulsory&gt;
        &lt;DefaultValue&gt;&lt;/DefaultValue&gt;
        &lt;VisibleBrowse&gt;Show|Hide&lt;/VisibleBrowse&gt;
        &lt;VisibleEdit&gt;Show|Hide&lt;/VisibleEdit&gt;
        &lt;VisibleNew&gt;Show|Hide&lt;/VisibleNew&gt;
      &lt;/Configuration&gt;
    &lt;/Field&gt;
    &lt;Field name=""NumberField"" type=""Number""&gt;
      &lt;DisplayName&gt;NumberField&lt;/DisplayName&gt;
      &lt;Description&gt;&lt;/Description&gt;
      &lt;Configuration&gt;
        &lt;MinValue&gt;0&lt;/MinValue&gt;
        &lt;MaxValue&gt;100.5&lt;/MaxValue&gt;
        &lt;Digits&gt;2&lt;/Digits&gt;
        &lt;ReadOnly&gt;false&lt;/ReadOnly&gt;
        &lt;Compulsory&gt;false&lt;/Compulsory&gt;
        &lt;DefaultValue&gt;&lt;/DefaultValue&gt;
        &lt;VisibleBrowse&gt;Show|Hide&lt;/VisibleBrowse&gt;
        &lt;VisibleEdit&gt;Show|Hide&lt;/VisibleEdit&gt;
        &lt;VisibleNew&gt;Show|Hide&lt;/VisibleNew&gt;
      &lt;/Configuration&gt;
    &lt;/Field&gt;
    &lt;Field name=""IntegerField"" type=""Integer""&gt;
      &lt;DisplayName&gt;IntegerField&lt;/DisplayName&gt;
      &lt;Description&gt;&lt;/Description&gt;
      &lt;Configuration&gt;
        &lt;MinValue&gt;0&lt;/MinValue&gt;
        &lt;MaxValue&gt;100&lt;/MaxValue&gt;
        &lt;ReadOnly&gt;false&lt;/ReadOnly&gt;
        &lt;Compulsory&gt;false&lt;/Compulsory&gt;
        &lt;DefaultValue&gt;&lt;/DefaultValue&gt;
        &lt;VisibleBrowse&gt;Show|Hide&lt;/VisibleBrowse&gt;
        &lt;VisibleEdit&gt;Show|Hide&lt;/VisibleEdit&gt;
        &lt;VisibleNew&gt;Show|Hide&lt;/VisibleNew&gt;
      &lt;/Configuration&gt;
    &lt;/Field&gt;
    &lt;Field name=""BooleanField"" type=""Boolean""&gt;
      &lt;DisplayName&gt;BooleanField&lt;/DisplayName&gt;
      &lt;Description&gt;&lt;/Description&gt;
      &lt;Configuration&gt;
        &lt;ReadOnly&gt;false&lt;/ReadOnly&gt;
        &lt;Compulsory&gt;false&lt;/Compulsory&gt;
        &lt;DefaultValue&gt;&lt;/DefaultValue&gt;
        &lt;VisibleBrowse&gt;Show|Hide&lt;/VisibleBrowse&gt;
        &lt;VisibleEdit&gt;Show|Hide&lt;/VisibleEdit&gt;
        &lt;VisibleNew&gt;Show|Hide&lt;/VisibleNew&gt;
      &lt;/Configuration&gt;
    &lt;/Field&gt;
    &lt;Field name=""ChoiceField"" type=""Choice""&gt;
      &lt;DisplayName&gt;ChoiceField&lt;/DisplayName&gt;
      &lt;Description&gt;&lt;/Description&gt;
      &lt;Configuration&gt;
        &lt;AllowMultiple&gt;false&lt;/AllowMultiple&gt;
        &lt;AllowExtraValue&gt;false&lt;/AllowExtraValue&gt;
        &lt;Options&gt;
          &lt;Option selected=""true""&gt;1&lt;/Option&gt;
          &lt;Option&gt;2&lt;/Option&gt;
        &lt;/Options&gt;
        &lt;ReadOnly&gt;false&lt;/ReadOnly&gt;
        &lt;Compulsory&gt;false&lt;/Compulsory&gt;
        &lt;DefaultValue&gt;&lt;/DefaultValue&gt;
        &lt;VisibleBrowse&gt;Show|Hide&lt;/VisibleBrowse&gt;
        &lt;VisibleEdit&gt;Show|Hide&lt;/VisibleEdit&gt;
        &lt;VisibleNew&gt;Show|Hide&lt;/VisibleNew&gt;
      &lt;/Configuration&gt;
    &lt;/Field&gt;
    &lt;Field name=""DateTimeField"" type=""DateTime""&gt;
      &lt;DisplayName&gt;DateTimeField&lt;/DisplayName&gt;
      &lt;Description&gt;&lt;/Description&gt;
      &lt;Configuration&gt;
        &lt;DateTimeMode&gt;DateAndTime&lt;/DateTimeMode&gt;
        &lt;Precision&gt;Second&lt;/Precision&gt;
        &lt;ReadOnly&gt;false&lt;/ReadOnly&gt;
        &lt;Compulsory&gt;false&lt;/Compulsory&gt;
        &lt;DefaultValue&gt;&lt;/DefaultValue&gt;
        &lt;VisibleBrowse&gt;Show|Hide&lt;/VisibleBrowse&gt;
        &lt;VisibleEdit&gt;Show|Hide&lt;/VisibleEdit&gt;
        &lt;VisibleNew&gt;Show|Hide&lt;/VisibleNew&gt;
      &lt;/Configuration&gt;
    &lt;/Field&gt;
    &lt;Field name=""ReferenceField"" type=""Reference""&gt;
      &lt;DisplayName&gt;ReferenceField&lt;/DisplayName&gt;
      &lt;Description&gt;&lt;/Description&gt;
      &lt;Configuration&gt;
        &lt;AllowMultiple&gt;true&lt;/AllowMultiple&gt;
        &lt;AllowedTypes&gt;
          &lt;Type&gt;Type1&lt;/Type&gt;
          &lt;Type&gt;Type2&lt;/Type&gt;
        &lt;/AllowedTypes&gt;
        &lt;SelectionRoot&gt;
          &lt;Path&gt;/Root/Path1&lt;/Path&gt;
          &lt;Path&gt;/Root/Path2&lt;/Path&gt;
        &lt;/SelectionRoot&gt;
        &lt;DefaultValue&gt;/Root/Path1,/Root/Path2&lt;/DefaultValue&gt;
        &lt;ReadOnly&gt;false&lt;/ReadOnly&gt;
        &lt;Compulsory&gt;false&lt;/Compulsory&gt;
        &lt;VisibleBrowse&gt;Show|Hide&lt;/VisibleBrowse&gt;
        &lt;VisibleEdit&gt;Show|Hide&lt;/VisibleEdit&gt;
        &lt;VisibleNew&gt;Show|Hide&lt;/VisibleNew&gt;
      &lt;/Configuration&gt;
    &lt;/Field&gt;
    &lt;Field name=""BinaryField"" type=""Binary""&gt;
      &lt;DisplayName&gt;BinaryField&lt;/DisplayName&gt;
      &lt;Description&gt;&lt;/Description&gt;
      &lt;Configuration&gt;
        &lt;IsText&gt;true&lt;/IsText&gt;
        &lt;ReadOnly&gt;false&lt;/ReadOnly&gt;
        &lt;Compulsory&gt;false&lt;/Compulsory&gt;
        &lt;DefaultValue&gt;&lt;/DefaultValue&gt;
        &lt;VisibleBrowse&gt;Show|Hide&lt;/VisibleBrowse&gt;
        &lt;VisibleEdit&gt;Show|Hide&lt;/VisibleEdit&gt;
        &lt;VisibleNew&gt;Show|Hide&lt;/VisibleNew&gt;
      &lt;/Configuration&gt;
    &lt;/Field&gt;
  &lt;/Fields&gt;
&lt;/ContentType&gt;";
                    
                    var cb = new ContentTypeBuilder(context.GetService<ILogger<ContentTypeBuilder>>());

                    cb.Type("ContentType")
                        .Field("Binary")
                        .DefaultValue(ctDefaultValue);

                    cb.Apply();

                    #endregion
                });

            builder.Patch("7.7.17", "7.7.18", "2021-02-17", "Upgrades sensenet content repository.")
                .Action(context =>
                {
                    #region String resources

                    var rb = new ResourceBuilder();

                    rb.Content("CtdResourcesQ.xml")
                        .Class("Ctd-Query")
                        .Culture("en")
                        .AddResource("UiFilters-DisplayName", "UI filters")
                        .AddResource("UiFilters-Description", "Technical field for filter data.")
                        .Culture("hu")
                        .AddResource("UiFilters-DisplayName", "UI szűrők")
                        .AddResource("UiFilters-Description", "Technikai mező szűrő adatoknak.");

                    rb.Content("ActionResources.xml")
                        .Class("Action")
                        .Culture("en")
                        .AddResource("Browse", "Details");

                    rb.Apply();

                    #endregion

                    #region CTD changes

                    var cb = new ContentTypeBuilder(context.GetService<ILogger<ContentTypeBuilder>>());

                    cb.Type("Query")
                        .Field("UiFilters", "LongText")
                        .DisplayName("$Ctd-Query,UiFilters-DisplayName")
                        .Description("$Ctd-Query,UiFilters-Description")
                        .VisibleBrowse(FieldVisibility.Hide)
                        .VisibleEdit(FieldVisibility.Hide)
                        .VisibleNew(FieldVisibility.Hide);

                    cb.Type("File")
                        .Field("Size")
                        .ControlHint("sn:FileSize");

                    cb.Apply();

                    #endregion
                });
            builder.Patch("7.7.18", "7.7.19", "2021-03-16", "Upgrades sensenet content repository.")
                .Action(context =>
                {
                    #region CTD changes

                    var cb = new ContentTypeBuilder(context.GetService<ILogger<ContentTypeBuilder>>());

                    cb.Type("ContentLink")
                        .Field("Name", "ShortText")
                        .FieldIndex(20)
                        .Field("Link", "Reference")
                        .FieldIndex(10);

                    cb.Type("DocumentLibrary")
                        .Field("Name", "ShortText")
                        .FieldIndex(10)
                        .Field("DisplayName", "ShortText")
                        .FieldIndex(20)
                        .Field("Index")
                        .RemoveConfiguration("FieldIndex")
                        .Field("InheritableVersioningMode")
                        .RemoveConfiguration("FieldIndex")
                        .Field("InheritableApprovingMode")
                        .RemoveConfiguration("FieldIndex")
                        .Field("AllowedChildTypes")
                        .RemoveConfiguration("FieldIndex");

                    cb.Type("Folder")
                        .Field("Name", "ShortText")
                        .FieldIndex(20)
                        .Field("DisplayName", "ShortText")
                        .FieldIndex(10);

                    cb.Type("Group")
                        .Field("Name", "ShortText")
                        .FieldIndex(30)
                        .Field("DisplayName", "ShortText")
                        .FieldIndex(20)
                        .Field("Version")
                        .RemoveConfiguration("FieldIndex")
                        .Field("Members", "Reference")
                        .FieldIndex(10)
                        .Field("Index")
                        .RemoveConfiguration("FieldIndex")
                        .Field("Description")
                        .RemoveConfiguration("FieldIndex");

                    cb.Type("Image")
                        .Field("Name", "ShortText")
                        .FieldIndex(30)
                        .Field("DateTaken", "DateTime")
                        .FieldIndex(20)
                        .Field("Keywords")
                        .RemoveConfiguration("FieldIndex")
                        .Field("Index", "Integer")
                        .FieldIndex(10);

                    cb.Type("ImageLibrary")
                        .RemoveField("Index")
                        .RemoveField("InheritableVersioningMode")
                        .RemoveField("InheritableApprovingMode")
                        .RemoveField("AllowedChildTypes")
                        .Field("DisplayName", "ShortText")
                        .FieldIndex(20)
                        .Field("CoverImage", "Reference")
                        .FieldIndex(10)
                        .Field("Description")
                        .RemoveConfiguration("FieldIndex");

                    cb.Type("ItemList")
                        .Field("Name", "ShortText")
                        .FieldIndex(20)
                        .Field("DisplayName", "ShortText")
                        .FieldIndex(10);

                    cb.Type("ListItem")
                        .Field("Name", "ShortText")
                        .FieldIndex(20)
                        .Field("DisplayName", "ShortText")
                        .FieldIndex(10);

                    cb.Type("Query")
                        .Field("Name", "ShortText")
                        .FieldIndex(20)
                        .Field("DisplayName", "ShortText")
                        .FieldIndex(10);

                    cb.Type("Workspace")
                        .Field("Name", "ShortText")
                        .FieldIndex(20)
                        .Configure("MaxLength", "100")
                        .Field("DisplayName", "ShortText")
                        .FieldIndex(10)
                        .Field("Description")
                        .RemoveConfiguration("FieldIndex")
                        .Field("AllowedChildTypes")
                        .RemoveConfiguration("FieldIndex")
                        .Field("InheritableVersioningMode")
                        .RemoveConfiguration("FieldIndex")
                        .Field("InheritableApprovingMode")
                        .RemoveConfiguration("FieldIndex")
                        .Field("Path")
                        .RemoveConfiguration("FieldIndex");

                    cb.Apply();

                    #endregion
                });
            builder.Patch("7.7.19", "7.7.20", "2021-04-14", "Upgrades sensenet content repository.")
                .Action(context =>
                {
                    #region CTD changes

                    var cb = new ContentTypeBuilder(context.GetService<ILogger<ContentTypeBuilder>>());

                    cb.Type("BinaryFieldSetting").Icon("FieldSetting");
                    cb.Type("ContentLink").Icon("ContentLink");
                    cb.Type("DocumentLibrary").Icon("DocumentLibrary");
                    cb.Type("Email").Icon("Email");
                    cb.Type("EventList").Icon("EventList");
                    cb.Type("ExecutableFile").Icon("ExecutableFile");
                    cb.Type("ImageLibrary").Icon("ImageLibrary");
                    cb.Type("Library").Icon("Library");
                    cb.Type("LinkList").Icon("LinkList");
                    cb.Type("Memo").Icon("Memo");
                    cb.Type("MemoList").Icon("MemoList");
                    cb.Type("NullFieldSetting").Icon("FieldSetting");
                    cb.Type("PermissionChoiceFieldSetting").Icon("FieldSetting");
                    cb.Type("Profiles").Icon("Profiles");
                    cb.Type("Resources").Icon("Resources");
                    cb.Type("Sites").Icon("Sites");
                    cb.Type("SystemFile").Icon("SystemFile");
                    cb.Type("SystemFolder").Icon("SystemFolder");
                    cb.Type("Task").Icon("Task");
                    cb.Type("TaskList").Icon("TaskList");
                    cb.Type("TrashBag").Icon("TrashBag");
                    cb.Type("TrashBin").Icon("TrashBin");

                    cb.Apply();

                    #endregion
                });

            builder.Patch("7.7.20", "7.7.21", "2021-05-12", "Upgrades sensenet content repository.")
                .Action(context =>
                {
                    #region CTD changes

                    var cb = new ContentTypeBuilder(context.GetService<ILogger<ContentTypeBuilder>>());

                    cb.Type("ItemList")
                        .Field("OwnerWhenVisitor")
                        .VisibleBrowse(FieldVisibility.Hide)
                        .VisibleEdit(FieldVisibility.Hide)
                        .VisibleNew(FieldVisibility.Hide);

                    cb.Apply();

                    #endregion
                });

            builder.Patch("7.7.21", "7.7.22", "2021-06-09", "Upgrades sensenet content repository.")
                .Action(context =>
                {
                    var logger = context.GetService<ILogger<ServicesComponent>>();

                    #region Permission changes

                    logger?.LogInformation("Adding Open permission for users on their own profiles.");

                    var aclEditor = Providers.Instance.SecurityHandler.SecurityContext.CreateAclEditor();

                    foreach (var profile in Content.All.Where(c => c.TypeIs("UserProfile"))
                        .AsEnumerable().Select(c => (UserProfile)c.ContentHandler))
                    {
                        var user = profile.User;
                        if (user == null)
                        {
                            logger?.LogWarning($"User not found for profile {profile.Path}, skipping permission set.");
                            continue;
                        }
                        
                        logger?.LogTrace($"Adding permissions for {user.Path} to profile {profile.Path}");

                        aclEditor.Allow(profile.Id, user.Id, false, PermissionType.Open);
                    }

                    aclEditor.ApplyAsync(CancellationToken.None).GetAwaiter().GetResult();

                    #endregion

                    #region CTD changes: longtext --> richtext field type changes

                    var ctLogger = context.GetService<ILogger<ContentTypeBuilder>>();
                    var longTextFieldsToConvert = ContentType.GetContentTypes().SelectMany(ct => ct.FieldSettings).Where(fs =>
                            fs.ShortName == "LongText" &&
                            fs is LongTextFieldSetting ltfs &&
                            (ltfs.TextType == TextType.RichText || ltfs.TextType == TextType.AdvancedRichText ||
                             ltfs.ControlHint == "sn:RichText"))
                        .Select(fs => fs.Name)
                        .Distinct().ToArray();

                    if (longTextFieldsToConvert.Any())
                    {
                        ctLogger.LogTrace($"Changing longtext fields to richtext: " +
                                          $"{string.Join(", ", longTextFieldsToConvert)}");

                        var cb = new ContentTypeBuilder(ctLogger);

                        foreach (var fieldName in longTextFieldsToConvert)
                        {
                            cb.ChangeFieldType(fieldName, "RichText");
                        }

                        cb.Apply();
                    }

                    #endregion
                });

            builder.Patch("7.7.22", "7.7.23", "2021-08-23", "Upgrades sensenet content repository.")
                .Action(context =>
                {
                    var logger = context.GetService<ILogger<ServicesComponent>>();

                    #region Settings content changes

                    #region Values
                    const string permissionSettings = @"{
   ""groups"":[
      {
         ""Read"":[""See"", ""Preview"", ""PreviewWithoutWatermark"", ""PreviewWithoutRedaction"", ""Open""]
      },
      {
         ""Write"":[ ""Save"", ""AddNew"", ""Delete""]
      },
      {
         ""Versioning"":[""OpenMinor"", ""Publish"", ""ForceCheckin"", ""Approve"", ""RecallOldVersion""]
      },
      {
         ""Advanced"":[""SeePermissions"", ""SetPermissions"", ""RunApplication""]
      }
   ]
}";
                    const string portalSettings = @"{
  ""ClientCacheHeaders"": [
    {
      ""ContentType"": ""PreviewImage"",
      ""MaxAge"": 1
    },
    {
      ""Extension"": ""jpeg"",
      ""MaxAge"": 604800
    },
    {
      ""Extension"": ""gif"",
      ""MaxAge"": 604800
    },
    {
      ""Extension"": ""jpg"",
      ""MaxAge"": 604800
    },
    {
      ""Extension"": ""png"",
      ""MaxAge"": 604800
    },
    {
      ""Extension"": ""swf"",
      ""MaxAge"": 604800
    },
    {
      ""Extension"": ""css"",
      ""MaxAge"": 600
    },
    {
      ""Extension"": ""js"",
      ""MaxAge"": 600
    }
  ],
  ""UploadFileExtensions"": {
    ""jpg"": ""Image"",
    ""jpeg"": ""Image"",
    ""gif"": ""Image"",
    ""png"": ""Image"",
    ""bmp"": ""Image"",
    ""svg"": ""Image"",
    ""svgz"": ""Image"",
    ""tif"": ""Image"",
    ""tiff"": ""Image"",
    ""xaml"": ""WorkflowDefinition"",
    ""DefaultContentType"": ""File""
  },
  ""BinaryHandlerClientCacheMaxAge"": 600,
  ""PermittedAppsWithoutOpenPermission"": ""Details"",
  ""AllowedOriginDomains"": [
    ""localhost:*"",
    ""*.sensenet.com"",
    ""*.sensenet.cloud""
  ]
}";
                    #endregion
                    
                    CreateSettings("Permission.settings", permissionSettings, 
                        "In this section you can manage and customize permission groups, " +
                        "add custom permissions that can be displayed and used in the permission editor.", true, logger);
                    CreateSettings("Portal.settings", portalSettings, 
                        "Here you can customize client cache headers, CORS values and other portal-related settings.", true, logger);
                    
                    #endregion
                });
            builder.Patch("7.7.23", "7.7.24", "2021-10-04", "Upgrades sensenet content repository.")
                .Action(context =>
                {
                    // there were only permission and cors changes that can be made manually if necessary
                });
            builder.Patch("7.7.24", "7.7.25", "2022-01-18", "Upgrades sensenet content repository.")
                .Action(context =>
                {
                    #region Permission changes
                    
                    var genericContentTypeId = ContentType.GetByName("GenericContent").Id;
                    var contentTypeId = ContentType.GetByName("ContentType").Id;
                    var contentTypesId = NodeHead.Get(Repository.ContentTypesFolderPath).Id;
                    var systemFolderContentTypeId = ContentType.GetByName("SystemFolder").Id;
                    var fieldSettingContentTypeId = ContentType.GetByName("FieldSettingContent")?.Id ?? 0;
                    var portalRootContentTypeId = ContentType.GetByName("PortalRoot")?.Id ?? 0;
                    var runtimeContentContainerContentTypeId = ContentType.GetByName("RuntimeContentContainer")?.Id ?? 0;
                    var sitesContentTypeId = ContentType.GetByName("Sites")?.Id ?? 0;
                    var sharingGroupContentTypeId = ContentType.GetByName("SharingGroup")?.Id ?? 0;

                    var publicAdminsGroupId = Node.LoadNode("/Root/IMS/Public/Administrators")?.Id ?? 0;

                    var aclEditor = Providers.Instance.SecurityHandler.CreateAclEditor();

                    // break inheritance on certain system types to hide them
                    if (fieldSettingContentTypeId != 0)
                        aclEditor.BreakInheritance(fieldSettingContentTypeId, new[] { EntryType.Normal });
                    if (portalRootContentTypeId != 0)
                        aclEditor.BreakInheritance(portalRootContentTypeId, new[] { EntryType.Normal });
                    if (runtimeContentContainerContentTypeId != 0)
                        aclEditor.BreakInheritance(runtimeContentContainerContentTypeId, new[] { EntryType.Normal });
                    if (sitesContentTypeId != 0)
                        aclEditor.BreakInheritance(sitesContentTypeId, new[] { EntryType.Normal });
                    if (sharingGroupContentTypeId != 0)
                        aclEditor.BreakInheritance(sharingGroupContentTypeId, new[] { EntryType.Normal });

                    // set permissions for public admins
                    if (publicAdminsGroupId != 0)
                    {
                        aclEditor.Allow(genericContentTypeId, publicAdminsGroupId, false,
                                PermissionType.Open, PermissionType.AddNew)
                            .Allow(contentTypeId, publicAdminsGroupId, false, PermissionType.See)
                            .Allow(systemFolderContentTypeId, publicAdminsGroupId, true, PermissionType.Open)
                            .Allow(contentTypesId, publicAdminsGroupId, true, PermissionType.AddNew);
                    }

                    aclEditor.ApplyAsync(CancellationToken.None).GetAwaiter().GetResult();

                    #endregion
                });

            builder.Patch("7.7.25", "7.7.26", "2022-04-11", "Upgrades sensenet content repository.")
                .Action(context =>
                {
                    var logger = context.GetService<ILogger<ServicesComponent>>();

                    #region String resource changes

                    logger.LogTrace("Adding string resources...");

                    var rb = new ResourceBuilder();

                    rb.Content("CtdResourcesEF.xml")
                        .Class("Ctd-EmailTemplate")
                        .Culture("en")
                        .AddResource("DisplayName", "Email template")
                        .AddResource("Description", "Defines an email template used by notification features.")
                        .AddResource("Subject-DisplayName", "Subject")
                        .AddResource("Subject-Description", "Email subject")
                        .AddResource("Body-DisplayName", "Body")
                        .AddResource("Body-Description", "Email message text in richtext format.")
                        .Culture("hu")
                        .AddResource("DisplayName", "Email minta")
                        .AddResource("Description", "Az értesítések küldéséhez használt email tartalomtípus.")
                        .AddResource("Subject-DisplayName", "Tárgy")
                        .AddResource("Subject-Description", "Levél tárgya")
                        .AddResource("Body-DisplayName", "Üzenet")
                        .AddResource("Body-Description", "Üzenet szövege.");

                    rb.Content("ActionResources.xml")
                        .Class("Action")
                        .Culture("en")
                        .AddResource("SendPasswordChange", "Send change password email")
                        .Culture("hu")
                        .AddResource("SendPasswordChange", "Jelszómódosító email küldése");

                    rb.Apply();

                    #endregion
                    
                    #region CTD changes

                    const string emailTemplateCtd = @"<ContentType name=""EmailTemplate"" parentType=""GenericContent"" handler=""SenseNet.ContentRepository.Email.EmailTemplate"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
  <DisplayName>$Ctd-EmailTemplate,DisplayName</DisplayName>
  <Description>$Ctd-EmailTemplate,Description</Description>
  <Icon>EmailTemplate</Icon>
  <Fields>
    <Field name=""Subject"" type=""ShortText"">
      <DisplayName>$Ctd-EmailTemplate,Subject-DisplayName</DisplayName>
      <Description>$Ctd-EmailTemplate,Subject-Description</Description>
    </Field>
    <Field name=""Body"" type=""RichText"">
      <DisplayName>$Ctd-EmailTemplate,Body-DisplayName</DisplayName>
      <Description>$Ctd-EmailTemplate,Body-Description</Description>
      <Indexing>
        <Analyzer>Standard</Analyzer>
      </Indexing>
    </Field>
  </Fields>
</ContentType>";

                    logger.LogTrace("Installing email template content type...");
                    ContentTypeInstaller.InstallContentType(emailTemplateCtd);

                    #endregion

                    #region Content changes (email template)

                    #region Content constants
                    const string emailTemplate = @"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"" />
    <title></title>
</head>
<body>
    <div style=""text-align: center;padding: 10px;padding-top:30px"">
        <div style=""width: 600px;margin: 0 auto;
                    text-align: left;
                    font-family: Roboto, Arial, Helvetica, sans-serif;
                    color: #757575;
                    font-size: 16px;
                    line-height: 150%;"">
            <div style=""text-align: center"">
                <img src=""https://github.com/SenseNet/sn-resources/blob/master/images/sn-icon/sensenet-icon-64.png?raw=true"" alt=""sensenet logo"" />
            </div>
            <div style=""padding: 10px"">
                <h1 style=""font-size: 16px"">Welcome to sensenet!</h1>
                <div>Before you log in, please change your password.</div>
                <div>If you did not create an account using this address (<a style=""color: #007C89 !important;text-decoration: none !important"">{Email}</a>), please skip this mail.</div>
                <table width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"">
                    <tr>
                        <td align=""center"" style=""padding: 30px"">
                            <table border=""0"" cellspacing=""0"" cellpadding=""0"">
                                <tr>
                                    <td align=""center"" style=""border-radius: 25px;text-align: center;"" bgcolor=""#13a5ad"">
                                        <a href=""{ActionUrl}"" target=""_blank""
                                           style="" font-family: Helvetica, Arial, sans-serif; color: #ffffff; text-decoration: none; text-decoration: none;border-radius: 25px;padding: 10px; border: 1px solid #13a5ad; display: inline-block;"">
                                            Change your password
                                        </a>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>
                </table>
                <div style=""text-align: center; width: auto; font-size: 80%;"">
                    Or using this link:<br />
                    <a style=""color: #007C89"" href=""{ActionUrl}"">{ActionUrl}</a>
                </div>
                <br />
                <div>Thank you!</div>
                <br />
                <div>regards,<br />sensenet team</div>
            </div>
            <br />
            <br />
            <div style=""width: 100%;clear: both;display:block;text-align: center;border-top: solid 1px #cfcfcf;margin-top: 20px;padding: 10px;margin: 20px 10px 10px"">
                <table style=""margin: 0 auto"">
                    <tr>
                        <td style=""padding: 10px"">
                            <a href=""https://github.com/SenseNet"">
                                <img width=""24"" src=""https://cdn-images.mailchimp.com/icons/social-block-v2/outline-gray-github-48.png"" />
                            </a>
                        </td>
                        <td style=""padding: 10px"">
                            <a href=""https://community.sensenet.com/"">
                                <img width=""24"" src=""https://cdn-images.mailchimp.com/icons/social-block-v2/outline-gray-link-48.png"" />
                            </a>
                        </td>
                        <td style=""padding: 10px"">
                            <a href=""https://www.linkedin.com/company/sense-net-inc/about/"">
                                <img width=""24"" src=""https://cdn-images.mailchimp.com/icons/social-block-v2/outline-gray-linkedin-48.png"" />
                            </a>
                        </td>
                        <td style=""padding: 10px"">
                            <a href=""https://medium.com/sensenet"">
                                <img width=""24"" src=""https://cdn-images.mailchimp.com/icons/social-block-v2/outline-gray-medium-48.png"" />
                            </a>
                        </td>
                    </tr>
                </table>
                <div style=""font-size: 12px"">
                    <em>Copyright © 2022 Sense/Net, All rights reserved.</em>
                </div>
            </div>
        </div>
    </div>
</body>
</html>";
                    const string templatesFolderPath = "/Root/System/Templates";
                    /*const*/ string emailTemplateParentPath = $"{templatesFolderPath}/Email/Registration";
                    const string emailTemplateName = "ChangePassword";
                    /*const*/ string emailTemplatePath = $"{emailTemplateParentPath}/{emailTemplateName}";
                    #endregion

                    if (Node.Exists(emailTemplatePath))
                    {
                        // previous email template was of a different type
                        logger.LogTrace("Deleting change password email template... ");
                        Node.ForceDeleteAsync(emailTemplatePath, CancellationToken.None)
                            .ConfigureAwait(false).GetAwaiter().GetResult();
                    }

                    logger.LogTrace("Creating email template (with parent structure if necessary)...");
                    var _ = RepositoryTools.CreateStructure(templatesFolderPath, "SystemFolder");
                    var parent = RepositoryTools.CreateStructure(emailTemplateParentPath, "Folder") ??
                                 Content.Load(emailTemplateParentPath);

                    var pwChangeEmailTemplate = Content.CreateNew("EmailTemplate", parent.ContentHandler,
                        emailTemplateName);

                    pwChangeEmailTemplate["Body"] = emailTemplate;
                    pwChangeEmailTemplate["Subject"] = "sensenet - Please change your password!";
                    pwChangeEmailTemplate.SaveSameVersionAsync(CancellationToken.None).GetAwaiter().GetResult();

                    #endregion

                    #region Permission changes

                    logger.LogTrace("Adding permissions for public administrators and owners on content types.");

                    var publicAdminsGroupId = Node.LoadNode("/Root/IMS/Public/Administrators")?.Id ?? 0;
                    var publicAdminUserId = Node.LoadNode("/Root/IMS/BuiltIn/Portal/Admin")?.Id ?? 0;
                    var genericContentTypeId = ContentType.GetByName("GenericContent").Id;

                    var aclEditor = Providers.Instance.SecurityHandler.CreateAclEditor();

                    aclEditor.Allow(genericContentTypeId, Identifiers.OwnersGroupId, false,
                        PermissionType.Save, PermissionType.AddNew, PermissionType.Delete, PermissionType.SetPermissions);

                    // set permissions for public admins
                    if (publicAdminsGroupId != 0)
                    {
                        aclEditor.Allow(publicAdminUserId, publicAdminsGroupId, false,
                                PermissionType.Open)
                            .Allow(genericContentTypeId, publicAdminsGroupId, false,
                                PermissionType.Save, PermissionType.AddNew, PermissionType.Delete,
                                PermissionType.SetPermissions);

                        // Public admins must not be able to modify built-in CTDs:
                        // DENY local Save, Delete, SetPermissions permission on protected paths.
                        foreach (var protectedId in Providers.Instance.ContentProtector.GetProtectedPaths().Where(p =>
                                         RepositoryPath.IsInTree(p, Repository.ContentTypesFolderPath))
                                     .Select(NodeHead.Get).Where(nh => nh != null)
                                     .Select(nh => nh.Id))
                        {
                            // deny all strong permissions locally on built-in content types
                            aclEditor.Deny(protectedId, publicAdminsGroupId, true,
                                PermissionType.Save, PermissionType.Delete, PermissionType.SetPermissions);
                        }

                    }

                    aclEditor.ApplyAsync(CancellationToken.None).GetAwaiter().GetResult();

                    #endregion
                });

            builder.Patch("7.7.26", "7.7.27", "2022-08-19", "Upgrades sensenet content repository.")
                .Action(context =>
                {
                    var logger = context.GetService<ILogger<ServicesComponent>>();
                    
                    #region Settings changes

                    try
                    {
                        // add client cache header value for Setting contents if necessary
                        var setting = Node.Load<Settings>("/Root/System/Settings/Portal.settings");
                        if (setting != null)
                        {
                            using var bs = setting.Binary.GetStream();
                            var jo = Settings.DeserializeToJObject(bs);
                            var cc = (JArray)jo?["ClientCacheHeaders"];
                            if (cc != null && cc.Children().All(jt => jt.Value<string>("ContentType") != "Settings"))
                            {
                                // no value for settings: add it
                                cc.Add(new JObject(
                                    new JProperty("ContentType", "Settings"),
                                    new JProperty("MaxAge", 1)));

                                var modifiedJson = JsonConvert.SerializeObject(jo, Formatting.Indented);

                                using var modifiedStream = RepositoryTools.GetStreamFromString(modifiedJson);
                                setting.Binary.SetStream(modifiedStream);
                                setting.SaveAsync(SavingMode.KeepVersion, CancellationToken.None).GetAwaiter().GetResult();
                            }
                            else
                            {
                                logger.LogTrace(cc == null
                                    ? "Settings contenttype header section could not be added to Portal settings."
                                    : "Settings contenttype header section already exists in Portal settings.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error loading or modifying Portal settings.");
                    }

                    #endregion

                    #region CTD changes

                    try
                    {
                        var cb = new ContentTypeBuilder(context.GetService<ILogger<ContentTypeBuilder>>());

                        cb.Type("User")
                            .Field("BirthDate")
                            .Configure("MaxValue", "@@Today@@");

                        cb.Apply();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error during User CTD changes.");
                    }

                    #endregion
                });

            builder.Patch("7.7.27", "7.7.28", "2022-12-16", "Upgrades sensenet content repository.")
                .Action(context =>
                {
                    var logger = context.GetService<ILogger<ServicesComponent>>();

                    #region Content changes

                    var contentFolder = Node.Load<GenericContent>("/Root/Content");
                    if (contentFolder != null)
                    {
                        logger.LogTrace("Adding File and Image as an allowed type on the Content folder.");
                        contentFolder.AllowChildTypes(new[] { "File", "Image" }, throwOnError: false, save: true);
                    }
                    
                    #endregion

                    #region String resource changes

                    logger.LogTrace("Adding string resources...");

                    var rb = new ResourceBuilder();

                    rb.Content("CtdResourcesAB.xml")
                        .Class("Ctd")
                        .Culture("en")
                        .AddResource("Enum-Folder-PreviewEnabled-Inherited", "Inherited")
                        .AddResource("Enum-Folder-PreviewEnabled-No", "No")
                        .AddResource("Enum-Folder-PreviewEnabled-Yes", "Yes")
                        .Culture("hu")
                        .AddResource("Enum-Folder-PreviewEnabled-Inherited", "Örökölt")
                        .AddResource("Enum-Folder-PreviewEnabled-No", "Nem")
                        .AddResource("Enum-Folder-PreviewEnabled-Yes", "Igen");

                    rb.Content("CtdResourcesCD.xml")
                        .Class("Ctd-ContentType")
                        .Culture("en")
                        .AddResource("IsSystemType-DisplayName", "System Type")
                        .AddResource("IsSystemType-Description", "This field is true if the content type is system type.")
                        .Culture("hu")
                        .AddResource("IsSystemType-DisplayName", "Rendszer típus")
                        .AddResource("IsSystemType-Description", "Akkor igaz, ha ez a tartalom típus egy rendszer-típus.");

                    rb.Content("CtdResourcesEF.xml")
                        .Class("Ctd-Folder")
                        .Culture("en")
                        .AddResource("PreviewEnabled-DisplayName", "Preview enabled")
                        .AddResource("PreviewEnabled-Description", "Switch on or off preview generation in this subtree. Can be enabled, disabled or inherited from the parent.")
                        .Culture("hu")
                        .AddResource("PreviewEnabled-DisplayName", "Előnézet engedélyezése")
                        .AddResource("PreviewEnabled-Description", "Be- vagy kikapcsolja az előnézeti képek generálását ezen a részfán. Engedélyezi, tiltja vagy a szülőről veszi az értéket.");

                    rb.Apply();

                    #endregion

                    #region Settings changes

                    try
                    {
                        // Change Trace values from false to null. The values true will be unchanged.
                        var setting = Node.Load<Settings>("/Root/System/Settings/Logging.settings");
                        if (setting != null)
                        {
                            logger.LogTrace("Updating logging settings...");

                            using var readStream = setting.Binary.GetStream();
                            using var reader = new StreamReader(readStream);
                            var jText = reader.ReadToEnd();
                            var changed = false;
                            if (JsonConvert.DeserializeObject(jText) is JObject jRoot)
                            {
                                if (jRoot["Trace"] is JObject jTrace)
                                {
                                    var namesToChange = jTrace.Properties()
                                        .Where(x => x.Value.ToObject<bool?>() == false)
                                        .Select(x => x.Name)
                                        .ToArray();

                                    foreach (var name in namesToChange)
                                        jTrace[name] = null;

                                    var modifiedJson =
                                        JsonConvert.SerializeObject(jRoot, Newtonsoft.Json.Formatting.Indented);

                                    using var modifiedStream = RepositoryTools.GetStreamFromString(modifiedJson);
                                    setting.Binary.SetStream(modifiedStream);
                                    setting.SaveAsync(SavingMode.KeepVersion, CancellationToken.None).GetAwaiter().GetResult();
                                    changed = true;
                                }
                            }

                            logger.LogTrace(changed
                                ? "Setting values are changed from false to null in Logging settings."
                                : "Logging settings was not changed.");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error loading or modifying Logging settings.");
                    }

                    #endregion

                    #region CTD changes

                    try
                    {
                        logger.LogTrace("Updating CTDs...");

                        var cb = new ContentTypeBuilder(context.GetService<ILogger<ContentTypeBuilder>>());

                        cb.Type("ContentType")
                            .Field("IsSystemType", "Boolean")
                            .DisplayName("$Ctd-ContentType,IsSystemType-DisplayName")
                            .Description("$Ctd-ContentType,IsSystemType-Description")
                            .Bind("IsSystemType")
                            .VisibleBrowse(FieldVisibility.Hide)
                            .VisibleEdit(FieldVisibility.Hide)
                            .VisibleNew(FieldVisibility.Hide)
                            .ReadOnly();

                        cb.Type("Folder")
                            .Field("PreviewEnabled", "Choice")
                            .DisplayName("$Ctd-Folder,PreviewEnabled-DisplayName")
                            .Description("$Ctd-Folder,PreviewEnabled-Description")
                            .VisibleBrowse(FieldVisibility.Hide)
                            .VisibleEdit(FieldVisibility.Advanced)
                            .VisibleNew(FieldVisibility.Advanced)
                            .Configure("AllowMultiple", "false")
                            .Configure("AllowExtraValue", "false")
                            .Configure("Options", "<Enum type=\"SenseNet.ContentRepository.PreviewEnabled\"/>");

                        // System ContentTypes
                        foreach (var ctd in new[]
                                 {
                                     "BinaryFieldSetting", "ChoiceFieldSetting", "CurrencyFieldSetting", "DateTimeFieldSetting",
                                     "Domains", "ExecutableFile", "FieldSettingContent", "GenericContent", "HyperLinkFieldSetting",
                                     "IndexingSettings", "IntegerFieldSetting", "ItemList", "Library", "ListItem", "LoggingSettings",
                                     "LongTextFieldSetting", "NullFieldSetting", "NumberFieldSetting", "PasswordFieldSetting",
                                     "PermissionChoiceFieldSetting", "PortalRoot", "PreviewImage", "ProfileDomain", "Profiles",
                                     "ReferenceFieldSetting", "Resources", "RuntimeContentContainer", "SharingGroup",
                                     "ShortTextFieldSetting", "Sites", "SystemFile", "TextFieldSetting", "TrashBag", "TrashBin",
                                     "UserProfile", "WebServiceApplication", "XmlFieldSetting", "YesNoFieldSetting"
                                 })
                            cb.Type(ctd).IsSystemType(true);

                        cb.Apply();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error during CTD changes.");
                    }

                    #endregion

                    #region Content changes

                    // set new fields after CTD changes
                    logger.LogTrace($"Switching ON preview generation by default on Root.");
                    
                    var rootNode = Node.Load<PortalRoot>("/Root");
                    rootNode.PreviewEnabled = PreviewEnabled.Yes;
                    rootNode.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();

                    #endregion
                });

            builder.Patch("7.7.28", "7.7.29", "2023-03-27", "Upgrades sensenet content repository.")
                .Action(Patch_7_7_29);

            builder.Patch("7.7.29", "7.7.30", "2023-06-09", "Upgrades sensenet content repository.")
                .Action(Patch_7_7_30);

            builder.Patch("7.7.30", "7.7.31", "2023-10-16", "Upgrades sensenet content repository.")
                .Action(Patch_7_7_31);

            builder.Patch("7.7.31", "7.7.32", "2024-01-17", "Upgrades sensenet content repository.")
                .Action(Patch_7_7_32);

            builder.Patch("7.7.32", "7.7.41", "2024-05-27", "Upgrades sensenet content repository.")
                .Action(Patch_7_7_41);

            builder.Patch("7.7.41", "7.8.0", "2025-02-26", "Upgrades sensenet content repository.")
                .Action(Patch_7_8_0);

            builder.Patch("7.8.0", "7.8.1", "2025-03-07", "Upgrades sensenet content repository.")
                .Action(Patch_7_8_1);
        }

        private void Patch_7_7_29(PatchExecutionContext context)
        {
            var logger = context.GetService<ILogger<ServicesComponent>>();

            #region Content changes

            var contentTemplates = RepositoryTools.CreateStructure(RepositoryStructure.ContentTemplateFolderPath, 
                "SystemFolder");

            logger.LogTrace(contentTemplates != null
                ? "ContentTemplates folder has been created."
                : "ContentTemplates folder already exists.");

            #endregion

            #region Permission changes

            var publicAdminsGroupId = Node.LoadNode("/Root/IMS/Public/Administrators")?.Id ?? 0;
            var systemFolderId = NodeHead.Get(Repository.SystemFolderPath).Id;
            var schemaFolderId = NodeHead.Get(Repository.SchemaFolderPath).Id;
            var editor = Providers.Instance.SecurityHandler.CreateAclEditor();

            // public admin permissions
            if (publicAdminsGroupId > 0)
            {
                // local See permission on the System and Schema folders
                editor.Allow(systemFolderId, publicAdminsGroupId, true, PermissionType.See)
                    .Allow(schemaFolderId, publicAdminsGroupId, true, PermissionType.See);

                // Somebody user
                editor.Allow(Identifiers.SomebodyUserId, publicAdminsGroupId, false, PermissionType.See);
            }

            // if we just created this folder above
            if (contentTemplates != null)
            {
                var developersGroupId = Node.LoadNode(N.R.Developers)?.Id ?? 0;
                var editorsGroupId = Node.LoadNode(N.R.Editors)?.Id ?? 0;

                if (publicAdminsGroupId > 0)
                    editor.Allow(contentTemplates.Id, publicAdminsGroupId, false, PermissionType.BuiltInPermissionTypes);
                if (developersGroupId > 0)
                    editor.Allow(contentTemplates.Id, developersGroupId, false, PermissionType.BuiltInPermissionTypes);
                if (editorsGroupId > 0)
                    editor.Allow(contentTemplates.Id, editorsGroupId, false, PermissionType.BuiltInPermissionTypes);

                editor.Allow(contentTemplates.Id, Identifiers.EveryoneGroupId, true, PermissionType.Open);
            }

            editor.ApplyAsync(CancellationToken.None).GetAwaiter().GetResult();
            logger.LogTrace("Permissions are successfully set.");

            #endregion
        }

        private void Patch_7_7_30(PatchExecutionContext context)
        {
            var logger = context.GetService<ILogger<ServicesComponent>>();

            #region Content changes

            // load it first so that we know if it exists in the repo or not
            var columnSettingsPath = RepositoryPath.Combine(Repository.SettingsFolderPath, "ColumnSettings.settings");
            var columnSettings = Node.LoadNode(columnSettingsPath);

            CreateSettings("ColumnSettings.settings", @"{
  ""columns"": [
    {
      ""field"": ""DisplayName"",
      ""title"": ""Display Name""
    },
    {
      ""field"": ""Locked"",
      ""title"": ""Locked""
    },
    {
      ""field"": ""CreatedBy"",
      ""title"": ""Created by""
    },
    {
      ""field"": ""CreationDate"",
      ""title"": ""Creation Date""
    },
    {
      ""field"": ""ModifiedBy"",
      ""title"": ""Modified by""
    },
    {
      ""field"": ""ModificationDate"",
      ""title"": ""Modification Date""
    },
    {
      ""field"": ""Actions"",
      ""title"": ""Actions""
    }
  ]
}",
                "In this setting section you can customize the columns visible in grids " +
                "throughout admin UI. It is also possible to set local column settings " +
                "(using the button in grid headers) to have container-specific columns.", false, logger);

            #endregion

            #region Permission changes

            var editor = Providers.Instance.SecurityHandler.CreateAclEditor();

            // Ensure the principle of minimal privilege on the builtin domain.
            var builtinDomainId = NodeHead.Get("/Root/IMS/BuiltIn")?.Id ?? 0;
            if (builtinDomainId > 0)
            {
                logger.LogTrace("Breaking permissions on the builtin domain.");
                editor.BreakInheritance(builtinDomainId, new[] {EntryType.Normal});
            }

            // Add permissions for public administrators to all domains except Builtin.
            var imsFolderId = NodeHead.Get("/Root/IMS")?.Id ?? 0;
            if (imsFolderId > 0)
            {
                logger.LogTrace("Adding permissions for public administrators and owners on the IMS folder.");
                var publicAdminsGroupId = NodeHead.Get("/Root/IMS/Public/Administrators")?.Id ?? 0;
                if (publicAdminsGroupId > 0)
                {
                    // Add required permissions to manage new domains
                    editor.Allow(imsFolderId, publicAdminsGroupId, false,
                        PermissionType.Open,
                        PermissionType.Save,
                        PermissionType.AddNew,
                        PermissionType.Delete,
                        PermissionType.SeePermissions,
                        PermissionType.SetPermissions);
                    // Avoid deleting or modifying the IMS root: local-only deny permissions
                    editor.Deny(imsFolderId, publicAdminsGroupId, true,
                        PermissionType.Save,
                        PermissionType.Delete,
                        PermissionType.SeePermissions,
                        PermissionType.SetPermissions);
                }
                // Add permissions for owners to manage their own domains.
                editor.Allow(imsFolderId, Identifiers.OwnersGroupId, false,
                    PermissionType.AddNew,
                    PermissionType.Delete,
                    PermissionType.SeePermissions,
                    PermissionType.SetPermissions);
            }

            // if we just created this content above
            if (columnSettings == null)
            {
                logger.LogTrace("Setting permissions for public developers and editors on the column settings content.");
                columnSettings = Node.LoadNode(columnSettingsPath);

                var developers = NodeHead.Get(N.R.Developers);
                var editors = NodeHead.Get(N.R.Editors);

                if (developers != null)
                    editor.Allow(columnSettings.Id, developers.Id, false, PermissionType.Save);
                if (editors != null)
                    editor.Allow(columnSettings.Id, editors.Id, false, PermissionType.Save);
            }

            editor.ApplyAsync(CancellationToken.None).GetAwaiter().GetResult();
            logger.LogTrace("Permissions are successfully set.");

            #endregion

            #region String resource changes

            var rb = new ResourceBuilder();

            rb.Content("CtdResourcesTZ.xml")
                .Class("Ctd-User")
                .Culture("en")
                .AddResource("MultiFactorEnabled-DisplayName", "Multifactor authentication enabled")
                .AddResource("MultiFactorEnabled-Description", "Multifactor authentication enabled")
                .AddResource("MultiFactorRegistered-DisplayName", "Multifactor authentication registered")
                .AddResource("MultiFactorRegistered-Description", "Whether the user already signed in using multifactor authentication.")
                .Culture("hu")
                .AddResource("MultiFactorEnabled-DisplayName", "Többfaktoros hitelesítés engedélyezve")
                .AddResource("MultiFactorEnabled-Description", "Többfaktoros hitelesítés engedélyezve")
                .AddResource("MultiFactorRegistered-DisplayName", "Többfaktoros hitelesítés regisztrálva")
                .AddResource("MultiFactorRegistered-Description", "A felhasználó belépett már többfaktoros hitelesítéssel.")
                ;

            rb.Apply();

            #endregion

            #region Settings changes

            CreateSettings("MultiFactorAuthentication.settings", @"{ ""MultiFactorAuthentication"": ""Optional"" }",
                "In this setting section you can define whether multi-factor authentication is enabled optionally " +
                "or forced in the system. This setting can be overridden locally.",
                false, logger,
                (settings, aclEditor) =>
                {
                    var developers = NodeHead.Get(N.R.Developers);
                    if (developers != null)
                        aclEditor.Allow(settings.Id, developers.Id, false, PermissionType.Save);
                    var publicAdmins = NodeHead.Get(N.R.PublicAdministrators);
                    if (publicAdmins != null)
                        aclEditor.Allow(settings.Id, publicAdmins.Id, false, PermissionType.Save);
                }
            );

            #endregion

            #region CTD changes

            try
            {
                const string publicDomainPath = "/Root/IMS/Public";
                var cb = new ContentTypeBuilder(context.GetService<ILogger<ContentTypeBuilder>>());

                // selection root changes
                void ReplaceSelectionRoot(string contentTypeName, string fieldName)
                {
                    var contentType = ContentType.GetByName(contentTypeName);
                    var field = (ReferenceFieldSetting)contentType.FieldSettings.First(f => f.Name == fieldName);
                    if (!field.SelectionRoots.Contains(publicDomainPath)) 
                        return;

                    // a new list, with the public domain replaced by /Root/IMS
                    var newList = field.SelectionRoots.Select(sr => sr == publicDomainPath ? "/Root/IMS" : sr)
                        .Distinct()
                        .ToList();

                    cb.Type(contentTypeName)
                        .Field(fieldName)
                        .Configure("SelectionRoot",
                            string.Join(string.Empty, newList.Select(sr => $"<Path>{sr}</Path>")));
                }

                logger.LogTrace("Editing CTDs: replacing Public domain selection roots with IMS...");

                ReplaceSelectionRoot("Group", "Members");
                ReplaceSelectionRoot("User", "Manager");

                // adding multifactor fields
                logger.LogTrace("Editing CTDs: adding multifactor fields...");

                cb.Type("User")
                    .Field("MultiFactorEnabled", "Boolean")
                    .DisplayName("$Ctd-User,MultiFactorEnabled-DisplayName")
                    .Description("$Ctd-User,MultiFactorEnabled-Description")
                    .FieldIndex(130)
                    .Field("MultiFactorRegistered", "Boolean")
                    .DisplayName("$Ctd-User,MultiFactorRegistered-DisplayName")
                    .Description("$Ctd-User,MultiFactorRegistered-Description")
                    .VisibleBrowse(FieldVisibility.Hide)
                    .VisibleEdit(FieldVisibility.Hide)
                    .VisibleNew(FieldVisibility.Hide)
                    .FieldIndex(135)
                    ;

                cb.Apply();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error during CTD changes.");
            }

            #endregion
        }

        private void Patch_7_7_31(PatchExecutionContext context)
        {
            var logger = context.GetService<ILogger<ServicesComponent>>();

            #region Content changes

            // email template for changed password
            var changePassTemplate = Content.Load("/Root/System/Templates/Email/Registration/ChangePassword");
            if (changePassTemplate != null)
            {
                // update only if it has NOT changed after installation
                var timeDiff = changePassTemplate.ModificationDate - changePassTemplate.CreationDate;
                if (timeDiff.TotalSeconds < 1)
                {
                    logger.LogInformation("Updating password change email template...");

                    changePassTemplate["Subject"] = "{FullName} - Please change your password in {RepositoryUrl}";
                    changePassTemplate["Body"] = @"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"" />
    <title>{FullName} - Please change your password in {RepositoryUrl}</title>
</head>
<body>
    <div style=""text-align: center;padding: 10px;padding-top:30px"">
        <div style=""width: 600px;margin: 0 auto;
                    text-align: left;
                    font-family: Roboto, Arial, Helvetica, sans-serif;
                    color: #757575;
                    font-size: 16px;
                    line-height: 150%;"">
            <div style=""text-align: center"">
                <img src=""https://github.com/SenseNet/sn-resources/blob/master/images/sn-icon/sensenet-icon-64.png?raw=true"" alt=""sensenet logo"" />
            </div>
            <div style=""padding: 10px"">
                <h1 style=""font-size: 16px"">Welcome {LoginName}</h1>
				<h4>Repository: {RepositoryUrl}</h4>
                <div>Before you log in with your user <strong>{LoginName}</strong>, please change your password.</div>
                <div>If you did not create an account using this address (<a style=""color: #007C89 !important;text-decoration: none !important"">{Email}</a>), please skip this mail.</div>
                <table width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"">
                    <tr>
                        <td align=""center"" style=""padding: 30px"">
                            <table border=""0"" cellspacing=""0"" cellpadding=""0"">
                                <tr>
                                    <td align=""center"" style=""border-radius: 25px;text-align: center;"" bgcolor=""#13a5ad"">
                                        <a href=""{ActionUrl}"" target=""_blank""
                                           style="" font-family: Helvetica, Arial, sans-serif; color: #ffffff; text-decoration: none; text-decoration: none;border-radius: 25px;padding: 10px; border: 1px solid #13a5ad; display: inline-block;"">
                                            Change your password
                                        </a>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>
                </table>
                <div style=""text-align: center; width: auto; font-size: 80%;"">
                    Or using this link:<br />
                    <a style=""color: #007C89"" href=""{ActionUrl}"">{ActionUrl}</a>
                </div>
                <br />
                <div>Thank you!</div>
                <br />
                <div>regards,<br />sensenet team</div>
            </div>
            <br />
            <br />
            <div style=""width: 100%;clear: both;display:block;text-align: center;border-top: solid 1px #cfcfcf;margin-top: 20px;padding: 10px;margin: 20px 10px 10px"">
                <table style=""margin: 0 auto"">
                    <tr>
                        <td style=""padding: 10px"">
                            <a href=""https://github.com/SenseNet"">
                                <img width=""24"" src=""https://cdn-images.mailchimp.com/icons/social-block-v2/outline-gray-github-48.png"" />
                            </a>
                        </td>
                        <td style=""padding: 10px"">
                            <a href=""https://sensenet.com"">
                                <img width=""24"" src=""https://cdn-images.mailchimp.com/icons/social-block-v2/outline-gray-link-48.png"" />
                            </a>
                        </td>
                        <td style=""padding: 10px"">
                            <a href=""https://www.linkedin.com/company/sense-net-inc/about/"">
                                <img width=""24"" src=""https://cdn-images.mailchimp.com/icons/social-block-v2/outline-gray-linkedin-48.png"" />
                            </a>
                        </td>
                    </tr>
                </table>
                <div style=""font-size: 12px"">
                    <em>Copyright © 2022 Sense/Net, All rights reserved.</em>
                </div>
            </div>
        </div>
    </div>
</body>
</html>
";
                    changePassTemplate.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
            }

            #endregion
        }

        private void Patch_7_7_32(PatchExecutionContext context)
        {
            var logger = context.GetService<ILogger<ServicesComponent>>();

            #region Content changes

            string[] defaultAiUserGroups =
            {
                N.R.Administrators,
                "/Root/IMS/BuiltIn/Portal/Editors",
                "/Root/IMS/Public/Administrators",
            };

            if (!Node.Exists(N.R.AITextUsers))
            {
                logger.LogTrace("Creating AITextUsers group...");

                var aiTextUsers = Content.CreateNew("Group", OrganizationalUnit.Portal, "AITextUsers");

                aiTextUsers.DisplayName = "AITextUsers";
                aiTextUsers["Members"] = Node.LoadNodes(defaultAiUserGroups.Select(NodeIdentifier.Get));
                aiTextUsers.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();
            }

            if (!Node.Exists(N.R.AIVisionUsers))
            {
                logger.LogTrace("Creating AIVisionUsers group...");

                var aiVisionUsers = Content.CreateNew("Group", OrganizationalUnit.Portal, "AIVisionUsers");

                aiVisionUsers.DisplayName = "AIVisionUsers";
                aiVisionUsers["Members"] = Node.LoadNodes(defaultAiUserGroups.Select(NodeIdentifier.Get));
                aiVisionUsers.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();
            }

            #endregion

            #region String resource changes

            var rb = new ResourceBuilder();
            rb.Content("CtdResourcesCD.xml")
                .Class("Ctd-ContentType")
                .Culture("en")
                .AddResource("Categories-DisplayName", "Categories")
                .AddResource("Categories-Description", "Space separated list of all categories.")
                .Culture("hu")
                .AddResource("Categories-DisplayName", "Kategóriák")
                .AddResource("Categories-Description", "Kategórianevek szóközzel elválasztva.");
            rb.Apply();

            #endregion

            #region Settings changes

            DeleteSettings("OAuth.settings", logger);
            DeleteSettings("TaskManagement.settings", logger);

            try
            {
                // add client cache header value for svg and webp files if necessary
                UpdatePortalSettings(new (string Key, string Value, int MaxAge)[]
                    {
                        ("Extension", "svg", 604800),
                        ("Extension", "webp", 604800)
                    },
                    new (string extension, string typeName)[]
                    {
                        ("webp", "Image")
                    },
                    logger);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error loading or modifying Portal settings.");
            }

            #endregion

            #region CTD changes

            try
            {
                var cb = new ContentTypeBuilder(context.GetService<ILogger<ContentTypeBuilder>>());
                cb.Type("ContentType")
                    .Field("Categories", "ShortText")
                    .DisplayName("$Ctd-ContentType,Categories-DisplayName")
                    .Description("$Ctd-ContentType,Categories-Description")
                    .Bind("CategoryNames")
                    .VisibleBrowse(FieldVisibility.Hide)
                    .VisibleEdit(FieldVisibility.Hide)
                    .VisibleNew(FieldVisibility.Hide)
                    .ReadOnly(true);
                cb.Apply();

                var category = "HideByDefault";
                cb.Type("Application").AddCategory(category);
                cb.Type("ApplicationOverride").AddCategory(category);
                cb.Type("Aspect").AddCategory(category);
                cb.Type("BinaryFieldSetting").AddCategory(category);
                cb.Type("ChoiceFieldSetting").AddCategory(category);
                cb.Type("ClientApplication").AddCategory(category);
                cb.Type("ContentList").AddCategory(category);
                cb.Type("CurrencyFieldSetting").AddCategory(category);
                cb.Type("CustomListItem").AddCategory(category);
                cb.Type("DateTimeFieldSetting").AddCategory(category);
                cb.Type("Device").AddCategory(category);
                cb.Type("Domains").AddCategory(category);
                cb.Type("ExecutableFile").AddCategory(category);
                cb.Type("FieldSettingContent").AddCategory(category);
                cb.Type("GenericContent").AddCategory(category);
                cb.Type("GenericODataApplication").AddCategory(category);
                cb.Type("HyperLinkFieldSetting").AddCategory(category);
                cb.Type("IndexingSettings").AddCategory(category);
                cb.Type("IntegerFieldSetting").AddCategory(category);
                cb.Type("ItemList").AddCategory(category);
                cb.Type("Library").AddCategory(category);
                cb.Type("ListItem").AddCategory(category);
                cb.Type("LoggingSettings").AddCategory(category);
                cb.Type("LongTextFieldSetting").AddCategory(category);
                cb.Type("NullFieldSetting").AddCategory(category);
                cb.Type("NumberFieldSetting").AddCategory(category);
                cb.Type("PasswordFieldSetting").AddCategory(category);
                cb.Type("PermissionChoiceFieldSetting").AddCategory(category);
                cb.Type("PortalRoot").AddCategory(category);
                cb.Type("PreviewImage").AddCategory(category);
                cb.Type("ProfileDomain").AddCategory(category);
                cb.Type("Profiles").AddCategory(category);
                cb.Type("ReferenceFieldSetting").AddCategory(category);
                cb.Type("Resources").AddCategory(category);
                cb.Type("RuntimeContentContainer").AddCategory(category);
                cb.Type("SharingGroup").AddCategory(category);
                cb.Type("ShortTextFieldSetting").AddCategory(category);
                cb.Type("Sites").AddCategory(category);
                cb.Type("SystemFile").AddCategory(category);
                cb.Type("TextFieldSetting").AddCategory(category);
                cb.Type("TrashBag").AddCategory(category);
                cb.Type("TrashBin").AddCategory(category);
                cb.Type("UserProfile").AddCategory(category);
                cb.Type("WebServiceApplication").AddCategory(category);
                cb.Type("XmlFieldSetting").AddCategory(category);
                cb.Type("YesNoFieldSetting").AddCategory(category);
                cb.Apply();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error during CTD changes.");
            }

            #endregion
        }

        private void Patch_7_7_41(PatchExecutionContext context)
        {
            var logger = context.GetService<ILogger<ServicesComponent>>();

            #region CTD changes

            var richTextFieldNames = ContentType.GetContentTypes()
                .SelectMany(ct => ct.FieldSettings)
                .Where(fs => fs is RichTextFieldSetting)
                .Select(fs => fs.Name)
                .Distinct()
                .ToArray();

            try
            {
                Patch_7_7_41_UpdateLongTextProperties(richTextFieldNames);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error during updating LongTextProperties table.");
            }

            try
            {
                var cb = new ContentTypeBuilder(context.GetService<ILogger<ContentTypeBuilder>>());
                foreach (var richTextFieldName in richTextFieldNames)
                    cb.ChangeFieldType(richTextFieldName, "LongText");
                cb.Apply();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error during CTD changes.");
            }

            #endregion
        }
        private void Patch_7_7_41_UpdateLongTextProperties(string[] richTextFieldNames)
        {
            if (!(Providers.Instance.DataProvider is RelationalDataProviderBase dataProvider))
                throw new PackagingException(
                    "This patch can run only with RelationalDataProviderBase (e.g. MsSqlDataProvider)");

            var joinedRichTextFieldNames = string.Join(", ", richTextFieldNames.Select(x => $"'{x}'"));
            var script = $@"SELECT LongTextProperties.LongTextPropertyId, LongTextProperties.Value
FROM LongTextProperties
	INNER JOIN PropertyTypes ON LongTextProperties.PropertyTypeId = PropertyTypes.PropertyTypeId
WHERE PropertyTypes.Name in ({joinedRichTextFieldNames})
";

            Dictionary<int, string> queryResult = null;
            using (var context = dataProvider.CreateDataContext(CancellationToken.None))
            {
                queryResult = context.ExecuteReaderAsync(script, async (reader, cancel) =>
                {
                    cancel.ThrowIfCancellationRequested();
                    var result = new Dictionary<int, string>();
                    while (await reader.ReadAsync(cancel).ConfigureAwait(false))
                        result.Add(DataReaderExtension.GetInt32(reader, "LongTextPropertyId"), reader.GetSafeString("Value"));
                    return result;
                }).GetAwaiter().GetResult();
            }

            var updates = new Dictionary<int, string>();
            var deletion = new List<int>();
            foreach (var item in queryResult)
            {
                var id = item.Key;
                var value = item.Value;
                RichTextFieldValue rtfValue = null;
                try
                {
                    rtfValue = JsonConvert.DeserializeObject<RichTextFieldValue>(value);
                }
                catch
                {
                    /* do nothing */
                }

                if (rtfValue == null)
                    continue;

                if (string.IsNullOrEmpty(rtfValue.Text))
                    deletion.Add(id);
                else
                    updates.Add(id, rtfValue.Text);
            }

            // Play deletion if there is any
            if (deletion.Count > 0)
            {
                var joinedIdsToDelete = string.Join(", ", deletion.Select(x => x.ToString()));
                var deletionSql = $@"DELETE FROM LongTextProperties WHERE LongTextPropertyId IN ({joinedIdsToDelete})";
                using var context = dataProvider.CreateDataContext(CancellationToken.None);
                var _ = context.ExecuteNonQueryAsync(deletionSql).GetAwaiter().GetResult();
            }

            // Play updates
            foreach (var item in updates)
            {
                var sql = "UPDATE LongTextProperties SET Value = @Value WHERE LongTextPropertyId = @Id";
                using var ctx = dataProvider.CreateDataContext(CancellationToken.None);
                var unused = ctx.ExecuteNonQueryAsync(sql, cmd =>
                {
                    cmd.Parameters.AddRange(new[]
                    {
                        ctx.CreateParameter("@Id", DbType.Int32, item.Key),
                        ctx.CreateParameter("@Value", DbType.String, item.Value),
                    });
                }).GetAwaiter().GetResult();
            }
        }

        private void Patch_7_8_0(PatchExecutionContext context)
        {
            var logger = context.GetService<ILogger<ServicesComponent>>();

            #region String resource changes

            logger.LogTrace("Adding string resources...");

            var rb = new ResourceBuilder();

            rb.Content("CtdResourcesNOP.xml")
                .Class("Ctd-Operation")
                .Culture("en")
                .AddResource("DisplayName", "Operation")
                .AddResource("Description", "Describes the details of an operation associated with a location and content type.")
                .AddResource("UIDescriptor-DisplayName", "UI Descriptor")
                .AddResource("UIDescriptor-Description", "Definition of the user interface required for the operation.")
                .Culture("hu")
                .AddResource("DisplayName", "Művelet")
                .AddResource("Description", "Leírja egy helyhez és tartalomtípushoz köthető művelet részleteit.")
                .AddResource("UIDescriptor-DisplayName", "UI Leíró")
                .AddResource("UIDescriptor-Description", "A művelethez szükséges felhasználói felület definíciója.");

            rb.Apply();

            #endregion

            #region CTD changes

            const string operationCtd = @"<ContentType name=""Operation"" parentType=""ClientApplication"" handler=""SenseNet.OperationFramework.Operation"" xmlns=""http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition"">
  <DisplayName>$Ctd-Operation,DisplayName</DisplayName>
  <Description>$Ctd-Operation,Description</Description>
  <Icon>Operation</Icon>
  <Fields>
    <Field name=""UIDescriptor"" type=""LongText"">
      <DisplayName>$Ctd-Operation,UIDescriptor-DisplayName</DisplayName>
      <Description>$Ctd-Operation,UIDescriptor-Description</Description>
    </Field>
    <Field name=""ClassName"" type=""ShortText"">
      <DisplayName>$Ctd-Operation,ClassName-DisplayName</DisplayName>
      <Description>$Ctd-Operation,ClassName-Description</Description>
    </Field>
    <Field name=""MethodName"" type=""ShortText"">
      <DisplayName>$Ctd-Operation,MethodName-DisplayName</DisplayName>
      <Description>$Ctd-Operation,MethodName-Description</Description>
    </Field>
    <Field name=""ActionTypeName"" type=""ShortText"">
      <DisplayName>$Ctd-Operation,ActionTypeName-DisplayName</DisplayName>
      <Description>$Ctd-Operation,ActionTypeName-Description</Description>
    </Field>
  </Fields>
</ContentType>";

            logger.LogTrace("Installing Operation content type...");
            ContentTypeInstaller.InstallContentType(operationCtd);

            #endregion
        }

        private void Patch_7_8_1(PatchExecutionContext context)
        {
            var logger = context.GetService<ILogger<ServicesComponent>>();

            #region String resource changes

            logger.LogTrace("Adding string resources...");

            var rb = new ResourceBuilder();

            rb.Content("CtdResourcesNOP.xml")
                .Class("Ctd-Operation")
                .Culture("en")
                .AddResource("ClassName-DisplayName", "Class/Controller name")
                .AddResource("ClassName-Description", "Fully qualified class name or simple ODataController name that contains the executable method.")
                .AddResource("MethodName-DisplayName", "Method name")
                .AddResource("MethodName-Description", "Name of the method to be executed, in case of ODataController: name of the corresponding operation name.")
                .AddResource("ActionTypeName-DisplayName", "Custom action type name")
                .AddResource("ActionTypeName-Description", "Type name of the custom action if there is")
                .Culture("hu")
                .AddResource("ClassName-DisplayName", "Osztály/Controller neve")
                .AddResource("ClassName-Description", "Teljes osztálynév vagy egyszerű ODataController név, amely tartalmazza a végrehajtandó metódust.")
                .AddResource("MethodName-DisplayName", "Metódusnév")
                .AddResource("MethodName-Description", "A végrehajtandó metódus neve vagy ODataController esetén a metódusnak megfelelő operáció neve.")
                .AddResource("ActionTypeName-DisplayName", "Egyéni művelet típusneve")
                .AddResource("ActionTypeName-Description", "Egyéni művelet típusneve, ha van ilyen");

            rb.Apply();

            #endregion
        }

        #region Patch template

        // =================================================================================================
        // Template method for a feature patch. When creating a patch for a feature, copy this method,
        // remove unnecessary parts and uncomment what you need.
        // =================================================================================================

        //private void Patch_Template_Feature(PatchExecutionContext context)
        //{
        //    var logger = context.GetService<ILogger<ServicesComponent>>();

        //    #region Content changes
        //    #endregion

        //    #region Permission changes

        //    //Providers.Instance.SecurityHandler.CreateAclEditor()
        //    //    .Allow()
        //    //    .Apply();

        //    #endregion

        //    #region String resource changes

        //    //var rb = new ResourceBuilder();
        //    //rb.Apply();

        //    #endregion

        //    #region Settings changes
        //    #endregion

        //    #region CTD changes

        //    //try
        //    //{
        //    //    var cb = new ContentTypeBuilder(context.GetService<ILogger<ContentTypeBuilder>>());
        //    //    cb.Apply();
        //    //}
        //    //catch (Exception ex)
        //    //{
        //    //    logger.LogWarning(ex, "Error during CTD changes.");
        //    //}

        //    #endregion
        //}

        #endregion

        private static void CreateSettings(string contentName, string value, string description, bool globalOnly, ILogger logger,
            Action<Settings, SnAclEditor> setPermissions = null)
        {
            if (Node.Exists(RepositoryPath.Combine(Repository.SettingsFolderPath, contentName)))
            {
                logger.LogTrace($"Settings {contentName} already exists.");
                return;
            }

            var parent = Node.LoadNode(Repository.SettingsFolderPath);
            if (parent == null)
            {
                logger.LogWarning("Settings folder not found.");
                return;
            }

            using var stream = RepositoryTools.GetStreamFromString(value);
            var settings = Content.CreateNew("Settings", parent, contentName);

            settings["GlobalOnly"] = globalOnly;
            settings["Binary"] = UploadHelper.CreateBinaryData(contentName, stream);
            settings["Description"] = description;
            settings.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();

            if (setPermissions != null)
            {
                var editor = Providers.Instance.SecurityHandler.CreateAclEditor();
                setPermissions(settings.ContentHandler as Settings, editor);
                editor.ApplyAsync(CancellationToken.None).GetAwaiter().GetResult();
            }

            logger.LogTrace($"Settings {contentName} was created.");
        }

        private static void DeleteSettings(string settingsName, ILogger logger)
        {
            var path = RepositoryPath.Combine(Repository.SettingsFolderPath, settingsName);
            try
            {
                logger.LogTrace($"Deleting {settingsName} ...");
                var setting = Node.Load<Settings>(path);
                if (setting != null)
                {
                    setting.ForceDeleteAsync(CancellationToken.None).GetAwaiter().GetResult();
                    logger.LogTrace($"{settingsName} successfully deleted.");
                }
                else
                {
                    logger.LogTrace($"{settingsName} already deleted.");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, $"Error deleting {settingsName} settings.");
            }
        }

        private static void UpdatePortalSettings(IEnumerable<(string Key, string Value, int MaxAge)> headers,
            IEnumerable<(string extension, string typeName)> uploadExtensions, ILogger logger)
        {
            var setting = Node.Load<Settings>("/Root/System/Settings/Portal.settings");
            if (setting == null)
                return;

            using var bs = setting.Binary.GetStream();
            var settingsObject = Settings.DeserializeToJObject(bs);
            var cacheHeaderArray = (JArray)settingsObject?["ClientCacheHeaders"];
            var uploadExtensionsObject = (JObject)settingsObject?["UploadFileExtensions"];
            var modified = false;

            if (headers != null && cacheHeaderArray != null)
            {
                foreach (var (key, value, maxAge) in headers)
                {
                    if (cacheHeaderArray.Children().All(jt => jt.Value<string>(key) != value))
                    {
                        // no value found: add it
                        cacheHeaderArray.Add(new JObject(
                            new JProperty(key, value),
                            new JProperty("MaxAge", maxAge)));

                        modified = true;
                    }
                    else
                    {
                        logger.LogTrace($"{key} {value} cache header section already exists in Portal settings.");
                    }
                }
            }

            if (uploadExtensions != null && uploadExtensionsObject != null)
            {
                foreach (var (extension, typeName) in uploadExtensions)
                {
                    if (uploadExtensionsObject.Properties().All(jt => jt.Name != extension))
                    {
                        // no value found: add it
                        uploadExtensionsObject.Add(new JProperty(extension, typeName));

                        modified = true;
                    }
                    else
                    {
                        logger.LogTrace($"{extension} {typeName} upload extension already exists in Portal settings.");
                    }
                }
            }

            if (!modified) 
                return;

            logger.LogTrace("Updating Portal settings with new cache header values...");

            var modifiedJson = JsonConvert.SerializeObject(settingsObject, Formatting.Indented);

            using var modifiedStream = RepositoryTools.GetStreamFromString(modifiedJson);
            setting.Binary.SetStream(modifiedStream);
            setting.SaveAsync(SavingMode.KeepVersion, CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
