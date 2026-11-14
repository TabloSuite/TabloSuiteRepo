using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Zeile der Schulliste im /admin.</summary>
public sealed record SchulZeile(
    Guid Id, string Name, string Ort, string Land, SchulStatus Status, string? StatusGrund, DateTime ErstelltAmUtc,
    Guid? ResponsableId, string? Responsable, int Schueler, int Personal, string? Direktor, string? DirektorEmail, long? PreisOverride);

/// <summary>Anzahl Schulen je Status.</summary>
public sealed record SchulZaehler(int Alle, int EnAttente, int Active, int Suspendue, int Refusee);

/// <summary>Auswahl eines Responsable (Name, Zahl der betreuten Schulen).</summary>
public sealed record ResponsableOption(Guid Id, string Name, int Schulen);

/// <summary>
/// Schulen verwalten (nur SuperAdmin): validieren/ablehnen mit Mail, suspendieren/reaktivieren mit Grund
/// und Mail an die Direktion, Responsable zuweisen. Jede Statusänderung wird protokolliert (Audit).
/// </summary>
public sealed class SchulVerwaltungService(
    IDbContextFactory<SkoulouDbContext> dbFactory,
    Zugriff zugriff,
    AuditService audit,
    PlattformMails mails)
{
    public const string FilterEnAttente = "en-attente";
    public const string FilterActive = "active";
    public const string FilterSuspendue = "suspendue";
    public const string FilterRefusee = "refusee";

    public static string StatusText(SchulStatus status) => status switch
    {
        SchulStatus.EnAttente => "À valider",
        SchulStatus.Active => "Active",
        SchulStatus.Suspendue => "Suspendue",
        _ => "Refusée"
    };

    public static string StatusArt(SchulStatus status) => status switch
    {
        SchulStatus.EnAttente => "warn",
        SchulStatus.Active => "erfolg",
        SchulStatus.Suspendue => "fehler",
        _ => "neutral"
    };

    public async Task<SchulZaehler> ZaehlerAsync(Guid akteurId, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await ZaehleAsync(db, ct);
    }

    internal static async Task<SchulZaehler> ZaehleAsync(SkoulouDbContext db, CancellationToken ct)
    {
        var je = await db.Schulen.GroupBy(s => s.Status).Select(g => new { g.Key, Anzahl = g.Count() }).ToListAsync(ct);
        int Zahl(SchulStatus s) => je.FirstOrDefault(x => x.Key == s)?.Anzahl ?? 0;
        return new SchulZaehler(je.Sum(x => x.Anzahl), Zahl(SchulStatus.EnAttente), Zahl(SchulStatus.Active), Zahl(SchulStatus.Suspendue), Zahl(SchulStatus.Refusee));
    }

    public async Task<(IReadOnlyList<SchulZeile> Eintraege, bool Mehr)> SucheAsync(Guid akteurId, string? suche, string? filter,
        int ueberspringen = 0, int anzahl = 50, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Schulen.AsNoTracking();
        q = filter switch
        {
            FilterEnAttente => q.Where(s => s.Status == SchulStatus.EnAttente),
            FilterActive => q.Where(s => s.Status == SchulStatus.Active),
            FilterSuspendue => q.Where(s => s.Status == SchulStatus.Suspendue),
            FilterRefusee => q.Where(s => s.Status == SchulStatus.Refusee),
            _ => q
        };
        if (PlattformListen.Suchtext(suche) is { } t)
            q = q.Where(s => s.Name.Contains(t) || s.Ort.Contains(t) || s.Land.Contains(t));
        var sortiert = filter == FilterEnAttente ? q.OrderBy(s => s.ErstelltAmUtc) : q.OrderBy(s => s.Status).ThenBy(s => s.Name);
        return await PlattformListen.SeiteAsync(Projektion(db, sortiert), ueberspringen, anzahl, ct);
    }

    internal static IQueryable<SchulZeile> Projektion(SkoulouDbContext db, IQueryable<Schule> schulen)
        => schulen.Select(s => new SchulZeile(
            s.Id, s.Name, s.Ort, s.Land, s.Status, s.StatusGrund, s.ErstelltAmUtc, s.ResponsableId,
            db.Users.Where(r => r.Id == s.ResponsableId).Select(r => r.Vorname + " " + r.Nachname).FirstOrDefault(),
            db.Users.Count(k => k.SchuleId == s.Id && k.Rolle == Rollen.Schueler && k.Status == KontoStatus.Actif),
            db.Users.Count(k => k.SchuleId == s.Id && (k.Rolle == Rollen.Lehrer || k.Rolle == Rollen.Direktor || k.Rolle == Rollen.Koordinator)
                                && k.Status != KontoStatus.Archive),
            db.Users.Where(k => k.SchuleId == s.Id && k.Rolle == Rollen.Direktor).OrderBy(k => k.ErstelltAmUtc).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefault(),
            db.Users.Where(k => k.SchuleId == s.Id && k.Rolle == Rollen.Direktor).OrderBy(k => k.ErstelltAmUtc).Select(k => k.Email).FirstOrDefault(),
            s.JahrespreisOverride));

    /// <summary>Schule validieren: Status « Active », wartende Direktionskonten werden aktiv, Mail an die Direktion.</summary>
    public Task ValidierenAsync(Guid akteurId, Guid schuleId, CancellationToken ct = default)
        => StatusAendernAsync(akteurId, schuleId, SchulStatus.Active, null, [SchulStatus.EnAttente, SchulStatus.Refusee],
            "Cet établissement a déjà été traité.", PlattformAudit.SchuleValidiert, "validé",
            (an, schule) => mails.SchuleValidiertAsync(an.Email, an.Vorname, schule, ct), ct);

    /// <summary>Registrierung ablehnen (Grund Pflicht, Mail an die Direktion). Daten bleiben erhalten.</summary>
    public Task AblehnenAsync(Guid akteurId, Guid schuleId, string? grund, CancellationToken ct = default)
    {
        var text = Grund(grund);
        return StatusAendernAsync(akteurId, schuleId, SchulStatus.Refusee, text, [SchulStatus.EnAttente],
            "Seule une inscription en attente peut être refusée.", PlattformAudit.SchuleAbgelehnt, "refusé",
            (an, schule) => mails.SchuleAbgelehntAsync(an.Email, an.Vorname, schule, text, ct), ct);
    }

    /// <summary>Aktive Schule suspendieren (Grund Pflicht, Mail an die Direktion); alle Schulkonten werden abgemeldet.</summary>
    public Task SuspendierenAsync(Guid akteurId, Guid schuleId, string? grund, CancellationToken ct = default)
    {
        var text = Grund(grund);
        return StatusAendernAsync(akteurId, schuleId, SchulStatus.Suspendue, text, [SchulStatus.Active],
            "Seul un établissement actif peut être suspendu.", PlattformAudit.SchuleSuspendiert, "suspendu",
            (an, schule) => mails.SchuleSuspendiertAsync(an.Email, an.Vorname, schule, text, ct), ct);
    }

    public Task ReaktivierenAsync(Guid akteurId, Guid schuleId, CancellationToken ct = default)
        => StatusAendernAsync(akteurId, schuleId, SchulStatus.Active, null, [SchulStatus.Suspendue],
            "Seul un établissement suspendu peut être réactivé.", PlattformAudit.SchuleReaktiviert, "réactivé",
            (an, schule) => mails.SchuleReaktiviertAsync(an.Email, an.Vorname, schule, ct), ct);

    private static string Grund(string? grund)
    {
        var t = Eingabe.Pflicht(grund, 500, "le motif", "motif");
        return t.Length < 5 ? throw new FachFehler("Précisez le motif (au moins 5 caractères).", "motif") : t;
    }

    private sealed record Direktion(string? Email, string Vorname);

    private async Task StatusAendernAsync(Guid akteurId, Guid schuleId, SchulStatus neu, string? grund, SchulStatus[] erlaubtVon,
        string meldungFalsch, string aktion, string verb, Func<Direktion, string, Task> mail, CancellationToken ct)
    {
        var akteur = await zugriff.AdminAsync(akteurId, ct);
        var (name, direktion) = await dbFactory.TransaktionAsync(async db =>
        {
            var schule = await db.Schulen.FirstOrDefaultAsync(s => s.Id == schuleId, ct) ?? throw new FachFehler("Établissement introuvable.");
            if (!erlaubtVon.Contains(schule.Status)) throw new FachFehler(meldungFalsch);
            var alt = schule.Status;
            schule.Status = neu;
            schule.StatusGrund = grund;
            var direktoren = await db.Users.Where(k => k.SchuleId == schuleId && k.Rolle == Rollen.Direktor).ToListAsync(ct);
            if (neu == SchulStatus.Active)
                foreach (var d in direktoren.Where(d => d.Status == KontoStatus.EnAttente)) d.Status = KontoStatus.Actif;
            audit.Erfasse(db, akteur.Id, schule.Id, aktion, nameof(Schule), schule.Id,
                $"Établissement « {schule.Name} » {verb} ({StatusText(alt)} → {StatusText(neu)}){(grund is null ? "" : $". Motif : {grund}")}.");
            await db.SaveChangesAsync(ct);
            return (schule.Name, direktoren.Where(d => d.Status != KontoStatus.Archive).Select(d => new Direktion(d.Email, d.Vorname)).ToList());
        }, ct);
        foreach (var d in direktion) await mail(d, name);
    }

    /// <summary>Responsable zuweisen (null = keiner). Nur der SuperAdmin.</summary>
    public async Task ResponsableZuweisenAsync(Guid akteurId, Guid schuleId, Guid? responsableId, CancellationToken ct = default)
    {
        var akteur = await zugriff.AdminAsync(akteurId, ct);
        await dbFactory.TransaktionAsync(async db =>
        {
            var schule = await db.Schulen.FirstOrDefaultAsync(s => s.Id == schuleId, ct) ?? throw new FachFehler("Établissement introuvable.");
            string? name = null;
            if (responsableId is { } rid)
            {
                name = await db.Users.Where(k => k.Id == rid && k.Rolle == Rollen.Responsable && k.Status == KontoStatus.Actif)
                    .Select(k => k.Vorname + " " + k.Nachname).FirstOrDefaultAsync(ct)
                    ?? throw new FachFehler("Choisissez un responsable actif.", "responsable");
            }
            if (schule.ResponsableId == responsableId) return;
            schule.ResponsableId = responsableId;
            audit.Erfasse(db, akteur.Id, schule.Id, PlattformAudit.ResponsableZugewiesen, nameof(Schule), schule.Id,
                name is null ? $"Plus de responsable pour « {schule.Name} »." : $"Responsable de « {schule.Name} » : {name}.");
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task<IReadOnlyList<ResponsableOption>> ResponsableOptionenAsync(Guid akteurId, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().Where(k => k.Rolle == Rollen.Responsable && k.Status == KontoStatus.Actif)
            .OrderBy(k => k.Vorname).ThenBy(k => k.Nachname)
            .Select(k => new ResponsableOption(k.Id, k.Vorname + " " + k.Nachname, db.Schulen.Count(s => s.ResponsableId == k.Id)))
            .ToListAsync(ct);
    }
}
