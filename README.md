# NovaPDF Editor

A professional, AI-powered PDF editor built with **C# ASP.NET Core MVC**, **SQLite**, and **Claude AI** — deployable to **Render.com** in minutes.

---

## ✨ Features

### Core Editing (Preserves Original Layout)
- **Inline text editing** — covers old text with a white mask, writes new text with matched font/size/color
- **Font detection** — reads all embedded fonts in the PDF and matches them for edits
- **Redaction** — permanently blacks out regions
- **Highlight/Annotate** — overlays highlights and text annotations
- **Image/signature copy** — cut out any region (logo, signature, stamp) and paste it elsewhere on the same or different page

### Table & Calculation Tools
- **Auto-detect tables** from line patterns in the PDF content stream
- **Add row above/below** any row while matching existing line widths and spacing
- **Calculate & fill totals** — sums numeric columns and writes totals back into the PDF

### AI Integration (Claude)
- **Natural language commands**: "Copy the signature from the bottom-left to top-right", "Add a new row to the table and put 500.00 in column 3", "Detect what fonts are used"
- **Structured operation detection**: AI returns a machine-readable operation JSON that the UI executes
- **Context-aware suggestions**: AI sees fonts, images, tables, and text blocks before responding
- **Full chat history** stored in SQLite per document

### Document Management
- Upload PDFs (up to 50MB)
- Auto-versioning — every edit creates a recoverable snapshot
- Version history browser with one-click restore
- Download edited PDF at any time
- Full edit operations log

---

## 🚀 Quick Start (Local)

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- An [Anthropic API key](https://console.anthropic.com)

### 1. Clone & Configure

```bash
git clone <your-repo>
cd PDFEditor
```

Edit `appsettings.json`:
```json
{
  "Anthropic": {
    "ApiKey": "sk-ant-YOUR_KEY_HERE"
  }
}
```

### 2. Run

```bash
dotnet restore
dotnet run
```

Open [http://localhost:5000](http://localhost:5000)

---

## ☁️ Deploy to Render

### Option A: Docker (Recommended)

1. Push your code to GitHub (make sure `appsettings.json` does NOT contain your API key — use env vars)
2. In [Render Dashboard](https://dashboard.render.com), click **New → Web Service**
3. Connect your GitHub repo
4. Set **Runtime** to `Docker`
5. Add Environment Variables:
   - `Anthropic__ApiKey` = `sk-ant-YOUR_KEY`
   - `ConnectionStrings__DefaultConnection` = `Data Source=/app/pdfeditor.db`
6. Add a **Disk** at `/app/wwwroot` (10GB) for PDF storage persistence
7. Click **Deploy**

### Option B: render.yaml (Blueprint)

The included `render.yaml` auto-configures the service. Just add your API key manually in the Render dashboard after deploy.

---

## 🏗️ Architecture

```
PDFEditor/
├── Controllers/
│   ├── HomeController.cs      # Upload, list, delete documents
│   └── EditorController.cs    # All edit operations + AI chat API
├── Services/
│   ├── PdfService.cs          # iText7-based PDF manipulation
│   └── AiService.cs           # Claude API integration
├── Models/
│   └── Models.cs              # All entities and DTOs
├── Data/
│   └── AppDbContext.cs        # SQLite EF Core context
├── Views/
│   ├── Home/Index.cshtml      # Dashboard (drag-drop upload)
│   └── Editor/Edit.cshtml     # Full PDF editor UI
└── wwwroot/
    ├── css/site.css
    ├── js/site.js
    ├── uploads/               # Uploaded PDFs
    └── versions/              # Auto-version snapshots
```

### Database Schema (SQLite)

| Table | Purpose |
|---|---|
| `PdfDocuments` | Document metadata, paths, page count |
| `EditSessions` | Groups operations per editing session |
| `EditOperations` | Individual edits (type, page, params, AI instruction) |
| `AiConversations` | Full chat history per document |
| `ExtractedElements` | Cached text blocks, images, tables |
| `DocumentVersions` | Snapshot paths for undo/restore |

---

## ⚙️ How Layout Preservation Works

NovaPDF uses a **mask-then-replace** strategy:

1. **Analyze** the page — extract exact bounding boxes for every text block using iText7's `ITextExtractionStrategy`
2. **White mask** — draw a solid white rectangle over the original text area
3. **Re-render** — place new text at the exact same coordinates with the detected font
4. For tables: detect horizontal/vertical lines from the PDF content stream, cluster them to identify rows/columns, then add new lines at the exact same weight

This means the surrounding layout (other text, images, borders) is **never touched** — only the masked region is modified.

---

## 📦 Key Dependencies

| Package | Purpose |
|---|---|
| `itext7` | PDF read/write, text extraction, canvas drawing |
| `SixLabors.ImageSharp` | Image processing for extracted regions |
| `Microsoft.EntityFrameworkCore.Sqlite` | SQLite database |
| `Anthropic.SDK` | Claude AI (optional — `AiService` also works via raw HTTP) |
| `Newtonsoft.Json` | JSON serialization |
| PDF.js (CDN) | Client-side PDF rendering in the browser |

---

## 🔑 Environment Variables

| Variable | Description |
|---|---|
| `Anthropic__ApiKey` | Your Anthropic Claude API key |
| `ConnectionStrings__DefaultConnection` | SQLite connection string |
| `PORT` | HTTP port (auto-set by Render) |
| `MaxFileSizeMB` | Upload size limit (default: 50) |

---

## 🛣️ Roadmap / Extending

- **OCR support**: Integrate Tesseract for scanned PDFs
- **Digital signatures**: iText7 supports PDF signing natively
- **Form filling**: iText7 `PdfAcroForm` for interactive forms
- **Batch processing**: Background jobs for multi-file operations
- **User auth**: Add ASP.NET Identity for multi-user support
- **Cloud storage**: Swap local disk for Azure Blob / S3
