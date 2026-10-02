using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkoulouApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Ankuendigungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Titel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InhaltHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AblaufAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ankuendigungen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommissionSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GlobalSubscriptionPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SchoolLeaderPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TeacherPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ProductOwnerPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MailNachrichten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GesendetVonID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Absender = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Empfaenger = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Betreff = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Inhalt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PostmarkMessageID = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IstEingehend = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailNachrichten", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Schools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    City = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DescriptionHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SubscriptionPriceOverride = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    SchoolLeaderCommissionPercentOverride = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    TeacherCommissionPercentOverride = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    ProductOwnerCommissionPercentOverride = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_RolePermissions_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Abrechnungsperioden",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VonUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BisUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Umsatz = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    AnzahlAbonnements = table.Column<int>(type: "int", nullable: false),
                    IstAbgeschlossen = table.Column<bool>(type: "bit", nullable: false),
                    AbgeschlossenAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErstelltVonUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AutomatischDurchKlassenabschlussErstellt = table.Column<bool>(type: "bit", nullable: false),
                    AusgeloestDurchDurchlaufId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AutomatischAusgeloestVonUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AutomatischErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IstSammelabrechnungAusGeschlossenenKlassen = table.Column<bool>(type: "bit", nullable: false),
                    AblehnungsGrund = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SchulLeiterReaktionAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ZahlungGemeldetVonUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BestaetigtAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BestaetigtVonSuperAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Abrechnungsperioden", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Abrechnungsperioden_Schools_SchulID",
                        column: x => x.SchulID,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExceptionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ExceptionType = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StackTrace = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestPath = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsHandled = table.Column<bool>(type: "bit", nullable: false),
                    HandledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HandledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExceptionLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExceptionLogs_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Faecher",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Faecher", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Faecher_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinanzKategorien",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Bezeichnung = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Richtung = table.Column<int>(type: "int", nullable: false),
                    SystemCode = table.Column<int>(type: "int", nullable: false),
                    IstSystemTyp = table.Column<bool>(type: "bit", nullable: false),
                    IstAktiv = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanzKategorien", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinanzKategorien_Schools_SchulID",
                        column: x => x.SchulID,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KlassenStufen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KlassenStufen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KlassenStufen_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Klassen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CEFRGroup = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecommendedLessonCount = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    MaxPlaetze = table.Column<int>(type: "int", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    KlassenStufeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IstSystemNiveau = table.Column<bool>(type: "bit", nullable: false),
                    AutoAssignSkoulouAppAboOnCycleStart = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Klassen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Klassen_KlassenStufen_KlassenStufeId",
                        column: x => x.KlassenStufeId,
                        principalTable: "KlassenStufen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Klassen_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Vorname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Nachname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TelefonNummer = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Geschlecht = table.Column<int>(type: "int", nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    IsSelfRegistered = table.Column<bool>(type: "bit", nullable: false),
                    ProfilBildAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActiveSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GeburtsdatumUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AgbVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AgbAcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BlobMediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BlobName = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    MediaType = table.Column<int>(type: "int", nullable: false),
                    VideoDuration = table.Column<TimeSpan>(type: "time", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlobMediaAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlobMediaAssets_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BlobMediaAssets_Klassen_KlasseId",
                        column: x => x.KlasseId,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BlobMediaAssets_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Exams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DurationInMinutes = table.Column<int>(type: "int", nullable: false),
                    SubmissionDueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsSuperseded = table.Column<bool>(type: "bit", nullable: false),
                    PassingScore = table.Column<int>(type: "int", nullable: false),
                    ExamType = table.Column<int>(type: "int", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsPublishedToStudents = table.Column<bool>(type: "bit", nullable: false),
                    IsSharedWithSchool = table.Column<bool>(type: "bit", nullable: false),
                    IsPdfExportEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exams_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Exams_Klassen_KlasseId",
                        column: x => x.KlasseId,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinanzBuchungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KategorieID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Richtung = table.Column<int>(type: "int", nullable: false),
                    Betrag = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Wirkungsdatum = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BeschreibungHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Belegnummer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Buchungsnummer = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: false, computedColumnSql: "'B-' + REPLACE(CONVERT(varchar(36), [Id]), '-', '')", stored: true),
                    ErfasstVonUserID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HerkunftReferenzID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanzBuchungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinanzBuchungen_AspNetUsers_ErfasstVonUserID",
                        column: x => x.ErfasstVonUserID,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinanzBuchungen_FinanzKategorien_KategorieID",
                        column: x => x.KategorieID,
                        principalTable: "FinanzKategorien",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinanzBuchungen_Schools_SchulID",
                        column: x => x.SchulID,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FormelFavoriten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BenutzerID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LaTeX = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Bezeichnung = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Vorschau = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormelFavoriten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormelFavoriten_AspNetUsers_BenutzerID",
                        column: x => x.BenutzerID,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KursNiveauDurchlaeufe",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NiveauPreis = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    StartDatum = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDatum = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErstelltVonUserID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KursNiveauDurchlaeufe", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KursNiveauDurchlaeufe_AspNetUsers_ErstelltVonUserID",
                        column: x => x.ErstelltVonUserID,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KursNiveauDurchlaeufe_Klassen_KlasseId",
                        column: x => x.KlasseId,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KursNiveauDurchlaeufe_Schools_SchulID",
                        column: x => x.SchulID,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LehrerFachZuordnungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LehrerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FachId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LehrerFachZuordnungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LehrerFachZuordnungen_AspNetUsers_LehrerId",
                        column: x => x.LehrerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LehrerFachZuordnungen_Faecher_FachId",
                        column: x => x.FachId,
                        principalTable: "Faecher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LehrerNiveauZuordnungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LehrerID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Wochenstunden = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LehrerNiveauZuordnungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LehrerNiveauZuordnungen_AspNetUsers_LehrerID",
                        column: x => x.LehrerID,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LehrerNiveauZuordnungen_Klassen_KlasseId",
                        column: x => x.KlasseId,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LehrerNiveauZuordnungen_Schools_SchulID",
                        column: x => x.SchulID,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Lessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ExerciseSubmissionDueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AssignmentVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    FachId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByTeacherId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lessons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lessons_AspNetUsers_CreatedByTeacherId",
                        column: x => x.CreatedByTeacherId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Lessons_Faecher_FachId",
                        column: x => x.FachId,
                        principalTable: "Faecher",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Lessons_Klassen_KlasseId",
                        column: x => x.KlasseId,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LevelEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AboTyp = table.Column<int>(type: "int", nullable: false),
                    IsUnlocked = table.Column<bool>(type: "bit", nullable: false),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    PriceAtEnrollment = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    SchoolLeaderCommissionAtEnrollment = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    TeacherCommissionAtEnrollment = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ProductOwnerCommissionAtEnrollment = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    UnlockedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AbgerechnetInPeriodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LevelEnrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LevelEnrollments_Abrechnungsperioden_AbgerechnetInPeriodeId",
                        column: x => x.AbgerechnetInPeriodeId,
                        principalTable: "Abrechnungsperioden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LevelEnrollments_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LevelEnrollments_Klassen_KlasseId",
                        column: x => x.KlasseId,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentLevelCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CurrentKlasseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedLessonsCount = table.Column<int>(type: "int", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ElternTelefonNummer = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ElternEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentProfiles_AspNetUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentProfiles_Klassen_CurrentKlasseId",
                        column: x => x.CurrentKlasseId,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentProfiles_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserSchools",
                columns: table => new
                {
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CanManageStudents = table.Column<bool>(type: "bit", nullable: false),
                    CanAccessFinances = table.Column<bool>(type: "bit", nullable: false),
                    CanViewFinanceDashboard = table.Column<bool>(type: "bit", nullable: false),
                    CanCreateFinanceEntries = table.Column<bool>(type: "bit", nullable: false),
                    CanManageCyclePayments = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSchools", x => new { x.AppUserId, x.SchoolId });
                    table.ForeignKey(
                        name: "FK_UserSchools_AspNetUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserSchools_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeacherLessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CourseTheme = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    IsSharedWithSchool = table.Column<bool>(type: "bit", nullable: false),
                    IsPdfExportEnabled = table.Column<bool>(type: "bit", nullable: false),
                    FachId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VideoAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherLessons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeacherLessons_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeacherLessons_BlobMediaAssets_VideoAssetId",
                        column: x => x.VideoAssetId,
                        principalTable: "BlobMediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TeacherLessons_Faecher_FachId",
                        column: x => x.FachId,
                        principalTable: "Faecher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TeacherLessons_Klassen_KlasseId",
                        column: x => x.KlasseId,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeadlineUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CorrectAnswers = table.Column<int>(type: "int", nullable: false),
                    TotalQuestions = table.Column<int>(type: "int", nullable: false),
                    ScorePercent = table.Column<int>(type: "int", nullable: false),
                    IsPassed = table.Column<bool>(type: "bit", nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    ManuelleNote = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    ManuelleNoteErfasstAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ManuelleNoteErfasstVonUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequiresManualGrading = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamAttempts_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamAttempts_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamExercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Consigne = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamExercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamExercises_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinanzBelege",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuchungID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Dateiname = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Inhaltstyp = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    BlobName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Inhalt = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanzBelege", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinanzBelege_FinanzBuchungen_BuchungID",
                        column: x => x.BuchungID,
                        principalTable: "FinanzBuchungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KlassenAbschlussEntscheidungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DurchlaufId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AbschlussNote = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    AbschlussNoteText = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsBestanden = table.Column<bool>(type: "bit", nullable: true),
                    EntscheidetAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EntscheidetVonUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KlassenAbschlussEntscheidungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KlassenAbschlussEntscheidungen_AspNetUsers_EntscheidetVonUserId",
                        column: x => x.EntscheidetVonUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KlassenAbschlussEntscheidungen_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KlassenAbschlussEntscheidungen_KursNiveauDurchlaeufe_DurchlaufId",
                        column: x => x.DurchlaufId,
                        principalTable: "KursNiveauDurchlaeufe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RatenZahlungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DurchlaufID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Betrag = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ZahlungsArt = table.Column<int>(type: "int", nullable: false),
                    Wirkungsdatum = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BeschreibungHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FinanzBuchungID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErfasstVonUserID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RatenZahlungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RatenZahlungen_AspNetUsers_ErfasstVonUserID",
                        column: x => x.ErfasstVonUserID,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RatenZahlungen_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RatenZahlungen_FinanzBuchungen_FinanzBuchungID",
                        column: x => x.FinanzBuchungID,
                        principalTable: "FinanzBuchungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RatenZahlungen_KursNiveauDurchlaeufe_DurchlaufID",
                        column: x => x.DurchlaufID,
                        principalTable: "KursNiveauDurchlaeufe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Exercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExerciseType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false, defaultValue: 10),
                    AudioText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TtsLanguage = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "de-DE"),
                    AcceptedAnswersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GapAnswersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exercises_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LessonMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StorageProvider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonMedia_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LessonProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrectExerciseCount = table.Column<int>(type: "int", nullable: false),
                    TotalExerciseCount = table.Column<int>(type: "int", nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastActivityUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonProgresses_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonProgresses_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LehrerProvisionsAuszahlungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AbrechnungsperiodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FreischaltungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LehrerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WochenstundenSnapshot = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    GesamtWochenstundenSnapshot = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Betrag = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    AlsErhaltenBestaetigtAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LehrerProvisionsAuszahlungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LehrerProvisionsAuszahlungen_Abrechnungsperioden_AbrechnungsperiodeId",
                        column: x => x.AbrechnungsperiodeId,
                        principalTable: "Abrechnungsperioden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LehrerProvisionsAuszahlungen_AspNetUsers_LehrerId",
                        column: x => x.LehrerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LehrerProvisionsAuszahlungen_LevelEnrollments_FreischaltungId",
                        column: x => x.FreischaltungId,
                        principalTable: "LevelEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProduktOwnerProvisionsAuszahlungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AbrechnungsperiodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FreischaltungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProduktOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Betrag = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    AlsErhaltenBestaetigtAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProduktOwnerProvisionsAuszahlungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProduktOwnerProvisionsAuszahlungen_Abrechnungsperioden_AbrechnungsperiodeId",
                        column: x => x.AbrechnungsperiodeId,
                        principalTable: "Abrechnungsperioden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProduktOwnerProvisionsAuszahlungen_AspNetUsers_ProduktOwnerId",
                        column: x => x.ProduktOwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProduktOwnerProvisionsAuszahlungen_LevelEnrollments_FreischaltungId",
                        column: x => x.FreischaltungId,
                        principalTable: "LevelEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeacherLessonPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeacherLessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    ContentHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherLessonPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeacherLessonPages_TeacherLessons_TeacherLessonId",
                        column: x => x.TeacherLessonId,
                        principalTable: "TeacherLessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamExerciseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Prompt = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    QuestionType = table.Column<int>(type: "int", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    AudioText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TtsLanguage = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "de-DE"),
                    AcceptedAnswersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GapAnswersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamQuestions_ExamExercises_ExamExerciseId",
                        column: x => x.ExamExerciseId,
                        principalTable: "ExamExercises",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExamQuestions_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExerciseAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SelectedOptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SelectedOptionIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TextAnswer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GapAnswersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    AssignmentVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    ExerciseSnapshotJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RequiresManualGrading = table.Column<bool>(type: "bit", nullable: false),
                    ManuallyGradedIsCorrect = table.Column<bool>(type: "bit", nullable: true),
                    ManualFeedback = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AwardedPoints = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ManuallyGradedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ManuallyGradedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnsweredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExerciseAttempts_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExerciseAttempts_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExerciseOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExerciseOptions_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SelectedOptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SelectedOptionIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FreeTextAnswer = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    GapAnswersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    IsManuallyGraded = table.Column<bool>(type: "bit", nullable: false),
                    ManualPoints = table.Column<int>(type: "int", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamAnswers_ExamAttempts_ExamAttemptId",
                        column: x => x.ExamAttemptId,
                        principalTable: "ExamAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamAnswers_ExamQuestions_ExamQuestionId",
                        column: x => x.ExamQuestionId,
                        principalTable: "ExamQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamQuestionOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamQuestionOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamQuestionOptions_ExamQuestions_ExamQuestionId",
                        column: x => x.ExamQuestionId,
                        principalTable: "ExamQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Abrechnungsperioden_SchulID_VonUtc",
                table: "Abrechnungsperioden",
                columns: new[] { "SchulID", "VonUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Ankuendigungen_AblaufAmUtc",
                table: "Ankuendigungen",
                column: "AblaufAmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoles_Name",
                table: "AspNetRoles",
                column: "Name",
                unique: true,
                filter: "[Name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_Email",
                table: "AspNetUsers",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_ProfilBildAssetId",
                table: "AspNetUsers",
                column: "ProfilBildAssetId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BlobMediaAssets_KlasseId",
                table: "BlobMediaAssets",
                column: "KlasseId");

            migrationBuilder.CreateIndex(
                name: "IX_BlobMediaAssets_OwnerId",
                table: "BlobMediaAssets",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_BlobMediaAssets_SchoolId_ErstelltAmUtc",
                table: "BlobMediaAssets",
                columns: new[] { "SchoolId", "ErstelltAmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BlobMediaAssets_SchoolId_KlasseId",
                table: "BlobMediaAssets",
                columns: new[] { "SchoolId", "KlasseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAnswers_ExamAttemptId",
                table: "ExamAnswers",
                column: "ExamAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamAnswers_ExamQuestionId",
                table: "ExamAnswers",
                column: "ExamQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_ExamId_StudentId",
                table: "ExamAttempts",
                columns: new[] { "ExamId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_StudentId",
                table: "ExamAttempts",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamExercises_ExamId",
                table: "ExamExercises",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamQuestionOptions_ExamQuestionId",
                table: "ExamQuestionOptions",
                column: "ExamQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamQuestions_ExamExerciseId",
                table: "ExamQuestions",
                column: "ExamExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamQuestions_ExamId",
                table: "ExamQuestions",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_AuthorId",
                table: "Exams",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_KlasseId",
                table: "Exams",
                column: "KlasseId");

            migrationBuilder.CreateIndex(
                name: "IX_ExceptionLogs_OccurredAtUtc",
                table: "ExceptionLogs",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ExceptionLogs_SchoolId_OccurredAtUtc",
                table: "ExceptionLogs",
                columns: new[] { "SchoolId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseAttempts_ExerciseId_StudentId",
                table: "ExerciseAttempts",
                columns: new[] { "ExerciseId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseAttempts_ExerciseId_StudentId_AssignmentVersion",
                table: "ExerciseAttempts",
                columns: new[] { "ExerciseId", "StudentId", "AssignmentVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseAttempts_StudentId",
                table: "ExerciseAttempts",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseOptions_ExerciseId",
                table: "ExerciseOptions",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_LessonId",
                table: "Exercises",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_Faecher_SchoolId_Name",
                table: "Faecher",
                columns: new[] { "SchoolId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanzBelege_BuchungID",
                table: "FinanzBelege",
                column: "BuchungID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanzBelege_SchulID",
                table: "FinanzBelege",
                column: "SchulID");

            migrationBuilder.CreateIndex(
                name: "IX_FinanzBuchungen_Buchungsnummer",
                table: "FinanzBuchungen",
                column: "Buchungsnummer",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanzBuchungen_ErfasstVonUserID",
                table: "FinanzBuchungen",
                column: "ErfasstVonUserID");

            migrationBuilder.CreateIndex(
                name: "IX_FinanzBuchungen_KategorieID",
                table: "FinanzBuchungen",
                column: "KategorieID");

            migrationBuilder.CreateIndex(
                name: "IX_FinanzBuchungen_SchulID_Richtung_Wirkungsdatum",
                table: "FinanzBuchungen",
                columns: new[] { "SchulID", "Richtung", "Wirkungsdatum" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanzBuchungen_SchulID_Wirkungsdatum",
                table: "FinanzBuchungen",
                columns: new[] { "SchulID", "Wirkungsdatum" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanzKategorien_SchulID_Richtung_Bezeichnung",
                table: "FinanzKategorien",
                columns: new[] { "SchulID", "Richtung", "Bezeichnung" });

            migrationBuilder.CreateIndex(
                name: "IX_FormelFavoriten_BenutzerID_LaTeX",
                table: "FormelFavoriten",
                columns: new[] { "BenutzerID", "LaTeX" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Klassen_KlassenStufeId",
                table: "Klassen",
                column: "KlassenStufeId");

            migrationBuilder.CreateIndex(
                name: "IX_Klassen_SchoolId_Code",
                table: "Klassen",
                columns: new[] { "SchoolId", "Code" },
                unique: true,
                filter: "[SchoolId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_KlassenAbschlussEntscheidungen_DurchlaufId_StudentId",
                table: "KlassenAbschlussEntscheidungen",
                columns: new[] { "DurchlaufId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KlassenAbschlussEntscheidungen_EntscheidetVonUserId",
                table: "KlassenAbschlussEntscheidungen",
                column: "EntscheidetVonUserId");

            migrationBuilder.CreateIndex(
                name: "IX_KlassenAbschlussEntscheidungen_StudentId",
                table: "KlassenAbschlussEntscheidungen",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_KlassenStufen_SchoolId_Name",
                table: "KlassenStufen",
                columns: new[] { "SchoolId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KursNiveauDurchlaeufe_ErstelltVonUserID",
                table: "KursNiveauDurchlaeufe",
                column: "ErstelltVonUserID");

            migrationBuilder.CreateIndex(
                name: "IX_KursNiveauDurchlaeufe_KlasseId",
                table: "KursNiveauDurchlaeufe",
                column: "KlasseId");

            migrationBuilder.CreateIndex(
                name: "IX_KursNiveauDurchlaeufe_SchulID_KlasseId_EndDatum",
                table: "KursNiveauDurchlaeufe",
                columns: new[] { "SchulID", "KlasseId", "EndDatum" });

            migrationBuilder.CreateIndex(
                name: "IX_LehrerFachZuordnungen_FachId",
                table: "LehrerFachZuordnungen",
                column: "FachId");

            migrationBuilder.CreateIndex(
                name: "IX_LehrerFachZuordnungen_LehrerId_FachId",
                table: "LehrerFachZuordnungen",
                columns: new[] { "LehrerId", "FachId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LehrerNiveauZuordnungen_KlasseId",
                table: "LehrerNiveauZuordnungen",
                column: "KlasseId");

            migrationBuilder.CreateIndex(
                name: "IX_LehrerNiveauZuordnungen_LehrerID_SchulID_KlasseId",
                table: "LehrerNiveauZuordnungen",
                columns: new[] { "LehrerID", "SchulID", "KlasseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LehrerNiveauZuordnungen_SchulID",
                table: "LehrerNiveauZuordnungen",
                column: "SchulID");

            migrationBuilder.CreateIndex(
                name: "IX_LehrerProvisionsAuszahlungen_AbrechnungsperiodeId_FreischaltungId_LehrerId",
                table: "LehrerProvisionsAuszahlungen",
                columns: new[] { "AbrechnungsperiodeId", "FreischaltungId", "LehrerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LehrerProvisionsAuszahlungen_FreischaltungId",
                table: "LehrerProvisionsAuszahlungen",
                column: "FreischaltungId");

            migrationBuilder.CreateIndex(
                name: "IX_LehrerProvisionsAuszahlungen_LehrerId",
                table: "LehrerProvisionsAuszahlungen",
                column: "LehrerId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonMedia_LessonId",
                table: "LessonMedia",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonProgresses_LessonId_StudentId",
                table: "LessonProgresses",
                columns: new[] { "LessonId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LessonProgresses_StudentId",
                table: "LessonProgresses",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_CreatedByTeacherId",
                table: "Lessons",
                column: "CreatedByTeacherId");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_FachId",
                table: "Lessons",
                column: "FachId");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_KlasseId",
                table: "Lessons",
                column: "KlasseId");

            migrationBuilder.CreateIndex(
                name: "IX_LevelEnrollments_AbgerechnetInPeriodeId",
                table: "LevelEnrollments",
                column: "AbgerechnetInPeriodeId");

            migrationBuilder.CreateIndex(
                name: "IX_LevelEnrollments_KlasseId",
                table: "LevelEnrollments",
                column: "KlasseId");

            migrationBuilder.CreateIndex(
                name: "IX_LevelEnrollments_StudentId_KlasseId",
                table: "LevelEnrollments",
                columns: new[] { "StudentId", "KlasseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MailNachrichten_PostmarkMessageID",
                table: "MailNachrichten",
                column: "PostmarkMessageID",
                unique: true,
                filter: "[PostmarkMessageID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MailNachrichten_SchulID_ErstelltAmUtc",
                table: "MailNachrichten",
                columns: new[] { "SchulID", "ErstelltAmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Name",
                table: "Permissions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProduktOwnerProvisionsAuszahlungen_AbrechnungsperiodeId_FreischaltungId_ProduktOwnerId",
                table: "ProduktOwnerProvisionsAuszahlungen",
                columns: new[] { "AbrechnungsperiodeId", "FreischaltungId", "ProduktOwnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProduktOwnerProvisionsAuszahlungen_FreischaltungId",
                table: "ProduktOwnerProvisionsAuszahlungen",
                column: "FreischaltungId");

            migrationBuilder.CreateIndex(
                name: "IX_ProduktOwnerProvisionsAuszahlungen_ProduktOwnerId",
                table: "ProduktOwnerProvisionsAuszahlungen",
                column: "ProduktOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_RatenZahlungen_DurchlaufID_StudentId",
                table: "RatenZahlungen",
                columns: new[] { "DurchlaufID", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_RatenZahlungen_ErfasstVonUserID",
                table: "RatenZahlungen",
                column: "ErfasstVonUserID");

            migrationBuilder.CreateIndex(
                name: "IX_RatenZahlungen_FinanzBuchungID",
                table: "RatenZahlungen",
                column: "FinanzBuchungID");

            migrationBuilder.CreateIndex(
                name: "IX_RatenZahlungen_StudentId",
                table: "RatenZahlungen",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Schools_Name",
                table: "Schools",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_AppUserId",
                table: "StudentProfiles",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_CurrentKlasseId",
                table: "StudentProfiles",
                column: "CurrentKlasseId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_SchoolId",
                table: "StudentProfiles",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherLessonPages_TeacherLessonId_PageNumber",
                table: "TeacherLessonPages",
                columns: new[] { "TeacherLessonId", "PageNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherLessons_AuthorId",
                table: "TeacherLessons",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherLessons_FachId",
                table: "TeacherLessons",
                column: "FachId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherLessons_KlasseId_Order",
                table: "TeacherLessons",
                columns: new[] { "KlasseId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_TeacherLessons_VideoAssetId",
                table: "TeacherLessons",
                column: "VideoAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSchools_SchoolId",
                table: "UserSchools",
                column: "SchoolId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                table: "AspNetUserClaims",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                table: "AspNetUserLogins",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                table: "AspNetUserRoles",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_BlobMediaAssets_ProfilBildAssetId",
                table: "AspNetUsers",
                column: "ProfilBildAssetId",
                principalTable: "BlobMediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BlobMediaAssets_Schools_SchoolId",
                table: "BlobMediaAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_Klassen_Schools_SchoolId",
                table: "Klassen");

            migrationBuilder.DropForeignKey(
                name: "FK_KlassenStufen_Schools_SchoolId",
                table: "KlassenStufen");

            migrationBuilder.DropForeignKey(
                name: "FK_BlobMediaAssets_AspNetUsers_OwnerId",
                table: "BlobMediaAssets");

            migrationBuilder.DropTable(
                name: "Ankuendigungen");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "CommissionSettings");

            migrationBuilder.DropTable(
                name: "ExamAnswers");

            migrationBuilder.DropTable(
                name: "ExamQuestionOptions");

            migrationBuilder.DropTable(
                name: "ExceptionLogs");

            migrationBuilder.DropTable(
                name: "ExerciseAttempts");

            migrationBuilder.DropTable(
                name: "ExerciseOptions");

            migrationBuilder.DropTable(
                name: "FinanzBelege");

            migrationBuilder.DropTable(
                name: "FormelFavoriten");

            migrationBuilder.DropTable(
                name: "KlassenAbschlussEntscheidungen");

            migrationBuilder.DropTable(
                name: "LehrerFachZuordnungen");

            migrationBuilder.DropTable(
                name: "LehrerNiveauZuordnungen");

            migrationBuilder.DropTable(
                name: "LehrerProvisionsAuszahlungen");

            migrationBuilder.DropTable(
                name: "LessonMedia");

            migrationBuilder.DropTable(
                name: "LessonProgresses");

            migrationBuilder.DropTable(
                name: "MailNachrichten");

            migrationBuilder.DropTable(
                name: "ProduktOwnerProvisionsAuszahlungen");

            migrationBuilder.DropTable(
                name: "RatenZahlungen");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "StudentProfiles");

            migrationBuilder.DropTable(
                name: "TeacherLessonPages");

            migrationBuilder.DropTable(
                name: "UserSchools");

            migrationBuilder.DropTable(
                name: "ExamAttempts");

            migrationBuilder.DropTable(
                name: "ExamQuestions");

            migrationBuilder.DropTable(
                name: "Exercises");

            migrationBuilder.DropTable(
                name: "LevelEnrollments");

            migrationBuilder.DropTable(
                name: "FinanzBuchungen");

            migrationBuilder.DropTable(
                name: "KursNiveauDurchlaeufe");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "TeacherLessons");

            migrationBuilder.DropTable(
                name: "ExamExercises");

            migrationBuilder.DropTable(
                name: "Lessons");

            migrationBuilder.DropTable(
                name: "Abrechnungsperioden");

            migrationBuilder.DropTable(
                name: "FinanzKategorien");

            migrationBuilder.DropTable(
                name: "Exams");

            migrationBuilder.DropTable(
                name: "Faecher");

            migrationBuilder.DropTable(
                name: "Schools");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "BlobMediaAssets");

            migrationBuilder.DropTable(
                name: "Klassen");

            migrationBuilder.DropTable(
                name: "KlassenStufen");
        }
    }
}
