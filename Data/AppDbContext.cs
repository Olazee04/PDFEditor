using Microsoft.EntityFrameworkCore;
using PDFEditor.Models;

namespace PDFEditor.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<PdfDocument> PdfDocuments { get; set; }
        public DbSet<EditSession> EditSessions { get; set; }
        public DbSet<EditOperation> EditOperations { get; set; }
        public DbSet<AiConversation> AiConversations { get; set; }
        public DbSet<ExtractedElement> ExtractedElements { get; set; }
        public DbSet<DocumentVersion> DocumentVersions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PdfDocument>()
                .HasMany(d => d.EditSessions)
                .WithOne(s => s.Document)
                .HasForeignKey(s => s.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EditSession>()
                .HasMany(s => s.Operations)
                .WithOne(o => o.Session)
                .HasForeignKey(o => o.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PdfDocument>()
                .HasMany(d => d.Versions)
                .WithOne(v => v.Document)
                .HasForeignKey(v => v.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}