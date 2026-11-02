using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.WebApp.Data;

namespace SkoulouApp.WebApp.Services;

public sealed record BulletinStammdatenEntwurf(Guid JahresZuordnungID, string Name, string? Matrikelnummer, string? Geburtsort, DateOnly? Geburtsdatum);

public sealed partial class BulletinService
{
    private static async Task StammdatenPruefenAsync(ApplicationDbContext db, BulletinBerichtigung e, BulletinStammdatenEntwurf s, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 200 || s.Matrikelnummer?.Length > 50 || s.Geburtsort?.Length > 150
            || s.Name.Any(char.IsControl) || (s.Matrikelnummer?.Any(char.IsControl) ?? false) || (s.Geburtsort?.Any(char.IsControl) ?? false)
            || s.Geburtsdatum > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new InvalidOperationException("Vérifiez le nom (200 caractères), le matricule (50), le lieu de naissance (150) et la date de naissance, qui ne peut pas être future.");
        if (!await db.Set<SchuelerJahresZuordnung>().AnyAsync(x => x.Id == s.JahresZuordnungID && x.SchulID == e.SchulID
            && x.SchuljahrID == e.SchuljahrID && x.KlasseID == e.KlasseID, token))
            throw new InvalidOperationException("Choisissez un élève de cette classe et de cette année scolaire.");
        if (!await db.Set<BulletinAusgabe>().AnyAsync(x => x.SchulID == e.SchulID && x.JahresZuordnungID == s.JahresZuordnungID && x.IstVeroeffentlicht, token))
            throw new InvalidOperationException("Cet élève n'a aucun bulletin publié à rectifier.");
    }

    private static BulletinSnapshot StammdatenAnwenden(BulletinSnapshot snapshot, BulletinStammdatenEntwurf s)
        => snapshot with { Schueler = s.Name.Trim(), Matrikelnummer = LeerZuNull(s.Matrikelnummer), Geburtsort = LeerZuNull(s.Geburtsort),
            Geburtsdatum = s.Geburtsdatum?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) };

    private static string? LeerZuNull(string? wert) => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();

    public Task<bool> BerichtigungsStammdatenAsync(Guid user, Guid id, BulletinStammdatenEntwurf eingabe, CancellationToken token = default)
        => UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
            var e = await EntwurfAsync(db, user, id, token);
            await StammdatenPruefenAsync(db, e, eingabe, token);
            var liste = JsonSerializer.Deserialize<List<BulletinStammdatenEntwurf>>(e.StammdatenEntwurfJson)!;
            liste.RemoveAll(x => x.JahresZuordnungID == eingabe.JahresZuordnungID);
            liste.Add(eingabe with { Name = eingabe.Name.Trim(), Matrikelnummer = LeerZuNull(eingabe.Matrikelnummer), Geburtsort = LeerZuNull(eingabe.Geburtsort) });
            e.StammdatenEntwurfJson = JsonSerializer.Serialize(liste); e.VorschauHash = null;
            await db.SaveChangesAsync(token); return true;
        }, token);

    public async Task<BulletinStammdatenEntwurf> BerichtigungsStammdatenLadenAsync(Guid user, Guid id, Guid zuordnungID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var e = await EntwurfAsync(db, user, id, token);
        if (!await db.Set<SchuelerJahresZuordnung>().AnyAsync(x => x.Id == zuordnungID && x.SchulID == e.SchulID
            && x.SchuljahrID == e.SchuljahrID && x.KlasseID == e.KlasseID, token))
            throw new InvalidOperationException("Choisissez un élève de cette classe et de cette année scolaire.");
        var vorhanden = JsonSerializer.Deserialize<List<BulletinStammdatenEntwurf>>(e.StammdatenEntwurfJson)!.SingleOrDefault(x => x.JahresZuordnungID == zuordnungID);
        if (vorhanden is not null) return vorhanden;
        var json = await db.Set<BulletinAusgabe>().AsNoTracking().Where(x => x.SchulID == e.SchulID && x.JahresZuordnungID == zuordnungID && x.IstVeroeffentlicht)
            .OrderByDescending(x => x.ErstelltAmUtc).Select(x => x.SnapshotJson).FirstOrDefaultAsync(token)
            ?? throw new InvalidOperationException("Cet élève n'a aucun bulletin publié à rectifier.");
        var s = JsonSerializer.Deserialize<BulletinSnapshot>(json)!;
        DateOnly? datum = DateOnly.TryParseExact(s.Geburtsdatum, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
        return new(zuordnungID, s.Schueler, s.Matrikelnummer, s.Geburtsort, datum);
    }
}
