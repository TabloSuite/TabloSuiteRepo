using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Organisation;

public sealed record KindKurz(Guid SchuelerId, string Name, string? Klasse, string Beziehung);

public sealed record ElternZeile(Guid KontoId, string Vorname, string Nachname, string Identifiant, string? Telefon, string? Email,
    KontoStatus Status, IReadOnlyList<KindKurz> Kinder)
{
    public string Name => $"{Vorname} {Nachname}".Trim();
}

public sealed record ElternZaehler(int Alle, int OhneKind, int Bloques, int Archives, int SchuelerOhneEltern);

public sealed record ElternEingabe(string? Vorname, string? Nachname, string? Identifiant, string? Email, string? Telefon, Geschlecht Geschlecht);

/// <summary>
/// Elternkonten (Seite « Parents »): anlegen und Kinder verknüpfen (Beziehung), trennen, Liste mit Suche
/// nach Eltern- oder Kindernamen. Höchstens 12 Kinder je Elternkonto; Kinder nur derselben Schule.
/// </summary>
public sealed class ElternService(
    IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, KontoFabrik fabrik, ZugangsAblage ablage, OrgMails mails)
{
    public const int MaxKinder = 12;
    public const string FilterOhneKind = "sans-enfant";
    public const string FilterBloques = "bloques";
    public const string FilterArchives = "archives";

    public static readonly IReadOnlyList<string> Beziehungen = ["Mère", "Père", "Tuteur", "Tutrice", "Autre"];

    private static IQueryable<Konto> Gefiltert(SkoulouDbContext db, Guid schuleId, string? filter)
    {
        var q = db.Users.AsNoTracking().Where(k => k.SchuleId == schuleId && k.Rolle == Rollen.Eltern);
        return filter switch
        {
            FilterOhneKind => q.Where(k => k.Status != KontoStatus.Archive && !db.ElternKinder.Any(ek => ek.ElternId == k.Id)),
            FilterBloques => q.Where(k => k.Status == KontoStatus.Bloque),
            FilterArchives => q.Where(k => k.Status == KontoStatus.Archive),
            _ => q.Where(k => k.Status != KontoStatus.Archive)
        };
    }

    public async Task<Seite<ElternZeile>> SucheAsync(Guid akteurId, string? filter, string? suche, int ueberspringen, int anzahl, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = Gefiltert(db, a.SchuleId, filter);
        if (Eingabe.Suche(suche) is { } s)
        {
            var muster = $"%{s}%";
            var kinderTreffer = from ek in db.ElternKinder
                                join kind in db.Users on ek.SchuelerId equals kind.Id
                                where EF.Functions.Like(kind.Vorname + " " + kind.Nachname, muster) || EF.Functions.Like(kind.Nachname + " " + kind.Vorname, muster)
                                      || db.SchuelerProfile.Any(p => p.KontoId == kind.Id && p.Matrikel != null && EF.Functions.Like(p.Matrikel, muster))
                                select ek.ElternId;
            q = q.Where(k => EF.Functions.Like(k.Vorname + " " + k.Nachname, muster) || EF.Functions.Like(k.Nachname + " " + k.Vorname, muster)
                             || EF.Functions.Like(k.UserName!, muster) || (k.Telefon != null && EF.Functions.Like(k.Telefon, muster))
                             || (k.Email != null && EF.Functions.Like(k.Email, muster)) || kinderTreffer.Contains(k.Id));
        }
        var seite = await Seite<Konto>.LadeAsync(q.OrderBy(k => k.Nachname).ThenBy(k => k.Vorname), ueberspringen, anzahl, ct);
        var ids = seite.Eintraege.Select(k => k.Id).ToList();
        var kinder = await KinderAsync(db, ids, ct);
        return new Seite<ElternZeile>(seite.Eintraege.Select(k => new ElternZeile(k.Id, k.Vorname, k.Nachname, k.UserName!, k.Telefon, k.Email, k.Status,
            kinder.Where(x => x.ElternId == k.Id).Select(x => x.Kind).ToList())).ToList(), seite.Mehr);
    }

    private sealed record KindMitEltern(Guid ElternId, KindKurz Kind);

    private static async Task<List<KindMitEltern>> KinderAsync(SkoulouDbContext db, List<Guid> elternIds, CancellationToken ct)
    {
        var aktive = SchulAbfragen.AktiveEinschreibungen(db);
        return await (from ek in db.ElternKinder.AsNoTracking()
                      join kind in db.Users on ek.SchuelerId equals kind.Id
                      where elternIds.Contains(ek.ElternId)
                      orderby kind.Vorname
                      select new KindMitEltern(ek.ElternId, new KindKurz(kind.Id, kind.Vorname + " " + kind.Nachname,
                          aktive.Where(e => e.SchuelerId == kind.Id).Join(db.Klassen, e => e.KlasseId, c => c.Id, (e, c) => c.Name).FirstOrDefault(),
                          ek.Beziehung)))
            .ToListAsync(ct);
    }

    public async Task<ElternZaehler> ZaehlerAsync(Guid akteurId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ohneEltern = await db.Users.CountAsync(k => k.SchuleId == a.SchuleId && k.Rolle == Rollen.Schueler && k.Status == KontoStatus.Actif
                                                        && !db.ElternKinder.Any(ek => ek.SchuelerId == k.Id), ct);
        return new ElternZaehler(
            await Gefiltert(db, a.SchuleId, null).CountAsync(ct),
            await Gefiltert(db, a.SchuleId, FilterOhneKind).CountAsync(ct),
            await Gefiltert(db, a.SchuleId, FilterBloques).CountAsync(ct),
            await Gefiltert(db, a.SchuleId, FilterArchives).CountAsync(ct),
            ohneEltern);
    }

    public async Task<ElternZeile> DetailAsync(Guid akteurId, Guid elternId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var k = await OrgAbfragen.KontoAsync(db, a, elternId, ct, Rollen.Eltern);
        var kinder = await KinderAsync(db, [k.Id], ct);
        return new ElternZeile(k.Id, k.Vorname, k.Nachname, k.UserName!, k.Telefon, k.Email, k.Status, kinder.Select(x => x.Kind).ToList());
    }

    /// <summary>Kurze Auswahl (höchstens 20) für « Lier un parent existant ».</summary>
    public async Task<IReadOnlyList<AuswahlEintrag>> AuswahlAsync(Guid akteurId, string? suche, CancellationToken ct = default)
    {
        var seite = await SucheAsync(akteurId, null, suche, 0, 20, ct);
        return seite.Eintraege.Select(e => new AuswahlEintrag(e.KontoId, e.Name,
            e.Kinder.Count == 0 ? e.Telefon ?? e.Identifiant : string.Join(", ", e.Kinder.Select(k => k.Name)))).ToList();
    }

    /// <summary>Vorbelegung aus dem Schülerprofil: Nachname des Kindes, Telefon/E-Mail des Elternkontakts.</summary>
    public async Task<ElternEingabe> VorbelegungAsync(Guid akteurId, Guid schuelerId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var kind = await OrgAbfragen.KontoAsync(db, a, schuelerId, ct, Rollen.Schueler);
        var p = await db.SchuelerProfile.AsNoTracking().Where(x => x.KontoId == kind.Id).Select(x => new { x.ElternTelefon, x.ElternEmail }).FirstOrDefaultAsync(ct);
        return new ElternEingabe(null, kind.Nachname, null, p?.ElternEmail, p?.ElternTelefon, Geschlecht.Divers);
    }

    /// <summary>Elternkonto anlegen und (optional) gleich mit einem Kind verknüpfen.</summary>
    public async Task<ZugangsStapel> AnlegenAsync(Guid akteurId, ElternEingabe e, Guid? kindId, string? beziehung, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        var daten = Daten(e);
        var bez = PruefeBeziehung(beziehung);
        var (zugang, schule) = await dbFactory.TransaktionAsync(async db =>
        {
            Konto? kind = kindId is { } k ? await OrgAbfragen.KontoAsync(db, a, k, ct, Rollen.Schueler) : null;
            var identifiant = await fabrik.IdentifiantAsync(db, e.Identifiant, daten.Vorname, daten.Nachname, ct: ct);
            var (konto, passwort) = await fabrik.AnlegenAsync(db, daten, identifiant, Rollen.Eltern, a.SchuleId, a.Id, ct: ct);
            if (kind is not null) db.ElternKinder.Add(new ElternKind { SchuleId = a.SchuleId, ElternId = konto.Id, SchuelerId = kind.Id, Beziehung = bez });
            audit.Erfasse(db, a.Id, a.SchuleId, AuditAktionen.KontoAngelegt, nameof(Konto), konto.Id,
                $"Parent {konto.VollerName} créé(e){(kind is null ? "" : $" et lié(e) à {kind.VollerName} ({bez})")}.");
            await db.SaveChangesAsync(ct);
            var name = await db.Schulen.Where(s => s.Id == a.SchuleId).Select(s => s.Name).FirstAsync(ct);
            return (new Zugang(konto.Id, konto.VollerName, Rollen.Eltern, identifiant, passwort, kind is null ? null : $"{bez} de {kind.VollerName}"), name);
        }, ct, DbArbeit.KontoVersuche);
        await mails.WillkommenAsync(new MailEmpfaenger(daten.Email, daten.Vorname, zugang.Identifiant, Rollen.Eltern), schule, ct);
        return ablage.Ablegen(a.Id, a.SchuleId, [zugang]);
    }

    public async Task AendernAsync(Guid akteurId, Guid elternId, ElternEingabe e, CancellationToken ct = default)
    {
        var daten = Daten(e);
        await dbFactory.TransaktionAsync(async db =>
        {
            var (a, konto) = await KontoVerwaltungService.ZielAsync(zugriff, db, akteurId, elternId, ct);
            if (konto.Rolle != Rollen.Eltern) throw new FachFehler("Ce compte n'est pas un compte de parent.");
            var identifiant = await fabrik.IdentifiantAsync(db, string.IsNullOrWhiteSpace(e.Identifiant) ? konto.UserName : e.Identifiant,
                daten.Vorname, daten.Nachname, konto.Id, ct: ct);
            if (identifiant != konto.UserName) fabrik.SetzeIdentifiant(konto, identifiant);
            konto.Vorname = daten.Vorname;
            konto.Nachname = daten.Nachname;
            konto.Telefon = daten.Telefon;
            konto.Geschlecht = daten.Geschlecht;
            fabrik.SetzeEmail(konto, daten.Email);
            audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.KontoGeaendert, nameof(Konto), konto.Id, $"Données du parent {konto.VollerName} modifiées.");
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>Kind verknüpfen (idempotent: vorhandene Verknüpfung bekommt die neue Beziehung).</summary>
    public async Task VerknuepfenAsync(Guid akteurId, Guid elternId, Guid schuelerId, string? beziehung, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        var bez = PruefeBeziehung(beziehung);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var eltern = await OrgAbfragen.KontoAsync(db, a, elternId, ct, Rollen.Eltern);
        var kind = await OrgAbfragen.KontoAsync(db, a, schuelerId, ct, Rollen.Schueler);
        if (eltern.Status == KontoStatus.Archive || kind.Status == KontoStatus.Archive) throw new FachFehler("Un des deux comptes est archivé.");
        var vorhanden = await db.ElternKinder.FirstOrDefaultAsync(ek => ek.ElternId == eltern.Id && ek.SchuelerId == kind.Id, ct);
        if (vorhanden is not null)
        {
            vorhanden.Beziehung = bez;
        }
        else
        {
            if (await db.ElternKinder.CountAsync(ek => ek.ElternId == eltern.Id, ct) >= MaxKinder)
                throw new FachFehler($"Un compte parent peut être lié à {MaxKinder} enfants au maximum.");
            db.ElternKinder.Add(new ElternKind { SchuleId = a.SchuleId, ElternId = eltern.Id, SchuelerId = kind.Id, Beziehung = bez });
        }
        audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.ElternVerknuepft, nameof(Konto), eltern.Id, $"{eltern.VollerName} lié(e) à {kind.VollerName} ({bez}).");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Verknüpfung lösen (das Elternkonto bleibt bestehen).</summary>
    public async Task TrennenAsync(Guid akteurId, Guid elternId, Guid schuelerId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var eltern = await OrgAbfragen.KontoAsync(db, a, elternId, ct, Rollen.Eltern);
        var link = await db.ElternKinder.FirstOrDefaultAsync(ek => ek.ElternId == eltern.Id && ek.SchuelerId == schuelerId, ct);
        if (link is null) return;
        var kindName = await db.Users.Where(k => k.Id == schuelerId).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefaultAsync(ct);
        db.ElternKinder.Remove(link);
        audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.ElternGetrennt, nameof(Konto), eltern.Id, $"Lien entre {eltern.VollerName} et {kindName} retiré.");
        await db.SaveChangesAsync(ct);
    }

    internal static KontoDaten Daten(ElternEingabe e)
        => new(Eingabe.Pflicht(e.Vorname, 100, "le prénom", "vorname"), Eingabe.Pflicht(e.Nachname, 100, "le nom", "nachname"),
            e.Identifiant, Eingabe.Email(e.Email), Eingabe.Telefon(e.Telefon), e.Geschlecht);

    internal static string PruefeBeziehung(string? beziehung)
    {
        var b = Eingabe.Text(beziehung, 40, "le lien de parenté") ?? "Autre";
        return Beziehungen.FirstOrDefault(x => string.Equals(x, b, StringComparison.OrdinalIgnoreCase)) ?? b;
    }
}
