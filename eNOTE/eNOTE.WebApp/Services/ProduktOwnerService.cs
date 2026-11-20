using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// ProduktOwner-Verwaltung: Admin legt POs an, weist ihnen Läden zu und steuert
/// deren Umsatz-Sichtbarkeit. POs sehen/verwalten nur zugewiesene + eigene Läden.
/// </summary>
public class ProduktOwnerService(IDbContextFactory<ApplicationDbContext> dbContextFactory, TimeProvider? zeit = null)
{
    private readonly TimeProvider uhr = zeit ?? TimeProvider.System;

    public record Zuweisung(
        Guid ZuweisungId,
        Guid SalonId,
        string SalonName,
        bool KannLadenUmsatzSehen,
        bool KannPlattformUmsatzSehen,
        bool SelbstAngelegt,
        decimal? ProzentProvision,
        decimal? FestbetragProvision);

    /// <summary>Dashboard-Karte eines Ladens für den PO: Umsatz + geschätzte Provision.</summary>
    public record DashboardLaden(
        Guid SalonId,
        string SalonName,
        bool IstFreigegeben,
        decimal UmsatzHeute,
        decimal UmsatzMonat,
        bool KannLadenUmsatzSehen,
        decimal? ProzentProvision,
        decimal? FestbetragProvision,
        decimal AboEinnahmenMonat);

    /// <summary>Alle Zuweisungen eines ProduktOwners.</summary>
    public async Task<List<Zuweisung>> GetZuweisungenAsync(Guid produktOwnerId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ProduktOwnerSalons
            .AsNoTracking()
            .Include(p => p.Salon)
            .Where(p => p.ProduktOwnerId == produktOwnerId && p.GueltigBisUtc == null)
            .OrderBy(p => p.Salon.Name)
            .Select(p => new Zuweisung(p.Id, p.SalonId, p.Salon.Name, p.KannLadenUmsatzSehen, p.KannPlattformUmsatzSehen, p.SelbstAngelegt, p.ProzentProvision, p.FestbetragProvision))
            .ToListAsync(cancellationToken);
    }

    /// <summary>IDs der Läden, die ein PO verwalten darf.</summary>
    public async Task<HashSet<Guid>> GetVerwalteteSalonIdsAsync(Guid produktOwnerId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ids = await db.ProduktOwnerSalons
            .AsNoTracking()
            .Where(p => p.ProduktOwnerId == produktOwnerId && p.GueltigBisUtc == null)
            .Select(p => p.SalonId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    /// <summary>
    /// Weist einen Laden einem PO zu (oder aktualisiert die Rechte).
    /// Ein PO kann mehrere Läden verwalten, aber ein Laden hat immer genau einen
    /// verantwortlichen PO. Der Admin kann den PO wechseln: eine Zuweisung an einen
    /// anderen PO ersetzt die bisherige Zuordnung dieses Ladens (eindeutige Provision/Abrechnung).
    /// </summary>
    public async Task<bool> WeiseSalonZuAsync(
        Guid produktOwnerId, Guid salonId, bool kannLadenUmsatz, bool kannPlattformUmsatz,
        bool selbstAngelegt = false, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Ein Laden = genau ein aktiver PO: bisherige aktive Zuordnungen anderer POs
        // für diesen Laden archivieren (GueltigBisUtc setzen), statt sie zu löschen.
        var jetzt = uhr.GetUtcNow().UtcDateTime;
        var fremdeZuweisungen = await db.ProduktOwnerSalons
            .Where(p => p.SalonId == salonId && p.ProduktOwnerId != produktOwnerId && p.GueltigBisUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var alt in fremdeZuweisungen)
        {
            alt.GueltigBisUtc = jetzt;
        }

        var vorhanden = await db.ProduktOwnerSalons
            .FirstOrDefaultAsync(p => p.ProduktOwnerId == produktOwnerId && p.SalonId == salonId && p.GueltigBisUtc == null, cancellationToken);

        if (vorhanden is null)
        {
            db.ProduktOwnerSalons.Add(new ProduktOwnerSalon
            {
                ProduktOwnerId = produktOwnerId,
                SalonId = salonId,
                KannLadenUmsatzSehen = kannLadenUmsatz,
                KannPlattformUmsatzSehen = kannPlattformUmsatz,
                SelbstAngelegt = selbstAngelegt
            });
        }
        else
        {
            vorhanden.KannLadenUmsatzSehen = kannLadenUmsatz;
            vorhanden.KannPlattformUmsatzSehen = kannPlattformUmsatz;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// PO legt selbst einen Laden an (mit neuem Besitzer-Konto) und verwaltet ihn
    /// anschließend direkt. Der Laden ist aktiv, muss aber vom Admin freigegeben werden.
    /// Login des Besitzers = eindeutiges Pseudonym; Kontakt-E-Mail darf mehrfach sein.
    /// </summary>
    public async Task<(bool Erfolg, string? Fehler, Salon? Salon)> LegeSalonSelbstAnAsync(
        Guid produktOwnerId,
        string vorname, string nachname, string pseudonym, string email, string initialPasswort,
        string salonName, string? standort, string? land,
        UserManager<AppUser> userManager,
        CancellationToken cancellationToken = default,
        string? telefon = null)
    {
        var bereinigtSalon = (salonName ?? string.Empty).Trim();
        var bereinigtPseudonym = (pseudonym ?? string.Empty).Trim();
        // Telefon des Besitzers ist Pflicht (wie bei der öffentlichen Registrierung).
        var (bereinigtTelefon, telefonFehler) = TelefonRegel.Pruefe(telefon, TelefonRegel.MeldungPflichtBesitzer);
        if (telefonFehler is not null)
        {
            return (false, telefonFehler, null);
        }
        if (bereinigtSalon.Length < 2)
        {
            return (false, "Veuillez saisir un nom d'établissement (au moins 2 caractères).", null);
        }
        // Klicktest 20.11.2026: überlange Namen scheitern sonst an der Datenbank bzw. später an jedem Verkauf.
        if (KontoProfilService.PruefeNamen(vorname, nachname, pflicht: false) is { } namensFehler) return (false, namensFehler, null);
        if (bereinigtSalon.Length > AuthService.LadenNameMax) return (false, $"Nom de l'établissement : {AuthService.LadenNameMax} caractères au maximum.", null);
        if (string.IsNullOrWhiteSpace(bereinigtPseudonym))
        {
            return (false, "Veuillez saisir un pseudonyme pour le propriétaire.", null);
        }
        if (await userManager.FindByNameAsync(bereinigtPseudonym) is not null)
        {
            return (false, "Ce pseudonyme est déjà utilisé.", null);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Salons.AnyAsync(s => s.Name == bereinigtSalon, cancellationToken))
        {
            return (false, "Un établissement portant ce nom existe déjà.", null);
        }

        var besitzer = new AppUser
        {
            UserName = bereinigtPseudonym,
            Email = (email ?? string.Empty).Trim(),
            Vorname = vorname.Trim(),
            Nachname = nachname.Trim(),
            FullName = $"{vorname.Trim()} {nachname.Trim()}",
            TelefonNummer = bereinigtTelefon,
            EmailConfirmed = true
        };
        var erstellen = await userManager.CreateAsync(besitzer, initialPasswort);
        if (!erstellen.Succeeded)
        {
            var fehler = string.Join(" ", erstellen.Errors.Select(e => e.Description));
            return (false, string.IsNullOrWhiteSpace(fehler) ? "La création a échoué." : fehler, null);
        }
        await userManager.AddToRoleAsync(besitzer, AppRollen.Besitzer);

        var salon = new Salon
        {
            Name = bereinigtSalon,
            Standort = string.IsNullOrWhiteSpace(standort) ? null : standort.Trim(),
            Land = string.IsNullOrWhiteSpace(land) ? null : land.Trim(),
            IstAktiv = true,
            IstFreigegeben = false, // Admin gibt frei
            ProbeBisUtc = AuthService.BerechneProbeEnde(uhr.GetUtcNow().UtcDateTime)
        };
        db.Salons.Add(salon);
        await db.SaveChangesAsync(cancellationToken);

        db.UserSalons.Add(new UserSalon
        {
            AppUserId = besitzer.Id,
            SalonId = salon.Id,
            KannFinanzenSehen = true,
            KannLeistungenVerwalten = true,
            KannRabatteVerwalten = true
        });

        // Direkte Verwaltung durch den anlegenden PO (SelbstAngelegt = true).
        db.ProduktOwnerSalons.Add(new ProduktOwnerSalon
        {
            ProduktOwnerId = produktOwnerId,
            SalonId = salon.Id,
            KannLadenUmsatzSehen = true,
            KannPlattformUmsatzSehen = false,
            SelbstAngelegt = true
        });
        await db.SaveChangesAsync(cancellationToken);

        return (true, null, salon);
    }

    /// <summary>
    /// Beendet eine Zuweisung (« Décocher » beim Admin). Archiviert statt zu löschen – wie beim PO-Wechsel –, damit die
    /// Provisionen weiter wissen, wer den Laden wann betreut hat. Eine spätere neue Zuweisung an denselben PO ist erlaubt
    /// (eindeutiger Index nur über aktive Zuordnungen, Migration ProduktOwnerSalonVerlauf).
    /// </summary>
    public async Task<bool> EntferneZuweisungAsync(Guid zuweisungId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var z = await db.ProduktOwnerSalons.FirstOrDefaultAsync(p => p.Id == zuweisungId && p.GueltigBisUtc == null, cancellationToken);
        if (z is null)
        {
            return false;
        }

        z.GueltigBisUtc = uhr.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Admin legt die Provision des PO für einen Laden fest.</summary>
    public async Task<bool> SetzeProvisionAsync(
        Guid zuweisungId, decimal? prozent, decimal? festbetrag, CancellationToken cancellationToken = default)
    {
        if (prozent is < 0 or > 100)
        {
            return false;
        }

        if (festbetrag is < 0)
        {
            return false;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var z = await db.ProduktOwnerSalons.FirstOrDefaultAsync(p => p.Id == zuweisungId, cancellationToken);
        if (z is null)
        {
            return false;
        }

        z.ProzentProvision = prozent;
        z.FestbetragProvision = festbetrag;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Dashboard-Daten für den PO: seine Läden mit Tages-/Monatsumsatz und
    /// geschätzter Provision aus den bestätigten Abo-Zahlungen des Monats.
    /// </summary>
    public async Task<List<DashboardLaden>> GetDashboardAsync(Guid produktOwnerId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var zuweisungen = await db.ProduktOwnerSalons
            .AsNoTracking()
            .Include(p => p.Salon)
            .Where(p => p.ProduktOwnerId == produktOwnerId && p.GueltigBisUtc == null)
            .ToListAsync(cancellationToken);

        var salonIds = zuweisungen.Select(z => z.SalonId).ToList();
        var heute = uhr.GetUtcNow().UtcDateTime.Date;
        var monatStart = new DateTime(uhr.GetUtcNow().UtcDateTime.Year, uhr.GetUtcNow().UtcDateTime.Month, 1);

        var umsatzHeute = await db.Verkaeufe.AsNoTracking()
            .Where(v => salonIds.Contains(v.SalonId) && v.ZeitUtc >= heute)
            .GroupBy(v => v.SalonId)
            .Select(g => new { g.Key, Summe = g.Sum(v => v.GesamtSumme) })
            .ToListAsync(cancellationToken);

        var umsatzMonat = await db.Verkaeufe.AsNoTracking()
            .Where(v => salonIds.Contains(v.SalonId) && v.ZeitUtc >= monatStart)
            .GroupBy(v => v.SalonId)
            .Select(g => new { g.Key, Summe = g.Sum(v => v.GesamtSumme) })
            .ToListAsync(cancellationToken);

        // Bestätigte Abo-Einnahmen des laufenden Monats je Laden (Plattform-Umsatz).
        var aboEinnahmen = await db.NutzungsAbos.AsNoTracking()
            .Where(a => salonIds.Contains(a.SalonId)
                && a.ZahlungStatus == Core.DataModels.Abo.AboZahlungStatus.Bestaetigt
                && a.ZahlungBestaetigtUtc >= monatStart)
            .GroupBy(a => a.SalonId)
            .Select(g => new { g.Key, Summe = g.Sum(a => a.PreisSnapshot) })
            .ToListAsync(cancellationToken);

        return zuweisungen.Select(z =>
        {
            var abo = aboEinnahmen.FirstOrDefault(x => x.Key == z.SalonId)?.Summe ?? 0m;
            return new DashboardLaden(
                z.SalonId,
                z.Salon.Name,
                z.Salon.IstFreigegeben,
                umsatzHeute.FirstOrDefault(x => x.Key == z.SalonId)?.Summe ?? 0m,
                umsatzMonat.FirstOrDefault(x => x.Key == z.SalonId)?.Summe ?? 0m,
                z.KannLadenUmsatzSehen,
                z.ProzentProvision,
                z.FestbetragProvision,
                abo);
        }).OrderBy(d => d.SalonName).ToList();
    }

    /// <summary>
    /// Berechnet die Provision eines PO für einen Monat aus den bestätigten
    /// Abo-Einnahmen seiner Läden und legt eine offene Auszahlung an (oder liefert
    /// die bereits vorhandene zurück).
    /// </summary>
    public async Task<Core.DataModels.Abo.ProduktOwnerAuszahlung> BerechneAuszahlungAsync(
        Guid produktOwnerId, int jahr, int monat, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var vorhanden = await db.ProduktOwnerAuszahlungen
            .FirstOrDefaultAsync(a => a.ProduktOwnerId == produktOwnerId && a.Jahr == jahr && a.Monat == monat, cancellationToken);
        if (vorhanden is not null)
        {
            return vorhanden;
        }

        var monatStart = new DateTime(jahr, monat, 1, 0, 0, 0, DateTimeKind.Utc);
        var monatEnde = monatStart.AddMonths(1);

        // Für die Abrechnung zählen Zuordnungen, die im Abrechnungsmonat aktiv waren
        // (GueltigAbUtc < Monatsende und noch nicht vor Monatsbeginn archiviert).
        var zuweisungen = await db.ProduktOwnerSalons
            .AsNoTracking()
            .Include(p => p.Salon)
            .Where(p => p.ProduktOwnerId == produktOwnerId
                && p.CreatedAtUtc < monatEnde
                && (p.GueltigBisUtc == null || p.GueltigBisUtc >= monatStart))
            .ToListAsync(cancellationToken);

        var salonIds = zuweisungen.Select(z => z.SalonId).ToList();
        // Bereits über das neue PO-Abrechnungssystem (PoZahlungseingang) erfasste
        // Abo-Einnahmen nicht erneut provisionieren, sonst würde doppelt gezahlt.
        var aboEinnahmen = await db.NutzungsAbos
            .AsNoTracking()
            .Where(a => salonIds.Contains(a.SalonId)
                && a.ZahlungStatus == Core.DataModels.Abo.AboZahlungStatus.Bestaetigt
                && a.ZahlungBestaetigtUtc >= monatStart && a.ZahlungBestaetigtUtc < monatEnde
                && !db.PoZahlungseingaenge.Any(e => e.NutzungsAboId == a.Id)
                // Seit der automatischen Provision (PoProvision je Zahlung) ebenfalls nicht doppelt.
                && !db.PoProvisionen.Any(p => p.NutzungsAboId == a.Id && p.ProduktOwnerId == produktOwnerId))
            .GroupBy(a => a.SalonId)
            .Select(g => new { g.Key, Summe = g.Sum(a => a.PreisSnapshot) })
            .ToListAsync(cancellationToken);

        decimal gesamt = 0m;
        var zeilen = new List<string>();
        foreach (var z in zuweisungen)
        {
            var einnahmen = aboEinnahmen.FirstOrDefault(x => x.Key == z.SalonId)?.Summe ?? 0m;
            if (einnahmen <= 0)
            {
                continue;
            }

            var provision = (z.ProzentProvision is not null ? einnahmen * z.ProzentProvision.Value / 100m : 0m)
                + (z.FestbetragProvision ?? 0m);
            provision = Math.Round(provision, 2);
            if (provision <= 0)
            {
                continue;
            }

            gesamt += provision;
            zeilen.Add($"{z.Salon.Name}: {einnahmen:N0} F → {provision:N0} F");
        }

        var auszahlung = new Core.DataModels.Abo.ProduktOwnerAuszahlung
        {
            ProduktOwnerId = produktOwnerId,
            Jahr = jahr,
            Monat = monat,
            Betrag = gesamt,
            Aufschluesselung = string.Join("\n", zeilen)
        };

        db.ProduktOwnerAuszahlungen.Add(auszahlung);
        await db.SaveChangesAsync(cancellationToken);
        return auszahlung;
    }

    /// <summary>SuperAdmin bestätigt eine Auszahlung.</summary>
    public async Task<bool> BestaetigeAuszahlungAsync(Guid auszahlungId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var auszahlung = await db.ProduktOwnerAuszahlungen.FirstOrDefaultAsync(a => a.Id == auszahlungId, cancellationToken);
        if (auszahlung is null || auszahlung.Status == Core.DataModels.Abo.AuszahlungStatus.Bestaetigt)
        {
            return false;
        }

        auszahlung.Status = Core.DataModels.Abo.AuszahlungStatus.Bestaetigt;
        auszahlung.BestaetigtUtc = uhr.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Auszahlungen eines Monats (für den SuperAdmin).</summary>
    public async Task<List<Core.DataModels.Abo.ProduktOwnerAuszahlung>> GetAuszahlungenAsync(int jahr, int monat, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ProduktOwnerAuszahlungen
            .AsNoTracking()
            .Include(a => a.ProduktOwner)
            .Where(a => a.Jahr == jahr && a.Monat == monat)
            .OrderByDescending(a => a.Betrag)
            .ToListAsync(cancellationToken);
    }
}
