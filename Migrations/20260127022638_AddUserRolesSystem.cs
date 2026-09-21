using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ZimbabweTenderAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRolesSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssignedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "CreatedAt", "CreatedBy", "DeletedAt", "DeletedBy", "Description", "IsActive", "IsDeleted", "Name", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 1, 27, 2, 26, 38, 3, DateTimeKind.Utc).AddTicks(6693), "System", null, null, "Full system access and administrative privileges", true, false, "Admin", null, null },
                    { 2, new DateTime(2026, 1, 27, 2, 26, 38, 3, DateTimeKind.Utc).AddTicks(6702), "System", null, null, "Read-only access to public data", true, false, "Guest", null, null },
                    { 3, new DateTime(2026, 1, 27, 2, 26, 38, 3, DateTimeKind.Utc).AddTicks(6707), "System", null, null, "Supervisory access with approval permissions", true, false, "Supervisor", null, null },
                    { 4, new DateTime(2026, 1, 27, 2, 26, 38, 3, DateTimeKind.Utc).AddTicks(6710), "System", null, null, "Human Resources access", true, false, "HR", null, null },
                    { 5, new DateTime(2026, 1, 27, 2, 26, 38, 3, DateTimeKind.Utc).AddTicks(6713), "System", null, null, "Project management access", true, false, "ProjectManager", null, null },
                    { 6, new DateTime(2026, 1, 27, 2, 26, 38, 3, DateTimeKind.Utc).AddTicks(6716), "System", null, null, "Standard employee access", true, false, "Employee", null, null },
                    { 7, new DateTime(2026, 1, 27, 2, 26, 38, 3, DateTimeKind.Utc).AddTicks(6732), "System", null, null, "Development team access", true, false, "Development", null, null }
                });

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
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2026, 1, 27, 2, 26, 38, 241, DateTimeKind.Utc).AddTicks(8060), "$2a$11$c1e8l5dCM2HpbU78k3XfTeKe87W/CNqSgJBVbNUJCtc5DmuRviCYe" });

            migrationBuilder.InsertData(
                table: "UserRoles",
                columns: new[] { "Id", "AssignedAt", "AssignedBy", "RoleId", "UserId" },
                values: new object[] { 1, new DateTime(2026, 1, 27, 2, 26, 38, 241, DateTimeKind.Utc).AddTicks(9045), "System", 1, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId_RoleId",
                table: "UserRoles",
                columns: new[] { "UserId", "RoleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "SystemConfigurations",
                keyColumn: "Id",
                keyValue: 1,
                column: "CreatedAt",
                value: new DateTime(2025, 12, 21, 6, 41, 41, 99, DateTimeKind.Utc).AddTicks(2891));

            migrationBuilder.UpdateData(
                table: "SystemConfigurations",
                keyColumn: "Id",
                keyValue: 2,
                column: "CreatedAt",
                value: new DateTime(2025, 12, 21, 6, 41, 41, 99, DateTimeKind.Utc).AddTicks(2898));

            migrationBuilder.UpdateData(
                table: "SystemConfigurations",
                keyColumn: "Id",
                keyValue: 3,
                column: "CreatedAt",
                value: new DateTime(2025, 12, 21, 6, 41, 41, 99, DateTimeKind.Utc).AddTicks(2902));

            migrationBuilder.UpdateData(
                table: "SystemConfigurations",
                keyColumn: "Id",
                keyValue: 4,
                column: "CreatedAt",
                value: new DateTime(2025, 12, 21, 6, 41, 41, 99, DateTimeKind.Utc).AddTicks(2905));

            migrationBuilder.UpdateData(
                table: "SystemConfigurations",
                keyColumn: "Id",
                keyValue: 5,
                column: "CreatedAt",
                value: new DateTime(2025, 12, 21, 6, 41, 41, 99, DateTimeKind.Utc).AddTicks(2909));

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash", "Role" },
                values: new object[] { new DateTime(2025, 12, 21, 6, 41, 41, 99, DateTimeKind.Utc).AddTicks(1955), "$2a$11$HG3e6TyOgLhbZcJok1O2cOQ5OzLBk35A2wRk3DTdLyZ/Uu/MJEuQi", "Admin" });
        }
    }
}
