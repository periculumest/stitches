using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StitchHelper.Migrations
{
    /// <inheritdoc />
    public partial class BetaExperienceFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParserVersion",
                table: "Imports",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "BetaAnnouncement",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    LinkText = table.Column<string>(type: "text", nullable: true),
                    LinkUrl = table.Column<string>(type: "text", nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Dismissible = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BetaAnnouncement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BetaAuditEvent",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    TargetId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BetaAuditEvent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BetaRelease",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    ReleaseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    BodyMarkdown = table.Column<string>(type: "text", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BetaRelease", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeedbackSubmission",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionKey = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DiagnosticsIncluded = table.Column<bool>(type: "boolean", nullable: false),
                    DiagnosticsDisclosureVersion = table.Column<int>(type: "integer", nullable: false),
                    Route = table.Column<string>(type: "text", nullable: false),
                    AppVersion = table.Column<string>(type: "text", nullable: false),
                    BrowserMetadata = table.Column<string>(type: "text", nullable: true),
                    ScreenMetadata = table.Column<string>(type: "text", nullable: true),
                    ProjectId = table.Column<string>(type: "text", nullable: true),
                    PatternRevisionId = table.Column<string>(type: "text", nullable: true),
                    ImportRunId = table.Column<string>(type: "text", nullable: true),
                    ImportMetadata = table.Column<string>(type: "text", nullable: true),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    CorrelationId = table.Column<string>(type: "text", nullable: true),
                    PatternAttachmentConsentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedByAdminUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackSubmission", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedbackSubmission_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserOnboardingState",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletedOnboardingVersion = table.Column<int>(type: "integer", nullable: false),
                    CompletedOrDismissedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserOnboardingState", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserOnboardingState_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementDismissal",
                columns: table => new
                {
                    AnnouncementId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DismissedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementDismissal", x => new { x.UserId, x.AnnouncementId });
                    table.ForeignKey(
                        name: "FK_AnnouncementDismissal_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnnouncementDismissal_BetaAnnouncement_AnnouncementId",
                        column: x => x.AnnouncementId,
                        principalTable: "BetaAnnouncement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FeedbackAttachment",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    FeedbackSubmissionId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    AssetId = table.Column<string>(type: "text", nullable: true),
                    Screenshot = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackAttachment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedbackAttachment_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FeedbackAttachment_FeedbackSubmission_FeedbackSubmissionId",
                        column: x => x.FeedbackSubmissionId,
                        principalTable: "FeedbackSubmission",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementDismissal_AnnouncementId",
                table: "AnnouncementDismissal",
                column: "AnnouncementId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackAttachment_AssetId",
                table: "FeedbackAttachment",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackAttachment_FeedbackSubmissionId",
                table: "FeedbackAttachment",
                column: "FeedbackSubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackSubmission_Status_CreatedAt",
                table: "FeedbackSubmission",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackSubmission_UserId_SubmissionKey",
                table: "FeedbackSubmission",
                columns: new[] { "UserId", "SubmissionKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnnouncementDismissal");

            migrationBuilder.DropTable(
                name: "BetaAuditEvent");

            migrationBuilder.DropTable(
                name: "BetaRelease");

            migrationBuilder.DropTable(
                name: "FeedbackAttachment");

            migrationBuilder.DropTable(
                name: "UserOnboardingState");

            migrationBuilder.DropTable(
                name: "BetaAnnouncement");

            migrationBuilder.DropTable(
                name: "FeedbackSubmission");

            migrationBuilder.DropColumn(
                name: "ParserVersion",
                table: "Imports");
        }
    }
}
