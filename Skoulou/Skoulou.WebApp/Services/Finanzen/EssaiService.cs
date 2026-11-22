using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Services.Finanzen;

/// <summary>Eine Abrechnungsperiode, die in die Probezeit fallen kann.</summary>
public sealed record EssaiPeriode(Guid Id, string Titel, string Schuljahr, DateOnly Beginn, DateOnly Ende);

/// <summary>
/// Stand der Probezeit einer Schule. Kandidaten = Abrechnungsperioden des ersten Schuljahres, die nach der Anmeldung der
/// Schule enden (in Reihenfolge); die ersten <see cref="Perioden"/> davon stellt SKOULOU nicht in Rechnung.
/// </summary>
public sealed record EssaiStand(Guid SchuleId, string Schule, int Perioden, int Max, int Min, bool Aenderbar, bool DarfAendern,
    IReadOnlyList<EssaiPeriode> Kandidaten)
{
    public IReadOnlyList<EssaiPeriode> Offert => Kandidaten.Take(Perioden).ToList();
    public string Text => EssaiService.Text(Perioden, Offert);
}

/// <summary>
/// Probezeit (Nutzer 18.11.2026): 0–4 Abrechnungsperioden zu Beginn des ersten Schuljahres ohne SKOULOU-Rechnung (und damit
/// ohne Part des Responsable). Ändern dürfen der Administrateur und der Responsable der Schule – nur solange die Schule noch
/// keine (nicht annullierte) SKOULOU-Rechnung hat, und nie unter die schon vergangenen Probe-Perioden. Jede Änderung eines
/// Responsable wird protokolliert und allen Administrateurs gemeldet. Die Direktion sieht den Stand (nur lesen).
/// </summary>
public sealed class EssaiService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, PlattformMails mails, TimeProvider zeit)
{
    public const int Max = 4;
    private DateOnly Heute => new SchulZeit(zeit).Heute;

    /// <summary>Kurzform für Protokoll und Mail.</summary>
    public static string Kurz(int n) => n == 0 ? "aucune période" : n == 1 ? "1 période" : $"{n} périodes";

    /// <summary>Satz für die Anzeige (Direktion, Responsable, Admin).</summary>
    public static string Text(int n, IReadOnlyList<EssaiPeriode> offert)
    {
        if (n <= 0) return "Pas de période d'essai.";
        if (offert.Count == 0)
            return n == 1 ? "Période d'essai : la première période de la première année scolaire, sans facture SKOULOU."
                          : $"Période d'essai : les {n} premières périodes de la première année scolaire, sans facture SKOULOU.";
        var jahr = offert[0].Schuljahr;
        return $"Période{(offert.Count > 1 ? "s" : "")} d'essai : {string.Join(", ", offert.Select(p => p.Titel))} {jahr} – sans facture SKOULOU.";
    }

    /// <summary>Abrechnungsperioden des ersten Schuljahres, die nach der Anmeldung der Schule enden (Kandidaten der Probezeit).</summary>
    internal static async Task<List<EssaiPeriode>> KandidatenAsync(SkoulouDbContext db, Schule schule, CancellationToken ct)
    {
        // Erstes Schuljahr = das erste, das nach der Anmeldung noch läuft: ein nachträglich angelegtes Vorjahr (z. B. für
        // Restbeträge) hob sonst die Probezeit auf (Klicktest 21.11.2026: Vorjahr 2025-26 → 1er trimestre 2026-27 berechnet).
        var ab = DateOnly.FromDateTime(schule.ErstelltAmUtc);
        var jahr = await db.Schuljahre.AsNoTracking().Where(j => j.SchuleId == schule.Id && j.Ende >= ab).OrderBy(j => j.Beginn).FirstOrDefaultAsync(ct);
        if (jahr is null) return [];
        var perioden = await db.Perioden.AsNoTracking().Where(p => p.SchuljahrId == jahr.Id && p.ZaehltFuerJahr && p.Ende >= ab)
            .OrderBy(p => p.Beginn).ThenBy(p => p.Nr).ToListAsync(ct);
        return perioden.Select(p => new EssaiPeriode(p.Id, p.Titel, jahr.Bezeichnung, p.Beginn, p.Ende)).ToList();
    }

    /// <summary>Ids der Perioden, die SKOULOU nicht in Rechnung stellt (Probezeit der Schule).</summary>
    internal static async Task<HashSet<Guid>> ProbePeriodenAsync(SkoulouDbContext db, Schule schule, CancellationToken ct)
        => schule.EssaiPerioden <= 0 ? [] : (await KandidatenAsync(db, schule, ct)).Take(schule.EssaiPerioden).Select(p => p.Id).ToHashSet();

    /// <summary>Stand für Admin, Responsable (seine Schulen) oder Direktion (eigene Schule, nur lesen).</summary>
    public async Task<EssaiStand> StandAsync(Guid akteurId, Guid schuleId, CancellationToken ct = default)
    {
        var (_, darfAendern) = await SichtAsync(akteurId, schuleId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var schule = await db.Schulen.AsNoTracking().FirstOrDefaultAsync(s => s.Id == schuleId, ct)
                     ?? throw new FachFehler("Cet établissement est introuvable.");
        return await StandAsync(db, schule, darfAendern, ct);
    }

    /// <summary>Stand der eigenen Schule (Direktion, Seite « Facturation SKOULOU »).</summary>
    public async Task<EssaiStand> EigeneSchuleAsync(Guid akteurId, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, false, ct, Faehigkeit.RechnungSkoulouBearbeiten, Faehigkeit.FinanzenEinsehen);
        return await StandAsync(akteurId, a.SchuleId, ct);
    }

    /// <summary>Probezeit setzen (Admin oder Responsable der Schule).</summary>
    public async Task<EssaiStand> SetzenAsync(Guid akteurId, Guid schuleId, int perioden, CancellationToken ct = default)
    {
        var (akteur, darfAendern) = await SichtAsync(akteurId, schuleId, ct);
        if (!darfAendern) throw new KeinZugriffFehler();
        string? vorherText = null, nachherText = null, schulName = null;
        var stand = await dbFactory.TransaktionAsync(async db =>
        {
            var schule = await db.Schulen.FirstOrDefaultAsync(s => s.Id == schuleId, ct) ?? throw new FachFehler("Cet établissement est introuvable.");
            var vorher = await StandAsync(db, schule, true, ct);
            if (!vorher.Aenderbar) throw new FachFehler("L'établissement a déjà une facture SKOULOU : la période d'essai ne peut plus être modifiée.");
            if (perioden < 0 || perioden > vorher.Max) throw new FachFehler($"Choisissez entre 0 et {vorher.Max} période(s).");
            if (perioden < vorher.Min)
                throw new FachFehler($"{Kurz(vorher.Min)} d'essai {(vorher.Min > 1 ? "sont déjà passées" : "est déjà passée")} : elle{(vorher.Min > 1 ? "s restent offertes" : " reste offerte")}.");
            if (perioden == schule.EssaiPerioden) return vorher;
            (vorherText, nachherText, schulName) = (Kurz(schule.EssaiPerioden), Kurz(perioden), schule.Name);
            schule.EssaiPerioden = perioden;
            audit.Erfasse(db, akteur.Id, schule.Id, PlattformAudit.EssaiGeaendert, nameof(Schule), schule.Id,
                $"Période d'essai : {vorherText} → {nachherText}.");
            await db.SaveChangesAsync(ct);
            return await StandAsync(db, schule, true, ct);
        }, ct);
        if (akteur.Rolle == Rollen.Responsable && schulName is not null)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var admins = await db.Users.AsNoTracking()
                .Where(k => k.Rolle == Rollen.SuperAdmin && k.Status == KontoStatus.Actif && k.Email != null)
                .Select(k => k.Email!).Distinct().ToListAsync(ct);
            foreach (var admin in admins) await mails.EssaiGeaendertAnAdminAsync(admin, schulName, akteur.Name, vorherText!, nachherText!, ct);
        }
        return stand;
    }

    private async Task<EssaiStand> StandAsync(SkoulouDbContext db, Schule schule, bool darfAendern, CancellationToken ct)
    {
        var kandidaten = await KandidatenAsync(db, schule, ct);
        var hatRechnung = await db.Plattformrechnungen.AnyAsync(r => r.SchuleId == schule.Id && r.Status != RechnungsStatus.Annulee, ct);
        var max = kandidaten.Count == 0 ? Max : Math.Min(Max, kandidaten.Count);
        var min = kandidaten.Take(schule.EssaiPerioden).Count(p => p.Ende < Heute);
        return new EssaiStand(schule.Id, schule.Name, schule.EssaiPerioden, max, min, !hatRechnung, darfAendern, kandidaten);
    }

    /// <summary>Admin: alle Schulen (ändern); Responsable: seine Schulen (ändern); Direktion: eigene Schule (lesen).</summary>
    private async Task<(Akteur Akteur, bool DarfAendern)> SichtAsync(Guid akteurId, Guid schuleId, CancellationToken ct)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        if (akteur.IstAdmin || akteur.Rolle == Rollen.Responsable)
        {
            if (akteur.Status != KontoStatus.Actif) throw new KeinZugriffFehler();
            if (akteur.IstAdmin) return (akteur, true);
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            if (!await db.Schulen.AnyAsync(s => s.Id == schuleId && s.ResponsableId == akteur.Id, ct))
                throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
            return (akteur, true);
        }
        var a = await zugriff.DirektionAsync(akteurId, false, ct, Faehigkeit.RechnungSkoulouBearbeiten, Faehigkeit.FinanzenEinsehen);
        if (a.SchuleId != schuleId) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
        return (akteur, false);
    }
}
