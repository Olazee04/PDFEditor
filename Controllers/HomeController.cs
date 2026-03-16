using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PDFEditor.Data;
using PDFEditor.Models;

namespace PDFEditor.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<HomeController> _logger;

        public HomeController(AppDbContext db, IWebHostEnvironment env, ILogger<HomeController> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var docs = await _db.PdfDocuments
                .OrderByDescending(d => d.UploadedAt)
                .Take(20)
                .ToListAsync();
            return View(docs);
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file, string? password)
        {
            if (file == null || file.Length == 0)
                return Json(new { success = false, message = "No file selected" });

            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "Only PDF files are allowed" });

            var maxBytes = 50L * 1024 * 1024;
            if (file.Length > maxBytes)
                return Json(new { success = false, message = "File too large (max 50MB)" });

            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
            Directory.CreateDirectory(uploadsDir);

            var uniqueName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
            var filePath = Path.Combine(uploadsDir, uniqueName);

            using (var stream = new FileStream(filePath, FileMode.Create))
                await file.CopyToAsync(stream);

            var originalPath = filePath + ".original";
            System.IO.File.Copy(filePath, originalPath);

            int pages = 1;
            try
            {
                using var reader = new iText.Kernel.Pdf.PdfReader(filePath);
                using var doc = new iText.Kernel.Pdf.PdfDocument(reader);
                pages = doc.GetNumberOfPages();
            }
            catch { }

            var document = new PdfDocument
            {
                FileName = file.FileName,
                StoragePath = filePath,
                OriginalPath = originalPath,
                FileSizeBytes = file.Length,
                PageCount = pages,
                UploadedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.PdfDocuments.Add(document);
            await _db.SaveChangesAsync();

            _db.DocumentVersions.Add(new DocumentVersion
            {
                DocumentId = document.Id,
                VersionNumber = 1,
                StoragePath = originalPath,
                Label = "Original",
                CreatedAt = DateTime.UtcNow,
                ChangesSummary = "Initial upload"
            });
            await _db.SaveChangesAsync();

            return Json(new { success = true, documentId = document.Id, pages });
        }

        [HttpDelete]
        public async Task<IActionResult> Delete(int id)
        {
            var doc = await _db.PdfDocuments.FindAsync(id);
            if (doc == null) return NotFound();

            if (System.IO.File.Exists(doc.StoragePath))
                System.IO.File.Delete(doc.StoragePath);

            if (doc.OriginalPath != null && System.IO.File.Exists(doc.OriginalPath))
                System.IO.File.Delete(doc.OriginalPath);

            _db.PdfDocuments.Remove(doc);
            await _db.SaveChangesAsync();
            return Json(new { success = true });
        }

        public IActionResult Error() => View();
    }
}