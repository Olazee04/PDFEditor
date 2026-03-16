using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PDFEditor.Models
{
    // ─── Core Document ───────────────────────────────────────────────────────────
    public class PdfDocument
    {
        public int Id { get; set; }
        [Required] public string FileName { get; set; } = "";
        [Required] public string StoragePath { get; set; } = "";
        public string? OriginalPath { get; set; }
        public long FileSizeBytes { get; set; }
        public int PageCount { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string? ThumbnailPath { get; set; }
        public string? MetadataJson { get; set; }
        public bool IsLocked { get; set; }
        public string? PasswordHash { get; set; }
        public ICollection<EditSession> EditSessions { get; set; } = new List<EditSession>();
        public ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
    }

    // ─── Edit Session ─────────────────────────────────────────────────────────────
    public class EditSession
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public PdfDocument Document { get; set; } = null!;
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? EndedAt { get; set; }
        public string SessionToken { get; set; } = Guid.NewGuid().ToString();
        public ICollection<EditOperation> Operations { get; set; } = new List<EditOperation>();
    }

    // ─── Individual Edit Operation ────────────────────────────────────────────────
    public class EditOperation
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public EditSession Session { get; set; } = null!;
        public string OperationType { get; set; } = ""; // text_edit, image_copy, line_add, sign_extract, etc.
        public int PageNumber { get; set; }
        public string OperationDataJson { get; set; } = "{}";
        public string? BeforeStateJson { get; set; }
        public string? AfterStateJson { get; set; }
        public bool IsReverted { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? AiInstruction { get; set; }
    }

    // ─── AI Conversation ──────────────────────────────────────────────────────────
    public class AiConversation
    {
        public int Id { get; set; }
        public int? DocumentId { get; set; }
        public string Role { get; set; } = "user"; // user | assistant
        public string Content { get; set; } = "";
        public string? OperationResultJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ─── Extracted PDF Element ────────────────────────────────────────────────────
    public class ExtractedElement
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public string ElementType { get; set; } = ""; // text_block, image, signature, table, line
        public int PageNumber { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public string? ContentJson { get; set; } // Serialized element data
        public string? FontName { get; set; }
        public float FontSize { get; set; }
        public string? Color { get; set; }
    }

    // ─── Document Version ─────────────────────────────────────────────────────────
    public class DocumentVersion
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public PdfDocument Document { get; set; } = null!;
        public int VersionNumber { get; set; }
        public string StoragePath { get; set; } = "";
        public string? Label { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? ChangesSummary { get; set; }
    }

    // ─── Request/Response DTOs ────────────────────────────────────────────────────
    public class AiCommandRequest
    {
        public int DocumentId { get; set; }
        public int PageNumber { get; set; }
        public string Command { get; set; } = "";
        public string? SelectedRegionJson { get; set; }
    }

    public class TextEditRequest
    {
        public int DocumentId { get; set; }
        public int PageNumber { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public string OldText { get; set; } = "";
        public string NewText { get; set; } = "";
        public string? FontName { get; set; }
        public float FontSize { get; set; }
        public string? FontColor { get; set; }
    }

    public class ImageCopyRequest
    {
        public int DocumentId { get; set; }
        public int SourcePage { get; set; }
        public int TargetPage { get; set; }
        public float SrcX { get; set; }
        public float SrcY { get; set; }
        public float SrcWidth { get; set; }
        public float SrcHeight { get; set; }
        public float DstX { get; set; }
        public float DstY { get; set; }
        public float DstWidth { get; set; }
        public float DstHeight { get; set; }
    }

    public class LineAddRequest
    {
        public int DocumentId { get; set; }
        public int PageNumber { get; set; }
        public float ReferenceRowY { get; set; }
        public bool InsertAbove { get; set; }
        public int ReferenceTableIndex { get; set; }
        public string? RowDataJson { get; set; }
    }

    public class PdfAnalysisResult
    {
        public List<FontInfo> Fonts { get; set; } = new();
        public List<TextBlock> TextBlocks { get; set; } = new();
        public List<ImageRegion> Images { get; set; } = new();
        public List<TableStructure> Tables { get; set; } = new();
        public List<LineElement> Lines { get; set; } = new();
        public PageInfo PageInfo { get; set; } = new();
    }

    public class FontInfo
    {
        public string Name { get; set; } = "";
        public float Size { get; set; }
        public string Style { get; set; } = "";
        public bool IsEmbedded { get; set; }
        public int UsageCount { get; set; }
    }

    public class TextBlock
    {
        public string Text { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public string FontName { get; set; } = "";
        public float FontSize { get; set; }
        public string Color { get; set; } = "#000000";
        public float Rotation { get; set; }
        public int PageNumber { get; set; }
    }

    public class ImageRegion
    {
        public int Index { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public string ImageType { get; set; } = "";
        public int PageNumber { get; set; }
        public bool PossiblySignature { get; set; }
        public bool PossiblyLogo { get; set; }
        public string? Base64Preview { get; set; }
    }

    public class TableStructure
    {
        public int TableIndex { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public List<TableRow> Rows { get; set; } = new();
        public List<float> ColumnBoundaries { get; set; } = new();
        public int PageNumber { get; set; }
    }

    public class TableRow
    {
        public int RowIndex { get; set; }
        public float Y { get; set; }
        public float Height { get; set; }
        public List<TableCell> Cells { get; set; } = new();
        public bool HasTopBorder { get; set; }
        public bool HasBottomBorder { get; set; }
    }

    public class TableCell
    {
        public int ColIndex { get; set; }
        public string Text { get; set; } = "";
        public float X { get; set; }
        public float Width { get; set; }
        public string? Alignment { get; set; }
        public bool IsNumeric { get; set; }
        public decimal? NumericValue { get; set; }
    }

    public class LineElement
    {
        public float X1 { get; set; }
        public float Y1 { get; set; }
        public float X2 { get; set; }
        public float Y2 { get; set; }
        public float LineWidth { get; set; }
        public string Color { get; set; } = "#000000";
        public int PageNumber { get; set; }
    }

    public class PageInfo
    {
        public float Width { get; set; }
        public float Height { get; set; }
        public int PageNumber { get; set; }
        public int TotalPages { get; set; }
    }

    public class AiOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? OperationType { get; set; }
        public object? OperationData { get; set; }
        public string? UpdatedPdfBase64 { get; set; }
        public string? AiExplanation { get; set; }
    }
}
