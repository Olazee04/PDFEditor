using Newtonsoft.Json;
using PDFEditor.Models;
using System.Text;

namespace PDFEditor.Services
{
    public interface IAiService
    {
        Task<AiOperationResult> ProcessCommandAsync(AiCommandRequest request, PdfAnalysisResult analysis);
        Task<string> ChatAsync(string message, List<ChatMessage> history, PdfAnalysisResult? context = null);
        Task<List<string>> SuggestActionsAsync(PdfAnalysisResult analysis);
    }

    public class AiService : IAiService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<AiService> _logger;
        private readonly HttpClient _http;

        public AiService(IConfiguration config, ILogger<AiService> logger, IHttpClientFactory? factory = null)
        {
            _config = config;
            _logger = logger;
            _http = new HttpClient();

            // Try both config paths
            var apiKey = config["Anthropic:ApiKey"]
                         ?? config.GetSection("Anthropic")["ApiKey"]
                         ?? "";

            _logger.LogInformation("API Key loaded: {present}", !string.IsNullOrEmpty(apiKey) ? "YES" : "NO - KEY IS MISSING");

            if (!string.IsNullOrEmpty(apiKey))
            {
                _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
                _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            }
        }
        public async Task<AiOperationResult> ProcessCommandAsync(AiCommandRequest request, PdfAnalysisResult analysis)
        {
            var systemPrompt = BuildSystemPrompt(analysis);
            var userPrompt = BuildCommandPrompt(request, analysis);

            try
            {
                var response = await CallClaudeAsync(systemPrompt, userPrompt);
                return ParseAiResponse(response, request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI command processing failed");
                return new AiOperationResult
                {
                    Success = false,
                    Message = "AI processing failed: " + ex.Message
                };
            }
        }

        public async Task<string> ChatAsync(string message, List<ChatMessage> history, PdfAnalysisResult? context = null)
        {
            var systemPrompt = @"You are an expert PDF editing assistant integrated into a professional PDF editor. 
You help users:
1. Edit text while preserving exact formatting, fonts, and layout
2. Detect and extract signatures, logos, stamps, and images
3. Analyze table structures and perform calculations
4. Identify font names, sizes, colors, and styles
5. Add/remove rows in tables while maintaining visual consistency
6. Suggest edits and explain PDF structure

Always respond in a structured, helpful way. When suggesting operations, be specific about coordinates and parameters.
When you identify an operation to perform, return it in this JSON format within <operation> tags:
<operation>
{
  ""type"": ""text_edit"" | ""image_copy"" | ""line_add"" | ""redact"" | ""calculate"" | ""font_detect"" | ""image_extract"",
  ""params"": { ... }
}
</operation>";

            if (context != null)
            {
                systemPrompt += $"\n\nCurrent document context:\n{JsonConvert.SerializeObject(new {
                    pages = context.PageInfo.TotalPages,
                    fonts = context.Fonts.Select(f => new { f.Name, f.Size }).Take(5),
                    tables = context.Tables.Count,
                    images = context.Images.Count,
                    textBlocks = context.TextBlocks.Count
                }, Formatting.Indented)}";
            }

            var messages = history.Select(h => new { role = h.Role, content = h.Content }).ToList<object>();
            messages.Add(new { role = "user", content = message });

            var payload = new
            {
                model = "claude-sonnet-4-5",
                max_tokens = 1024,
                system = systemPrompt,
                messages
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync("https://api.anthropic.com/v1/messages", content);

            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                _logger.LogError("Claude API error: {err}", err);
                return "I'm having trouble connecting to the AI service. Please check your API key in settings.";
            }

            var body = await resp.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<dynamic>(body);
            return result?.content?[0]?.text?.ToString() ?? "No response";
        }

        public async Task<List<string>> SuggestActionsAsync(PdfAnalysisResult analysis)
        {
            var suggestions = new List<string>();

            if (analysis.Tables.Count > 0)
            {
                suggestions.Add($"📊 Detected {analysis.Tables.Count} table(s). I can add rows, calculate totals, or reformat them.");
                var numericCols = analysis.Tables.FirstOrDefault()?.Rows
                    .SelectMany(r => r.Cells.Where(c => c.IsNumeric)).Any() ?? false;
                if (numericCols) suggestions.Add("🔢 Found numeric columns — I can auto-calculate totals or subtotals.");
            }

            if (analysis.Images.Any(i => i.PossiblySignature))
                suggestions.Add("✍️ Possible signature detected. I can copy it to another location.");

            if (analysis.Images.Any(i => i.PossiblyLogo))
                suggestions.Add("🏷️ Logo/stamp detected. I can duplicate or reposition it.");

            if (analysis.Fonts.Count > 0)
            {
                var primaryFont = analysis.Fonts.First();
                suggestions.Add($"🔤 Primary font: {primaryFont.Name} ({primaryFont.Size:F1}pt). I can match this when editing text.");
            }

            if (analysis.TextBlocks.Count > 0)
                suggestions.Add($"📝 Found {analysis.TextBlocks.Count} text blocks. Click any to edit inline.");

            suggestions.Add("💬 Describe what you want to do in plain English and I'll handle it.");

            return await Task.FromResult(suggestions);
        }

        private string BuildSystemPrompt(PdfAnalysisResult analysis)
        {
            return $@"You are a PDF editing AI. You have analyzed the current page and found:
- Fonts: {string.Join(", ", analysis.Fonts.Take(3).Select(f => $"{f.Name} {f.Size}pt"))}
- {analysis.TextBlocks.Count} text blocks
- {analysis.Images.Count} images ({analysis.Images.Count(i => i.PossiblySignature)} possible signatures, {analysis.Images.Count(i => i.PossiblyLogo)} possible logos)
- {analysis.Tables.Count} table(s)
- {analysis.Lines.Count} line elements

Page dimensions: {analysis.PageInfo.Width:F0} x {analysis.PageInfo.Height:F0} points

You must return a JSON operation plan. Always respond with valid JSON in this format:
{{
  ""understood"": ""<plain English summary of what the user wants>"",
  ""operation"": ""text_edit|image_copy|line_add|calculate|detect_font|extract_image|redact|none"",
  ""confidence"": 0.0-1.0,
  ""params"": {{ ... operation-specific parameters ... }},
  ""explanation"": ""<what will happen and why this preserves the document layout>""
}}";
        }

        private string BuildCommandPrompt(AiCommandRequest request, PdfAnalysisResult analysis)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"User command: \"{request.Command}\"");
            sb.AppendLine($"Page: {request.PageNumber}");

            if (!string.IsNullOrEmpty(request.SelectedRegionJson))
            {
                sb.AppendLine($"Selected region: {request.SelectedRegionJson}");
            }

            if (analysis.Tables.Count > 0)
            {
                var t = analysis.Tables[0];
                sb.AppendLine($"\nTable detected at ({t.X:F0},{t.Y:F0}), {t.Rows.Count} rows x {t.ColumnBoundaries.Count - 1} columns");
                sb.AppendLine("Row data: " + JsonConvert.SerializeObject(
                    t.Rows.Take(3).Select(r => r.Cells.Select(c => c.Text))));
            }

            return sb.ToString();
        }

        private AiOperationResult ParseAiResponse(string response, AiCommandRequest request)
        {
            try
            {
                var parsed = JsonConvert.DeserializeObject<dynamic>(response);
                if (parsed == null) throw new Exception("Empty response");

                return new AiOperationResult
                {
                    Success = true,
                    Message = parsed.understood?.ToString() ?? "Operation ready",
                    OperationType = parsed.operation?.ToString(),
                    OperationData = parsed.@params,
                    AiExplanation = parsed.explanation?.ToString()
                };
            }
            catch
            {
                return new AiOperationResult
                {
                    Success = true,
                    Message = response,
                    OperationType = "none",
                    AiExplanation = response
                };
            }
        }

        private async Task<string> CallClaudeAsync(string system, string userMessage)
        {
            var payload = new
            {
                model = "claude-sonnet-4-5",
                max_tokens = 1024,
                system,
                messages = new[] { new { role = "user", content = userMessage } }
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync("https://api.anthropic.com/v1/messages", content);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Claude API: {body}");

            var result = JsonConvert.DeserializeObject<dynamic>(body);
            return result?.content?[0]?.text?.ToString() ?? "{}";
        }
    }

    public class ChatMessage
    {
        public string Role { get; set; } = "user";
        public string Content { get; set; } = "";
    }

    // ─── Stub services ───────────────────────────────────────────────────────────

    public interface IFontDetectionService
    {
        Task<List<PDFEditor.Models.FontInfo>> DetectFontsAsync(string filePath);
    }

    public class FontDetectionService : IFontDetectionService
    {
        private readonly IPdfService _pdf;
        public FontDetectionService(IPdfService pdf) => _pdf = pdf;

        public async Task<List<PDFEditor.Models.FontInfo>> DetectFontsAsync(string filePath)
        {
            var analysis = await _pdf.AnalyzePdfAsync(filePath);
            return analysis.Fonts;
        }
    }

    public interface IImageExtractionService
    {
        Task<List<ImageRegion>> ExtractAsync(string filePath, int page);
    }

    public class ImageExtractionService : IImageExtractionService
    {
        private readonly IPdfService _pdf;
        public ImageExtractionService(IPdfService pdf) => _pdf = pdf;

        public async Task<List<ImageRegion>> ExtractAsync(string filePath, int page) =>
            await _pdf.ExtractImagesAsync(filePath, page);
    }

    public interface ITableAnalysisService
    {
        Task<TableStructure?> AnalyzeAsync(string filePath, int page, float x, float y);
    }

    public class TableAnalysisService : ITableAnalysisService
    {
        private readonly IPdfService _pdf;
        public TableAnalysisService(IPdfService pdf) => _pdf = pdf;

        public async Task<TableStructure?> AnalyzeAsync(string filePath, int page, float x, float y) =>
            await _pdf.DetectTableAsync(filePath, page, x, y);
    }
}
