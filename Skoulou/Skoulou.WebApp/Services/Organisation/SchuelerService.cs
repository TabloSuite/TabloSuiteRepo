using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Organisation;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Medien;

namespace Skoulou.WebApp.Services.Organisation;

public sealed record SchuelerZeile(Guid KontoId, string Vorname, string Nachname, string Identifiant, KontoStatus Status, bool SelbstRegistriert,
    string? Matrikel, Guid? EinschreibungId, Guid? KlasseId, string? Klasse, string? Jahr, bool Wiederholer, int Eltern, DateTime ErstelltAmUtc)
{
    public string Name => $"{Vorname} {Nachname}".Trim();
}

public sealed record SchuelerZaehler(int Alle, int AValider, int SansClasse, int Bloques, int Archives);

public sealed record SchuelerEingabe(string? Vorname, string? Nachname, Geschlecht Geschlecht, DateOnly? Geburtsdatum, string? Geburtsort,
    string? Nationalitaet, bool IstInterne, bool IstAffecte, string? ElternTelefon, string? ElternEmail, string? Email, string? Telefon,
    string? Identifiant, string? Matrikel);

public sealed record EinschreibungZeile(Guid Id, Guid SchuljahrId, string Jahr, Guid KlasseId, string Klasse, DateOnly Von, DateOnly? Bis,
    EinschreibungsStatus Status, bool Wiederholer, string? Austrittsgrund, bool Storniert, string? StornoGrund,
    DateOnly JahrBeginn = default, DateOnly JahrEnde = default, bool JahrOffen = false);

public sealed record ElternKurz(Guid KontoId, string Name, string Identifiant, string Beziehung, string? Telefon, KontoStatus Status);

public sealed record SchuelerDetail(Guid KontoId, string Identifiant, KontoStatus Status, bool SelbstRegistriert, SchuelerEingabe Daten,
    Guid? FotoMediumId, DateTime? LetzteAnmeldungUtc, IReadOnlyList<EinschreibungZeile> Einschreibungen, IReadOnlyList<ElternKurz> Eltern)
{
    public string Name => $"{Daten.Vorname} {Daten.Nachname}".Trim();
    public EinschreibungZeile? Aktive => Einschreibungen.FirstOrDefault(e => e.Status == EinschreibungsStatus.Active && !e.Storniert);
}

/// <summary>
/// Schülerkonten (Seite « Élèves »): Liste mit Filtern, Anlage mit Matrikel und optional Klasse,
/// Bearbeiten ohne Statusänderung, Freigabe/Ablehnung einer Selbstregistrierung, Foto, Kartencode.
/// </summary>
public sealed class SchuelerService(
    IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, KontoFabrik fabrik, ZugangsAblage ablage,
    OrgMails mails, NummernkreisService nummern, IMedienSpeicher speicher, TimeProvider zeit)
{
    public const string FilterAValider = "a-valider";
    public const string FilterSansClasse = "sans-classe";
    public const string FilterBloques = "bloques";
    public const string FilterArchives = "archives";

    private static IQueryable<Konto> Gefiltert(SkoulouDbContext db, Guid schuleId, string? filter, Guid? klasseId, DateOnly heute)
    {
        // Aktuelle Klasse = laufende Einschreibung (Session-Klassen enden automatisch mit ihrer Session).
        var aktive = SchulAbfragen.LaufendeEinschreibungen(db, heute);
        var q = db.Users.AsNoTracking().Where(k => k.SchuleId == schuleId && k.Rolle == Rollen.Schueler);
        q = filter switch
        {
            FilterAValider => q.Where(k => k.Status == KontoStatus.EnAttente),
            FilterSansClasse => q.Where(k => k.Status == KontoStatus.Actif && !aktive.Any(e => e.SchuelerId == k.Id)),
            FilterBloques => q.Where(k => k.Status == KontoStatus.Bloque),
            FilterArchives => q.Where(k => k.Status == KontoStatus.Archive),
            _ => q.Where(k => k.Status != KontoStatus.Archive)
        };
        if (klasseId is { } kl) q = q.Where(k => aktive.Any(e => e.SchuelerId == k.Id && e.KlasseId == kl));
        return q;
    }

    public async Task<Seite<SchuelerZeile>> SucheAsync(Guid akteurId, string? filter, Guid? klasseId, string? suche, int ueberspringen, int anzahl, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var heute = new SchulZeit(zeit).Heute;
        var q = Gefiltert(db, a.SchuleId, filter, klasseId, heute);
        if (Eingabe.Suche(suche) is { } s)
        {
            var muster = $"%{s}%";
            q = q.Where(k => EF.Functions.Like(k.Vorname + " " + k.Nachname, muster) || EF.Functions.Like(k.Nachname + " " + k.Vorname, muster)
                             || EF.Functions.Like(k.UserName!, muster)
                             || db.SchuelerProfile.Any(p => p.KontoId == k.Id && p.Matrikel != null && EF.Functions.Like(p.Matrikel, muster)));
        }
        var aktive = SchulAbfragen.LaufendeEinschreibungen(db, heute);
        var sortiert = filter == FilterAValider ? q.OrderBy(k => k.ErstelltAmUtc) : q.OrderBy(k => k.Nachname).ThenBy(k => k.Vorname);
        var abfrage = sortiert.Select(k => new SchuelerZeile(k.Id, k.Vorname, k.Nachname, k.UserName!, k.Status, k.SelbstRegistriert,
            db.SchuelerProfile.Where(p => p.KontoId == k.Id).Select(p => p.Matrikel).FirstOrDefault(),
            aktive.Where(e => e.SchuelerId == k.Id).Select(e => (Guid?)e.Id).FirstOrDefault(),
            aktive.Where(e => e.SchuelerId == k.Id).Select(e => (Guid?)e.KlasseId).FirstOrDefault(),
            aktive.Where(e => e.SchuelerId == k.Id).Join(db.Klassen, e => e.KlasseId, c => c.Id, (e, c) => c.Name).FirstOrDefault(),
            aktive.Where(e => e.SchuelerId == k.Id).Join(db.Schuljahre, e => e.SchuljahrId, j => j.Id, (e, j) => j.Bezeichnung).FirstOrDefault(),
            aktive.Any(e => e.SchuelerId == k.Id && e.IstWiederholer),
            db.ElternKinder.Count(ek => ek.SchuelerId == k.Id),
            k.ErstelltAmUtc));
        return await Seite<SchuelerZeile>.LadeAsync(abfrage, ueberspringen, anzahl, ct);
    }

    public async Task<SchuelerZaehler> ZaehlerAsync(Guid akteurId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var heute = new SchulZeit(zeit).Heute;
        return new SchuelerZaehler(
            await Gefiltert(db, a.SchuleId, null, null, heute).CountAsync(ct),
            await Gefiltert(db, a.SchuleId, FilterAValider, null, heute).CountAsync(ct),
            await Gefiltert(db, a.SchuleId, FilterSansClasse, null, heute).CountAsync(ct),
            await Gefiltert(db, a.SchuleId, FilterBloques, null, heute).CountAsync(ct),
            await Gefiltert(db, a.SchuleId, FilterArchives, null, heute).CountAsync(ct));
    }

    /// <summary>Kurze Auswahl (höchstens 20) für Verknüpfungen, z. B. Kind eines Elternteils.</summary>
    public async Task<IReadOnlyList<AuswahlEintrag>> AuswahlAsync(Guid akteurId, string? suche, CancellationToken ct = default)
    {
        var seite = await SucheAsync(akteurId, null, null, suche, 0, 20, ct);
        return seite.Eintraege.Select(s => new AuswahlEintrag(s.KontoId, s.Name, s.Klasse ?? s.Matrikel)).ToList();
    }

    public async Task<SchuelerDetail> DetailAsync(Guid akteurId, Guid schuelerId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var k = await OrgAbfragen.KontoAsync(db, a, schuelerId, ct, Rollen.Schueler);
        var p = await db.SchuelerProfile.AsNoTracking().FirstOrDefaultAsync(x => x.KontoId == k.Id, ct) ?? new SchuelerProfil();
        var einschreibungen = await (from e in db.Einschreibungen.AsNoTracking()
                                     join kl in db.Klassen on e.KlasseId equals kl.Id
                                     join j in db.Schuljahre on e.SchuljahrId equals j.Id
                                     where e.SchuelerId == k.Id
                                     orderby e.Von descending, e.ErstelltAmUtc descending
                                     select new EinschreibungZeile(e.Id, j.Id, j.Bezeichnung, kl.Id, kl.Name, e.Von, e.Bis, e.Status, e.IstWiederholer,
                                         e.Austrittsgrund, e.StorniertAmUtc != null, e.StornoGrund, j.Beginn, j.Ende,
                                         j.Status != SchuljahrStatus.Abgeschlossen)).ToListAsync(ct);
        var eltern = await (from ek in db.ElternKinder.AsNoTracking()
                            join u in db.Users on ek.ElternId equals u.Id
                            where ek.SchuelerId == k.Id
                            orderby u.Nachname
                            select new ElternKurz(u.Id, u.Vorname + " " + u.Nachname, u.UserName!, ek.Beziehung, u.Telefon, u.Status)).ToListAsync(ct);
        var daten = new SchuelerEingabe(k.Vorname, k.Nachname, k.Geschlecht, k.Geburtsdatum, p.Geburtsort, p.Nationalitaet, p.IstInterne, p.IstAffecte,
            p.ElternTelefon, p.ElternEmail, k.Email, k.Telefon, k.UserName, p.Matrikel);
        return new SchuelerDetail(k.Id, k.UserName!, k.Status, k.SelbstRegistriert, daten, k.FotoMediumId, k.LetzteAnmeldungUtc, einschreibungen, eltern);
    }

    /// <summary>
    /// Schüler anlegen (sofort aktiv, Startpasswort, Matrikel automatisch) – optional gleich in eine Klasse, eingeschrieben
    /// ab <paramref name="von"/> (Standard: Beginn des Schuljahres, <see cref="EinschreibungsBeginn"/>).
    /// </summary>
    public async Task<ZugangsStapel> AnlegenAsync(Guid akteurId, SchuelerEingabe e, Guid? klasseId, bool wiederholer = false, DateOnly? von = null,
        CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        var daten = Daten(e);
        var heute = new SchulZeit(zeit).Heute;
        var (zugang, schulName) = await dbFactory.TransaktionAsync(async db =>
        {
            var schule = await db.Schulen.AsNoTracking().FirstAsync(s => s.Id == a.SchuleId, ct);
            Klasse? klasse = null;
            Schuljahr? jahr = null;
            var beginn = heute;
            if (klasseId is { } kid)
            {
                klasse = await OrgAbfragen.KlasseAsync(db, a, kid, ct);
                jahr = await db.Schuljahre.FirstAsync(j => j.Id == klasse.SchuljahrId, ct);
                EinschreibungService.PruefeKlasseOffen(klasse, jahr);
                beginn = EinschreibungService.Beginn(jahr, von);
            }
            var identifiant = await fabrik.IdentifiantAsync(db, e.Identifiant, daten.Konto.Vorname, daten.Konto.Nachname, ct: ct);
            var matrikel = daten.Matrikel is { } m
                ? await PruefeMatrikelFreiAsync(db, a.SchuleId, m, null, ct)
                : await NaechsteMatrikelAsync(db, nummern, schule, (jahr?.Beginn ?? heute).Year, null, ct);
            var (konto, passwort) = await fabrik.AnlegenAsync(db, daten.Konto, identifiant, Rollen.Schueler, a.SchuleId, a.Id, ct: ct);
            db.SchuelerProfile.Add(NeuesProfil(konto.Id, a.SchuleId, matrikel, daten));
            if (klasse is not null && jahr is not null)
            {
                db.Einschreibungen.Add(new Einschreibung
                {
                    SchuleId = a.SchuleId, SchuelerId = konto.Id, SchuljahrId = jahr.Id, KlasseId = klasse.Id, Von = beginn,
                    Status = EinschreibungsStatus.Active, IstWiederholer = wiederholer
                });
            }
            audit.Erfasse(db, a.Id, a.SchuleId, AuditAktionen.KontoAngelegt, nameof(Konto), konto.Id,
                $"Élève {konto.VollerName} créé(e) (matricule {matrikel}{(klasse is null ? "" : $", classe {klasse.Name}")}).");
            await db.SaveChangesAsync(ct);
            return (new Zugang(konto.Id, konto.VollerName, Rollen.Schueler, identifiant, passwort, klasse?.Name), schule.Name);
        }, ct, DbArbeit.KontoVersuche);
        await mails.WillkommenAsync(new MailEmpfaenger(daten.Konto.Email, daten.Konto.Vorname, zugang.Identifiant, Rollen.Schueler), schulName, ct);
        return ablage.Ablegen(a.Id, a.SchuleId, [zugang]);
    }

    /// <summary>Daten ändern – der Kontostatus bleibt unverändert (Freigabe nur über « Valider l'inscription »).</summary>
    public async Task AendernAsync(Guid akteurId, Guid schuelerId, SchuelerEingabe e, CancellationToken ct = default)
    {
        var daten = Daten(e);
        await dbFactory.TransaktionAsync(async db =>
        {
            var (a, konto) = await KontoVerwaltungService.ZielAsync(zugriff, db, akteurId, schuelerId, ct);
            if (konto.Rolle != Rollen.Schueler) throw new FachFehler("Ce compte n'est pas un compte d'élève.");
            var identifiant = await fabrik.IdentifiantAsync(db, string.IsNullOrWhiteSpace(e.Identifiant) ? konto.UserName : e.Identifiant,
                daten.Konto.Vorname, daten.Konto.Nachname, konto.Id, ct: ct);
            if (identifiant != konto.UserName) fabrik.SetzeIdentifiant(konto, identifiant);
            konto.Vorname = daten.Konto.Vorname;
            konto.Nachname = daten.Konto.Nachname;
            konto.Telefon = daten.Konto.Telefon;
            konto.Geschlecht = daten.Konto.Geschlecht;
            konto.Geburtsdatum = daten.Konto.Geburtsdatum;
            fabrik.SetzeEmail(konto, daten.Konto.Email);
            var profil = await db.SchuelerProfile.FirstOrDefaultAsync(p => p.KontoId == konto.Id, ct);
            if (profil is null)
            {
                var schule = await db.Schulen.AsNoTracking().FirstAsync(s => s.Id == a.SchuleId, ct);
                var m = daten.Matrikel is { } mm ? await PruefeMatrikelFreiAsync(db, a.SchuleId, mm, null, ct)
                    : await NaechsteMatrikelAsync(db, nummern, schule, new SchulZeit(zeit).Heute.Year, null, ct);
                db.SchuelerProfile.Add(NeuesProfil(konto.Id, a.SchuleId, m, daten));
            }
            else
            {
                if (daten.Matrikel is { } m && m != profil.Matrikel) profil.Matrikel = await PruefeMatrikelFreiAsync(db, a.SchuleId, m, profil.Id, ct);
                UebernehmeProfil(profil, daten);
            }
            audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.KontoGeaendert, nameof(Konto), konto.Id, $"Données de l'élève {konto.VollerName} modifiées.");
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>
    /// « Valider l'inscription »: Selbstregistrierung freigeben und in einem Schritt einschreiben – ab <paramref name="von"/>
    /// (Standard: Beginn des Schuljahres, <see cref="EinschreibungsBeginn"/>).
    /// </summary>
    public async Task ValidierenAsync(Guid akteurId, Guid schuelerId, Guid klasseId, bool wiederholer, DateOnly? von = null, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        var (empfaenger, schulName, klassenName) = await dbFactory.TransaktionAsync(async db =>
        {
            var konto = await OrgAbfragen.KontoAsync(db, a, schuelerId, ct, Rollen.Schueler);
            if (konto.Status != KontoStatus.EnAttente) throw new FachFehler("Cette inscription a déjà été traitée.");
            var klasse = await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
            var jahr = await db.Schuljahre.FirstAsync(j => j.Id == klasse.SchuljahrId, ct);
            EinschreibungService.PruefeKlasseOffen(klasse, jahr);
            var beginn = EinschreibungService.Beginn(jahr, von);
            var schule = await db.Schulen.AsNoTracking().FirstAsync(s => s.Id == a.SchuleId, ct);
            var profil = await db.SchuelerProfile.FirstOrDefaultAsync(p => p.KontoId == konto.Id, ct);
            if (profil is null)
            {
                profil = new SchuelerProfil { KontoId = konto.Id, SchuleId = a.SchuleId, Matrikel = await NaechsteMatrikelAsync(db, nummern, schule, jahr.Beginn.Year, null, ct) };
                db.SchuelerProfile.Add(profil);
            }
            else if (profil.Matrikel is null)
            {
                profil.Matrikel = await NaechsteMatrikelAsync(db, nummern, schule, jahr.Beginn.Year, null, ct);
            }
            profil.AufnahmeBestaetigtAmUtc = zeit.GetUtcNow().UtcDateTime;
            konto.Status = KontoStatus.Actif;
            if (!await SchulAbfragen.AktiveEinschreibungen(db).AnyAsync(e => e.SchuelerId == konto.Id, ct))
            {
                db.Einschreibungen.Add(new Einschreibung
                {
                    SchuleId = a.SchuleId, SchuelerId = konto.Id, SchuljahrId = jahr.Id, KlasseId = klasse.Id, Von = beginn,
                    Status = EinschreibungsStatus.Active, IstWiederholer = wiederholer
                });
            }
            audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.InscriptionValidiert, nameof(Konto), konto.Id, $"Inscription de {konto.VollerName} validée en {klasse.Name}.");
            await db.SaveChangesAsync(ct);
            return (new MailEmpfaenger(konto.Email, konto.Vorname, konto.UserName!, Rollen.Schueler), schule.Name, klasse.Name);
        }, ct);
        await mails.AktiviertAsync(empfaenger, schulName, klassenName, ct);
    }

    /// <summary>Selbstregistrierung ablehnen: ohne Schuldaten wird das Konto gelöscht, sonst archiviert.</summary>
    public async Task AblehnenAsync(Guid akteurId, Guid schuelerId, string? grund, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        var text = Eingabe.Text(grund, 300, "le motif");
        var (empfaenger, schulName) = await dbFactory.TransaktionAsync(async db =>
        {
            var konto = await OrgAbfragen.KontoAsync(db, a, schuelerId, ct, Rollen.Schueler);
            if (konto.Status != KontoStatus.EnAttente) throw new FachFehler("Cette inscription a déjà été traitée.");
            var empfaenger = new MailEmpfaenger(konto.Email, konto.Vorname, konto.UserName!, Rollen.Schueler);
            var hatDaten = await db.Einschreibungen.AnyAsync(e => e.SchuelerId == konto.Id, ct) || await db.Versuche.AnyAsync(v => v.SchuelerId == konto.Id, ct)
                           || await db.Zahlungen.AnyAsync(z => z.SchuelerId == konto.Id, ct);
            audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.InscriptionAbgelehnt, nameof(Konto), konto.Id,
                $"Inscription de {konto.VollerName} refusée{(text is null ? "" : $" : {text}")}.");
            if (hatDaten)
            {
                konto.Status = KontoStatus.Archive;
            }
            else
            {
                db.ElternKinder.RemoveRange(await db.ElternKinder.Where(x => x.SchuelerId == konto.Id).ToListAsync(ct));
                db.SchuelerProfile.RemoveRange(await db.SchuelerProfile.Where(x => x.KontoId == konto.Id).ToListAsync(ct));
                db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == konto.Id).ToListAsync(ct));
                await db.SaveChangesAsync(ct);
                db.Users.Remove(konto);
            }
            await db.SaveChangesAsync(ct);
            return (empfaenger, await db.Schulen.Where(s => s.Id == a.SchuleId).Select(s => s.Name).FirstAsync(ct));
        }, ct);
        await mails.AbgelehntAsync(empfaenger, schulName, text, ct);
    }

    /// <summary>Foto für die Schülerkarte (PNG/JPEG/WebP, höchstens 5 MB).</summary>
    public async Task<Guid> FotoHochladenAsync(Guid akteurId, Guid schuelerId, Stream datei, string? dateiname, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        var (inhalt, typ) = await BildDatei.LeseAsync(datei, ct);
        return await dbFactory.TransaktionAsync(async db =>
        {
            var konto = await OrgAbfragen.KontoAsync(db, a, schuelerId, ct, Rollen.Schueler);
            var medium = await BildDatei.SpeichereAsync(db, speicher, a.SchuleId, konto.Id, MedienZweck.Foto, inhalt, typ, dateiname, ct);
            if (konto.FotoMediumId is { } alt)
                db.MedienNutzungen.RemoveRange(await db.MedienNutzungen.Where(n => n.MediumId == alt && n.Typ == "Konto.Foto").ToListAsync(ct));
            db.MedienNutzungen.Add(new MedienNutzung { MediumId = medium.Id, Typ = "Konto.Foto", ZielId = konto.Id });
            await db.SaveChangesAsync(ct);
            konto.FotoMediumId = medium.Id;
            await db.SaveChangesAsync(ct);
            return medium.Id;
        }, ct);
    }

    public async Task FotoEntfernenAsync(Guid akteurId, Guid schuelerId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var konto = await OrgAbfragen.KontoAsync(db, a, schuelerId, ct, Rollen.Schueler);
        if (konto.FotoMediumId is { } alt)
            db.MedienNutzungen.RemoveRange(await db.MedienNutzungen.Where(n => n.MediumId == alt && n.Typ == "Konto.Foto").ToListAsync(ct));
        konto.FotoMediumId = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Neuer QR-Code der Schülerkarte (alte Karte wird ungültig, z. B. bei Verlust).</summary>
    public async Task KartenCodeErneuernAsync(Guid akteurId, Guid schuelerId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var konto = await OrgAbfragen.KontoAsync(db, a, schuelerId, ct, Rollen.Schueler);
        var profil = await db.SchuelerProfile.FirstOrDefaultAsync(p => p.KontoId == konto.Id, ct) ?? throw new FachFehler("Le profil de l'élève est incomplet.");
        profil.KartenToken = NeuerKartenToken();
        audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.KartenCodeErneuert, nameof(Konto), konto.Id, $"Carte d'élève de {konto.VollerName} renouvelée (ancien code invalide).");
        await db.SaveChangesAsync(ct);
    }

    public static string NeuerKartenToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    // ---------------- Hilfen (auch für Import und Einschreibungen) ----------------

    internal sealed record GeprueftDaten(KontoDaten Konto, string? Geburtsort, string? Nationalitaet, bool IstInterne, bool IstAffecte,
        string? ElternTelefon, string? ElternEmail, string? Matrikel);

    internal GeprueftDaten Daten(SchuelerEingabe e)
    {
        if (e.Geburtsdatum is { } g && (g.Year < 1900 || g > new SchulZeit(zeit).Heute))
            throw new FachFehler("La date de naissance n'est pas plausible.", "geburtsdatum");
        var matrikel = MatrikelRegel.Normalisiere(e.Matrikel);
        if (matrikel is not null && !MatrikelRegel.IstGueltig(matrikel)) throw new FachFehler(MatrikelRegel.MeldungFormat, "matrikel");
        return new GeprueftDaten(
            new KontoDaten(Eingabe.Pflicht(e.Vorname, 100, "le prénom", "vorname"), Eingabe.Pflicht(e.Nachname, 100, "le nom", "nachname"),
                e.Identifiant, Eingabe.Email(e.Email), Eingabe.Telefon(e.Telefon), e.Geschlecht, e.Geburtsdatum),
            Eingabe.Text(e.Geburtsort, 150, "le lieu de naissance"), Eingabe.Text(e.Nationalitaet, 80, "la nationalité"), e.IstInterne, e.IstAffecte,
            Eingabe.Telefon(e.ElternTelefon, "elternTelefon"), Eingabe.Email(e.ElternEmail, "elternEmail"), matrikel);
    }

    internal static SchuelerProfil NeuesProfil(Guid kontoId, Guid schuleId, string matrikel, GeprueftDaten d)
    {
        var p = new SchuelerProfil { KontoId = kontoId, SchuleId = schuleId, Matrikel = matrikel };
        UebernehmeProfil(p, d);
        return p;
    }

    private static void UebernehmeProfil(SchuelerProfil p, GeprueftDaten d)
    {
        p.Geburtsort = d.Geburtsort;
        p.Nationalitaet = d.Nationalitaet;
        p.IstInterne = d.IstInterne;
        p.IstAffecte = d.IstAffecte;
        p.ElternTelefon = d.ElternTelefon;
        p.ElternEmail = d.ElternEmail;
    }

    /// <summary>Manuelle Matrikel prüfen (eindeutig je Schule, auch gegen archivierte Schüler).</summary>
    internal static async Task<string> PruefeMatrikelFreiAsync(SkoulouDbContext db, Guid schuleId, string matrikel, Guid? ausserProfil, CancellationToken ct)
    {
        if (await db.SchuelerProfile.AnyAsync(p => p.SchuleId == schuleId && p.Matrikel == matrikel && p.Id != ausserProfil, ct))
            throw new FachFehler($"Le matricule {matrikel} est déjà attribué à un autre élève.", "matrikel");
        return matrikel;
    }

    /// <summary>Nächste freie automatische Matrikel « {Präfix}{JJ}{0001} » (überspringt manuell vergebene).</summary>
    internal static async Task<string> NaechsteMatrikelAsync(SkoulouDbContext db, NummernkreisService nummern, Schule schule, int jahr,
        ISet<string>? reserviert, CancellationToken ct)
    {
        var praefix = schule.MatrikelPraefix ?? NummernFormat.PraefixAusName(schule.Name);
        for (var i = 0; i < 50; i++)
        {
            var m = await nummern.NaechsteNummerAsync(db, schule.Id, NummernArt.Matrikel, jahr, praefix, ct);
            if (reserviert?.Contains(m) == true) continue;
            if (!await db.SchuelerProfile.AnyAsync(p => p.SchuleId == schule.Id && p.Matrikel == m, ct)) return m;
        }
        throw new FachFehler("Aucun matricule libre n'a été trouvé : saisissez le matricule à la main.");
    }
}
