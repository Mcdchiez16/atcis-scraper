using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ZimbabweTenderAPI.Migrations
{
    /// <inheritdoc />
    public partial class FixEntityConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AwardNotices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AwardNoticeNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenderId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AwardTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Awardee = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AwardDate = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ParsedAwardDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DetailsUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContractValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LastScrapedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_AwardNotices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LiveTenders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CategoryCodes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CategoryNames = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProcuringEntity = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosingDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DetailsUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    LastScrapedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastVerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MovedToClosed = table.Column<bool>(type: "bit", nullable: false),
                    MovedToClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_LiveTenders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcurementPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcuringEntity = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Year = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ViewAppUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TotalItems = table.Column<int>(type: "int", nullable: false),
                    TotalEstimatedValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LastScrapedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_ProcurementPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScrapingJobHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ItemsProcessed = table.Column<int>(type: "int", nullable: false),
                    ItemsAdded = table.Column<int>(type: "int", nullable: false),
                    ItemsUpdated = table.Column<int>(type: "int", nullable: false),
                    ItemsFailed = table.Column<int>(type: "int", nullable: false),
                    ErrorDetails = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TriggeredBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScrapingJobHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
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
                    table.PrimaryKey("PK_SystemConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    RefreshToken = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RefreshTokenExpiryTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginIP = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FailedLoginAttempts = table.Column<int>(type: "int", nullable: false),
                    LockoutEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AwardNoticeAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AwardNoticeNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Changes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangeDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IPAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AwardNoticeId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardNoticeAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwardNoticeAuditLogs_AwardNotices_AwardNoticeId",
                        column: x => x.AwardNoticeId,
                        principalTable: "AwardNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClosedTenders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CategoryCodes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CategoryNames = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProcuringEntity = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PublishDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosingDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DetailsUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ActualClosingDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastScrapedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AwardNoticeId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_ClosedTenders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClosedTenders_AwardNotices_AwardNoticeId",
                        column: x => x.AwardNoticeId,
                        principalTable: "AwardNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcurementPlanAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcurementPlanId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Changes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangeDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IPAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcurementPlanAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcurementPlanAuditLogs_ProcurementPlans_ProcurementPlanId",
                        column: x => x.ProcurementPlanId,
                        principalTable: "ProcurementPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcurementPlanItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcurementPlanId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RefNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClassOfProcurement = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ObjectCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PmoEndUser = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProcurementMethod = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EoiPublicationDate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EoiClosingDate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TenderPublicationDate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BidClosingDate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AwardNoticeDate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ContractSigningDate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CycleDays = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LeadTime = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Spoc = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SourceOfFunds = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UnitOfMeasurement = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Quantity = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Comments = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsSupplement = table.Column<bool>(type: "bit", nullable: false),
                    ParsedTenderPublicationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ParsedBidClosingDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ParsedAwardNoticeDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ParsedContractSigningDate = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_ProcurementPlanItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcurementPlanItems_ProcurementPlans_ProcurementPlanId",
                        column: x => x.ProcurementPlanId,
                        principalTable: "ProcurementPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangeDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IPAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAuditLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenderAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Changes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangeDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IPAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LiveTenderId = table.Column<int>(type: "int", nullable: true),
                    ClosedTenderId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenderAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenderAuditLogs_ClosedTenders_ClosedTenderId",
                        column: x => x.ClosedTenderId,
                        principalTable: "ClosedTenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenderAuditLogs_LiveTenders_LiveTenderId",
                        column: x => x.LiveTenderId,
                        principalTable: "LiveTenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "SystemConfigurations",
                columns: new[] { "Id", "Category", "CreatedAt", "CreatedBy", "DeletedAt", "DeletedBy", "Description", "IsDeleted", "Key", "UpdatedAt", "UpdatedBy", "Value" },
                values: new object[,]
                {
                    { 1, "Scraping", new DateTime(2025, 12, 21, 6, 41, 41, 99, DateTimeKind.Utc).AddTicks(2891), "System", null, null, "Enable/disable automatic scraping", false, "ScrapingEnabled", null, null, "true" },
                    { 2, "Scraping", new DateTime(2025, 12, 21, 6, 41, 41, 99, DateTimeKind.Utc).AddTicks(2898), "System", null, null, "Interval between automatic scraping jobs in minutes", false, "ScrapingIntervalMinutes", null, null, "60" },
                    { 3, "Scraping", new DateTime(2025, 12, 21, 6, 41, 41, 99, DateTimeKind.Utc).AddTicks(2902), "System", null, null, "Maximum number of concurrent scraping operations", false, "MaxConcurrentScrapes", null, null, "3" },
                    { 4, "Data", new DateTime(2025, 12, 21, 6, 41, 41, 99, DateTimeKind.Utc).AddTicks(2905), "System", null, null, "Number of days to retain closed tenders in database", false, "RetentionDaysClosedTenders", null, null, "365" },
                    { 5, "Email", new DateTime(2025, 12, 21, 6, 41, 41, 99, DateTimeKind.Utc).AddTicks(2909), "System", null, null, "Enable/disable email notifications", false, "EmailNotificationsEnabled", null, null, "false" }
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAt", "CreatedBy", "DeletedAt", "DeletedBy", "Email", "EmailConfirmed", "FailedLoginAttempts", "FirstName", "IsActive", "IsDeleted", "LastLoginAt", "LastLoginIP", "LastName", "LockoutEnd", "PasswordHash", "RefreshToken", "RefreshTokenExpiryTime", "Role", "UpdatedAt", "UpdatedBy", "Username" },
                values: new object[] { 1, new DateTime(2025, 12, 21, 6, 41, 41, 99, DateTimeKind.Utc).AddTicks(1955), "System", null, null, "admin@tendersystem.com", true, 0, "System", true, false, null, null, "Administrator", null, "$2a$11$HG3e6TyOgLhbZcJok1O2cOQ5OzLBk35A2wRk3DTdLyZ/Uu/MJEuQi", null, null, "Admin", null, null, "admin" });

            migrationBuilder.CreateIndex(
                name: "IX_AwardNoticeAuditLogs_AwardNoticeId",
                table: "AwardNoticeAuditLogs",
                column: "AwardNoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardNoticeAuditLogs_AwardNoticeNumber_ChangeDate",
                table: "AwardNoticeAuditLogs",
                columns: new[] { "AwardNoticeNumber", "ChangeDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AwardNotices_AwardNoticeNumber",
                table: "AwardNotices",
                column: "AwardNoticeNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AwardNotices_TenderId",
                table: "AwardNotices",
                column: "TenderId");

            migrationBuilder.CreateIndex(
                name: "IX_ClosedTenders_AwardNoticeId",
                table: "ClosedTenders",
                column: "AwardNoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_ClosedTenders_ClosingDate",
                table: "ClosedTenders",
                column: "ClosingDate");

            migrationBuilder.CreateIndex(
                name: "IX_ClosedTenders_ReferenceNumber",
                table: "ClosedTenders",
                column: "ReferenceNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ClosedTenders_TenderId",
                table: "ClosedTenders",
                column: "TenderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveTenders_ClosingDate",
                table: "LiveTenders",
                column: "ClosingDate");

            migrationBuilder.CreateIndex(
                name: "IX_LiveTenders_ProcuringEntity",
                table: "LiveTenders",
                column: "ProcuringEntity");

            migrationBuilder.CreateIndex(
                name: "IX_LiveTenders_ReferenceNumber",
                table: "LiveTenders",
                column: "ReferenceNumber");

            migrationBuilder.CreateIndex(
                name: "IX_LiveTenders_TenderId",
                table: "LiveTenders",
                column: "TenderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementPlanAuditLogs_ProcurementPlanId_ChangeDate",
                table: "ProcurementPlanAuditLogs",
                columns: new[] { "ProcurementPlanId", "ChangeDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementPlanItems_ProcurementPlanId_RefNo",
                table: "ProcurementPlanItems",
                columns: new[] { "ProcurementPlanId", "RefNo" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementPlans_ProcuringEntity_Year",
                table: "ProcurementPlans",
                columns: new[] { "ProcuringEntity", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScrapingJobHistory_JobType_StartTime",
                table: "ScrapingJobHistory",
                columns: new[] { "JobType", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemConfigurations_Key",
                table: "SystemConfigurations",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenderAuditLogs_ClosedTenderId",
                table: "TenderAuditLogs",
                column: "ClosedTenderId");

            migrationBuilder.CreateIndex(
                name: "IX_TenderAuditLogs_LiveTenderId",
                table: "TenderAuditLogs",
                column: "LiveTenderId");

            migrationBuilder.CreateIndex(
                name: "IX_TenderAuditLogs_TenderId_ChangeDate",
                table: "TenderAuditLogs",
                columns: new[] { "TenderId", "ChangeDate" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAuditLogs_UserId_ChangeDate",
                table: "UserAuditLogs",
                columns: new[] { "UserId", "ChangeDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AwardNoticeAuditLogs");

            migrationBuilder.DropTable(
                name: "ProcurementPlanAuditLogs");

            migrationBuilder.DropTable(
                name: "ProcurementPlanItems");

            migrationBuilder.DropTable(
                name: "ScrapingJobHistory");

            migrationBuilder.DropTable(
                name: "SystemConfigurations");

            migrationBuilder.DropTable(
                name: "TenderAuditLogs");

            migrationBuilder.DropTable(
                name: "UserAuditLogs");

            migrationBuilder.DropTable(
                name: "ProcurementPlans");

            migrationBuilder.DropTable(
                name: "ClosedTenders");

            migrationBuilder.DropTable(
                name: "LiveTenders");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "AwardNotices");
        }
    }
}
