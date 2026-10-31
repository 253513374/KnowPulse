using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddVerifiedWeChatAccountBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppWeChatAccountBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CodeSecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccountSecurityStampHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SubjectHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BoundAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ActiveAccountKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActiveSubjectKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppWeChatAccountBindings", x => x.Id);
                    table.CheckConstraint("CK_WeChatBinding_State", "(\"BoundAtUtc\" IS NULL AND \"SubjectHash\" IS NULL AND \"ActiveAccountKey\" IS NULL AND \"ActiveSubjectKey\" IS NULL) OR (\"BoundAtUtc\" IS NOT NULL AND \"SubjectHash\" IS NOT NULL AND length(\"SubjectHash\") = 64 AND ((\"RevokedAtUtc\" IS NULL AND \"ActiveAccountKey\" IS NOT NULL AND length(\"ActiveAccountKey\") = 64 AND \"ActiveSubjectKey\" IS NOT NULL AND length(\"ActiveSubjectKey\") = 64) OR (\"RevokedAtUtc\" IS NOT NULL AND \"ActiveAccountKey\" IS NULL AND \"ActiveSubjectKey\" IS NULL)))");
                    table.CheckConstraint("CK_WeChatBinding_Time", "\"ExpiresAtUtc\" > \"IssuedAtUtc\" AND (\"BoundAtUtc\" IS NULL OR \"BoundAtUtc\" >= \"IssuedAtUtc\") AND (\"RevokedAtUtc\" IS NULL OR \"RevokedAtUtc\" >= \"IssuedAtUtc\")");
                    table.ForeignKey(
                        name: "FK_AppWeChatAccountBindings_AbpUsers_AccountUserId",
                        column: x => x.AccountUserId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppWeChatAccountBindings_AccountUserId",
                table: "AppWeChatAccountBindings",
                column: "AccountUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WeChatBinding_ActiveAccount",
                table: "AppWeChatAccountBindings",
                column: "ActiveAccountKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeChatBinding_ActiveSubject",
                table: "AppWeChatAccountBindings",
                column: "ActiveSubjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeChatBinding_Owner",
                table: "AppWeChatAccountBindings",
                columns: new[] { "TenantId", "AccountUserId", "IssuedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "AppWeChatAccountBindings") THEN
                        RAISE EXCEPTION 'WeChat binding history exists; downgrade requires an explicit compatible restore.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "AppWeChatAccountBindings");
        }
    }
}
