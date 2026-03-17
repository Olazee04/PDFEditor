using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PDFEditor.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiConversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    OperationResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExtractedElements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    ElementType = table.Column<string>(type: "TEXT", nullable: false),
                    PageNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    X = table.Column<float>(type: "REAL", nullable: false),
                    Y = table.Column<float>(type: "REAL", nullable: false),
                    Width = table.Column<float>(type: "REAL", nullable: false),
                    Height = table.Column<float>(type: "REAL", nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: true),
                    FontName = table.Column<string>(type: "TEXT", nullable: true),
                    FontSize = table.Column<float>(type: "REAL", nullable: false),
                    Color = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractedElements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PdfDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalPath = table.Column<string>(type: "TEXT", nullable: true),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ThumbnailPath = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PdfDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ChangesSummary = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentVersions_PdfDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "PdfDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EditSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SessionToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EditSessions_PdfDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "PdfDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EditOperations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    OperationType = table.Column<string>(type: "TEXT", nullable: false),
                    PageNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    OperationDataJson = table.Column<string>(type: "TEXT", nullable: false),
                    BeforeStateJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterStateJson = table.Column<string>(type: "TEXT", nullable: true),
                    IsReverted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AiInstruction = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EditOperations_EditSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "EditSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_DocumentId",
                table: "DocumentVersions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_EditOperations_SessionId",
                table: "EditOperations",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_EditSessions_DocumentId",
                table: "EditSessions",
                column: "DocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiConversations");

            migrationBuilder.DropTable(
                name: "DocumentVersions");

            migrationBuilder.DropTable(
                name: "EditOperations");

            migrationBuilder.DropTable(
                name: "ExtractedElements");

            migrationBuilder.DropTable(
                name: "EditSessions");

            migrationBuilder.DropTable(
                name: "PdfDocuments");
        }
    }
}
