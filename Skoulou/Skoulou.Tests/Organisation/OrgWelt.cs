using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;
using Skoulou.WebApp.Services.Infrastruktur.Medien;
using Skoulou.WebApp.Services.Organisation;
using Skoulou.WebApp.Services.Organisation.Import;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Organisation;

/// <summary>Medienspeicher im Arbeitsspeicher.</summary>
internal sealed class SpeicherAttrappe : IMedienSpeicher
{
    public Dictionary<string, byte[]> Dateien { get; } = [];

    public async Task<long> SpeichernAsync(string blobName, Stream inhalt, string contentType, CancellationToken ct = default)
    {
        using var ziel = new MemoryStream();
        await inhalt.CopyToAsync(ziel, ct);
        Dateien[blobName] = ziel.ToArray();
        return ziel.Length;
    }

    public Task<Stream?> OeffnenAsync(string blobName, CancellationToken ct = default)
        => Task.FromResult<Stream?>(Dateien.TryGetValue(blobName, out var b) ? new MemoryStream(b) : null);

    public Task<bool> LoeschenAsync(string blobName, CancellationToken ct = default) => Task.FromResult(Dateien.Remove(blobName));
}

/// <summary>Alle Dienste des Bereichs Organisation über einer beliebigen DbContext-Fabrik (InMemory oder SQL Server).</summary>
internal sealed class OrgDienste
{
    public OrgDienste(IDbContextFactory<SkoulouDbContext> db, TimeProvider zeit)
    {
        Db = db;
        Zeit = zeit;
        var cache = new MemoryCache(new MemoryCacheOptions());
        Zugriff = new Zugriff(db);
        var audit = new AuditService(zeit);
        var nummern = new NummernkreisService(zeit);
        var fabrik = new KontoFabrik(new PasswordHasher<Konto>(), new UpperInvariantLookupNormalizer(), zeit);
        Ablage = new ZugangsAblage(cache, zeit);
        var mailAusgang = new MailAusgangService(db, Versender, new MailSignal(), zeit, NullLogger<MailAusgangService>.Instance);
        var mails = new OrgMails(mailAusgang, new OptionsWrapper<AppOptionen>(new AppOptionen { BaseUrl = "https://skoulou.test" }), NullLogger<OrgMails>.Instance);
        var sitzungen = new SitzungsPruefung(db, cache);

        Schule = new SchulService(db, Zugriff, audit, Speicher);
        Jahre = new SchuljahrService(db, Zugriff, audit, zeit);
        Klassen = new KlassenService(db, Zugriff, zeit);
        Faecher = new FaecherService(db, Zugriff);
        Unterricht = new UnterrichtService(db, Zugriff);
        Konten = new KontoVerwaltungService(db, Zugriff, audit, fabrik, Ablage, sitzungen, zeit);
        Personal = new PersonalService(db, Zugriff, audit, fabrik, Ablage, mails);
        Schueler = new SchuelerService(db, Zugriff, audit, fabrik, Ablage, mails, nummern, Speicher, zeit);
        Einschreibung = new EinschreibungService(db, Zugriff, audit, sitzungen, zeit);
        Eltern = new ElternService(db, Zugriff, audit, fabrik, Ablage, mails, zeit);
        ElternKinder = new ElternKinderService(db, Zugriff, zeit);
        Stundenplan = new StundenplanService(db, Zugriff);
        Anwesenheit = new AnwesenheitService(db, Zugriff, zeit);
        Cockpit = new CockpitService(db, Zugriff, zeit);
        Import = new ImportService(db, Zugriff, audit, fabrik, Ablage, mails, nummern, zeit);
    }

    public IDbContextFactory<SkoulouDbContext> Db { get; }
    public TimeProvider Zeit { get; }
    public FakeVersender Versender { get; } = new();
    public SpeicherAttrappe Speicher { get; } = new();
    public Zugriff Zugriff { get; }
    public ZugangsAblage Ablage { get; }
    public SchulService Schule { get; }
    public SchuljahrService Jahre { get; }
    public KlassenService Klassen { get; }
    public ElternKinderService ElternKinder { get; }
    public FaecherService Faecher { get; }
    public UnterrichtService Unterricht { get; }
    public KontoVerwaltungService Konten { get; }
    public PersonalService Personal { get; }
    public SchuelerService Schueler { get; }
    public EinschreibungService Einschreibung { get; }
    public ElternService Eltern { get; }
    public StundenplanService Stundenplan { get; }
    public AnwesenheitService Anwesenheit { get; }
    public CockpitService Cockpit { get; }
    public ImportService Import { get; }
}

/// <summary>
/// Testschule « Collège Alpha » (+ « Collège Beta » als fremde Schule) mit laufendem Jahr 2026-2027 (drei Trimester),
/// Jahr 2027-2028 in Vorbereitung, Stufen 6e/5e, Klassen 6e A/6e B/5e A, Fächern, Lehraufträgen, Schülern,
/// Eltern, einem veröffentlichten und einem Entwurfs-Stundenplan. Konten ohne Passwort (schnell).
/// Heute (Testuhr) = 13/11/2026, mitten im 1er trimestre.
/// </summary>
internal sealed class OrgWelt
{
    public static readonly DateOnly Heute = new(2026, 11, 13);
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 1, 2, 3, 4];

    private OrgWelt(OrgDienste d, string kennung)
    {
        D = d;
        Kennung = kennung;
    }

    public OrgDienste D { get; }
    public string Kennung { get; }
    public string SchulNameA => "Collège Alpha " + Kennung;

    public Guid SchuleA, SchuleB, Jahr, JahrVorb, P1, P2, P3, PV1, PV2, PV3, Stufe6, Stufe5, Klasse6A, Klasse6B, Klasse5A;
    public Guid FachMath, FachFr, Kf6AMath, Kf6AFr, Kf6BMath, Auftrag, Auftrag6B, PlanPub, PlanEntwurf, Stunde;
    public Guid Direktor, Koordinator, Lehrer, Lehrer2, LehrerZiel, KoordZiel, Schueler, SchuelerZiel, SchuelerOhne, SchuelerWartend;
    public Guid Eltern, ElternZiel, Responsable, Admin, DirektorB, EinschreibungSchueler, EinschreibungZiel;

    /// <summary>Konto-Id je Rollenbuchstabe: D, K, L, S, E, R, A (X = Direktor der fremden Schule).</summary>
    public Guid Akteur(char rolle) => rolle switch
    {
        'D' => Direktor,
        'K' => Koordinator,
        'L' => Lehrer,
        'S' => Schueler,
        'E' => Eltern,
        'R' => Responsable,
        'A' => Admin,
        'X' => DirektorB,
        _ => throw new ArgumentOutOfRangeException(nameof(rolle))
    };

    public static Task<OrgWelt> ErstelleAsync() => ErstelleAsync(TestDbContextFactory.Create(), new TestZeit());

    public static async Task<OrgWelt> ErstelleAsync(IDbContextFactory<SkoulouDbContext> fabrik, TimeProvider zeit)
    {
        var w = new OrgWelt(new OrgDienste(fabrik, zeit), Guid.NewGuid().ToString("N")[..6]);
        await w.BefuellenAsync();
        return w;
    }

    private Dictionary<string, Guid> rollen = [];

    private async Task BefuellenAsync()
    {
        await using var db = await D.Db.CreateDbContextAsync();
        foreach (var name in Rollen.Alle)
            if (!await db.Roles.AnyAsync(r => r.Name == name))
                db.Roles.Add(new Rolle { Id = Guid.NewGuid(), Name = name, NormalizedName = name.ToUpperInvariant(), ConcurrencyStamp = Guid.NewGuid().ToString() });
        if (!await db.PlattformEinstellungen.AnyAsync(p => p.Id == PlattformEinstellung.EinzigeId))
            db.PlattformEinstellungen.Add(new PlattformEinstellung { Id = PlattformEinstellung.EinzigeId, JahrespreisProSchueler = 12_500, AgbVersion = string.Empty });
        await db.SaveChangesAsync();
        rollen = await db.Roles.ToDictionaryAsync(r => r.Name!, r => r.Id);

        Responsable = Konto(db, Rollen.Responsable, "resp", null);
        Admin = Konto(db, Rollen.SuperAdmin, "admin", null);
        await db.SaveChangesAsync();

        var a = new Schule { Name = SchulNameA, NameNormalisiert = TextNormalisierung.Schluessel(SchulNameA), Ort = "Lomé", Status = SchulStatus.Active, ResponsableId = Responsable, MatrikelPraefix = "CA" };
        var b = new Schule { Name = "Collège Beta " + Kennung, NameNormalisiert = TextNormalisierung.Schluessel("Collège Beta " + Kennung), Ort = "Kara", Status = SchulStatus.Active };
        db.Schulen.AddRange(a, b);
        await db.SaveChangesAsync();
        SchuleA = a.Id;
        SchuleB = b.Id;

        Direktor = Konto(db, Rollen.Direktor, "dir", SchuleA);
        Koordinator = Konto(db, Rollen.Koordinator, "coord", SchuleA);
        Lehrer = Konto(db, Rollen.Lehrer, "prof", SchuleA);
        Lehrer2 = Konto(db, Rollen.Lehrer, "prof2", SchuleA);
        LehrerZiel = Konto(db, Rollen.Lehrer, "profziel", SchuleA);
        KoordZiel = Konto(db, Rollen.Koordinator, "coordziel", SchuleA);
        Schueler = Konto(db, Rollen.Schueler, "eleve", SchuleA);
        SchuelerZiel = Konto(db, Rollen.Schueler, "eleveziel", SchuleA);
        SchuelerOhne = Konto(db, Rollen.Schueler, "eleveohne", SchuleA);
        SchuelerWartend = Konto(db, Rollen.Schueler, "elevewartend", SchuleA, KontoStatus.EnAttente, selbst: true);
        Eltern = Konto(db, Rollen.Eltern, "parent", SchuleA);
        ElternZiel = Konto(db, Rollen.Eltern, "parentziel", SchuleA);
        DirektorB = Konto(db, Rollen.Direktor, "dirb", SchuleB);
        await db.SaveChangesAsync();

        var jahr = new Schuljahr
        {
            SchuleId = SchuleA, Bezeichnung = "2026-2027", Beginn = new(2026, 9, 1), Ende = new(2027, 6, 30),
            Status = SchuljahrStatus.Laufend, PeriodenArt = PeriodenArt.Trimestre, PlattformJahrespreis = 12_500
        };
        var vorb = new Schuljahr { SchuleId = SchuleA, Bezeichnung = "2027-2028", Beginn = new(2027, 9, 1), Ende = new(2028, 6, 30), PeriodenArt = PeriodenArt.Trimestre };
        db.Schuljahre.AddRange(jahr, vorb);
        Jahr = jahr.Id;
        JahrVorb = vorb.Id;
        P1 = Periode(db, Jahr, 1, "1er trimestre", new(2026, 9, 1), new(2026, 12, 18));
        P2 = Periode(db, Jahr, 2, "2e trimestre", new(2027, 1, 4), new(2027, 3, 26));
        P3 = Periode(db, Jahr, 3, "3e trimestre", new(2027, 4, 6), new(2027, 6, 30));
        PV1 = Periode(db, JahrVorb, 1, "1er trimestre", new(2027, 9, 1), new(2027, 12, 18));
        PV2 = Periode(db, JahrVorb, 2, "2e trimestre", new(2028, 1, 4), new(2028, 3, 26));
        PV3 = Periode(db, JahrVorb, 3, "3e trimestre", new(2028, 4, 6), new(2028, 6, 30));

        var s5 = new Stufe { SchuleId = SchuleA, Name = "5e", Reihenfolge = 2, Zyklus = Zyklus.College };
        var s6 = new Stufe { SchuleId = SchuleA, Name = "6e", Reihenfolge = 1, Zyklus = Zyklus.College, NachfolgerId = s5.Id };
        db.Stufen.AddRange(s5, s6);
        Stufe5 = s5.Id;
        Stufe6 = s6.Id;
        Klasse6A = Klasse(db, Stufe6, "6e A", "Salle 1");
        Klasse6B = Klasse(db, Stufe6, "6e B", "Salle 2");
        Klasse5A = Klasse(db, Stufe5, "5e A", "Salle 3");

        var math = new Fach { SchuleId = SchuleA, Name = "Mathématiques", Kuerzel = "MATH", Gruppe = FachGruppe.Sciences, Reihenfolge = 1 };
        var fr = new Fach { SchuleId = SchuleA, Name = "Français", Kuerzel = "FR", Gruppe = FachGruppe.Lettres, Reihenfolge = 2 };
        db.Faecher.AddRange(math, fr);
        FachMath = math.Id;
        FachFr = fr.Id;
        Kf6AMath = Klassenfach(db, Klasse6A, FachMath, 3);
        Kf6AFr = Klassenfach(db, Klasse6A, FachFr, 2);
        Kf6BMath = Klassenfach(db, Klasse6B, FachMath, 4);
        var auftrag = new Lehrauftrag { SchuleId = SchuleA, KlassenfachId = Kf6AMath, LehrerId = Lehrer, Wochenstunden = 3 };
        var auftrag6B = new Lehrauftrag { SchuleId = SchuleA, KlassenfachId = Kf6BMath, LehrerId = LehrerZiel, Wochenstunden = 4 };
        db.Lehrauftraege.AddRange(auftrag, auftrag6B);
        Auftrag = auftrag.Id;
        Auftrag6B = auftrag6B.Id;
        await db.SaveChangesAsync();

        Profil(db, Schueler, "CA260001");
        Profil(db, SchuelerZiel, "CA260002");
        Profil(db, SchuelerOhne, "CA260003");
        Profil(db, SchuelerWartend, null);
        db.Nummernkreise.Add(new Nummernkreis { SchuleId = SchuleA, Art = NummernArt.Matrikel, Jahr = 2026, Letzte = 3 });
        EinschreibungSchueler = Einschreiben(db, Schueler, Klasse6A);
        EinschreibungZiel = Einschreiben(db, SchuelerZiel, Klasse6A);
        db.ElternKinder.Add(new ElternKind { SchuleId = SchuleA, ElternId = Eltern, SchuelerId = Schueler, Beziehung = "Mère" });

        var plan = new Stundenplan { SchuleId = SchuleA, KlasseId = Klasse6A, GueltigAb = jahr.Beginn, Veroeffentlicht = true };
        var stunde = new StundenplanEintrag { StundenplanId = plan.Id, LehrauftragId = Auftrag, Wochentag = DayOfWeek.Monday, Beginn = new(8, 0), Ende = new(9, 0), Raum = "Salle 1" };
        var entwurf = new Stundenplan { SchuleId = SchuleA, KlasseId = Klasse6B, GueltigAb = jahr.Beginn };
        db.Stundenplaene.AddRange(plan, entwurf);
        db.StundenplanEintraege.Add(stunde);
        PlanPub = plan.Id;
        PlanEntwurf = entwurf.Id;
        Stunde = stunde.Id;
        await db.SaveChangesAsync();
    }

    private Guid Konto(SkoulouDbContext db, string rolle, string name, Guid? schuleId, KontoStatus status = KontoStatus.Actif, bool selbst = false)
    {
        var identifiant = $"{name}.{Kennung}";
        var k = new Konto
        {
            Id = NeueId.Erzeuge(), UserName = identifiant, NormalizedUserName = identifiant.ToUpperInvariant(), Vorname = "Test", Nachname = name,
            Rolle = rolle, SchuleId = schuleId, Status = status, SelbstRegistriert = selbst, AgbVersion = string.Empty,
            SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true
        };
        db.Users.Add(k);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = k.Id, RoleId = rollen[rolle] });
        return k.Id;
    }

    private Guid Periode(SkoulouDbContext db, Guid jahr, int nr, string titel, DateOnly von, DateOnly bis)
    {
        var p = new Periode { SchuleId = SchuleA, SchuljahrId = jahr, Nr = nr, Titel = titel, Beginn = von, Ende = bis };
        db.Perioden.Add(p);
        return p.Id;
    }

    private Guid Klasse(SkoulouDbContext db, Guid stufe, string name, string raum)
    {
        var k = new Klasse { SchuleId = SchuleA, SchuljahrId = Jahr, StufeId = stufe, Name = name, Raum = raum, Kapazitaet = 40 };
        db.Klassen.Add(k);
        return k.Id;
    }

    private Guid Klassenfach(SkoulouDbContext db, Guid klasse, Guid fach, decimal koeff)
    {
        var kf = new Klassenfach { SchuleId = SchuleA, KlasseId = klasse, FachId = fach, Koeffizient = koeff };
        db.Klassenfaecher.Add(kf);
        return kf.Id;
    }

    private void Profil(SkoulouDbContext db, Guid konto, string? matrikel)
        => db.SchuelerProfile.Add(new SchuelerProfil { KontoId = konto, SchuleId = SchuleA, Matrikel = matrikel });

    private Guid Einschreiben(SkoulouDbContext db, Guid schueler, Guid klasse)
    {
        var e = new Einschreibung { SchuleId = SchuleA, SchuelerId = schueler, SchuljahrId = Jahr, KlasseId = klasse, Von = new(2026, 9, 1), Status = EinschreibungsStatus.Active };
        db.Einschreibungen.Add(e);
        return e.Id;
    }

    /// <summary>Gültige Schülerdaten für Anlage/Änderung.</summary>
    public static SchuelerEingabe SchuelerDaten(string vorname, string nachname, string? matrikel = null, string? email = null)
        => new(vorname, nachname, Geschlecht.Weiblich, new DateOnly(2014, 3, 14), "Lomé", "Togolaise", false, false, null, null, email, null, null, matrikel);

    public async Task<T> MitDbAsync<T>(Func<SkoulouDbContext, Task<T>> abfrage)
    {
        await using var db = await D.Db.CreateDbContextAsync();
        return await abfrage(db);
    }
}
