using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Data;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Ergebnis der Anmeldeprüfung (Konto nur bei richtigem Passwort).</summary>
public sealed record AnmeldePruefung(AnmeldeErgebnis Ergebnis, Konto? Konto);

/// <summary>
/// Anmeldung mit Identifiant + Passwort. Sperrhinweise erst nach richtigem Passwort;
/// Lockout 5 Fehlversuche / 15 Minuten. Jede Anmeldung erzeugt eine neue Sitzung (die alte endet).
/// </summary>
public sealed class AnmeldeService(
    UserManager<Konto> users,
    IDbContextFactory<SkoulouDbContext> dbFactory,
    SitzungsPruefung sitzungen,
    TimeProvider zeit)
{
    public async Task<AnmeldePruefung> PruefeAsync(string? identifiant, string? passwort, CancellationToken ct = default)
    {
        var name = identifiant?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(passwort))
            return new AnmeldePruefung(AnmeldeErgebnis.FalscheDaten, null);

        var konto = await users.FindByNameAsync(name);
        if (konto is null) return new AnmeldePruefung(AnmeldeErgebnis.FalscheDaten, null);
        if (await users.IsLockedOutAsync(konto)) return new AnmeldePruefung(AnmeldeErgebnis.Verriegelt, null);

        if (!await users.CheckPasswordAsync(konto, passwort))
        {
            await users.AccessFailedAsync(konto);
            return new AnmeldePruefung(await users.IsLockedOutAsync(konto) ? AnmeldeErgebnis.Verriegelt : AnmeldeErgebnis.FalscheDaten, null);
        }
        await users.ResetAccessFailedCountAsync(konto);

        SchulStatus? schulStatus = null;
        if (konto.SchuleId is { } schuleId)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            schulStatus = await db.Schulen.AsNoTracking().Where(s => s.Id == schuleId).Select(s => (SchulStatus?)s.Status).FirstOrDefaultAsync(ct);
        }
        return new AnmeldePruefung(AnmeldeRegeln.Pruefe(konto.Status, konto.Rolle, schulStatus), konto);
    }

    /// <summary>Neue Sitzung: ersetzt jede andere (anderes Gerät wird abgemeldet).</summary>
    public async Task NeueSitzungAsync(Konto konto, CancellationToken ct = default)
    {
        konto.AktiveSitzungId = Guid.NewGuid();
        konto.LetzteAnmeldungUtc = zeit.GetUtcNow().UtcDateTime;
        var ergebnis = await users.UpdateAsync(konto);
        if (!ergebnis.Succeeded) throw new InvalidOperationException("La session n'a pas pu être enregistrée : " + KontoAnlage.Fehlertext(ergebnis));
        sitzungen.Vergessen(konto.Id);
    }

    /// <summary>Abmelden: Sitzung ungültig machen (auch ein kopiertes Cookie verliert seine Wirkung).</summary>
    public async Task SitzungBeendenAsync(Guid kontoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var konto = await db.Users.FirstOrDefaultAsync(k => k.Id == kontoId, ct);
        if (konto is null) return;
        konto.AktiveSitzungId = null;
        await db.SaveChangesAsync(ct);
        sitzungen.Vergessen(kontoId);
    }
}
