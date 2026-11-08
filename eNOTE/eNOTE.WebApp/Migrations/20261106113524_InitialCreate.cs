using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Vorname = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Nachname = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TelefonNummer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    IsSelfRegistered = table.Column<bool>(type: "bit", nullable: false),
                    IstErsterAdmin = table.Column<bool>(type: "bit", nullable: false),
                    IstInternerKassierer = table.Column<bool>(type: "bit", nullable: false),
                    ActiveSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                name: "BesitzerBetreuungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BesitzerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProduktOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GueltigAbUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GueltigBisUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BesitzerBetreuungen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BlobMediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateiName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BlobName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GroesseBytes = table.Column<long>(type: "bigint", nullable: false),
                    HochgeladenVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlobMediaAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExceptionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZeitUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Nachricht = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Ausnahme = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Pfad = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BenutzerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IstBehandelt = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExceptionLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MailNachrichten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Empfaenger = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Betreff = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HtmlInhalt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IstGesendet = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GesendetUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailNachrichten", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonatsPreisStaende",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GueltigAbUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MonatsPreis = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonatsPreisStaende", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlattformPreise",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Intervall = table.Column<int>(type: "int", nullable: false),
                    Preis = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IstAktuell = table.Column<bool>(type: "bit", nullable: false),
                    GueltigAbUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlattformPreise", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PoAbrechnungsEreignisse",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProduktOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BenutzerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Benutzer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VorgangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZeitpunktUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Aktion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Betrag = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Notiz = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoAbrechnungsEreignisse", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PoZahlungsanforderungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SammellaufId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProduktOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StichtagUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FaelligUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Betrag = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VerrechneteProvision = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoZahlungsanforderungen", x => x.Id);
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
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "ProduktOwnerAuszahlungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProduktOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Jahr = table.Column<int>(type: "int", nullable: false),
                    Monat = table.Column<int>(type: "int", nullable: false),
                    Betrag = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Aufschluesselung = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    BestaetigtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProduktOwnerAuszahlungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProduktOwnerAuszahlungen_AspNetUsers_ProduktOwnerId",
                        column: x => x.ProduktOwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Salons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Standort = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Land = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Beschreibung = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IstAktiv = table.Column<bool>(type: "bit", nullable: false),
                    TelefonNummer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Adresse = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BonQrLink = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IstFreigegeben = table.Column<bool>(type: "bit", nullable: false),
                    ProbeBisUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IndividuellerMonatsPreis = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    VertragsIntervall = table.Column<int>(type: "int", nullable: false),
                    NaechstesVertragsIntervall = table.Column<int>(type: "int", nullable: true),
                    VertragsBeginnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VertragsWechselAbUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Slogan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LogoAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Salons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Salons_BlobMediaAssets_LogoAssetId",
                        column: x => x.LogoAssetId,
                        principalTable: "BlobMediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PoGeldbewegungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProduktOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnforderungId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IstProvision = table.Column<bool>(type: "bit", nullable: false),
                    Betrag = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ZahlungsdatumUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErfasstUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Referenz = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoGeldbewegungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PoGeldbewegungen_PoZahlungsanforderungen_AnforderungId",
                        column: x => x.AnforderungId,
                        principalTable: "PoZahlungsanforderungen",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AusgabenKategorien",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IstAktiv = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AusgabenKategorien", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AusgabenKategorien_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BesitzerHinweise",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AusgeloestVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AusgeloestVonName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IstUngelesen = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GelesenUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BesitzerHinweise", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BesitzerHinweise_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GeraetSalonFavoriten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeraeteSchluessel = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeraeteName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeraetSalonFavoriten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeraetSalonFavoriten_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KassenSitzungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KassiererId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GestartetUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AbgeschlossenUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Startbestand = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EndbestandGezaehlt = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    BareinnahmenSoll = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Differenz = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    AbschlussNotiz = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KassenSitzungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KassenSitzungen_AspNetUsers_KassiererId",
                        column: x => x.KassiererId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KassenSitzungen_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeistungKategorien",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IstAktiv = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeistungKategorien", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeistungKategorien_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NutzungsAbos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Intervall = table.Column<int>(type: "int", nullable: false),
                    PreisSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MonatsBasisSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RabattProzentSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IndividuellerPreisSnapshot = table.Column<bool>(type: "bit", nullable: false),
                    IstTeilperiode = table.Column<bool>(type: "bit", nullable: false),
                    FaelligUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    KassenSperreAbUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AutomatischErstellt = table.Column<bool>(type: "bit", nullable: false),
                    ZeitraumVonUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ZeitraumBisUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ZahlungStatus = table.Column<int>(type: "int", nullable: false),
                    ZahlungGemeldetUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ZahlungBestaetigtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ZahlungsNotiz = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutzungsAbos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NutzungsAbos_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProduktOwnerSalons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProduktOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KannLadenUmsatzSehen = table.Column<bool>(type: "bit", nullable: false),
                    KannPlattformUmsatzSehen = table.Column<bool>(type: "bit", nullable: false),
                    SelbstAngelegt = table.Column<bool>(type: "bit", nullable: false),
                    ProzentProvision = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    FestbetragProvision = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GueltigBisUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProduktOwnerSalons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProduktOwnerSalons_AspNetUsers_ProduktOwnerId",
                        column: x => x.ProduktOwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProduktOwnerSalons_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSalons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KassenPinHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BesitzerPinHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PinFehlversuche = table.Column<int>(type: "int", nullable: false),
                    PinGesperrtBisUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    KannFinanzenSehen = table.Column<bool>(type: "bit", nullable: false),
                    KannLeistungenVerwalten = table.Column<bool>(type: "bit", nullable: false),
                    KannRabatteVerwalten = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSalons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSalons_AspNetUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserSalons_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Ausgaben",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Betrag = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DatumUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    KategorieId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Notiz = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BelegAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErfasstVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ausgaben", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ausgaben_AusgabenKategorien_KategorieId",
                        column: x => x.KategorieId,
                        principalTable: "AusgabenKategorien",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Ausgaben_BlobMediaAssets_BelegAssetId",
                        column: x => x.BelegAssetId,
                        principalTable: "BlobMediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Ausgaben_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Verkaeufe",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KassenSitzungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZeitUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ZahlungsArt = table.Column<int>(type: "int", nullable: false),
                    Zwischensumme = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RabattSumme = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GesamtSumme = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Gegeben = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Rueckgeld = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    KarteZahlungBestaetigt = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Verkaeufe", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Verkaeufe_KassenSitzungen_KassenSitzungId",
                        column: x => x.KassenSitzungId,
                        principalTable: "KassenSitzungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Leistungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Beschreibung = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Preis = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Kaufpreis = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    StrichCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    KategorieId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HatStueckzahl = table.Column<bool>(type: "bit", nullable: false),
                    Stueckzahl = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MindestbestandWarnung = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    BasisLeistungId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BasisAbzugMenge = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IstAktiv = table.Column<bool>(type: "bit", nullable: false),
                    IstStandardArtikel = table.Column<bool>(type: "bit", nullable: false),
                    BildAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leistungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Leistungen_BlobMediaAssets_BildAssetId",
                        column: x => x.BildAssetId,
                        principalTable: "BlobMediaAssets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Leistungen_LeistungKategorien_KategorieId",
                        column: x => x.KategorieId,
                        principalTable: "LeistungKategorien",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Leistungen_Leistungen_BasisLeistungId",
                        column: x => x.BasisLeistungId,
                        principalTable: "Leistungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Leistungen_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PoZahlungseingaenge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProduktOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NutzungsAboId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Besitzer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Laden = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VonUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BisUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EingangUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErfasstUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Betrag = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ProzentProvision = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FestProvision = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Provision = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Referenz = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnforderungId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoZahlungseingaenge", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PoZahlungseingaenge_NutzungsAbos_NutzungsAboId",
                        column: x => x.NutzungsAboId,
                        principalTable: "NutzungsAbos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PoZahlungseingaenge_PoZahlungsanforderungen_AnforderungId",
                        column: x => x.AnforderungId,
                        principalTable: "PoZahlungsanforderungen",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VertragsHistorien",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NutzungsAboId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BenutzerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZeitpunktUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Aktion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Begruendung = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VertragsHistorien", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VertragsHistorien_NutzungsAbos_NutzungsAboId",
                        column: x => x.NutzungsAboId,
                        principalTable: "NutzungsAbos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeistungFavoriten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeistungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GesetztVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeistungFavoriten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeistungFavoriten_Leistungen_LeistungId",
                        column: x => x.LeistungId,
                        principalTable: "Leistungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Rabatte",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Bedingung = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Art = table.Column<int>(type: "int", nullable: false),
                    Wert = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LeistungId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IstAktiv = table.Column<bool>(type: "bit", nullable: false),
                    GueltigVonUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GueltigBisUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rabatte", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rabatte_Leistungen_LeistungId",
                        column: x => x.LeistungId,
                        principalTable: "Leistungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rabatte_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VerkaufsPositionen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VerkaufId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeistungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeistungName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EinzelPreis = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Menge = table.Column<int>(type: "int", nullable: false),
                    RabattId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RabattBetrag = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ZeilenSumme = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerkaufsPositionen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerkaufsPositionen_Leistungen_LeistungId",
                        column: x => x.LeistungId,
                        principalTable: "Leistungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerkaufsPositionen_Rabatte_RabattId",
                        column: x => x.RabattId,
                        principalTable: "Rabatte",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerkaufsPositionen_Verkaeufe_VerkaufId",
                        column: x => x.VerkaufId,
                        principalTable: "Verkaeufe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

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
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Ausgaben_BelegAssetId",
                table: "Ausgaben",
                column: "BelegAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Ausgaben_KategorieId",
                table: "Ausgaben",
                column: "KategorieId");

            migrationBuilder.CreateIndex(
                name: "IX_Ausgaben_SalonId",
                table: "Ausgaben",
                column: "SalonId");

            migrationBuilder.CreateIndex(
                name: "IX_AusgabenKategorien_SalonId",
                table: "AusgabenKategorien",
                column: "SalonId");

            migrationBuilder.CreateIndex(
                name: "IX_BesitzerHinweise_SalonId_IstUngelesen",
                table: "BesitzerHinweise",
                columns: new[] { "SalonId", "IstUngelesen" });

            migrationBuilder.CreateIndex(
                name: "IX_GeraetSalonFavoriten_GeraeteSchluessel",
                table: "GeraetSalonFavoriten",
                column: "GeraeteSchluessel",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeraetSalonFavoriten_SalonId",
                table: "GeraetSalonFavoriten",
                column: "SalonId");

            migrationBuilder.CreateIndex(
                name: "IX_KassenSitzungen_KassiererId",
                table: "KassenSitzungen",
                column: "KassiererId");

            migrationBuilder.CreateIndex(
                name: "IX_KassenSitzungen_SalonId",
                table: "KassenSitzungen",
                column: "SalonId");

            migrationBuilder.CreateIndex(
                name: "IX_Leistungen_BasisLeistungId",
                table: "Leistungen",
                column: "BasisLeistungId");

            migrationBuilder.CreateIndex(
                name: "IX_Leistungen_BildAssetId",
                table: "Leistungen",
                column: "BildAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Leistungen_KategorieId",
                table: "Leistungen",
                column: "KategorieId");

            migrationBuilder.CreateIndex(
                name: "IX_Leistungen_SalonId",
                table: "Leistungen",
                column: "SalonId");

            migrationBuilder.CreateIndex(
                name: "IX_LeistungFavoriten_LeistungId",
                table: "LeistungFavoriten",
                column: "LeistungId");

            migrationBuilder.CreateIndex(
                name: "IX_LeistungFavoriten_SalonId_LeistungId",
                table: "LeistungFavoriten",
                columns: new[] { "SalonId", "LeistungId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeistungKategorien_SalonId_Name",
                table: "LeistungKategorien",
                columns: new[] { "SalonId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NutzungsAbos_SalonId",
                table: "NutzungsAbos",
                column: "SalonId");

            migrationBuilder.CreateIndex(
                name: "IX_PoGeldbewegungen_AnforderungId",
                table: "PoGeldbewegungen",
                column: "AnforderungId");

            migrationBuilder.CreateIndex(
                name: "IX_PoZahlungseingaenge_AnforderungId",
                table: "PoZahlungseingaenge",
                column: "AnforderungId");

            migrationBuilder.CreateIndex(
                name: "IX_PoZahlungseingaenge_NutzungsAboId",
                table: "PoZahlungseingaenge",
                column: "NutzungsAboId");

            migrationBuilder.CreateIndex(
                name: "IX_ProduktOwnerAuszahlungen_ProduktOwnerId_Jahr_Monat",
                table: "ProduktOwnerAuszahlungen",
                columns: new[] { "ProduktOwnerId", "Jahr", "Monat" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProduktOwnerSalons_ProduktOwnerId_SalonId",
                table: "ProduktOwnerSalons",
                columns: new[] { "ProduktOwnerId", "SalonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProduktOwnerSalons_SalonId",
                table: "ProduktOwnerSalons",
                column: "SalonId",
                unique: true,
                filter: "[GueltigBisUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Rabatte_LeistungId",
                table: "Rabatte",
                column: "LeistungId");

            migrationBuilder.CreateIndex(
                name: "IX_Rabatte_SalonId",
                table: "Rabatte",
                column: "SalonId");

            migrationBuilder.CreateIndex(
                name: "IX_Salons_LogoAssetId",
                table: "Salons",
                column: "LogoAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Salons_Name",
                table: "Salons",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSalons_AppUserId",
                table: "UserSalons",
                column: "AppUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSalons_AppUserId_SalonId",
                table: "UserSalons",
                columns: new[] { "AppUserId", "SalonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSalons_SalonId",
                table: "UserSalons",
                column: "SalonId");

            migrationBuilder.CreateIndex(
                name: "IX_Verkaeufe_KassenSitzungId",
                table: "Verkaeufe",
                column: "KassenSitzungId");

            migrationBuilder.CreateIndex(
                name: "IX_VerkaufsPositionen_LeistungId",
                table: "VerkaufsPositionen",
                column: "LeistungId");

            migrationBuilder.CreateIndex(
                name: "IX_VerkaufsPositionen_RabattId",
                table: "VerkaufsPositionen",
                column: "RabattId");

            migrationBuilder.CreateIndex(
                name: "IX_VerkaufsPositionen_VerkaufId",
                table: "VerkaufsPositionen",
                column: "VerkaufId");

            migrationBuilder.CreateIndex(
                name: "IX_VertragsHistorien_NutzungsAboId",
                table: "VertragsHistorien",
                column: "NutzungsAboId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "Ausgaben");

            migrationBuilder.DropTable(
                name: "BesitzerBetreuungen");

            migrationBuilder.DropTable(
                name: "BesitzerHinweise");

            migrationBuilder.DropTable(
                name: "ExceptionLogs");

            migrationBuilder.DropTable(
                name: "GeraetSalonFavoriten");

            migrationBuilder.DropTable(
                name: "LeistungFavoriten");

            migrationBuilder.DropTable(
                name: "MailNachrichten");

            migrationBuilder.DropTable(
                name: "MonatsPreisStaende");

            migrationBuilder.DropTable(
                name: "PlattformPreise");

            migrationBuilder.DropTable(
                name: "PoAbrechnungsEreignisse");

            migrationBuilder.DropTable(
                name: "PoGeldbewegungen");

            migrationBuilder.DropTable(
                name: "PoZahlungseingaenge");

            migrationBuilder.DropTable(
                name: "ProduktOwnerAuszahlungen");

            migrationBuilder.DropTable(
                name: "ProduktOwnerSalons");

            migrationBuilder.DropTable(
                name: "UserSalons");

            migrationBuilder.DropTable(
                name: "VerkaufsPositionen");

            migrationBuilder.DropTable(
                name: "VertragsHistorien");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AusgabenKategorien");

            migrationBuilder.DropTable(
                name: "PoZahlungsanforderungen");

            migrationBuilder.DropTable(
                name: "Rabatte");

            migrationBuilder.DropTable(
                name: "Verkaeufe");

            migrationBuilder.DropTable(
                name: "NutzungsAbos");

            migrationBuilder.DropTable(
                name: "Leistungen");

            migrationBuilder.DropTable(
                name: "KassenSitzungen");

            migrationBuilder.DropTable(
                name: "LeistungKategorien");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Salons");

            migrationBuilder.DropTable(
                name: "BlobMediaAssets");
        }
    }
}
