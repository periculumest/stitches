using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StitchHelper.Migrations
{
    /// <inheritdoc />
    public partial class PredictableDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PendingObjectDeletions",
                table: "PendingObjectDeletions");

            migrationBuilder.AddColumn<string>(
                name: "StoreKind",
                table: "PendingObjectDeletions",
                type: "text",
                nullable: false,
                defaultValue: "backup");

            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "PendingObjectDeletions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NotBefore",
                table: "PendingObjectDeletions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QueuedAt",
                table: "PendingObjectDeletions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PendingObjectDeletions",
                table: "PendingObjectDeletions",
                columns: new[] { "StoreKind", "StorageKey" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingObjectDeletions_NotBefore",
                table: "PendingObjectDeletions",
                column: "NotBefore");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"PendingObjectDeletions\" WHERE \"StoreKind\" <> 'backup') THEN RAISE EXCEPTION 'Complete pending source deletions before downgrading the deletion queue.'; END IF; END $$;");
            migrationBuilder.DropPrimaryKey(
                name: "PK_PendingObjectDeletions",
                table: "PendingObjectDeletions");

            migrationBuilder.DropIndex(
                name: "IX_PendingObjectDeletions_NotBefore",
                table: "PendingObjectDeletions");

            migrationBuilder.DropColumn(
                name: "StoreKind",
                table: "PendingObjectDeletions");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "PendingObjectDeletions");

            migrationBuilder.DropColumn(
                name: "NotBefore",
                table: "PendingObjectDeletions");

            migrationBuilder.DropColumn(
                name: "QueuedAt",
                table: "PendingObjectDeletions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PendingObjectDeletions",
                table: "PendingObjectDeletions",
                column: "StorageKey");
        }
    }
}
