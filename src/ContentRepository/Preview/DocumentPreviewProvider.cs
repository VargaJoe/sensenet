using System;
using System.Collections.Generic;
using IO = System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SenseNet.ApplicationModel;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.ContentRepository.Storage.Events;
using SenseNet.ContentRepository.Storage.Schema;
using SenseNet.ContentRepository.Storage.Search;
using SenseNet.ContentRepository.Versioning;
using SenseNet.Diagnostics;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Security;
using Newtonsoft.Json.Converters;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Fields;
using SenseNet.ContentRepository.Schema;
using SenseNet.Extensions.DependencyInjection;
using SenseNet.TaskManagement.Core;
using SenseNet.Tools;
using SkiaSharp;
using BinaryData = SenseNet.ContentRepository.Storage.BinaryData;
using Retrier = SenseNet.ContentRepository.Storage.Retrier;
using Task = System.Threading.Tasks.Task;
// ReSharper disable InconsistentNaming

// ReSharper disable once CheckNamespace
namespace SenseNet.Preview
{
    public enum WatermarkPosition { BottomLeftToUpperRight, UpperLeftToBottomRight, Top, Bottom, Center }
    public enum DocumentFormat { NonDefined, Doc, Docx, Pdf, Ppt, Pptx, Xls, Xlsx }

    [Flags]
    public enum RestrictionType
    {
        NoAccess = 1,
        NoRestriction = 2,
        Redaction = 4,
        Watermark = 8
    }

    public enum PreviewStatus
    {
        NoProvider = -5,
        Postponed = -4,
        Error = -3,
        NotSupported = -2,
        InProgress = -1,
        EmptyDocument = 0,
        Ready = 1
    }

    public class PreviewImageOptions
    {
        public string BinaryFieldName { get; set; }
        public RestrictionType? RestrictionType { get; set; }
        public int? Rotation { get; set; }
    }

    public class WatermarkDrawingInfo
    {
        public string WatermarkText { get; set; }
        public SKFont Font { get; set; }
        public WatermarkPosition Position { get; set; }
        public SKColor Color { get; set; }
        public SKBitmap Image { get; }
        public SKCanvas DrawingContext { get; }
        public SKPaint Paint { get; }

        public WatermarkDrawingInfo(SKBitmap image, SKCanvas context, SKPaint paint)
        {
            Image = image ?? throw new ArgumentNullException(nameof(image));
            DrawingContext = context ?? throw new ArgumentNullException(nameof(context));
            Paint = paint;
        }

        public SKSize MeasureString(string text)
        {
            var rectangle = new SKRect();
            var _ = Paint.MeasureText(text, ref rectangle);
            return new SKSize(rectangle.Width, rectangle.Height);
        }
    }

    public abstract class DocumentPreviewProvider : IPreviewProvider
    {
        public static readonly string PREVIEWIMAGE_CONTENTTYPE = "PreviewImage";
        public static readonly string DOCUMENTPREVIEW_SETTINGS = "DocumentPreview";
        public static readonly string WATERMARK_TEXT = "WatermarkText";
        public static readonly string WATERMARK_ENABLED = "WatermarkEnabled";
        public static readonly string WATERMARK_FONT = "WatermarkFont";
        public static readonly string WATERMARK_BOLD = "WatermarkBold";
        public static readonly string WATERMARK_ITALIC = "WatermarkItalic";
        public static readonly string WATERMARK_FONTSIZE = "WatermarkFontSize";
        public static readonly string WATERMARK_POSITION = "WatermarkPosition";
        public static readonly string WATERMARK_OPACITY = "WatermarkOpacity";
        public static readonly string WATERMARK_COLOR = "WatermarkColor";
        public static readonly string MAXPREVIEWCOUNT = "MaxPreviewCount";        
        public static readonly string PREVIEWS_FOLDERNAME = "Previews";
        public static readonly string PREVIEW_THUMBNAIL_REGEX = "(preview|thumbnail)(?<page>\\d+).png";
        public static readonly string THUMBNAIL_REGEX = "thumbnail(?<page>\\d+).png";

        // these values must be the same as in the preview library
        internal static readonly int THUMBNAIL_WIDTH = 200;
        internal static readonly int THUMBNAIL_HEIGHT = 200;
        internal static readonly int PREVIEW_WIDTH = 1754;
        internal static readonly int PREVIEW_HEIGHT = 1754;
        internal static readonly string PREVIEW_IMAGENAME = "preview{0}.png";
        internal static readonly string THUMBNAIL_IMAGENAME = "thumbnail{0}.png";

        protected static readonly float THUMBNAIL_PREVIEW_WIDTH_RATIO = THUMBNAIL_WIDTH / (float)PREVIEW_WIDTH;
        protected static readonly float THUMBNAIL_PREVIEW_HEIGHT_RATIO = THUMBNAIL_HEIGHT / (float)PREVIEW_HEIGHT;

        protected static readonly int PREVIEW_PDF_WIDTH = 600;
        protected static readonly int PREVIEW_PDF_HEIGHT = 850;
        protected static readonly int PREVIEW_WORD_WIDTH = 800;
        protected static readonly int PREVIEW_WORD_HEIGHT = 870;
        protected static readonly int PREVIEW_EXCEL_WIDTH = 1000;
        protected static readonly int PREVIEW_EXCEL_HEIGHT = 750;

        protected static readonly int WATERMARK_MAXLINECOUNT = 3;

        protected static readonly string FIELD_PAGEATTRIBUTES = "PageAttributes";

        // ===================================================================================================== Static provider instance

        // This property has a duplicate in the Storage layer in the PreviewProvider
        // class. If you change this, please propagate changes there.
        // In this layer we assume that the provider is an instance of the DocumentPreviewProvider
        // class that we are in now. The setter extension methods in the Preview package
        // will make sure this is true.
        public static DocumentPreviewProvider Current => (DocumentPreviewProvider)Providers.Instance.PreviewProvider;

        protected TaskManagementOptions TaskManagementOptions { get; }
        protected ITaskManager TaskManager { get; }

        protected DocumentPreviewProvider(IOptions<TaskManagementOptions> taskManagementOptions, ITaskManager taskManager)
        {
            TaskManagementOptions = taskManagementOptions?.Value ?? new TaskManagementOptions();
            TaskManager = taskManager;
        }


        // ===================================================================================================== Helper methods

        protected static string GetPreviewsSubfolderName(Node content)
        {
            return content.Version.ToString();
        }
        
        protected static string GetPreviewNameFromPageNumber(int page)
        {
            return string.Format(PREVIEW_IMAGENAME, page);
        }
        protected static string GetThumbnailNameFromPageNumber(int page)
        {
            return string.Format(THUMBNAIL_IMAGENAME, page);
        }

        protected static bool GetDisplayWatermarkQueryParameter()
        {
            var watermarkVal = CompatibilitySupport.GetRequestItem("watermark");
            if (string.IsNullOrEmpty(watermarkVal))
                return false;

            if (watermarkVal == "1")
                return true;

            bool wm;
            return bool.TryParse(watermarkVal, out wm) && wm;
        }

        protected static int? GetRotationQueryParameter()
        {
            var paramVal = CompatibilitySupport.GetRequestItem("rotation");
            if (string.IsNullOrEmpty(paramVal))
                return null;


            if (!int.TryParse(paramVal, out var rotation))
                return null;

            // both positive and negative values are accepted
            switch (Math.Abs(rotation))
            {
                case 90:
                case 180:
                case 270:
                    return rotation;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Gets a list containing preview image options for certain pages (e.g. rotation).
        /// </summary>
        /// <param name="content">A document containing one or more pages.</param>
        /// <returns>A page number / page option dictionary. The option value is a dynamic object that may expand in the future.</returns>
        protected static IDictionary<int, dynamic> GetPageAttributes(Content content)
        {
            var pageAttributes = new Dictionary<int, dynamic>();

            if (content == null || !content.Fields.ContainsKey(FIELD_PAGEATTRIBUTES))
                return pageAttributes;

            // load the text field
            var pageAttributesFieldValue = content[FIELD_PAGEATTRIBUTES] as string;
            if (string.IsNullOrEmpty(pageAttributesFieldValue))
                return pageAttributes;

            var pageAttributeArray = JsonConvert.DeserializeObject(pageAttributesFieldValue) as JArray;
            if (pageAttributeArray == null)
                return null;
            foreach (dynamic pa in pageAttributeArray)
                pageAttributes.Add((int) pa.pageNum, pa.options);

            return pageAttributes;
        }

        //UNDONE:xxxDrawing: Remove System.Drawing features: ParseColor
        protected static System.Drawing.Color ParseColor(string color)
        {
            // rgba(0,0,0,1)
            if (string.IsNullOrEmpty(color))
                return System.Drawing.Color.DarkBlue;

            var i1 = color.IndexOf('(');
            var colorVals = color.Substring(i1 + 1, color.IndexOf(')') - i1 - 1).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            return System.Drawing.Color.FromArgb(Convert.ToInt32(colorVals[3]), Convert.ToInt32(colorVals[0]),
                                  Convert.ToInt32(colorVals[1]), Convert.ToInt32(colorVals[2]));
        }
        //UNDONE:xxxDrawing: Remove System.Drawing features: ResizeImage
        protected static System.Drawing.Image ResizeImage(System.Drawing.Image image, int maxWidth, int maxHeight)
        {
            if (image == null)
                return null;

            // do not scale up the image
            if (image.Width < maxWidth && image.Height < maxHeight)
                return image;

            int newWidth;
            int newHeight;

            ComputeResizedDimensions(image.Width, image.Height, maxWidth, maxHeight, out newWidth, out newHeight);

            try
            {
                var newImage = new System.Drawing.Bitmap(newWidth, newHeight);
                using (var graphicsHandle = System.Drawing.Graphics.FromImage(newImage))
                {
                    graphicsHandle.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphicsHandle.DrawImage(image, 0, 0, newWidth, newHeight);
                }

                return newImage;
            }
            catch (OutOfMemoryException omex)
            {
                SnLog.WriteException(omex);
                return null;
            }
        }
        //UNDONE:xxxDrawing: Remove System.Drawing features: ComputeResizedDimensions
        protected static void ComputeResizedDimensions(int originalWidth, int originalHeight, int maxWidth, int maxHeight, out int newWidth, out int newHeight)
        {
            // do not scale up the image
            if (originalWidth <= maxWidth && originalHeight <= maxHeight)
            {
                newWidth = originalWidth;
                newHeight = originalHeight;
                return;
            }

            var percentWidth = (float)maxWidth / (float)originalWidth;
            var percentHeight = (float)maxHeight / (float)originalHeight;

            // determine which dimension scale should we use (the smaller)
            var percent = percentHeight < percentWidth ? percentHeight : percentWidth;

            // compute new width and height, based on the final scale
            newWidth = (int)(originalWidth * percent);
            newHeight = (int)(originalHeight * percent);
        }
        //UNDONE:xxxDrawing: Remove System.Drawing features: ComputeResizedDimensionsWithRotation
        protected static void ComputeResizedDimensionsWithRotation(Image previewImage, int maxWidthHeight, int? rotation, out int newWidth, out int newHeight)
        {
            // Compute dimensions using a SQUARE (max width and height are equal). This way
            // the image quality and size will be correct. The page will be rotated
            // later to match the image orientation.
            ComputeResizedDimensions(previewImage.Width, previewImage.Height, maxWidthHeight, maxWidthHeight, out newWidth, out newHeight);

            // if the page is rotated, we need to swap the two coordinates (180 degree does not count, but negative values do)
            if (rotation.HasValue && (Math.Abs(rotation.Value) == 90 || Math.Abs(rotation.Value) == 270))
            {
                int tempWidth = newWidth;
                newWidth = newHeight;
                newHeight = tempWidth;
            }
        }

        protected static void SavePageCount(File file, int pageCount)
        {
            if (file == null || file.PageCount == pageCount)
                return;

            using (new SystemAccount())
            {
                Retrier.Retry<Node>(3, 100,
                    () =>
                    {
                        // try
                        file.PageCount = pageCount;
                        file.DisableObserver(TypeResolver.GetType(NodeObserverNames.DOCUMENTPREVIEW, false));
                        file.DisableObserver(TypeResolver.GetType(NodeObserverNames.NOTIFICATION, false));

                        file.KeepWorkflowsAlive();
                        file.SaveAsync(SavingMode.KeepVersion, CancellationToken.None).GetAwaiter().GetResult();
                        return file;
                    },
                    (_, _, exception) =>
                    {
                        // test
                        if (exception == null)
                            return true;

                        if (!(exception is NodeIsOutOfDateException))
                            throw new Exception(exception.Message, exception);

                        // compensate
                        file = Node.Load<File>(file.Id);
                        return false;
                    });
            }
        }

        protected static VersionNumber GetVersionFromPreview(NodeHead previewHead)
        {
            if (previewHead == null)
                return null;

            // Expected structure: /Root/.../DocumentLibrary/doc1.docx/Previews/V1.2.D/preview1.png
            var parentName = RepositoryPath.GetFileName(RepositoryPath.GetParentPath(previewHead.Path));
            VersionNumber version;

            return !VersionNumber.TryParse(parentName, out version) ? null : version;
        }

        protected static File GetDocumentForPreviewImage(NodeHead previewHead)
        {
            using (new SystemAccount())
            {
                var document = Node.GetAncestorOfType<File>(Node.LoadNode(previewHead.ParentId));

                // we need to load the appropriate document version for this preview image
                var version = GetVersionFromPreview(previewHead);
                if (version != null && version.VersionString != document.Version.VersionString)
                    document = Node.Load<File>(document.Id, version);

                return document;
            }
        }

        /// <summary>
        /// This method ensures the existence of all the preview images in a range. 
        /// It synchronously waits for all the images to be created.
        /// </summary>
        protected static void CheckPreviewImages(Content content, int start, int end)
        {
            if (content == null)
                throw new PreviewNotAvailableException("Content deleted.", -1, 0);

            var pc = (int)content["PageCount"];
            if (pc < 0)
                throw new PreviewNotAvailableException("Preview not available. State: " + pc + ".", -1, pc);
            if (end < 0)
                throw new PreviewNotAvailableException("Invalid 'end' value: " + end, -1, pc);

            Image image;
            var missingIndexes = new List<int>();
            for (var i = start; i <= end; i++)
            {
                AssertResultIsStillRequired();
                image = DocumentPreviewProvider.Current.GetPreviewImage(content, i);
                if (image == null || image.Index < 1)
                    missingIndexes.Add(i);
            }
            foreach (var i in missingIndexes)
            {
                do
                {
                    AssertResultIsStillRequired();

                    // this call will start a preview workflow if the image does not exist
                    image = DocumentPreviewProvider.Current.GetPreviewImage(content, i);
                    if (image == null || image.Index < 1)
                    {
                        // document was deleted in the meantime
                        if (!Node.Exists(content.Path))
                            throw new PreviewNotAvailableException("Content deleted.", -1, 0);

                        Thread.Sleep(1000);
                    }

                } while (image == null);
            }
        }

        protected static IEnumerable<Node> QueryPreviewImages(string path)
        {
            var previewType = GetPreviewImageType();
            if (previewType == null)
                return new Node[0];

            return NodeQuery.QueryNodesByTypeAndPath(previewType, false, path + "/", false)
                .Identifiers
                .Select(NodeHead.Get)
                .Where(h => (h != null) && h.Name.StartsWith("preview", StringComparison.OrdinalIgnoreCase))
                .Select(Node.LoadNode)
                .Where(x => x != null)
                .OrderBy(p => p.Index);
        }

        protected static void AssertResultIsStillRequired()
        {
            if (!CompatibilitySupport.Response_IsClientConnected)
            {
                //TODO: create a new exception class for this
                throw new Exception("Client is disconnected");
            }
        }

        protected static void DrawLines(IList<string> lines, WatermarkDrawingInfo info)
        {
            var numberOfLines = lines.Count(x => !string.IsNullOrEmpty(x));
            var blockHeight = 0.0f;

            for (var j = 0; j < numberOfLines; j++)
            {
                blockHeight += info.MeasureString(lines[j]).Height;
            }

            for (var j = 0; j < numberOfLines; j++)
            {
                var currentLineSize = info.MeasureString(lines[j]);
                var wx = -currentLineSize.Width / 2;
                var wy = 0.0f;

                switch (info.Position)
                {
                    case WatermarkPosition.BottomLeftToUpperRight:
                    case WatermarkPosition.UpperLeftToBottomRight:
                        wy = -(blockHeight / 2) + j * (blockHeight / numberOfLines);
                        break;
                    case WatermarkPosition.Top:
                        wx = (info.Image.Width - currentLineSize.Width) / 2;
                        wy = currentLineSize.Height * (j + 1);
                        break;
                    case WatermarkPosition.Bottom:
                        wx = (info.Image.Width - currentLineSize.Width) / 2;
                        wy = info.Image.Height - blockHeight + currentLineSize.Height * j;
                        break;
                    case WatermarkPosition.Center:
                        wx = (info.Image.Width - currentLineSize.Width) / 2;
                        wy = (info.Image.Height / 2.0f) - (blockHeight / 2) + j * (blockHeight / numberOfLines);
                        break;
                }

                info.DrawingContext.DrawText(lines[j], new SKPoint(wx, wy), info.Paint);
            }
        }

        protected static IEnumerable<string> BreakTextIntoLines(WatermarkDrawingInfo info, double maxTextWithOnImage, float charSize)
        {
            var maxCharNumInLine = (int)Math.Round((maxTextWithOnImage - (maxTextWithOnImage * 0.2)) / charSize);
            var words = info.WatermarkText.Trim().Split(' ');
            var lines = new string[WATERMARK_MAXLINECOUNT];

            var lineNumber = 0;
            var lineLength = 0;

            for (var j = 0; j < words.Length; j++)
            {
                if (lineNumber < WATERMARK_MAXLINECOUNT)
                {
                    if (lineLength < maxCharNumInLine && (lineLength + words[j].Length + 1) < maxCharNumInLine)
                    {
                        if (lineLength == 0)
                        {
                            lines[lineNumber] = lines[lineNumber] + words[j];
                            lineLength += words[j].Length;
                        }
                        else
                        {
                            lines[lineNumber] = lines[lineNumber] + " " + words[j];
                            lineLength += (words[j].Length + 1);
                        }
                    }
                    else
                    {
                        j--;
                        lineLength = 0;
                        lineNumber += 1;
                    }
                }
                else
                {
                    break;
                }
            }

            if (lines.Count(x => !string.IsNullOrEmpty(x)) == 0)
            {
                var charactersToSplit = maxCharNumInLine - 2;
                var maxCharNumber = WATERMARK_MAXLINECOUNT * charactersToSplit;

                if (info.WatermarkText.Length > maxCharNumber)
                {
                    info.WatermarkText = info.WatermarkText.Substring(0, maxCharNumber);
                }

                var watermarkText = string.Empty;
                for (var i = 0; i < WATERMARK_MAXLINECOUNT; i++)
                {
                    var charToSplit = (i + 1) * charactersToSplit <= info.WatermarkText.Length
                        ? charactersToSplit
                        : info.WatermarkText.Length - (i * charactersToSplit);

                    if (charToSplit <= 0)
                    {
                        break;
                    }

                    watermarkText += string.Format("{0} ", info.WatermarkText.Substring(i * charactersToSplit, charToSplit));
                }

                info.WatermarkText = watermarkText;

                return BreakTextIntoLines(info, maxTextWithOnImage, charSize);
            }

            return lines.AsEnumerable();
        }

        private static NodeType GetPreviewImageType()
        {
            return Providers.Instance.StorageSchema.NodeTypes[PREVIEWIMAGE_CONTENTTYPE];
        }


        // ===================================================================================================== Server-side interface

        /// <summary>
        /// General method that returns true if the preview can be generated for the given <see cref="Node"/>.
        /// </summary>
        /// <remarks>
        /// This method takes the preview switch on the given content into account and if the feature is "on", calls
        /// the provider specific <see cref="IsContentSupported"/> method in order to decide, whether the provider can
        /// generate or not.
        /// </remarks>
        public bool IsPreviewEnabled(Node content)
        {
            var contentType = ContentType.GetByName(content.NodeType.Name);
            return content.IsPreviewEnabled && contentType.Preview && IsContentSupported(content);
        }

        /// <summary>
        /// Provider specific method that returns true if it can generate preview of the given <see cref="Node"/>.
        /// </summary>
        public abstract bool IsContentSupported(Node content);
        public abstract string GetPreviewGeneratorTaskName(string contentPath);
        public abstract string GetPreviewGeneratorTaskTitle(string contentPath);
        public abstract string[] GetSupportedTaskNames();

        public virtual bool IsPreviewOrThumbnailImage(NodeHead imageHead)
        {
            if (imageHead == null)
                return false;

            var previewType = GetPreviewImageType();
            if (previewType == null)
                return false;

            return imageHead.GetNodeType().IsInstaceOfOrDerivedFrom(previewType) &&
                   imageHead.Path.Contains(RepositoryPath.PathSeparator + PREVIEWS_FOLDERNAME + RepositoryPath.PathSeparator) &&
                   new Regex(PREVIEW_THUMBNAIL_REGEX).IsMatch(imageHead.Name);
        }

        public virtual bool IsThumbnailImage(Image image)
        {
            if (image == null)
                return false;

            var previewType = GetPreviewImageType();
            if (previewType == null)
                return false;

            return image.NodeType.IsInstaceOfOrDerivedFrom(previewType) &&
                   new Regex(THUMBNAIL_REGEX).IsMatch(image.Name);
        }

        public bool HasPreviewPermission(NodeHead nodeHead)
        {
            return (GetRestrictionType(nodeHead) & RestrictionType.NoAccess) != RestrictionType.NoAccess;
        }

        public virtual bool IsPreviewAccessible(NodeHead previewHead)
        {
            if (!HasPreviewPermission(previewHead))
                return false;

            var version = GetVersionFromPreview(previewHead);

            // The image is outside of a version folder (which is not valid), we have to allow accessing the image.
            if (version == null)
                return true;

            // This method was created to check if the user has access to preview images of minor document versions,
            // so do not bother if this is a preview for a major version.
            if (version.IsMajor)
                return true;

            // Here we assume that permissions are not broken on previews! This means the current user
            // has the same permissions (e.g. OpenMinor) on the preview image as on the document (if this 
            // is a false assumption, than we need to load the document itself and check OpenMinor on it).
            return Providers.Instance.SecurityHandler.HasPermission(previewHead, PermissionType.OpenMinor);
        }


        public virtual RestrictionType GetRestrictionType(NodeHead nodeHead)
        {
            var securityHandler = Providers.Instance.SecurityHandler;
            // if the lowest preview permission is not granted, the user has no access to the preview image
            if (nodeHead == null || !securityHandler.HasPermission(nodeHead, PermissionType.Preview))
                return RestrictionType.NoAccess;

            // has Open permission: means no restriction
            if (securityHandler.HasPermission(nodeHead, PermissionType.Open))
                return RestrictionType.NoRestriction;

            var seeWithoutRedaction = securityHandler.HasPermission(nodeHead, PermissionType.PreviewWithoutRedaction);
            var seeWithoutWatermark = securityHandler.HasPermission(nodeHead, PermissionType.PreviewWithoutWatermark);

            // both restrictions should be applied
            if (!seeWithoutRedaction && !seeWithoutWatermark)
                return RestrictionType.Redaction | RestrictionType.Watermark;

            if (!seeWithoutRedaction)
                return RestrictionType.Redaction;

            if (!seeWithoutWatermark)
                return RestrictionType.Watermark;

            return RestrictionType.NoRestriction;
        }

        public virtual IEnumerable<Content> GetPreviewImages(Content content)
        {
            if (content == null || !this.IsContentSupported(content.ContentHandler) || !HasPreviewPermission(NodeHead.Get(content.Id)))
                return new List<Content>();

            var pc = (int)content["PageCount"];

            if (content.ContentHandler.IsPreviewEnabled && content.ContentType.Preview)
            {
                while (pc == (int) PreviewStatus.InProgress || pc == (int) PreviewStatus.Postponed)
                {
                    // Create task if it does not exists. Otherwise page count will not be calculated.
                    StartPreviewGenerationInternal(content.ContentHandler, priority: TaskPriority.Immediately);

                    Thread.Sleep(4000);

                    AssertResultIsStillRequired();

                    content = Content.Load(content.Id);
                    if (content == null)
                        throw new PreviewNotAvailableException("Content deleted.", -1, 0);

                    pc = (int) content["PageCount"];
                }
            }

            var previewPath = RepositoryPath.Combine(content.Path, PREVIEWS_FOLDERNAME, GetPreviewsSubfolderName(content.ContentHandler));

            // Elevation is OK here as we already checked that the user has preview permissions for 
            // the content. It is needed because of backward compatibility: some preview images 
            // may have been created in a versioned folder as a content with minor version (e.g. 0.1.D).
            using (new SystemAccount())
            {
                var images = QueryPreviewImages(previewPath).ToArray();

                // all preview images exist
                if (images.Length != pc)
                {
                    // check all preview images one-by-one (wait for complete)
                    CheckPreviewImages(content, 1, pc);

                    // We need to clear the context cache because image Index values 
                    // may be cached as 0 and that would affect ordering.
                    StorageContext.L2Cache.Clear();

                    images = QueryPreviewImages(previewPath).ToArray();
                }

                return images.Select(n => Content.Create(n));
            }
        }

        /// <summary>
        /// Collects existing preview images. Returns only the first uninterrupted interval of 
        /// preview images (e.g. if there is a gap, subsequent images will not be returned).
        /// </summary>
        /// <param name="content">The content that has preview images.</param>
        public virtual IEnumerable<Content> GetExistingPreviewImages(Content content)
        {
            if (content == null || !this.IsContentSupported(content.ContentHandler) || !HasPreviewPermission(NodeHead.Get(content.Id)))
                yield break;

            var previewPath = RepositoryPath.Combine(content.Path, PREVIEWS_FOLDERNAME, GetPreviewsSubfolderName(content.ContentHandler));
            var pageNumber = 1;

            // Iterate through existing images. The page number (Index)
            // list should be a list of consecutive numbers.
            foreach (var image in QueryPreviewImages(previewPath).Where(pi => pi.Index > 0))
            {
                // If there is a gap, we should stop here.
                if (image.Index > pageNumber)
                    yield break;

                pageNumber++;

                yield return Content.Create(image);
            }
        }

        public virtual bool HasPreviewImages(Node content)
        {
            var pageCount = (int)content["PageCount"];
            if (pageCount > 0)
                return true;

            var status = (PreviewStatus)pageCount;
            switch (status)
            {
                case PreviewStatus.Postponed:
                case PreviewStatus.InProgress:
                // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
                case PreviewStatus.Ready:
                    return true;
                default:
                    return false;
            }
        }

        public virtual Image GetPreviewImage(Content content, int page)
        {
            return GetImage(content, page, false);
        }

        public virtual Image GetThumbnailImage(Content content, int page)
        {
            return GetImage(content, page, true);
        }

        private Image GetImage(Content content, int page, bool thumbnail)
        {
            if (content == null || page < 1)
                return null;

            // invalid request: not a file or not enough pages
            var file = content.ContentHandler as File;
            if (file == null || file.PageCount < page)
                return null;

            using (new SystemAccount())
            {
                var previewName = thumbnail ? GetThumbnailNameFromPageNumber(page) : GetPreviewNameFromPageNumber(page);
                var path = RepositoryPath.Combine(content.Path, PREVIEWS_FOLDERNAME, GetPreviewsSubfolderName(content.ContentHandler), previewName);
                var img = Node.Load<Image>(path);
                if (img != null)
                    return img;

                if (file.IsPreviewEnabled && content.ContentType.Preview)
                    StartPreviewGenerationInternal(file, page - 1, TaskPriority.Immediately);
            }

            return null;
        }

        [Obsolete("Please use and override the overload with a PreviewImageOptions parameter instead.", true)]
        public virtual IO.Stream GetRestrictedImage(Image image, string binaryFieldName = null, RestrictionType? restrictionType = null)
        {
            return GetRestrictedImage(image, new PreviewImageOptions 
            { 
                BinaryFieldName = binaryFieldName,
                RestrictionType = restrictionType                
            });
        }

        public virtual IO.Stream GetRestrictedImage(Image image, PreviewImageOptions options = null)
        {
            var previewImage = image;

            // we need to reload the image in elevated mode to have access to its properties
            if (previewImage.IsHeadOnly)
                using (new SystemAccount())
                    previewImage = Node.Load<Image>(image.Id);

            options ??= new PreviewImageOptions();

            BinaryData binaryData = null;

            if (!string.IsNullOrEmpty(options.BinaryFieldName))
            {
                var property = previewImage.PropertyTypes[options.BinaryFieldName];
                if (property != null && property.DataType == DataType.Binary)
                    binaryData = previewImage.GetBinary(property);
            }

            binaryData ??= previewImage.Binary;

            // if the image is not a preview, return the requested binary without changes
            var isPreviewOrThumbnailImage = IsPreviewOrThumbnailImage(NodeHead.Get(previewImage.Id));
            if (!isPreviewOrThumbnailImage)
                return binaryData.GetStream();

            var isThumbnail = IsThumbnailImage(previewImage);

            // check restriction type
            var previewHead = NodeHead.Get(previewImage.Id);
            var restriction = GetRestrictionType(previewHead);
            if (options.RestrictionType.HasValue)
                restriction |= options.RestrictionType.Value;

            var displayRedaction = restriction.HasFlag(RestrictionType.Redaction) || 
                                   restriction.HasFlag(RestrictionType.NoAccess);
            var displayWatermark = restriction.HasFlag(RestrictionType.Watermark) || 
                                   restriction.HasFlag(RestrictionType.NoAccess) ||
                                   GetDisplayWatermarkQueryParameter();

            // check watermark master switch in settings
            if (!Settings.GetValue(DOCUMENTPREVIEW_SETTINGS, WATERMARK_ENABLED, image.Path, true))
                displayWatermark = false;

            // rotation option takes precedence over the query parameter
            var rotation = options.Rotation.HasValue ? options.Rotation : GetRotationQueryParameter();

            // if no need to manipulate the image, return the original stream
            if (!displayRedaction && !displayWatermark && !rotation.HasValue)
            {
                return binaryData.GetStream();
            }

            // load the parent document in elevated mode to have access to its properties
            var document = GetDocumentForPreviewImage(previewHead);

            var shapes = document != null ? (string)document["Shapes"] : null;
            var watermark = document != null ? document.Watermark : null;

            // if local watermark is empty, look for setting
            if (string.IsNullOrEmpty(watermark))
                watermark = Settings.GetValue<string>(DOCUMENTPREVIEW_SETTINGS, WATERMARK_TEXT, image.Path);

            // no redaction/highlight data found and no need to rotate the image
            if (string.IsNullOrEmpty(shapes) && string.IsNullOrEmpty(watermark) && !rotation.HasValue)
                return binaryData.GetStream();

            // return a memory stream containing the new image
            var outputStream = new IO.MemoryStream();

            // Do not use "using" statement here, because the "bitmap" is changed in the safe block.
            var bitmap = SKBitmap.Decode(binaryData.GetStream());
            try
            {
                // draw redaction before rotating the image, because redaction rectangles
                // are defined in the coordinating system of the original image
                if (displayRedaction && !string.IsNullOrEmpty(shapes))
                {
                    var temporaryBitmap = Redaction(document, previewImage, isThumbnail, bitmap, shapes);
                    if(!ReferenceEquals(temporaryBitmap, bitmap))
                    {
                        bitmap.Dispose();
                        bitmap = temporaryBitmap;
                    }
                }

                // Rotate image if necessary, before drawing the watermark. RotateFlip method is
                // faster than using the Graphics object to rotate the image.
                if (rotation.HasValue)
                {
                    var temporaryBitmap = Rotate(bitmap, rotation.Value);
                    if (!ReferenceEquals(temporaryBitmap, bitmap))
                    {
                        bitmap.Dispose();
                        bitmap = temporaryBitmap;
                    }
                }

                // draw watermark
                if (displayWatermark && !string.IsNullOrEmpty(watermark))
                {
                    watermark = TemplateManager.Replace(typeof(WatermarkTemplateReplacer), watermark,
                        new[] {document, image});

                    var fontName = Settings.GetValue<string>(DOCUMENTPREVIEW_SETTINGS, WATERMARK_FONT, image.Path) ??
                                   "Microsoft Sans Serif";
                    var weight = SKFontStyleWeight.Normal;
                    if (Settings.GetValue(DOCUMENTPREVIEW_SETTINGS, WATERMARK_BOLD, image.Path, true))
                        weight = SKFontStyleWeight.Bold;
                    var slant = SKFontStyleSlant.Upright;
                    if (Settings.GetValue(DOCUMENTPREVIEW_SETTINGS, WATERMARK_ITALIC, image.Path, false))
                        slant = SKFontStyleSlant.Italic;

                    var size = Settings.GetValue(DOCUMENTPREVIEW_SETTINGS, WATERMARK_FONTSIZE, image.Path, 72.0f);
                    if (isThumbnail)
                        size *= THUMBNAIL_PREVIEW_WIDTH_RATIO;

                    var position = Settings.GetValue(DOCUMENTPREVIEW_SETTINGS, WATERMARK_POSITION, image.Path,
                        WatermarkPosition.BottomLeftToUpperRight);

                    var color = GetWatermarkColor(image.Path);

                    var typeFace = SKTypeface.FromFamilyName(fontName, weight, SKFontStyleWidth.Normal, slant);
                    var paint = new SKPaint
                    {
                        Typeface = typeFace,
                        TextSize = size,
                        IsAntialias = true,
                        Color = color
                    };
                    var font = new SKFont(typeFace, size);

                    var wmInfo = new WatermarkDrawingInfo(bitmap, new SKCanvas(bitmap), paint)
                    {
                        WatermarkText = watermark,
                        Font = font,
                        Color = color,
                        Position = position
                    };

                    DrawWatermark(wmInfo);
                }

                SKEncodedImageFormat imgFormat;

                switch (IO.Path.GetExtension(previewImage.Path).ToLower())
                {
                    case ".png":
                        imgFormat = SKEncodedImageFormat.Png;
                        break;
                    case ".jpg":
                    case ".jpeg":
                        imgFormat = SKEncodedImageFormat.Jpeg;
                        break;
                    case ".bmp":
                        imgFormat = SKEncodedImageFormat.Bmp;
                        break;
                    default:
                        throw new SnNotSupportedException("Unknown image preview type: " + previewImage.Path);
                }

                var imageToSave = SKImage.FromBitmap(bitmap);
                var data = imageToSave.Encode(imgFormat, 90);
                data.SaveTo(outputStream);
            }
            finally
            {
                bitmap.Dispose();
            }

            outputStream.Seek(0, IO.SeekOrigin.Begin);
            return outputStream;
        }

        private SKBitmap Redaction(File document, Image previewImage, bool isThumbnail, SKBitmap bitmap, string shapes)
        {
            using (var canvas = new SKCanvas(bitmap))
            {
                var imageIndex = GetPreviewImagePageIndex(previewImage);
                var settings = new JsonSerializerSettings();
                var serializer = JsonSerializer.Create(settings);
                var jReader = new JsonTextReader(new IO.StringReader(shapes));
                var shapeCollection = (JArray)serializer.Deserialize(jReader);
                if (shapeCollection == null)
                    return bitmap;
                if (!shapeCollection.Any())
                    return bitmap;
                var redactionsToken = shapeCollection[0]["redactions"];
                if(redactionsToken == null)
                    return bitmap;
                var redactions = redactionsToken.Where(jt => (int)jt["imageIndex"] == imageIndex).ToList();

                var realWidthRatio = THUMBNAIL_PREVIEW_WIDTH_RATIO;
                var realHeightRatio = THUMBNAIL_PREVIEW_HEIGHT_RATIO;

                if (redactions.Any() && isThumbnail)
                {
                    // If this is a thumbnail, we will need the real preview image to determine 
                    // the page width and height ratios to draw redactions to the correct place.
                    var pi = GetPreviewImage(Content.Create(document), imageIndex);

                    if (pi != null)
                    {
                        // Compute the exact position of the shape based on the size ratio of 
                        // the real preview image and this thumbnail. 
                        realWidthRatio = (float)bitmap.Width / (float)pi.Width;
                        realHeightRatio = (float)bitmap.Height / (float)pi.Height;
                    }
                    else
                    {
                        // We could not find the main preview image that this thumbnail is 
                        // related to (maybe because it is not generated yet). Use the old 
                        // inaccurate algorithm (that builds on the default image ratios) 
                        // as a workaround.
                    }
                }

                foreach (var redaction in redactions)
                {
                    var color = SKColors.Black;
                    var shapeBrush = new SKPaint {IsAntialias = true, Color = color};
                    var x = redaction["x"]?.Value<int>() ?? 0;
                    var y = redaction["y"]?.Value<int>() ?? 0;
                    var w = redaction["w"]?.Value<int>() ?? 0;
                    var h = redaction["h"]?.Value<int>() ?? 0;

                    // there could be negative coordinates in the db, correct them here
                    NormalizeRectangle(ref x, ref y, ref w, ref h);

                    var shapeRectangle = new SKRectI(x, y, x + w, y + h);

                    // convert shape to thumbnail size if needed
                    if (isThumbnail)
                    {
                        shapeRectangle = new SKRectI(
                            (int)Math.Round(shapeRectangle.Top * realWidthRatio),
                            (int)Math.Round(shapeRectangle.Bottom * realHeightRatio),
                            (int)Math.Round(shapeRectangle.Width * realWidthRatio),
                            (int)Math.Round(shapeRectangle.Height * realHeightRatio));
                    }

                    canvas.DrawRect(shapeRectangle, shapeBrush);
                }

                canvas.Save();
            }

            return bitmap;
        }

        private SKBitmap Rotate(SKBitmap bitmap, int rotation)
        {
            SKBitmap rotated;
            SKCanvas canvas;
            switch (rotation)
            {
                case 90:
                case -270:
                    rotated = new SKBitmap(bitmap.Height, bitmap.Width);
                    canvas = new SKCanvas(rotated);
                    canvas.RotateDegrees(90);
                    canvas.DrawBitmap(bitmap, 0, -bitmap.Height);
                    break;
                case 180:
                case -180:
                    rotated = new SKBitmap(bitmap.Width, bitmap.Height);
                    canvas = new SKCanvas(rotated);
                    canvas.RotateDegrees(180);
                    canvas.DrawBitmap(bitmap, -bitmap.Width, -bitmap.Height);
                    break;
                case 270:
                case -90:
                    rotated = new SKBitmap(bitmap.Height, bitmap.Width);
                    canvas = new SKCanvas(rotated);
                    canvas.RotateDegrees(270);
                    canvas.DrawBitmap(bitmap, -bitmap.Width, 0);
                    break;
                default:
                    rotated = new SKBitmap(bitmap.Width, bitmap.Height);
                    canvas = new SKCanvas(rotated);
                    canvas.DrawBitmap(bitmap, 0, 0);
                    break;
            }

            canvas.Dispose();
            return rotated;
        }

        internal static SKColor GetWatermarkColor(string contentPath = null)
        {
            var alpha = Settings.GetValue(DOCUMENTPREVIEW_SETTINGS, WATERMARK_OPACITY, contentPath, 50);
            alpha = Math.Max(0, Math.Min(0xFF, alpha));

            var colorName = Settings.GetValue(DOCUMENTPREVIEW_SETTINGS, WATERMARK_COLOR, contentPath, "Black");
            var color = SKColors.Black;

            try
            {
                color = ColorField.ColorFromString(colorName);
            }
            catch (Exception e)
            {
                SnLog.WriteWarning($"Document preview provider: watermark color {colorName} for {contentPath} could not be converted to a color object. {e.Message}");
            }

            return new SKColor(color.Red, color.Green, color.Blue, Convert.ToByte(alpha));
        }

        private static void NormalizeRectangle(ref int x, ref int y, ref int w, ref int h)
        {
            // This methods recalculates rectangle coordinates if it has negative numbers 
            // as a height or width (because it was drawn backwards).

            // normalize width and X
            var pos = x;
            var delta = w;
            if (NormalizeCoordinates(ref pos, ref delta))
            {
                x = pos;
                w = delta;
            }

            // normalize height and Y
            pos = y;
            delta = h;
            if (NormalizeCoordinates(ref pos, ref delta))
            {
                y = pos;
                h = delta;
            }
        }

        private static bool NormalizeCoordinates(ref int pos, ref int delta)
        {
            if (delta < 0)
            {
                delta = Math.Abs(delta);
                pos = pos - delta;
                return true;
            }

            return false;
        }

        protected virtual void DrawWatermark(WatermarkDrawingInfo info)
        {
            if (string.IsNullOrEmpty(info.WatermarkText)) 
                return;

            var textSize = info.MeasureString(info.WatermarkText);
            var charCount = info.WatermarkText.Length;
            var charSize = textSize.Width / charCount;
            double maxTextWithOnImage = 0;

            info.DrawingContext.Save();
            switch (info.Position)
            {
                case WatermarkPosition.BottomLeftToUpperRight:
                    info.DrawingContext.Translate(info.Image.Width / 2.0f, info.Image.Height / 2.0f);
                    info.DrawingContext.RotateDegrees(-45);
                    maxTextWithOnImage = Math.Sqrt((info.Image.Width * info.Image.Width) + (info.Image.Height * info.Image.Height)) * 0.7;
                    break;
                case WatermarkPosition.UpperLeftToBottomRight:
                    info.DrawingContext.Translate(info.Image.Width / 2.0f, info.Image.Height / 2.0f);
                    info.DrawingContext.RotateDegrees(45);
                    maxTextWithOnImage = Math.Sqrt((info.Image.Width * info.Image.Width) + (info.Image.Height * info.Image.Height)) * 0.7;
                    break;
                case WatermarkPosition.Top:
                    maxTextWithOnImage = info.Image.Width;
                    break;
                case WatermarkPosition.Bottom:
                    maxTextWithOnImage = info.Image.Width;
                    break;
                case WatermarkPosition.Center:
                    maxTextWithOnImage = info.Image.Width;
                    break;
                default:
                    info.DrawingContext.RotateDegrees(45);
                    break;
            }

            var lines = BreakTextIntoLines(info, maxTextWithOnImage, charSize).ToList();

            DrawLines(lines, info);
            
            info.DrawingContext.Restore();
        }

        public IO.Stream GetPreviewImagesDocumentStream(Content content, DocumentFormat? documentFormat = null, RestrictionType? restrictionType = null)
        {
            if (!documentFormat.HasValue)
                documentFormat = DocumentFormat.NonDefined;

            var pImages = GetPreviewImages(content);
            return GetPreviewImagesDocumentStream(content, pImages.AsEnumerable().Select(c => c.ContentHandler as Image), documentFormat.Value, restrictionType);
        }

        protected virtual IO.Stream GetPreviewImagesDocumentStream(Content content, IEnumerable<Image> previewImages, DocumentFormat documentFormat, RestrictionType? restrictionType = null)
        {
            throw new SnNotSupportedException("Please implement PDF generator mechanism in your custom preview provider.");
        }

        protected virtual int GetPreviewImagePageIndex(Image image)
        {
            if (image == null)
                return 0;

            // preview5.png --> 5
            var r = new Regex(PREVIEW_THUMBNAIL_REGEX);
            var m = r.Match(image.Name);

            return m.Success ? Convert.ToInt32(m.Groups["page"].Value) : 0;
        }

        /// <summary>
        /// Loads or creates the appropriate previews folder for the specified version.
        /// </summary>
        /// <param name="content">The content version to load or create the previews folder for.</param>
        /// <param name="reCreate">If true, an existing previews folder will be deleted and re-created.</param>
        /// <returns>A loaded or newly created previews folder (e.g. Previews/V1.0A).</returns>
        protected virtual Node GetPreviewsFolder(Node content, bool reCreate = false)
        {
            // load or create the container of preview folders
            var previewsFolderPath = RepositoryPath.Combine(content.Path, PREVIEWS_FOLDERNAME);
            var previewsFolder = Node.Load<GenericContent>(previewsFolderPath);
            if (previewsFolder == null)
            {
                using (new SystemAccount())
                {
                    try
                    {
                        // preview folders and images should not be versioned
                        previewsFolder = new SystemFolder(content)
                        {
                            Name = PREVIEWS_FOLDERNAME,
                            VersioningMode = VersioningType.None,
                            InheritableVersioningMode = InheritableVersioningType.None
                        };

                        previewsFolder.DisableObserver(TypeResolver.GetType(NodeObserverNames.NOTIFICATION, false));
                        previewsFolder.DisableObserver(TypeResolver.GetType(NodeObserverNames.WORKFLOWNOTIFICATION, false));
                        previewsFolder.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();
                    }
                    catch (NodeAlreadyExistsException)
                    {
                        // no problem, reload to have the correct node
                        previewsFolder = Node.Load<GenericContent>(previewsFolderPath);
                    }
                }
            }

            // load, create or re-create the previews subfolder for this particular version
            var previewSubfolderName = GetPreviewsSubfolderName(content);
            var previewsSubfolderPath = RepositoryPath.Combine(previewsFolderPath, previewSubfolderName);

            using (new SystemAccount())
            {
                var previewsSubfolder = Node.LoadNode(previewsSubfolderPath);
                if (previewsSubfolder != null && reCreate)
                {
                    // if the caller wanted to re-create the folder (for cleanup reasons), we need to delete it first
                    previewsSubfolder.DisableObserver(TypeResolver.GetType(NodeObserverNames.NOTIFICATION, false));
                    previewsSubfolder.DisableObserver(TypeResolver.GetType(NodeObserverNames.WORKFLOWNOTIFICATION, false));
                    previewsSubfolder.ForceDeleteAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
                    previewsSubfolder = null;
                }

                if (previewsSubfolder == null)
                {
                    try
                    {
                        previewsSubfolder = new SystemFolder(previewsFolder) {Name = previewSubfolderName};
                        previewsSubfolder.DisableObserver(TypeResolver.GetType(NodeObserverNames.NOTIFICATION, false));
                        previewsSubfolder.DisableObserver(TypeResolver.GetType(NodeObserverNames.WORKFLOWNOTIFICATION, false));
                        previewsSubfolder.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();
                    }
                    catch (NodeAlreadyExistsException)
                    {
                        // no problem, reload to have the correct node
                        previewsSubfolder = Node.LoadNode(previewsSubfolderPath);
                    }
                }

                return previewsSubfolder;
            }
        }
        protected virtual Node EmptyPreviewsFolder(Node previews)
        {
            var gc = previews as GenericContent;
            if (gc == null)
                return null;

            using (new SystemAccount())
            {
                var parent = previews.Parent;
                var name = previews.Name;
                var type = previews.NodeType.Name;

                previews.DisableObserver(TypeResolver.GetType(NodeObserverNames.NOTIFICATION, false));
                previews.ForceDeleteAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();

                var content = Content.CreateNew(type, parent, name);
                content.ContentHandler.DisableObserver(TypeResolver.GetType(NodeObserverNames.NOTIFICATION, false));
                content.ContentHandler.DisableObserver(TypeResolver.GetType(NodeObserverNames.WORKFLOWNOTIFICATION, false));
                content.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();

                return Node.LoadNode(content.Id);
            }
        }

        /// <summary>
        /// Starts copying previously generated preview images on a new thread under a newly created version.
        /// </summary>
        /// <param name="previousVersion">Previous version.</param>
        /// <param name="currentVersion">Current version that has no preview images yet.</param>
        /// <returns>True if preview images were found and the copy operation started.</returns>
        protected internal virtual bool StartCopyingPreviewImages(Node previousVersion, Node currentVersion)
        {
            // do not throw an error here: this is an internal speedup feature
            if (previousVersion == null || currentVersion == null)
                return false;
            
            Node prevFolder;
            Node currentFolder;

            // copying preview images must not be affected by the current users permissions
            using (new SystemAccount())
            {
                prevFolder = GetPreviewsFolder(previousVersion);
                currentFolder = GetPreviewsFolder(currentVersion);
            }

            if (prevFolder == null || currentFolder == null)
                return false;

            // collect all preview and thumbnail images
            var previewType = GetPreviewImageType();
            if (previewType == null)
                return false;

            var previewIds = NodeQuery.QueryNodesByTypeAndPath(previewType, false, prevFolder.Path + RepositoryPath.PathSeparator, true).Identifiers.ToList();

            if (previewIds.Count == 0) 
                return false;

            // copy images on a new thread
            System.Threading.Tasks.Task.Run(() =>
            {
                // copying preview images must not be affected by the current users permissions
                using (new SystemAccount())
                {
                // make sure that there is no existing preview image in the target folder
                currentFolder = EmptyPreviewsFolder(currentFolder);

                var errors = new List<Exception>();

                Node.Copy(previewIds, currentFolder.Path, ref errors);
                }
            });

            return true;
        }

        /// <summary>
        /// Deletes preview images for one version or for all of them asynchronously.
        /// </summary>
        /// <param name="nodeId">Id of the content.</param>
        /// <param name="version">Version that needs preview cleanup.</param>
        /// <param name="allVersions">Whether to cleanup all preview images or just for one version.</param>
        public virtual System.Threading.Tasks.Task RemovePreviewImagesAsync(int nodeId, VersionNumber version = null, bool allVersions = false)
        {
            if (!allVersions && version == null)
                throw  new ArgumentNullException("version");

            return System.Threading.Tasks.Task.Run(() =>
            {
                using (new SystemAccount())
                {
                    var head = NodeHead.Get(nodeId);
                    if (head == null)
                        return;

                    // collect deletable versions
                    var versions = allVersions
                        ? head.Versions.Select(nv => nv.VersionNumber)
                        : new[] { version };

                    // delete previews folders
                    foreach (var nodeVersion in versions)
                    {
                        // simulate the behavior of the GetPreviewsSubfolderName method
                        var previewsPath = RepositoryPath.Combine(head.Path, PREVIEWS_FOLDERNAME, nodeVersion.ToString());

                        try
                        {
                            // existence check to avoid exceptions
                            if (Node.Exists(previewsPath))
                                Node.ForceDeleteAsync(previewsPath, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            SnLog.WriteException(ex);
                        }
                    }
                }
            });
        }

        protected virtual string GetTaskFinalizeUrl(Content content)
        {
            // a relative url that points to the finalizer action
            return RepositoryTools.OData.CreateSingleContentUrl(content, "DocumentPreviewFinalizer");
        }

        protected virtual string GetTaskTag(Content content)
        {
            return ((GenericContent)content.ContentHandler).WorkspacePath;
        }

        protected virtual string GetTaskApplicationId(Content content)
        {
            return TaskManagementOptions.GetApplicationIdOrSetting();
        }
        /// <summary>
        /// Legacy providers may customize this to determine where to send task requests.
        /// </summary>
        protected virtual string GetTaskApplicationUrl(Content content)
        {
            return TaskManagementOptions.GetApplicationUrlOrSetting();
        }

        // ===================================================================================================== Static access

        public static void InitializePreviewGeneration(Node node)
        {
            var previewProvider = DocumentPreviewProvider.Current;
            if (previewProvider == null)
                return;

            // check if the feature is enabled on the content type
            var typeName = node?.NodeType?.Name;

            // it is possible that the node type is not available yet (e.g. during installation)
            if (typeName == null)
                return;

            var contentType = ContentType.GetByName(typeName);
            if (!(contentType?.Preview ?? false))
                return;

            // check if content is supported by the provider. if not, don't bother starting the preview generation)
            if (!previewProvider.IsContentSupported(node) || previewProvider.IsPreviewOrThumbnailImage(NodeHead.Get(node.Id)))
                DocumentPreviewProvider.SetPreviewStatusWithoutSave(node as File, PreviewStatus.NotSupported);
            else if (!node.IsPreviewEnabled)
                DocumentPreviewProvider.SetPreviewStatusWithoutSave(node as File, PreviewStatus.Postponed);
        }

        public static void StartPreviewGeneration(Node node, TaskPriority priority = TaskPriority.Normal)
        {
            var previewProvider = Current;
            if (previewProvider == null)
                return;

            // check if the feature is enabled on the content type
            var content = Content.Create(node);
            if (!content.ContentType.Preview)
                return;

            // check if content is supported by the provider. if not, don't bother starting the preview generation)
            if (!previewProvider.IsPreviewEnabled(node) || previewProvider.IsPreviewOrThumbnailImage(NodeHead.Get(node.Id)))
                return;

            previewProvider.StartPreviewGenerationInternal(node, priority: priority);
        }

        private void StartPreviewGenerationInternal(Node relatedContent, int startIndex = 0, TaskPriority priority = TaskPriority.Normal)
        {
            if (this is DefaultDocumentPreviewProvider)
            {
                SnTrace.System.Write("Preview image generation is not available in this edition. No document preview provider is present.");
                return;
            }
            if (TaskManager == null)
            {
                SnTrace.System.Write("Preview image generation is not available: no task manager is present.");
                return;
            }

            string previewData;
            var content = Content.Create(relatedContent);
            var maxPreviewCount = Settings.GetValue(DOCUMENTPREVIEW_SETTINGS, MAXPREVIEWCOUNT, relatedContent.Path, 10);
            var roundedStartIndex = startIndex - startIndex % maxPreviewCount;
            var communicationUrl = GetTaskApplicationUrl(content);

            // serialize data for preview generator task (json format)
            var serializer = new JsonSerializer();
            serializer.Converters.Add(new JavaScriptDateTimeConverter());

            using (var sw = new IO.StringWriter())
            {
                using (JsonWriter writer = new JsonTextWriter(sw))
                {
                    serializer.Serialize(writer, new
                    {
                        Id = relatedContent.Id,
                        Version = relatedContent.Version.ToString(),
                        StartIndex = roundedStartIndex,
                        CommunicationUrl = communicationUrl,
                        MaxPreviewCount = maxPreviewCount,
                        DisplayName = relatedContent.DisplayName,
                        ContextPath = relatedContent.Path
                    });
                }

                previewData = sw.GetStringBuilder().ToString();
            }
            
            var taskName = GetPreviewGeneratorTaskName(relatedContent.Path);

            var requestData = new RegisterTaskRequest
            {
                Type = taskName,
                Title = GetPreviewGeneratorTaskTitle(relatedContent.Path),
                Priority = priority,
                Tag = GetTaskTag(content),
                AppId = GetTaskApplicationId(content),
                TaskData = previewData,
                FinalizeUrl = GetTaskFinalizeUrl(content)
            };

            // start generating previews only if there is a task type defined
            if (!string.IsNullOrEmpty(taskName))
            {
                // Fire and forget: we do not need the result of the register operation.
                // (we have to start a task here instead of calling RegisterTaskAsync 
                // directly because the asp.net sync context callback would fail)
                System.Threading.Tasks.Task.Run(() => TaskManager.RegisterTaskAsync(requestData, CancellationToken.None));
            }
        }

        private const string SetPreviewStatus_NotSupportedExceptionMessage = "Setting preview status to Ready is not supported. This scenario is handled by the document preview provider itself.";
        public static void SetPreviewStatus(File file, PreviewStatus status)
        {
            if (file == null)
                return;

            if (status == PreviewStatus.Ready)
                throw new NotSupportedException(SetPreviewStatus_NotSupportedExceptionMessage);

            try
            {
                SavePageCount(file, (int)status);
            }
            catch (Exception ex)
            {
                SnLog.WriteWarning("Error setting preview status. " + ex,
                    EventId.Preview,
                    properties: new Dictionary<string, object>
                    {
                        {"Path", file.Path},
                        {"Status", Enum.GetName(typeof(PreviewStatus), status)}
                    });
            }
        }
        private static void SetPreviewStatusWithoutSave(File file, PreviewStatus status)
        {
            if (file == null)
                return;

            if (status == PreviewStatus.Ready)
                throw new NotSupportedException(SetPreviewStatus_NotSupportedExceptionMessage);

            file.PageCount = (int)status;
        }

        /* ========================================================================================== OData interface */

        /// <summary>Gets all preview images for a content. If any of the images are
        /// missing or the page count is not yet determined, it starts preview
        /// generation and waits for it to complete. This may last long in case
        /// of a huge document.</summary>
        /// <param name="content"></param>
        /// <returns>The list of preview images. Thumbnail images are not included.</returns>
        [ODataFunction("GetPreviewImages", Category = "Preview", Description = "$Action,GetPreviewImages")]
        [ContentTypes(N.CT.File)]
        [AllowedRoles(N.R.Everyone)]
        [RequiredPermissions(N.P.Preview)]
        public static IEnumerable<Content> GetPreviewImagesForOData(Content content)
        {
            return Current != null ? Current.GetPreviewImages(content) : null;
        }

        /// <summary>Gets the path and image dimensions of a specific preview page of a document.
        /// If the page count for the content is available but the image does not exist, this
        /// action will start preview generation in the background (but will not wait for it).
        /// </summary>
        /// <param name="content"></param>
        /// <param name="page">A specific page number to check.</param>
        /// <returns>A custom object containing image path and dimensions.
        /// In case the image is not available yet, it will return a similar object with the
        /// PreviewAvailable property set to null.
        /// </returns>
        /// <example>
        /// <code>
        /// {
        ///    PreviewAvailable: "/Root/Content/DocLib/MyDoc.docx/Previews/1.0.A/preview1.png",
        ///    Width: 500,
        ///    Height: 800
        /// }
        /// </code>
        /// </example>
        [ODataFunction(Category = "Preview")]
        [ContentTypes(N.CT.File)]
        [AllowedRoles(N.R.Everyone)]
        public static PreviewAvailableResponse PreviewAvailable(Content content, int page)
        {
            var thumb = Current != null ? Current.GetThumbnailImage(content, page) : null;
            if (thumb != null)
            {
                var pi = Current != null ? Current.GetPreviewImage(content, page) : null;
                if (pi != null)
                {
                    return new PreviewAvailableResponse
                    {
                        PreviewAvailable = pi.Path,
                        Width = (int)pi["Width"],
                        Height = (int)pi["Height"]
                    };
                }
            }

            return new PreviewAvailableResponse { PreviewAvailable = (string)null };
        }

        public class PreviewAvailableResponse
        {
            public string PreviewAvailable { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }

        /// <summary>Gets path and dimension information for existing preview images.
        /// This action will not start preview generation, only return a list
        /// of consecutive preview images starting from the beginning.</summary>
        /// <param name="content"></param>
        /// <returns>
        /// A list of custom objects containing path and dimensions of preview images.
        /// Thumbnail images are not included.
        /// </returns>
        /// <example>
        /// <code>
        /// [
        ///     {
        ///         PreviewAvailable: "/Root/Content/DocLib/MyDoc.docx/Previews/1.0.A/preview1.png",
        ///         Width: 500,
        ///         Height: 800,
        ///         Index: 1
        ///     },
        ///     {
        ///         PreviewAvailable: "/Root/Content/DocLib/MyDoc.docx/Previews/1.0.A/preview2.png",
        ///         Width: 500,
        ///         Height: 800,
        ///         Index: 2
        ///     }
        /// ]
        /// </code>
        /// </example>
        [ODataFunction("GetExistingPreviewImages", Category = "Preview", Description = "$Action,GetExistingPreviewImages")]
        [ContentTypes(N.CT.File)]
        [AllowedRoles(N.R.Everyone)]
        public static IEnumerable<GetExistingPreviewImagesResponse> GetExistingPreviewImagesForOData(Content content)
        {
            foreach (var image in DocumentPreviewProvider.Current.GetExistingPreviewImages(content))
            {
                yield return new GetExistingPreviewImagesResponse
                {
                    PreviewAvailable = image.Path,
                    Width = (int)image["Width"],
                    Height = (int)image["Height"],
                    Index = image.Index
                };
            }
        }
        public class GetExistingPreviewImagesResponse : PreviewAvailableResponse
        {
            public int Index { get; set; }
        }

        /// <summary>Gets the number of preview pages of a document. If preview generation
        /// is not yet started, this action will start the process in the background.</summary>
        /// <remarks>
        /// In case previews are not yet available or not possible to generate them, this value will contain one of the
        /// following statuses:
        /// - NoProvider: -5
        /// - Postponed: -4
        /// - Error: -3
        /// - NotSupported: -2
        /// - InProgress: -1
        /// - EmptyDocument: 0
        /// </remarks>
        /// <param name="content"></param>
        /// <returns>Page count of a document or a status code.</returns>
        [ODataAction(Category = "Preview", Description = "Get page count")]
        [ContentTypes(N.CT.File)]
        [AllowedRoles(N.R.Everyone)]
        public static int GetPageCount(Content content)
        {
            var pageCount = (int)content["PageCount"];
            var file = content.ContentHandler as File;

            if (DocumentPreviewProvider.Current is DefaultDocumentPreviewProvider && pageCount == -4)
            {
                pageCount = (int)PreviewStatus.NoProvider;
            }
            else
            {
                if (pageCount == -4)
                {
                    // status is postponed --> set status to in progress and start preview generation
                    SetPreviewStatus(file, PreviewStatus.InProgress);

                    pageCount = (int)PreviewStatus.InProgress;
                    StartPreviewGeneration(file, TaskPriority.Immediately);
                }
                else if (pageCount == -1)
                {
                    StartPreviewGeneration(file, TaskPriority.Immediately);
                }
            }
            return pageCount;
        }

        /// <summary>Gets the id and path of the folder containing preview images
        /// for the specified target version of a content.
        /// Tha target version can be specified by the version url parameter. This
        /// action does not generate preview images.</summary>
        /// <param name="content"></param>
        /// <param name="empty">True if the preview folder should be deleted and re-created.</param>
        /// <returns>A response object containing the id and path of the folder.</returns>
        /// <example>
        /// <code>
        /// {
        ///     Id: 1234
        ///     Path: "/Root/Content/DocLib/MyDoc.docx/Previews/1.0.A"
        /// }
        /// </code>
        /// </example>
        [ODataAction(Category = "Preview", Description = "Get previews folder")]
        [ContentTypes(N.CT.File)]
        [AllowedRoles(N.R.Everyone)]
        public static GetPreviewsFolderResponse GetPreviewsFolder(Content content, bool empty)
        {
            if (content == null)
                throw new ArgumentNullException("content");

            // load, create or re-create the previews folder
            var previewsFolder = DocumentPreviewProvider.Current.GetPreviewsFolder(content.ContentHandler, empty);

            return new GetPreviewsFolderResponse
            {
                Id = previewsFolder.Id,
                Path = previewsFolder.Path
            };
        }
        public class GetPreviewsFolderResponse
        {
            public int Id { get; set; }
            public string Path { get; set; }
        }

        /// <summary>Sets the preview status if a document.
        /// Available options are the following:
        /// - NoProvider,
        /// - Postponed,
        /// - Error,
        /// - NotSupported,
        /// - InProgress,
        /// - EmptyDocument
        /// </summary>
        /// <param name="content"></param>
        /// <param name="status">Preview status value as a string.</param>
        [ODataAction(Category = "Preview", Description = "Set preview status")]
        [ContentTypes(N.CT.File)]
        [AllowedRoles(N.R.Everyone)]
        public static void SetPreviewStatus(Content content, PreviewStatus status)
        {
            if (content == null)
                throw new ArgumentNullException("content");

            SetPreviewStatus(content.ContentHandler as File, status);
        }

        /// <summary>Sets the page count of the document.
        /// This action does not generate preview images.</summary>
        /// <param name="content"></param>
        /// <param name="pageCount">Page count value</param>
        [ODataAction(Category = "Preview", Description = "Set page count")]
        [ContentTypes(N.CT.File)]
        [AllowedRoles(N.R.Everyone)]
        public static void SetPageCount(Content content, int pageCount)
        {
            if (content == null)
                throw new ArgumentNullException("content");

            SavePageCount(content.ContentHandler as File, pageCount);
        }

        /// <summary>Sets the initial security-related properties (Owner, CreatedBy, etc.)
        /// of a preview image automatically. The values come from the original document.</summary>
        /// <param name="content"></param>
        [ODataAction(Category = "Preview")]
        [ContentTypes(N.CT.PreviewImage)]
        [AllowedRoles(N.R.Everyone)]
        public static void SetInitialPreviewProperties(Content content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            if (!(content.ContentHandler is Image previewImage))
                throw new InvalidOperationException("This content is not an image.");

            var document = GetDocumentForPreviewImage(NodeHead.Get(content.Id));
            if (document == null)
                throw new ContentNotFoundException("Document not found for preview image: " + content.Path);

            var realCreatorUser = document.CreatedBy;

            // set the owner and creator/modifier user of the preview image: it should be 
            // the owner/creator of the document instead of admin
            previewImage.Owner = document.Owner;
            previewImage.CreatedBy = realCreatorUser;
            previewImage.ModifiedBy = realCreatorUser;
            previewImage.VersionCreatedBy = realCreatorUser;
            previewImage.VersionModifiedBy = realCreatorUser;
            previewImage.Index = DocumentPreviewProvider.Current.GetPreviewImagePageIndex(previewImage);

            previewImage.SaveAsync(SavingMode.KeepVersion, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>Sets the preview status of the document to In progress
        /// and starts generating preview images - regardless of existing images.</summary>
        /// <param name="content"></param>
        /// <returns>A response object containing the current page count (likely to be
        /// -1 meaning In progress and 0 as the current preview count).</returns>
        [ODataAction(Category = "Preview", Description = "Regenerate preview images")]
        [ContentTypes(N.CT.File)]
        [AllowedRoles(N.R.Everyone)]
        public static RegeneratePreviewsResponse RegeneratePreviews(Content content)
        {
            if (content == null)
                throw new ArgumentNullException("content");

            // Regardless of the current status, generate preview images again
            // (e.g. because previously there was an error) if the preview generation is enabled.
            if (content.ContentHandler.IsPreviewEnabled && content.ContentType.Preview)
            {
                SetPreviewStatus(content.ContentHandler as File, PreviewStatus.InProgress);
                StartPreviewGeneration(content.ContentHandler, TaskPriority.Immediately);
            }
            else
            {
                SetPreviewStatus(content.ContentHandler as File, PreviewStatus.Postponed);
            }

            // reload to make sure we have the latest value
            var file = Node.Load<File>(content.Id);

            return new RegeneratePreviewsResponse { PageCount = file.PageCount, PreviewCount = 0 };
        }
        public class RegeneratePreviewsResponse
        {
            public int PageCount { get; set; }
            public int PreviewCount { get; set; }
        }

        /// <summary>Checks the number of pages and preview images of a document.</summary>
        /// <param name="content"></param>
        /// <param name="generateMissing">True if preview image generation should start
        /// in case images are missing.</param>
        /// <returns>A custom object containing page and preview count.</returns>
        /// <example>
        /// <code>
        /// {
        ///     PageCount: 5,
        ///     PreviewCount: 5
        /// }
        /// </code>
        /// </example>
        [ODataAction(Category = "Preview", Description = "Check preview images")]
        [ContentTypes(N.CT.File)]
        [AllowedRoles(N.R.Everyone)]
        public static CheckPreviewsResponse CheckPreviews(Content content, bool generateMissing)
        {
            if (content == null)
                throw new ArgumentNullException("content");

            var file = content.ContentHandler as File;
            if (file == null || !Current.IsContentSupported(file) || !Current.HasPreviewPermission(NodeHead.Get(content.Id)))
                return new CheckPreviewsResponse { PageCount = 0, PreviewCount = 0 };

            // the page count is unknown yet or never will be known
            if (file.PageCount < 1)
            {
                var status = (PreviewStatus) file.PageCount;
                switch (status)
                {
                    case PreviewStatus.InProgress:
                    case PreviewStatus.Postponed:
                        if (generateMissing)
                            StartPreviewGeneration(file, TaskPriority.Immediately);
                        break;
                }

                return new CheckPreviewsResponse { PageCount = file.PageCount, PreviewCount = 0 };
            }

            // check if there is a folder for preview images
            var previewsFolder = Current.GetPreviewsFolder(file);
            if (previewsFolder == null)
                return new CheckPreviewsResponse { PageCount = file.PageCount, PreviewCount = 0 };

            // number of existing preview images
            var existingCount = QueryPreviewImages(previewsFolder.Path).Count(pi => pi.Index > 0);

            if (existingCount < file.PageCount && generateMissing)
            {
                // start a background task for iterating all page numbers and generating missing images
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        CheckPreviewImages(content, 1, file.PageCount);
                    }
                    catch (PreviewNotAvailableException)
                    {
                        // this is a background task, no need to do anything
                    }
                });
            }

            return new CheckPreviewsResponse { PageCount = file.PageCount, PreviewCount = existingCount };
        }

        public class CheckPreviewsResponse
        {
            public int PageCount { get; set; }
            public int PreviewCount { get; set; }
        }

        /// <summary>Finalizes a preview generation task for a document.
        /// This action is intended for internal use by the Task Management
        /// module.</summary>
        /// <param name="content"></param>
        /// <param name="context"></param>
        /// <param name="result">Result of the preview generation task.</param>
        [ODataAction(Category = "Preview")]
        [AllowedRoles(N.R.Everyone)]
        public static async Task DocumentPreviewFinalizer(Content content, HttpContext context, SnTaskResult result)
        {
            await (context.RequestServices.GetRequiredService<ITaskManager>())
                .OnTaskFinishedAsync(result, context.RequestAborted).ConfigureAwait(false);

            // not enough information
            if (result.Task == null || string.IsNullOrEmpty(result.Task.TaskData))
                return;

            // the task was executed successfully without an error message
            if (result.Successful && result.Error == null)
                return;

            try
            {
                // deserialize task data to retrieve content info
                dynamic previewData = JsonConvert.DeserializeObject(result.Task.TaskData);
                int nodeId = previewData.Id;

                using (new SystemAccount())
                {
                    DocumentPreviewProvider.SetPreviewStatus(Node.Load<File>(nodeId), PreviewStatus.Error);
                }
            }
            catch (Exception ex)
            {
                SnLog.WriteException(ex);
            }
        }        
    }

    /// <summary>
    /// Built-in preview provider that does not do anything. You cannot inherit from this class,
    /// inherit from <see cref="DocumentPreviewProvider"/> instead.
    /// </summary>
    public sealed class DefaultDocumentPreviewProvider : DocumentPreviewProvider
    {
        public DefaultDocumentPreviewProvider() : base(null, null) { }

        public override string GetPreviewGeneratorTaskName(string contentPath)
        {
            return string.Empty;
        }
        public override string GetPreviewGeneratorTaskTitle(string contentPath)
        {
            return string.Empty;
        }

        public override string[] GetSupportedTaskNames()
        {
            return new string[0];
        }

        public override bool IsContentSupported(Node content)
        {
            // in community edition support only files stored in libraries
            return content != null && content is File && content.ContentListId > 0;
        }
    }
}
