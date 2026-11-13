using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Organisation;

public sealed record PersonalZeile(Guid KontoId, string Vorname, string Nachname, string Rolle, string Identifiant, string? Email, string? Telefon,
    KontoStatus Status, int Auftraege, decimal Wochenstunden, DateTime? LetzteAnmeldungUtc, bool MussPasswortAendern)
{
    public string Name => $"{Vorname} {Nachname}".Trim();
}

public sealed record PersonalZaehler(int Alle, int Enseignants, int Coordination, int SansAffectation, int Bloques, int Archives);

public sealed record PersonalEingabe(string Rolle, string? Vorname, string? Nachname, string? Identifiant, string? Email, string? Telefon, Geschlecht Geschlecht);

public sealed record PersonalDetail(Guid KontoId, string Rolle, string Vorname, string Nachname, string Identifiant, string? Email, string? Telefon,
    Geschlecht Geschlecht, KontoStatus Status);

/// <summary>Personal der Schule (Seite « Équipe »): Lehrer und – nur durch den Direktor – Koordinatoren.</summary>
public sealed class PersonalService(
    IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, KontoFabrik fabrik, ZugangsAblage ablage, OrgMails mails)
{
    public const string FilterEnseignants = "enseignants";
    public const string FilterCoordination = "coordination";
    public const string FilterSansAffectation = "sans-affectation";
    public const string FilterBloques = "bloques";
    public const string FilterArchives = "archives";

    private static IQueryable<Konto> Gefiltert(SkoulouDbContext db, Guid schuleId, Guid? jahrId, string? filter)
    {
        var q = db.Users.AsNoTracking().Where(k => k.SchuleId == schuleId && OrgRollen.Personal.Contains(k.Rolle));
        var klassenDesJahres = db.Klassen.Where(c => c.SchuljahrId == jahrId).Select(c => c.Id);
        return filter switch
        {
            FilterEnseignants => q.Where(k => k.Rolle == Rollen.Lehrer && k.Status != KontoStatus.Archive),
            FilterCoordination => q.Where(k => k.Rolle != Rollen.Lehrer && k.Status != KontoStatus.Archive),
            FilterSansAffectation => q.Where(k => k.Rolle == Rollen.Lehrer && k.Status == KontoStatus.Actif
                && !db.Lehrauftraege.Any(l => l.LehrerId == k.Id
                    && db.Klassenfaecher.Any(kf => kf.Id == l.KlassenfachId && klassenDesJahres.Contains(kf.KlasseId)))),
            FilterBloques => q.Where(k => k.Status == KontoStatus.Bloque),
            FilterArchives => q.Where(k => k.Status == KontoStatus.Archive),
            _ => q.Where(k => k.Status != KontoStatus.Archive)
        };
    }

    public async Task<Seite<PersonalZeile>> SucheAsync(Guid akteurId, Guid? jahrId, string? filter, string? suche, int ueberspringen, int anzahl, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var auftraege = from l in db.Lehrauftraege
                        join kf in db.Klassenfaecher on l.KlassenfachId equals kf.Id
                        join k in db.Klassen on kf.KlasseId equals k.Id
                        where k.SchuljahrId == jahrId
                        select l;
        var q = OrgAbfragen.SucheKonten(Gefiltert(db, a.SchuleId, jahrId, filter), suche)
            .OrderBy(k => k.Rolle == Rollen.Lehrer ? 1 : 0).ThenBy(k => k.Nachname).ThenBy(k => k.Vorname)
            .Select(k => new PersonalZeile(k.Id, k.Vorname, k.Nachname, k.Rolle, k.UserName!, k.Email, k.Telefon, k.Status,
                auftraege.Count(l => l.LehrerId == k.Id), auftraege.Where(l => l.LehrerId == k.Id).Sum(l => (decimal?)l.Wochenstunden) ?? 0,
                k.LetzteAnmeldungUtc, k.MussPasswortAendern));
        return await Seite<PersonalZeile>.LadeAsync(q, ueberspringen, anzahl, ct);
    }

    public async Task<PersonalZaehler> ZaehlerAsync(Guid akteurId, Guid? jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return new PersonalZaehler(
            await Gefiltert(db, a.SchuleId, jahrId, null).CountAsync(ct),
            await Gefiltert(db, a.SchuleId, jahrId, FilterEnseignants).CountAsync(ct),
            await Gefiltert(db, a.SchuleId, jahrId, FilterCoordination).CountAsync(ct),
            await Gefiltert(db, a.SchuleId, jahrId, FilterSansAffectation).CountAsync(ct),
            await Gefiltert(db, a.SchuleId, jahrId, FilterBloques).CountAsync(ct),
            await Gefiltert(db, a.SchuleId, jahrId, FilterArchives).CountAsync(ct));
    }

    public async Task<PersonalDetail> DetailAsync(Guid akteurId, Guid kontoId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var k = await OrgAbfragen.KontoAsync(db, a, kontoId, ct, OrgRollen.Personal);
        return new PersonalDetail(k.Id, k.Rolle, k.Vorname, k.Nachname, k.UserName!, k.Email, k.Telefon, k.Geschlecht, k.Status);
    }

    /// <summary>Lehrer oder (nur Direktor) Koordinator anlegen; Zugangsdaten einmalig im Stapel.</summary>
    public async Task<ZugangsStapel> AnlegenAsync(Guid akteurId, PersonalEingabe e, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        PruefeRolle(a, e.Rolle);
        var daten = Daten(e);
        var (zugang, schule) = await dbFactory.TransaktionAsync(async db =>
        {
            var identifiant = await fabrik.IdentifiantAsync(db, e.Identifiant, daten.Vorname, daten.Nachname, ct: ct);
            var (konto, passwort) = await fabrik.AnlegenAsync(db, daten, identifiant, e.Rolle, a.SchuleId, a.Id, ct: ct);
            audit.Erfasse(db, a.Id, a.SchuleId, AuditAktionen.KontoAngelegt, nameof(Konto), konto.Id,
                $"{Rollen.Bezeichnung(e.Rolle)} {konto.VollerName} créé(e) (identifiant {identifiant}).");
            await db.SaveChangesAsync(ct);
            var name = await db.Schulen.Where(s => s.Id == a.SchuleId).Select(s => s.Name).FirstAsync(ct);
            return (new Zugang(konto.Id, konto.VollerName, e.Rolle, identifiant, passwort), name);
        }, ct);
        await mails.WillkommenAsync(new MailEmpfaenger(daten.Email, daten.Vorname, zugang.Identifiant, e.Rolle), schule, ct);
        return ablage.Ablegen(a.Id, a.SchuleId, [zugang]);
    }

    /// <summary>Daten ändern; Rolle und Status bleiben unverändert (keine stillen Rollenwechsel).</summary>
    public async Task AendernAsync(Guid akteurId, Guid kontoId, PersonalEingabe e, CancellationToken ct = default)
    {
        var daten = Daten(e);
        await dbFactory.TransaktionAsync(async db =>
        {
            var (a, konto) = await KontoVerwaltungService.ZielAsync(zugriff, db, akteurId, kontoId, ct);
            if (!OrgRollen.Personal.Contains(konto.Rolle)) throw new FachFehler("Ce compte n'appartient pas à l'équipe.");
            if (e.Rolle != konto.Rolle) throw new FachFehler("Le rôle d'un compte ne se change pas : créez un nouveau compte si nécessaire.");
            var identifiant = await fabrik.IdentifiantAsync(db, string.IsNullOrWhiteSpace(e.Identifiant) ? konto.UserName : e.Identifiant,
                daten.Vorname, daten.Nachname, konto.Id, ct: ct);
            if (identifiant != konto.UserName) fabrik.SetzeIdentifiant(konto, identifiant);
            konto.Vorname = daten.Vorname;
            konto.Nachname = daten.Nachname;
            konto.Telefon = daten.Telefon;
            konto.Geschlecht = daten.Geschlecht;
            fabrik.SetzeEmail(konto, daten.Email);
            audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.KontoGeaendert, nameof(Konto), konto.Id, $"Données de {konto.VollerName} modifiées.");
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    private static void PruefeRolle(SchulAkteur a, string rolle)
    {
        switch (rolle)
        {
            case Rollen.Lehrer: return;
            case Rollen.Koordinator: Zugriff.Pruefe(a.Akteur, Faehigkeit.KoordinatorenVerwalten, schreiben: true); return;
            default: throw new FachFehler("Choisissez le rôle « Enseignant » ou « Coordinateur ».");
        }
    }

    internal static KontoDaten Daten(PersonalEingabe e)
        => new(Eingabe.Pflicht(e.Vorname, 100, "le prénom", "vorname"), Eingabe.Pflicht(e.Nachname, 100, "le nom", "nachname"),
            e.Identifiant, Eingabe.Email(e.Email), Eingabe.Telefon(e.Telefon), e.Geschlecht);
}
