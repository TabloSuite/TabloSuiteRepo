using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;

namespace Skoulou.WebApp.Services.Infrastruktur;

/// <summary>Angemeldete Person, frisch aus der Datenbank geladen (nie aus UI-Flags).</summary>
public sealed record Akteur(
    Guid Id,
    string Rolle,
    Guid? SchuleId,
    KoordinatorRechte Rechte,
    KontoStatus Status,
    SchulStatus? SchulStatus,
    string Name)
{
    public bool IstAdmin => Rolle == Rollen.SuperAdmin;
    public bool IstDirektion => Rollen.IstDirektion(Rolle);
}

/// <summary>
/// Zentrale Rechteprüfung: lädt den Akteur aus der DB, prüft Fähigkeit (Matrix aus <see cref="RechteMatrix"/>)
/// und die Schule der Ressource. Wirft <see cref="KeinZugriffFehler"/>; liefert die wirksame Zugriffsart
/// (bei « Eigene » prüft der Service zusätzlich Lehrauftrag, Kinder bzw. eigene Schulen).
/// </summary>
public sealed class Zugriff(IDbContextFactory<SkoulouDbContext> dbFactory)
{
    public const string MeldungAndereSchule = "Cette ressource appartient à un autre établissement.";
    public const string MeldungKontoInaktiv = "Votre compte n'est pas actif.";
    public const string MeldungSchuleGesperrt = "L'accès à votre établissement est suspendu ou pas encore validé.";

    /// <summary>Lädt den Akteur; unbekanntes Konto → <see cref="KeinZugriffFehler"/>.</summary>
    public async Task<Akteur> LadeAkteurAsync(Guid kontoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var k = await db.Users.AsNoTracking()
            .Where(x => x.Id == kontoId)
            .Select(x => new
            {
                x.Id, x.Rolle, x.SchuleId, x.KoordinatorRechte, x.Status, x.Vorname, x.Nachname,
                SchulStatus = x.Schule == null ? (SchulStatus?)null : x.Schule.Status
            })
            .FirstOrDefaultAsync(ct) ?? throw new KeinZugriffFehler("Votre compte est introuvable.");

        // Nutzungsbedingungen sperren nichts (wie eNOTE: Zustimmung bei der Registrierung; Konten der Schule fallen darunter).
        return new Akteur(k.Id, k.Rolle, k.SchuleId, k.KoordinatorRechte, k.Status, k.SchulStatus,
            $"{k.Vorname} {k.Nachname}".Trim());
    }

    /// <summary>Prüft eine Fähigkeit ohne Ressourcenbezug (rein, ohne DB).</summary>
    public static Zugriffsart Pruefe(Akteur akteur, Faehigkeit faehigkeit, bool schreiben = false)
    {
        if (akteur.Status != KontoStatus.Actif) throw new KeinZugriffFehler(MeldungKontoInaktiv);
        if (Rollen.IstSchulRolle(akteur.Rolle) && akteur.SchulStatus != SchulStatus.Active)
            throw new KeinZugriffFehler(MeldungSchuleGesperrt);

        var art = RechteMatrix.Zugriff(akteur.Rolle, faehigkeit, akteur.Rechte);
        if (art == Zugriffsart.Nein || (schreiben && !RechteMatrix.DarfSchreiben(art)))
            throw new KeinZugriffFehler();
        return art;
    }

    /// <summary>Lädt den Akteur und prüft Fähigkeit sowie (optional) die Schule der Ressource.</summary>
    public async Task<(Akteur Akteur, Zugriffsart Art)> PruefeAsync(
        Guid kontoId, Faehigkeit faehigkeit, Guid? schuleId = null, bool schreiben = false, CancellationToken ct = default)
    {
        var akteur = await LadeAkteurAsync(kontoId, ct);
        var art = Pruefe(akteur, faehigkeit, schreiben);
        if (schuleId is { } id) await PruefeSchuleAsync(akteur, id, ct);
        return (akteur, art);
    }

    /// <summary>Gehört die Schule der Ressource zum Akteur? (Admin: alle; Responsable: betreute Schulen.)</summary>
    public async Task PruefeSchuleAsync(Akteur akteur, Guid schuleId, CancellationToken ct = default)
    {
        if (akteur.IstAdmin) return;
        if (akteur.Rolle == Rollen.Responsable)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            if (!await db.Schulen.AnyAsync(s => s.Id == schuleId && s.ResponsableId == akteur.Id, ct))
                throw new KeinZugriffFehler(MeldungAndereSchule);
            return;
        }
        if (akteur.SchuleId != schuleId) throw new KeinZugriffFehler(MeldungAndereSchule);
    }
}
