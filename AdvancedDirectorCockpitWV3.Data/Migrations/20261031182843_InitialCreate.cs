using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdvancedDirectorCockpitWV3.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "ConfigDocument",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Rev = table.Column<int>(type: "int", nullable: false),
                    SavedAt = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    SavedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "sysdatetimeoffset()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigDocument", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConfigKey",
                schema: "dbo",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ValueJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rev = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "sysdatetimeoffset()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigKey", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Connection",
                schema: "dbo",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Server = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Database = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AuthMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SecretLocation = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "sysdatetimeoffset()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connection", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "Loan",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Lender = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LoanNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Principal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    AprPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    MinPayment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MaturityDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StatementDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "sysdatetimeoffset()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loan", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SchemaMeta",
                schema: "dbo",
                columns: table => new
                {
                    Version = table.Column<int>(type: "int", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "sysdatetimeoffset()"),
                    Notes = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchemaMeta", x => x.Version);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfigDocument",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ConfigKey",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Connection",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Loan",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SchemaMeta",
                schema: "dbo");
        }
    }
}
