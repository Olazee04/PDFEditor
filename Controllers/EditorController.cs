using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PDFEditor.Data;
using PDFEditor.Models;
using PDFEditor.Services;
using Newtonsoft.Json;

namespace PDFEditor.Controllers
{
    public class EditorController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IPdfService _pdf;
        private readonly IAiService _ai;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<EditorController> _logger;

        public EditorController(AppDbContext db, IPdfService pdf, IAiService ai,
            IWebHostEnvironment env, ILogger<EditorController> logger)
        {
            _db = db; _pdf = pdf; _ai = ai; _env = env; _logger = logger;
        }

        public async Task<IActionResult> Edit(int id)
        {
            var doc = await _db.PdfDocuments.FindAsync(id);
            if (doc == null) return NotFound();
            ViewBag.Document = doc;
            return View(doc);
        }

        [HttpGet]
        public async Task<IActionResult> GetPdf(int id)
        {
            var doc = await _db.PdfDocuments.FindAsync(id);
            if (doc == null || !System.IO.File.Exists(doc.StoragePath))
                return NotFound();

            var bytes = await System.IO.File.ReadAllBytesAsync(doc.StoragePath);
            return File(bytes, "application/pdf");
        }

        [HttpGet]
        public async Task<IActionResult> Analyze(int id, int page = 1)
        {
            var doc = await _db.PdfDocuments.FindAsync(id);
            if (doc == null) return NotFound();

            try
            {
                var analysis = await _pdf.AnalyzePdfAsync(doc.StoragePath, page);
                var suggestions = await _ai.SuggestActionsAsync(analysis);
                return Json(new { success = true, analysis, suggestions });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Analysis failed for doc {id}", id);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AiCommand([FromBody] AiCommandRequest request)
        {
            var doc = await _db.PdfDocuments.FindAsync(request.DocumentId);
            if (doc == null) return NotFound();

            try
            {
                var analysis = await _pdf.AnalyzePdfAsync(doc.StoragePath, request.PageNumber);
                var result = await _ai.ProcessCommandAsync(request, analysis);

                if (result.Success && result.OperationType != "none" && result.OperationData != null)
                {
                    await LogOperation(doc.Id, request.PageNumber, result.OperationType!,
                        JsonConvert.SerializeObject(result.OperationData), request.Command);
                }

                return Json(result);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Chat([FromBody] ChatRequest request)
        {
            var doc = request.DocumentId > 0
                ? await _db.PdfDocuments.FindAsync(request.DocumentId)
                : null;

            PdfAnalysisResult? analysis = null;
            if (doc != null)
            {
                try { analysis = await _pdf.AnalyzePdfAsync(doc.StoragePath, request.PageNumber); }
                catch { }
            }

            var history = request.History ?? new List<ChatMessage>();
            var response = await _ai.ChatAsync(request.Message, history, analysis);

            _db.AiConversations.Add(new AiConversation
            {
                DocumentId = doc?.Id,
                Role = "user",
                Content = request.Message,
                CreatedAt = DateTime.UtcNow
            });
            _db.AiConversations.Add(new AiConversation
            {
                DocumentId = doc?.Id,
                Role = "assistant",
                Content = response,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            return Json(new { success = true, response });
        }

        [HttpPost]
        public async Task<IActionResult> EditText([FromBody] TextEditRequest request)
        {
            var doc = await _db.PdfDocuments.FindAsync(request.DocumentId);
            if (doc == null) return NotFound();

            try
            {
                await CreateVersionSnapshot(doc);
                var edited = await _pdf.EditTextAsync(doc.StoragePath, request);
                await System.IO.File.WriteAllBytesAsync(doc.StoragePath, edited);
                doc.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await LogOperation(doc.Id, request.PageNumber, "text_edit",
                    JsonConvert.SerializeObject(request), null);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CopyImage([FromBody] ImageCopyRequest request)
        {
            var doc = await _db.PdfDocuments.FindAsync(request.DocumentId);
            if (doc == null) return NotFound();

            try
            {
                await CreateVersionSnapshot(doc);
                var edited = await _pdf.CopyImageRegionAsync(doc.StoragePath, request);
                await System.IO.File.WriteAllBytesAsync(doc.StoragePath, edited);
                doc.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await LogOperation(doc.Id, request.SourcePage, "image_copy",
                    JsonConvert.SerializeObject(request), null);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddRow([FromBody] LineAddRequest request)
        {
            var doc = await _db.PdfDocuments.FindAsync(request.DocumentId);
            if (doc == null) return NotFound();

            try
            {
                await CreateVersionSnapshot(doc);
                var edited = await _pdf.AddTableRowAsync(doc.StoragePath, request);
                await System.IO.File.WriteAllBytesAsync(doc.StoragePath, edited);
                doc.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await LogOperation(doc.Id, request.PageNumber, "row_add",
                    JsonConvert.SerializeObject(request), null);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CalculateTotals([FromBody] CalcRequest request)
        {
            var doc = await _db.PdfDocuments.FindAsync(request.DocumentId);
            if (doc == null) return NotFound();

            try
            {
                await CreateVersionSnapshot(doc);
                var edited = await _pdf.CalculateAndFillTableTotalsAsync(
                    doc.StoragePath, request.PageNumber, request.TableIndex);
                await System.IO.File.WriteAllBytesAsync(doc.StoragePath, edited);
                doc.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Redact([FromBody] RedactRequest request)
        {
            var doc = await _db.PdfDocuments.FindAsync(request.DocumentId);
            if (doc == null) return NotFound();

            try
            {
                await CreateVersionSnapshot(doc);
                var edited = await _pdf.RedactRegionAsync(doc.StoragePath,
                    request.PageNumber, request.X, request.Y, request.W, request.H);
                await System.IO.File.WriteAllBytesAsync(doc.StoragePath, edited);
                doc.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SaveAnnotations([FromBody] SaveAnnotationsRequest request)
        {
            var doc = await _db.PdfDocuments.FindAsync(request.DocumentId);
            if (doc == null) return NotFound();

            try
            {
                await CreateVersionSnapshot(doc);
                var edited = await _pdf.MergeAnnotationsAsync(doc.StoragePath, request.Annotations);
                await System.IO.File.WriteAllBytesAsync(doc.StoragePath, edited);
                doc.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Download(int id)
        {
            var doc = await _db.PdfDocuments.FindAsync(id);
            if (doc == null || !System.IO.File.Exists(doc.StoragePath))
                return NotFound();

            var bytes = await System.IO.File.ReadAllBytesAsync(doc.StoragePath);
            var name = Path.GetFileNameWithoutExtension(doc.FileName) + "_edited.pdf";
            return File(bytes, "application/pdf", name);
        }

        [HttpPost]
        public async Task<IActionResult> RestoreVersion(int documentId, int versionId)
        {
            var doc = await _db.PdfDocuments.FindAsync(documentId);
            var ver = await _db.DocumentVersions.FindAsync(versionId);
            if (doc == null || ver == null) return NotFound();

            if (System.IO.File.Exists(ver.StoragePath))
            {
                System.IO.File.Copy(ver.StoragePath, doc.StoragePath, true);
                doc.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Version file missing" });
        }

        [HttpGet]
        public async Task<IActionResult> Versions(int id)
        {
            var versions = await _db.DocumentVersions
                .Where(v => v.DocumentId == id)
                .OrderByDescending(v => v.VersionNumber)
                .ToListAsync();
            return Json(versions);
        }

        [HttpGet]
        public async Task<IActionResult> OperationsLog(int id)
        {
            var sessions = await _db.EditSessions
                .Where(s => s.DocumentId == id)
                .Include(s => s.Operations)
                .OrderByDescending(s => s.StartedAt)
                .Take(5)
                .ToListAsync();

            var ops = sessions.SelectMany(s => s.Operations)
                .OrderByDescending(o => o.CreatedAt)
                .Take(50)
                .Select(o => new {
                    o.Id,
                    o.OperationType,
                    o.PageNumber,
                    o.AiInstruction,
                    o.CreatedAt,
                    o.IsReverted
                });
            return Json(ops);
        }

        private async Task CreateVersionSnapshot(PdfDocument doc)
        {
            var versionCount = await _db.DocumentVersions.CountAsync(v => v.DocumentId == doc.Id);
            var versionDir = Path.Combine(_env.WebRootPath, "versions", doc.Id.ToString());
            Directory.CreateDirectory(versionDir);

            var vPath = Path.Combine(versionDir, $"v{versionCount + 1}.pdf");
            System.IO.File.Copy(doc.StoragePath, vPath, true);

            _db.DocumentVersions.Add(new DocumentVersion
            {
                DocumentId = doc.Id,
                VersionNumber = versionCount + 1,
                StoragePath = vPath,
                Label = $"Version {versionCount + 1}",
                CreatedAt = DateTime.UtcNow,
                ChangesSummary = "Auto-saved before edit"
            });
            await _db.SaveChangesAsync();
        }

        private async Task LogOperation(int docId, int page, string type, string data, string? aiInstruction)
        {
            var session = await _db.EditSessions
                .Where(s => s.DocumentId == docId && s.EndedAt == null)
                .FirstOrDefaultAsync();

            if (session == null)
            {
                session = new EditSession { DocumentId = docId, StartedAt = DateTime.UtcNow };
                _db.EditSessions.Add(session);
                await _db.SaveChangesAsync();
            }

            _db.EditOperations.Add(new EditOperation
            {
                SessionId = session.Id,
                OperationType = type,
                PageNumber = page,
                OperationDataJson = data,
                AiInstruction = aiInstruction,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
    }

    // ── Extra request model classes ────────────────────────────────────────────
    public class ChatRequest
    {
        public int DocumentId { get; set; }
        public int PageNumber { get; set; } = 1;
        public string Message { get; set; } = "";
        public List<ChatMessage>? History { get; set; }
    }

    public class CalcRequest
    {
        public int DocumentId { get; set; }
        public int PageNumber { get; set; }
        public int TableIndex { get; set; }
    }

    public class RedactRequest
    {
        public int DocumentId { get; set; }
        public int PageNumber { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float W { get; set; }
        public float H { get; set; }
    }

    public class SaveAnnotationsRequest
    {
        public int DocumentId { get; set; }
        public List<AnnotationData> Annotations { get; set; } = new();
    }
}