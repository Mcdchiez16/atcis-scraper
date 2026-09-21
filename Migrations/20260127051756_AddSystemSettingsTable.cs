using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ZimbabweTenderAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemSettingsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 5, 17, 55, 538, DateTimeKind.Utc).AddTicks(8135));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 2,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 5, 17, 55, 538, DateTimeKind.Utc).AddTicks(8231));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 3,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 5, 17, 55, 538, DateTimeKind.Utc).AddTicks(8236));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 4,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 5, 17, 55, 538, DateTimeKind.Utc).AddTicks(8239));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 5,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 5, 17, 55, 538, DateTimeKind.Utc).AddTicks(8243));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 6,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 5, 17, 55, 538, DateTimeKind.Utc).AddTicks(8246));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 7,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 5, 17, 55, 538, DateTimeKind.Utc).AddTicks(8249));

            migrationBuilder.UpdateData(
                table: "SystemConfigurations",
                keyColumn: "Id",
                keyValue: 1,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(4823));

            migrationBuilder.UpdateData(
                table: "SystemConfigurations",
                keyColumn: "Id",
                keyValue: 2,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(4830));

            migrationBuilder.UpdateData(
                table: "SystemConfigurations",
                keyColumn: "Id",
                keyValue: 3,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(4835));

            migrationBuilder.UpdateData(
                table: "SystemConfigurations",
                keyColumn: "Id",
                keyValue: 4,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(4841));

            migrationBuilder.UpdateData(
                table: "SystemConfigurations",
                keyColumn: "Id",
                keyValue: 5,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(4845));

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "CreatedBy", "DeletedAt", "DeletedBy", "Description", "IsDeleted", "Key", "UpdatedAt", "UpdatedBy", "Value" },
                values: new object[,]
                {
                    { 1, "BackgroundJobs", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5071), "System", null, null, "Enable/disable live tenders sync job", false, "BackgroundJobs:LiveTendersSync:Enabled", null, null, "true" },
                    { 2, "BackgroundJobs", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5075), "System", null, null, "Live tenders sync interval in hours (0.0833 = 5 minutes)", false, "BackgroundJobs:LiveTendersSync:IntervalHours", null, null, "0.0833" },
                    { 3, "BackgroundJobs", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5078), "System", null, null, "Enable/disable closed tenders sync job", false, "BackgroundJobs:ClosedTendersSync:Enabled", null, null, "true" },
                    { 4, "BackgroundJobs", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5081), "System", null, null, "Closed tenders sync interval in hours", false, "BackgroundJobs:ClosedTendersSync:IntervalHours", null, null, "24" },
                    { 5, "BackgroundJobs", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5084), "System", null, null, "Hour to run closed tenders sync (24-hour format)", false, "BackgroundJobs:ClosedTendersSync:RunAtHour", null, null, "3" },
                    { 6, "BackgroundJobs", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5087), "System", null, null, "Enable/disable award notices sync job", false, "BackgroundJobs:AwardNoticesSync:Enabled", null, null, "true" },
                    { 7, "BackgroundJobs", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5090), "System", null, null, "Award notices sync interval in hours", false, "BackgroundJobs:AwardNoticesSync:IntervalHours", null, null, "24" },
                    { 8, "BackgroundJobs", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5093), "System", null, null, "Hour to run award notices sync (24-hour format)", false, "BackgroundJobs:AwardNoticesSync:RunAtHour", null, null, "2" },
                    { 9, "BackgroundJobs", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5096), "System", null, null, "Enable/disable procurement plans sync job", false, "BackgroundJobs:ProcurementPlansSync:Enabled", null, null, "true" },
                    { 10, "BackgroundJobs", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5099), "System", null, null, "Procurement plans sync interval in hours (168 = weekly)", false, "BackgroundJobs:ProcurementPlansSync:IntervalHours", null, null, "168" },
                    { 11, "BackgroundJobs", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5102), "System", null, null, "Hour to run procurement plans sync (24-hour format)", false, "BackgroundJobs:ProcurementPlansSync:RunAtHour", null, null, "4" },
                    { 12, "BackgroundJobs", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5106), "System", null, null, "Enable/disable verify and move tenders job", false, "BackgroundJobs:VerifyAndMoveTenders:Enabled", null, null, "true" },
                    { 13, "BackgroundJobs", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5109), "System", null, null, "Verify and move interval in hours", false, "BackgroundJobs:VerifyAndMoveTenders:IntervalHours", null, null, "6" },
                    { 14, "JWT", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5111), "System", null, null, "JWT token expiration time in minutes (1440 = 24 hours)", false, "JWT:ExpirationMinutes", null, null, "1440" },
                    { 15, "JWT", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5114), "System", null, null, "Refresh token expiration in days", false, "JWT:RefreshTokenExpirationDays", null, null, "7" },
                    { 16, "Gemini", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5117), "System", null, null, "Gemini AI model to use", false, "Gemini:Model", null, null, "gemini-2.0-flash-exp" },
                    { 17, "Gemini", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5120), "System", null, null, "Gemini AI temperature (0.0-1.0)", false, "Gemini:Temperature", null, null, "0.7" },
                    { 18, "Gemini", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5123), "System", null, null, "Maximum tokens for Gemini responses", false, "Gemini:MaxTokens", null, null, "8000" },
                    { 19, "Gemini", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5126), "System", null, null, "Enable/disable Gemini AI features", false, "Gemini:Enabled", null, null, "true" },
                    { 20, "Scraping", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5129), "System", null, null, "Number of items to process in each batch", false, "Scraping:BatchSize", null, null, "100" },
                    { 21, "Scraping", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5132), "System", null, null, "HTTP request timeout in seconds", false, "Scraping:TimeoutSeconds", null, null, "30" },
                    { 22, "Scraping", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5135), "System", null, null, "Number of retry attempts for failed requests", false, "Scraping:RetryAttempts", null, null, "3" },
                    { 23, "Scraping", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5137), "System", null, null, "Delay between scraping requests in milliseconds", false, "Scraping:DelayBetweenRequestsMs", null, null, "500" },
                    { 24, "Pagination", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5140), "System", null, null, "Default number of items per page", false, "Pagination:DefaultPageSize", null, null, "20" },
                    { 25, "Pagination", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5143), "System", null, null, "Maximum allowed page size", false, "Pagination:MaxPageSize", null, null, "100" },
                    { 26, "Analytics", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5146), "System", null, null, "Default days to look back for analytics", false, "Analytics:DefaultDaysBack", null, null, "90" },
                    { 27, "Analytics", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5149), "System", null, null, "Minimum confidence score for recommendations", false, "Analytics:MinConfidenceScore", null, null, "60" },
                    { 28, "Analytics", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5151), "System", null, null, "Maximum number of opportunities to return", false, "Analytics:MaxOpportunities", null, null, "50" },
                    { 29, "Logging", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5154), "System", null, null, "Enable logging to files", false, "Logging:EnableFileLogging", null, null, "true" },
                    { 30, "Logging", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5157), "System", null, null, "Directory path for log files", false, "Logging:LogPath", null, null, "C:/Temp" },
                    { 31, "Logging", new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(5160), "System", null, null, "Number of days to keep log files", false, "Logging:RetentionDays", null, null, "30" }
                });

            migrationBuilder.UpdateData(
                table: "UserRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "AssignedAt",
                value: new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(4728));

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2026, 1, 27, 5, 17, 55, 758, DateTimeKind.Utc).AddTicks(3981), "$2a$11$zXfMD8iiny3W5VSCLKSwVeSHkRg9H57M6YVEcLIyC4/bi3WBc/6Ge" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_Key",
                table: "SystemSettings",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 2, 26, 38, 3, DateTimeKind.Utc).AddTicks(6693));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 2,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 2, 26, 38, 3, DateTimeKind.Utc).AddTicks(6702));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 3,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 2, 26, 38, 3, DateTimeKind.Utc).AddTicks(6707));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 4,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 2, 26, 38, 3, DateTimeKind.Utc).AddTicks(6710));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 5,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 2, 26, 38, 3, DateTimeKind.Utc).AddTicks(6713));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 6,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 2, 26, 38, 3, DateTimeKind.Utc).AddTicks(6716));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 7,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 2, 26, 38, 3, DateTimeKind.Utc).AddTicks(6732));

            migrationBuilder.UpdateData(
                table: "SystemConfigurations",
                keyColumn: "Id",
                keyValue: 1,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 2, 26, 38, 241, DateTimeKind.Utc).AddTicks(9104));

            migrationBuilder.UpdateData(
                table: "SystemConfigurations",
                keyColumn: "Id",
                keyValue: 2,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 2, 26, 38, 241, DateTimeKind.Utc).AddTicks(9112));

            migrationBuilder.UpdateData(
                table: "SystemConfigurations",
                keyColumn: "Id",
                keyValue: 3,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 2, 26, 38, 241, DateTimeKind.Utc).AddTicks(9116));

            migrationBuilder.UpdateData(
                table: "SystemConfigurations",
                keyColumn: "Id",
                keyValue: 4,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 2, 26, 38, 241, DateTimeKind.Utc).AddTicks(9120));

            migrationBuilder.UpdateData(
                table: "SystemConfigurations",
                keyColumn: "Id",
                keyValue: 5,
                column: "CreatedAt",
                value: new DateTime(2026, 1, 27, 2, 26, 38, 241, DateTimeKind.Utc).AddTicks(9124));

            migrationBuilder.UpdateData(
                table: "UserRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "AssignedAt",
                value: new DateTime(2026, 1, 27, 2, 26, 38, 241, DateTimeKind.Utc).AddTicks(9045));

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2026, 1, 27, 2, 26, 38, 241, DateTimeKind.Utc).AddTicks(8060), "$2a$11$c1e8l5dCM2HpbU78k3XfTeKe87W/CNqSgJBVbNUJCtc5DmuRviCYe" });
        }
    }
}
