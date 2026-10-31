using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackMessageReading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MessageReadAtUtc",
                table: "AppEducationFeedbackNotices",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_EduFeedbackNotices_MessageReadTime",
                table: "AppEducationFeedbackNotices",
                sql: "\"MessageReadAtUtc\" IS NULL OR \"MessageReadAtUtc\" >= \"RecordedAtUtc\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EduFeedbackNotices_MessageReadTime",
                table: "AppEducationFeedbackNotices");

            migrationBuilder.DropColumn(
                name: "MessageReadAtUtc",
                table: "AppEducationFeedbackNotices");
        }
    }
}
