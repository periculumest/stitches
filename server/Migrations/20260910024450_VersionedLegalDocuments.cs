using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StitchHelper.Migrations
{
    /// <inheritdoc />
    public partial class VersionedLegalDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegalDocuments",
                columns: table => new
                {
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    ChangeSummary = table.Column<string>(type: "text", nullable: false),
                    AcceptanceText = table.Column<string>(type: "text", nullable: false),
                    RequiresAcceptance = table.Column<bool>(type: "boolean", nullable: false),
                    RequireReacceptance = table.Column<bool>(type: "boolean", nullable: false),
                    DraftRevision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalDocuments", x => x.Slug);
                });

            migrationBuilder.CreateTable(
                name: "LegalVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    DocumentSlug = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    ChangeSummary = table.Column<string>(type: "text", nullable: false),
                    AcceptanceText = table.Column<string>(type: "text", nullable: false),
                    RequiresAcceptance = table.Column<bool>(type: "boolean", nullable: false),
                    AcceptanceGeneration = table.Column<int>(type: "integer", nullable: false),
                    ContentSha256 = table.Column<string>(type: "text", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalVersions_LegalDocuments_DocumentSlug",
                        column: x => x.DocumentSlug,
                        principalTable: "LegalDocuments",
                        principalColumn: "Slug",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LegalAcceptances",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<string>(type: "text", nullable: false),
                    ContentSha256 = table.Column<string>(type: "text", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalAcceptances", x => new { x.UserId, x.VersionId });
                    table.ForeignKey(
                        name: "FK_LegalAcceptances_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LegalAcceptances_LegalVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "LegalVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegalAcceptances_VersionId",
                table: "LegalAcceptances",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "IX_LegalVersions_DocumentSlug_Version",
                table: "LegalVersions",
                columns: new[] { "DocumentSlug", "Version" },
                unique: true);
            migrationBuilder.Sql("""
                INSERT INTO "LegalDocuments" ("Slug", "Title", "Body", "ChangeSummary", "AcceptanceText", "RequiresAcceptance", "RequireReacceptance", "DraftRevision", "UpdatedAt")
                VALUES ('terms-of-service', 'Terms of Service', '', '', '', true, true, 0, CURRENT_TIMESTAMP),
                       ('privacy-policy', 'Privacy Policy', '', '', '', true, true, 0, CURRENT_TIMESTAMP);
                CREATE FUNCTION reject_legal_version_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Published legal versions are immutable; publish a new version.'; END $$;
                CREATE TRIGGER legal_versions_immutable BEFORE UPDATE OR DELETE ON "LegalVersions"
                  FOR EACH ROW EXECUTE FUNCTION reject_legal_version_mutation();
                CREATE FUNCTION reject_legal_receipt_update() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Legal acceptance receipts cannot be updated.'; END $$;
                CREATE TRIGGER legal_receipts_immutable BEFORE UPDATE ON "LegalAcceptances"
                  FOR EACH ROW EXECUTE FUNCTION reject_legal_receipt_update();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS reject_legal_version_mutation() CASCADE; DROP FUNCTION IF EXISTS reject_legal_receipt_update() CASCADE;");
            migrationBuilder.DropTable(
                name: "LegalAcceptances");

            migrationBuilder.DropTable(
                name: "LegalVersions");

            migrationBuilder.DropTable(
                name: "LegalDocuments");
        }
    }
}
