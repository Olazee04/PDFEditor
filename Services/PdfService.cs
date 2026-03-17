using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Geom;
using iText.Kernel.Font;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Kernel.Colors;
using iText.Kernel.Pdf.Canvas;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.IO.Image;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Newtonsoft.Json;
using System.Text;

// Aliases to avoid conflict between our PdfDocument model and iText's PdfDocument
using iTextRectangle = iText.Kernel.Geom.Rectangle;
using AppPdfDocument = PDFEditor.Models.PdfDocument;

namespace PDFEditor.Services
{
    public interface IPdfService
    {
        Task<PDFEditor.Models.PdfAnalysisResult> AnalyzePdfAsync(string filePath, int pageNumber = 1);
        Task<byte[]> EditTextAsync(string filePath, PDFEditor.Models.TextEditRequest request);
        Task<byte[]> CopyImageRegionAsync(string filePath, PDFEditor.Models.ImageCopyRequest request);
        Task<byte[]> AddTableRowAsync(string filePath, PDFEditor.Models.LineAddRequest request);
        Task<byte[]> ExtractPageAsImageAsync(string filePath, int pageNumber, int dpi = 150);
        Task<string> SaveEditedPdfAsync(byte[] pdfBytes, string originalPath);
        Task<List<PDFEditor.Models.ImageRegion>> ExtractImagesAsync(string filePath, int pageNumber);
        Task<PDFEditor.Models.TableStructure?> DetectTableAsync(string filePath, int pageNumber, float x, float y);
        Task<byte[]> CalculateAndFillTableTotalsAsync(string filePath, int pageNumber, int tableIndex);
        Task<byte[]> RedactRegionAsync(string filePath, int pageNumber, float x, float y, float w, float h);
        Task<byte[]> MergeAnnotationsAsync(string filePath, List<AnnotationData> annotations);
    }

    public class PdfService : IPdfService
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<PdfService> _logger;

        public PdfService(IWebHostEnvironment env, ILogger<PdfService> logger)
        {
            _env = env;
            _logger = logger;
        }

        public async Task<PDFEditor.Models.PdfAnalysisResult> AnalyzePdfAsync(string filePath, int pageNumber = 1)
        {
            return await Task.Run(() =>
            {
                var result = new PDFEditor.Models.PdfAnalysisResult();
                using var reader = new PdfReader(filePath);
                using var pdfDoc = new PdfDocument(reader);

                int totalPages = pdfDoc.GetNumberOfPages();
                var page = pdfDoc.GetPage(pageNumber);
                var pageSize = page.GetPageSize();

                result.PageInfo = new PDFEditor.Models.PageInfo
                {
                    Width = pageSize.GetWidth(),
                    Height = pageSize.GetHeight(),
                    PageNumber = pageNumber,
                    TotalPages = totalPages
                };

                try
                {
                    var strategy = new PreciseTextExtractionStrategy();
                    PdfTextExtractor.GetTextFromPage(page, strategy);
                    result.TextBlocks = strategy.GetTextBlocks(pageNumber);
                    result.Fonts = strategy.GetFonts();
                    result.Images = ExtractImageRegionsFromPage(page, pageNumber);
                    result.Lines = ExtractLinesFromPage(page, pageNumber);
                    result.Tables = DetectTablesFromLines(result.Lines, result.TextBlocks, pageNumber, pageSize);

                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Text extraction skipped for page {page}: {msg}",
                        pageNumber, ex.Message);
                    result.TextBlocks = new List<PDFEditor.Models.TextBlock>();
                    result.Fonts = new List<PDFEditor.Models.FontInfo>();
                }

                return result;
            });
        }

        private List<PDFEditor.Models.ImageRegion> ExtractImageRegionsFromPage(PdfPage page, int pageNumber)
        {
            var images = new List<PDFEditor.Models.ImageRegion>();
            var resources = page.GetResources();

            // Get XObject names safely
            var xObjectDict = resources.GetResource(PdfName.XObject);
            if (xObjectDict == null) return images;

            var xObjectPdfDict = xObjectDict as PdfDictionary;
            if (xObjectPdfDict == null) return images;

            int idx = 0;
            foreach (var entry in xObjectPdfDict.EntrySet())
            {
                var xObj = entry.Value;
                if (xObj is PdfStream stream)
                {
                    var subtype = stream.GetAsName(PdfName.Subtype);
                    if (PdfName.Image.Equals(subtype))
                    {
                        var region = new PDFEditor.Models.ImageRegion
                        {
                            Index = idx++,
                            PageNumber = pageNumber,
                            ImageType = "raster",
                            X = 0,
                            Y = 0,
                            Width = 100,
                            Height = 100
                        };

                        var w = stream.GetAsNumber(PdfName.Width);
                        var h = stream.GetAsNumber(PdfName.Height);
                        if (w != null) region.Width = w.FloatValue();
                        if (h != null) region.Height = h.FloatValue();

                        region.PossiblySignature = region.Width < 200 && region.Height < 100;
                        region.PossiblyLogo = region.Width < 300 && region.Height < 150 && !region.PossiblySignature;

                        try
                        {
                            var bytes = stream.GetBytes();
                            if (bytes != null && bytes.Length > 0)
                                region.Base64Preview = Convert.ToBase64String(bytes.Take(4096).ToArray());
                        }
                        catch { }

                        images.Add(region);
                    }
                }
            }
            return images;
        }

        private List<PDFEditor.Models.LineElement> ExtractLinesFromPage(PdfPage page, int pageNumber)
        {
            var lines = new List<PDFEditor.Models.LineElement>();
            try
            {
                var contentBytes = page.GetContentBytes();
                var content = Encoding.Latin1.GetString(contentBytes);
                var linePattern = new System.Text.RegularExpressions.Regex(
                    @"([\d.]+)\s+([\d.]+)\s+m\s+([\d.]+)\s+([\d.]+)\s+l");

                foreach (System.Text.RegularExpressions.Match m in linePattern.Matches(content))
                {
                    if (float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float x1) &&
                        float.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float y1) &&
                        float.TryParse(m.Groups[3].Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float x2) &&
                        float.TryParse(m.Groups[4].Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float y2))
                    {
                        lines.Add(new PDFEditor.Models.LineElement
                        {
                            X1 = x1,
                            Y1 = y1,
                            X2 = x2,
                            Y2 = y2,
                            LineWidth = 0.5f,
                            Color = "#000000",
                            PageNumber = pageNumber
                        });
                    }
                }
            }
            catch { }
            return lines;
        }

        private List<PDFEditor.Models.TableStructure> DetectTablesFromLines(
            List<PDFEditor.Models.LineElement> lines,
            List<PDFEditor.Models.TextBlock> textBlocks,
            int pageNumber, iTextRectangle pageSize)
        {
            var tables = new List<PDFEditor.Models.TableStructure>();
            if (lines.Count < 4) return tables;

            var hLines = lines.Where(l => Math.Abs(l.Y1 - l.Y2) < 2).OrderBy(l => l.Y1).ToList();
            var vLines = lines.Where(l => Math.Abs(l.X1 - l.X2) < 2).OrderBy(l => l.X1).ToList();

            if (hLines.Count < 2 || vLines.Count < 2) return tables;

            float tolerance = 5f;
            var yLevels = ClusterValues(hLines.Select(l => l.Y1).ToList(), tolerance);
            var xBounds = ClusterValues(vLines.Select(l => l.X1).ToList(), tolerance);

            if (yLevels.Count < 2 || xBounds.Count < 2) return tables;

            float tableX = xBounds.Min();
            float tableY = yLevels.Min();
            float tableWidth = xBounds.Max() - tableX;
            float tableHeight = yLevels.Max() - tableY;

            var table = new PDFEditor.Models.TableStructure
            {
                TableIndex = 0,
                X = tableX,
                Y = tableY,
                Width = tableWidth,
                Height = tableHeight,
                PageNumber = pageNumber,
                ColumnBoundaries = xBounds
            };

            for (int r = 0; r < yLevels.Count - 1; r++)
            {
                float rowY = yLevels[r];
                float rowH = yLevels[r + 1] - rowY;

                var row = new PDFEditor.Models.TableRow
                {
                    RowIndex = r,
                    Y = rowY,
                    Height = rowH,
                    HasTopBorder = true,
                    HasBottomBorder = true
                };

                for (int c = 0; c < xBounds.Count - 1; c++)
                {
                    float cellX = xBounds[c];
                    float cellW = xBounds[c + 1] - cellX;

                    var cellText = textBlocks.Where(tb =>
                        tb.PageNumber == pageNumber &&
                        tb.X >= cellX - 5 && tb.X <= cellX + cellW &&
                        tb.Y >= rowY - 5 && tb.Y <= rowY + rowH + 5
                    ).Select(tb => tb.Text).FirstOrDefault() ?? "";

                    decimal? numVal = null;
                    bool isNum = false;
                    if (decimal.TryParse(cellText.Replace(",", "").Replace("$", "").Trim(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out decimal parsed))
                    {
                        numVal = parsed;
                        isNum = true;
                    }

                    row.Cells.Add(new PDFEditor.Models.TableCell
                    {
                        ColIndex = c,
                        Text = cellText,
                        X = cellX,
                        Width = cellW,
                        IsNumeric = isNum,
                        NumericValue = numVal
                    });
                }
                table.Rows.Add(row);
            }

            tables.Add(table);
            return tables;
        }

        private List<float> ClusterValues(List<float> values, float tolerance)
        {
            var sorted = values.OrderBy(v => v).ToList();
            var clusters = new List<float>();
            if (!sorted.Any()) return clusters;

            float current = sorted[0];
            float sum = current;
            int count = 1;

            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i] - current <= tolerance)
                {
                    sum += sorted[i];
                    count++;
                    current = sorted[i];
                }
                else
                {
                    clusters.Add(sum / count);
                    current = sorted[i];
                    sum = current;
                    count = 1;
                }
            }
            clusters.Add(sum / count);
            return clusters;
        }

        public async Task<byte[]> EditTextAsync(string filePath, PDFEditor.Models.TextEditRequest request)
        {
            return await Task.Run(() =>
            {
                using var ms = new MemoryStream();
                using (var reader = new PdfReader(filePath))
                using (var writer = new PdfWriter(ms))
                using (var pdfDoc = new PdfDocument(reader, writer))
                {
                    var page = pdfDoc.GetPage(request.PageNumber);
                    var pageHeight = page.GetPageSize().GetHeight();

                    var canvas = new PdfCanvas(page);
                    canvas.SaveState();
                    canvas.SetFillColor(ColorConstants.WHITE);
                    canvas.Rectangle(request.X, pageHeight - request.Y - request.Height,
                        request.Width, request.Height);
                    canvas.Fill();
                    canvas.RestoreState();

                    using var layoutDoc = new Document(pdfDoc);
                    PdfFont font;
                    try
                    {
                        font = PdfFontFactory.CreateFont(
                            string.IsNullOrEmpty(request.FontName)
                                ? StandardFonts.HELVETICA : request.FontName,
                            PdfEncodings.WINANSI,
                            PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
                    }
                    catch
                    {
                        font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
                    }

                    var para = new Paragraph(request.NewText)
                        .SetFont(font)
                        .SetFontSize(request.FontSize > 0 ? request.FontSize : 10f)
                        .SetFixedPosition(request.PageNumber,
                            request.X,
                            pageHeight - request.Y - request.Height,
                            request.Width);

                    if (!string.IsNullOrEmpty(request.FontColor) && request.FontColor.StartsWith("#"))
                    {
                        try
                        {
                            var hex = request.FontColor.TrimStart('#');
                            int r2 = Convert.ToInt32(hex.Substring(0, 2), 16);
                            int g2 = Convert.ToInt32(hex.Substring(2, 2), 16);
                            int b2 = Convert.ToInt32(hex.Substring(4, 2), 16);
                            para.SetFontColor(new DeviceRgb(r2, g2, b2));
                        }
                        catch { }
                    }

                    layoutDoc.Add(para);
                }
                return ms.ToArray();
            });
        }

        public async Task<byte[]> CopyImageRegionAsync(string filePath, PDFEditor.Models.ImageCopyRequest request)
        {
            return await Task.Run(() =>
            {
                using var ms = new MemoryStream();
                using (var reader = new PdfReader(filePath))
                using (var writer = new PdfWriter(ms))
                using (var pdfDoc = new PdfDocument(reader, writer))
                {
                    var srcPage = pdfDoc.GetPage(request.SourcePage);
                    var dstPage = pdfDoc.GetPage(request.TargetPage);
                    var dstPageHeight = dstPage.GetPageSize().GetHeight();

                    var xobj = srcPage.CopyAsFormXObject(pdfDoc);
                    var canvas = new PdfCanvas(dstPage);

                    float scaleX = request.DstWidth / request.SrcWidth;
                    float scaleY = request.DstHeight / request.SrcHeight;
                    float dstY = dstPageHeight - request.DstY - request.DstHeight;

                    canvas.SaveState();
                    canvas.Rectangle(request.DstX, dstY, request.DstWidth, request.DstHeight);
                    canvas.Clip();
                    canvas.EndPath();

                    var matrix = new AffineTransform(scaleX, 0, 0, scaleY,
                        request.DstX - request.SrcX * scaleX,
                        dstY - (srcPage.GetPageSize().GetHeight() - request.SrcY - request.SrcHeight) * scaleY);
                    canvas.ConcatMatrix(matrix);
                    canvas.AddXObjectAt(xobj, 0, 0);
                    canvas.RestoreState();
                }
                return ms.ToArray();
            });
        }

        public async Task<byte[]> AddTableRowAsync(string filePath, PDFEditor.Models.LineAddRequest request)
        {
            return await Task.Run(() =>
            {
                using var ms = new MemoryStream();
                using (var reader = new PdfReader(filePath))
                using (var writer = new PdfWriter(ms))
                using (var pdfDoc = new PdfDocument(reader, writer))
                {
                    var page = pdfDoc.GetPage(request.PageNumber);
                    var pageHeight = page.GetPageSize().GetHeight();
                    var pageWidth = page.GetPageSize().GetWidth();
                    var canvas = new PdfCanvas(page);

                    float refY = pageHeight - request.ReferenceRowY;
                    float rowHeight = 20f;
                    float insertY = request.InsertAbove ? refY + rowHeight : refY - rowHeight;

                    canvas.SaveState();
                    canvas.SetStrokeColor(ColorConstants.BLACK);
                    canvas.SetLineWidth(0.5f);
                    canvas.MoveTo(50, insertY);
                    canvas.LineTo(pageWidth - 50, insertY);
                    canvas.Stroke();
                    canvas.RestoreState();

                    if (!string.IsNullOrEmpty(request.RowDataJson))
                    {
                        var cells = JsonConvert.DeserializeObject<List<string>>(request.RowDataJson);
                        if (cells != null)
                        {
                            using var layoutDoc = new Document(pdfDoc);
                            var font2 = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
                            float colWidth = (pageWidth - 100) / cells.Count;
                            for (int i = 0; i < cells.Count; i++)
                            {
                                var txt = new Paragraph(cells[i])
                                    .SetFont(font2).SetFontSize(9f)
                                    .SetFixedPosition(request.PageNumber,
                                        50 + i * colWidth, insertY + 3, colWidth);
                                layoutDoc.Add(txt);
                            }
                        }
                    }
                }
                return ms.ToArray();
            });
        }

        public async Task<byte[]> CalculateAndFillTableTotalsAsync(string filePath, int pageNumber, int tableIndex)
        {
            var analysis = await AnalyzePdfAsync(filePath, pageNumber);
            if (tableIndex >= analysis.Tables.Count)
                throw new InvalidOperationException("Table not found");

            var table = analysis.Tables[tableIndex];
            var totals = new Dictionary<int, decimal>();

            foreach (var row in table.Rows)
                foreach (var cell in row.Cells.Where(c => c.IsNumeric && c.NumericValue.HasValue))
                {
                    if (!totals.ContainsKey(cell.ColIndex)) totals[cell.ColIndex] = 0;
                    totals[cell.ColIndex] += cell.NumericValue!.Value;
                }

            return await Task.Run(() =>
            {
                using var ms = new MemoryStream();
                using (var reader = new PdfReader(filePath))
                using (var writer = new PdfWriter(ms))
                using (var pdfDoc = new PdfDocument(reader, writer))
                using (var layoutDoc = new Document(pdfDoc))
                {
                    var page = pdfDoc.GetPage(pageNumber);
                    var pageHeight = page.GetPageSize().GetHeight();
                    var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

                    float totalsY = table.Y + table.Height + 5;

                    foreach (var (colIdx, total) in totals)
                    {
                        if (colIdx < table.ColumnBoundaries.Count - 1)
                        {
                            float colX = table.ColumnBoundaries[colIdx];
                            float colW = table.ColumnBoundaries[colIdx + 1] - colX;

                            var para = new Paragraph(total.ToString("N2"))
                                .SetFont(font).SetFontSize(9f)
                                .SetFixedPosition(pageNumber, colX,
                                    pageHeight - totalsY - 15, colW)
                                .SetTextAlignment(TextAlignment.RIGHT);
                            layoutDoc.Add(para);
                        }
                    }
                }
                return ms.ToArray();
            });
        }

        public async Task<byte[]> ExtractPageAsImageAsync(string filePath, int pageNumber, int dpi = 150)
        {
            return await Task.Run(() =>
            {
                using var ms = new MemoryStream();
                int width = (int)(8.5 * dpi);
                int height = (int)(11 * dpi);
                using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
                img.Mutate(ctx => ctx.BackgroundColor(SixLabors.ImageSharp.Color.White));
                img.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
                return ms.ToArray();
            });
        }

        public async Task<string> SaveEditedPdfAsync(byte[] pdfBytes, string originalPath)
        {
            var dir = System.IO.Path.GetDirectoryName(originalPath)!;
            var fname = System.IO.Path.GetFileNameWithoutExtension(originalPath);
            var newPath = System.IO.Path.Combine(dir, $"{fname}_edited_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf");
            await File.WriteAllBytesAsync(newPath, pdfBytes);
            return newPath;
        }

        public async Task<List<PDFEditor.Models.ImageRegion>> ExtractImagesAsync(string filePath, int pageNumber)
        {
            var analysis = await AnalyzePdfAsync(filePath, pageNumber);
            return analysis.Images;
        }

        public async Task<PDFEditor.Models.TableStructure?> DetectTableAsync(string filePath, int pageNumber, float x, float y)
        {
            var analysis = await AnalyzePdfAsync(filePath, pageNumber);
            return analysis.Tables.FirstOrDefault(t =>
                x >= t.X && x <= t.X + t.Width &&
                y >= t.Y && y <= t.Y + t.Height);
        }

        public async Task<byte[]> RedactRegionAsync(string filePath, int pageNumber, float x, float y, float w, float h)
        {
            return await Task.Run(() =>
            {
                using var ms = new MemoryStream();
                using (var reader = new PdfReader(filePath))
                using (var writer = new PdfWriter(ms))
                using (var pdfDoc = new PdfDocument(reader, writer))
                {
                    var page = pdfDoc.GetPage(pageNumber);
                    var pageHeight = page.GetPageSize().GetHeight();
                    var canvas = new PdfCanvas(page);

                    canvas.SaveState();
                    canvas.SetFillColor(ColorConstants.BLACK);
                    canvas.Rectangle(x, pageHeight - y - h, w, h);
                    canvas.Fill();
                    canvas.RestoreState();
                }
                return ms.ToArray();
            });
        }

        public async Task<byte[]> MergeAnnotationsAsync(string filePath, List<AnnotationData> annotations)
        {
            return await Task.Run(() =>
            {
                using var ms = new MemoryStream();
                using (var reader = new PdfReader(filePath))
                using (var writer = new PdfWriter(ms))
                using (var pdfDoc = new PdfDocument(reader, writer))
                using (var layoutDoc = new Document(pdfDoc))
                {
                    var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
                    foreach (var ann in annotations)
                    {
                        var page = pdfDoc.GetPage(ann.PageNumber);
                        float pageHeight = page.GetPageSize().GetHeight();

                        if (ann.Type == "text")
                        {
                            var para = new Paragraph(ann.Content)
                                .SetFont(font)
                                .SetFontSize(ann.FontSize > 0 ? ann.FontSize : 10f)
                                .SetFontColor(new DeviceRgb(0, 0, 0))
                                .SetFixedPosition(ann.PageNumber, ann.X,
                                    pageHeight - ann.Y - 20, ann.Width);
                            layoutDoc.Add(para);
                        }
                        else if (ann.Type == "highlight")
                        {
                            var canvas = new PdfCanvas(page);
                            canvas.SaveState();
                            canvas.SetFillColor(new DeviceRgb(255, 255, 0));
                            canvas.Rectangle(ann.X, pageHeight - ann.Y - ann.Height,
                                ann.Width, ann.Height);
                            canvas.Fill();
                            canvas.RestoreState();
                        }
                    }
                }
                return ms.ToArray();
            });
        }
    }

    public class AnnotationData
    {
        public int PageNumber { get; set; }
        public string Type { get; set; } = "text";
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public string Content { get; set; } = "";
        public float FontSize { get; set; }
        public string Color { get; set; } = "#000000";
    }

    public class PreciseTextExtractionStrategy : ITextExtractionStrategy
    {
        private readonly List<TextChunk> _chunks = new();
        private readonly Dictionary<string, int> _fontCount = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT) return;
            var renderInfo = (TextRenderInfo)data;
            var text = renderInfo.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;

            var font = renderInfo.GetFont();
            var fontName = font?.GetFontProgram()?.GetFontNames()?.GetFontName() ?? "Unknown";
            var fontSize = renderInfo.GetFontSize();
            var baseline = renderInfo.GetBaseline();
            var ascentLine = renderInfo.GetAscentLine();

            var start = baseline.GetStartPoint();
            var end = baseline.GetEndPoint();

            _chunks.Add(new TextChunk
            {
                Text = text,
                X = start.Get(0),
                Y = start.Get(1),
                Width = Math.Abs(end.Get(0) - start.Get(0)),
                Height = Math.Abs(ascentLine.GetStartPoint().Get(1)
                          - baseline.GetStartPoint().Get(1)) + 2,
                FontName = fontName,
                FontSize = fontSize,
                Color = "#000000"
            });

            if (!_fontCount.ContainsKey(fontName)) _fontCount[fontName] = 0;
            _fontCount[fontName]++;
        }

        public string GetResultantText() =>
            string.Join(" ", _chunks.Select(c => c.Text));

        public ICollection<EventType> GetSupportedEvents() =>
            new HashSet<EventType> { EventType.RENDER_TEXT };

        public List<PDFEditor.Models.TextBlock> GetTextBlocks(int pageNumber) =>
            _chunks.Select(c => new PDFEditor.Models.TextBlock
            {
                Text = c.Text,
                X = c.X,
                Y = c.Y,
                Width = Math.Max(c.Width, 5),
                Height = Math.Max(c.Height, 10),
                FontName = c.FontName,
                FontSize = c.FontSize,
                Color = c.Color,
                PageNumber = pageNumber
            }).ToList();

        public List<PDFEditor.Models.FontInfo> GetFonts() =>
            _fontCount.Select(kv => new PDFEditor.Models.FontInfo
            {
                Name = kv.Key,
                Size = _chunks.Where(c => c.FontName == kv.Key)
                              .Select(c => c.FontSize)
                              .DefaultIfEmpty(10f).Average(),
                UsageCount = kv.Value
            }).OrderByDescending(f => f.UsageCount).ToList();

        private class TextChunk
        {
            public string Text { get; set; } = "";
            public float X { get; set; }
            public float Y { get; set; }
            public float Width { get; set; }
            public float Height { get; set; }
            public string FontName { get; set; } = "";
            public float FontSize { get; set; }
            public string Color { get; set; } = "#000000";
        }
    }
}