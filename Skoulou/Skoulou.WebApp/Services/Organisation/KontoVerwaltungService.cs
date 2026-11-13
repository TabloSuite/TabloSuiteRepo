using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Services.Organisation;

/// <summary>
/// Gemeinsame Kontoaktionen der Direktion für Lehrer, Koordinatoren (nur Direktor), Schüler und Eltern:
/// sperren/entsperren, archivieren/« réintégrer », Startpasswort neu setzen (einzeln oder Auswahl → Zugangsliste).
/// Jede Aktion mit Audit; der Status ändert sich nur über diese ausdrücklichen Aktionen.
/// </summary>
public sealed class KontoVerwaltungService(
    IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, KontoFabrik fabrik,
    ZugangsAblage ablage, SitzungsPruefung sitzungen)
{
    public const string MeldungSelbst = "Pour votre propre compte, utilisez la page « Mon profil ».";
    public const string MeldungDirektor = "Le compte du directeur est géré par l'équipe SKOULOU.";

    /// <summary>Freier Identifiant-Vorschlag « prenom.nom » (…2, …3).</summary>
    public async Task<string> IdentifiantVorschlagAsync(Guid akteurId, string? vorname, string? nachname, CancellationToken ct = default)
    {
        await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await fabrik.VorschlagAsync(db, vorname, nachname, ct: ct);
    }

    /// <summary>Lädt das Zielkonto und prüft das Recht je Rolle (Koordinator nur Direktor, Direktor niemand, nie sich selbst).</summary>
    internal static async Task<(SchulAkteur Akteur, Konto Konto)> ZielAsync(Zugriff zugriff, SkoulouDbContext db, Guid akteurId, Guid kontoId, CancellationToken ct)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        var konto = await OrgAbfragen.KontoAsync(db, a, kontoId, ct);
        PruefeZiel(a, konto);
        return (a, konto);
    }

    internal static void PruefeZiel(SchulAkteur a, Konto konto)
    {
        if (konto.Id == a.Id) throw new FachFehler(MeldungSelbst);
        switch (konto.Rolle)
        {
            case Rollen.Direktor:
                throw new KeinZugriffFehler(MeldungDirektor);
            case Rollen.Koordinator:
                Zugriff.Pruefe(a.Akteur, Faehigkeit.KoordinatorenVerwalten, schreiben: true);
                break;
            case Rollen.Lehrer or Rollen.Schueler or Rollen.Eltern:
                break;
            default:
                throw new KeinZugriffFehler();
        }
    }

    public async Task SperrenAsync(Guid akteurId, Guid kontoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (a, konto) = await ZielAsync(zugriff, db, akteurId, kontoId, ct);
        switch (konto.Status)
        {
            case KontoStatus.Bloque: return;
            case KontoStatus.EnAttente: throw new FachFehler("Cette inscription attend votre validation : validez-la ou refusez-la.");
            case KontoStatus.Archive: throw new FachFehler("Ce compte est archivé.");
        }
        konto.Status = KontoStatus.Bloque;
        konto.AktiveSitzungId = Guid.NewGuid();
        audit.Erfasse(db, a.Id, a.SchuleId, AuditAktionen.KontoGesperrt, nameof(Konto), konto.Id, $"{Rollen.Bezeichnung(konto.Rolle)} {konto.VollerName} bloqué(e).");
        await db.SaveChangesAsync(ct);
        sitzungen.Vergessen(konto.Id);
    }

    public async Task EntsperrenAsync(Guid akteurId, Guid kontoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (a, konto) = await ZielAsync(zugriff, db, akteurId, kontoId, ct);
        if (konto.Status == KontoStatus.Actif) return;
        if (konto.Status != KontoStatus.Bloque) throw new FachFehler("Seul un compte bloqué peut être débloqué.");
        konto.Status = KontoStatus.Actif;
        audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.KontoEntsperrt, nameof(Konto), konto.Id, $"{Rollen.Bezeichnung(konto.Rolle)} {konto.VollerName} débloqué(e).");
        await db.SaveChangesAsync(ct);
        sitzungen.Vergessen(konto.Id);
    }

    /// <summary>Archivieren (Schüler nur ohne aktive Einschreibung: zuerst « Départ de l'élève »).</summary>
    public async Task ArchivierenAsync(Guid akteurId, Guid kontoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (a, konto) = await ZielAsync(zugriff, db, akteurId, kontoId, ct);
        if (konto.Status == KontoStatus.Archive) return;
        if (konto.Status == KontoStatus.EnAttente) throw new FachFehler("Cette inscription attend votre validation : validez-la ou refusez-la.");
        if (konto.Rolle == Rollen.Schueler && await SchulAbfragen.AktiveEinschreibungen(db).AnyAsync(e => e.SchuelerId == konto.Id, ct))
            throw new FachFehler("Cet élève est encore inscrit dans une classe : enregistrez d'abord son départ (« Départ de l'élève »).");
        konto.Status = KontoStatus.Archive;
        konto.AktiveSitzungId = Guid.NewGuid();
        audit.Erfasse(db, a.Id, a.SchuleId, AuditAktionen.KontoArchiviert, nameof(Konto), konto.Id, $"{Rollen.Bezeichnung(konto.Rolle)} {konto.VollerName} archivé(e).");
        await db.SaveChangesAsync(ct);
        sitzungen.Vergessen(konto.Id);
    }

    /// <summary>« Réintégrer »: archiviertes Konto wieder aktiv (ausdrückliche Aktion mit Audit).</summary>
    public async Task ReintegrierenAsync(Guid akteurId, Guid kontoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (a, konto) = await ZielAsync(zugriff, db, akteurId, kontoId, ct);
        if (konto.Status != KontoStatus.Archive) throw new FachFehler("Ce compte n'est pas archivé.");
        konto.Status = KontoStatus.Actif;
        audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.KontoReintegriert, nameof(Konto), konto.Id, $"{Rollen.Bezeichnung(konto.Rolle)} {konto.VollerName} réintégré(e).");
        await db.SaveChangesAsync(ct);
        sitzungen.Vergessen(konto.Id);
    }

    /// <summary>
    /// Neues Startpasswort für ein oder mehrere Konten (Pflicht zum Ändern, Sitzungen enden, Audit) –
    /// Ergebnis als Zugangsstapel für Anzeige und « Liste des accès ».
    /// </summary>
    public async Task<ZugangsStapel> PasswortZuruecksetzenAsync(Guid akteurId, IReadOnlyCollection<Guid> kontoIds, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        var ids = kontoIds.Distinct().ToList();
        if (ids.Count == 0) throw new FachFehler("Sélectionnez au moins un compte.");
        if (ids.Count > 500) throw new FachFehler("Sélectionnez au plus 500 comptes à la fois.");
        var zugaenge = await dbFactory.TransaktionAsync(async db =>
        {
            var konten = await db.Users.Where(k => ids.Contains(k.Id)).ToListAsync(ct);
            if (konten.Count != ids.Count) throw new FachFehler("Un des comptes sélectionnés est introuvable.");
            var klassen = await KlassenNamenAsync(db, ids, ct);
            var liste = new List<Zugang>();
            foreach (var konto in konten.OrderBy(k => k.Nachname).ThenBy(k => k.Vorname))
            {
                if (konto.SchuleId != a.SchuleId) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
                PruefeZiel(a, konto);
                if (konto.Status is KontoStatus.Archive or KontoStatus.EnAttente)
                    throw new FachFehler($"{konto.VollerName} : le compte est {(konto.Status == KontoStatus.Archive ? "archivé" : "en attente de validation")}.");
                var passwort = fabrik.NeuesStartpasswort(konto);
                audit.Erfasse(db, a.Id, a.SchuleId, AuditAktionen.PasswortZurueckgesetzt, nameof(Konto), konto.Id,
                    $"Nouveau mot de passe de départ pour {konto.VollerName} ({Rollen.Bezeichnung(konto.Rolle).ToLowerInvariant()}).");
                liste.Add(new Zugang(konto.Id, konto.VollerName, konto.Rolle, konto.UserName!, passwort, klassen.GetValueOrDefault(konto.Id)));
            }
            await db.SaveChangesAsync(ct);
            return liste;
        }, ct);
        foreach (var z in zugaenge) sitzungen.Vergessen(z.KontoId);
        return ablage.Ablegen(a.Id, a.SchuleId, zugaenge);
    }

    /// <summary>Aktuelle Klasse je Schüler (für Zusatzangaben der Zugangsliste).</summary>
    internal static async Task<Dictionary<Guid, string>> KlassenNamenAsync(SkoulouDbContext db, IReadOnlyCollection<Guid> kontoIds, CancellationToken ct)
        => await (from e in SchulAbfragen.AktiveEinschreibungen(db)
                  join k in db.Klassen on e.KlasseId equals k.Id
                  where kontoIds.Contains(e.SchuelerId)
                  select new { e.SchuelerId, k.Name })
            .ToDictionaryAsync(x => x.SchuelerId, x => x.Name, ct);
}
