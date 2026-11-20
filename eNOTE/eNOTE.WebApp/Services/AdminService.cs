using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// SuperAdmin-/ProduktOwner-Verwaltung: Salons freigeben/ablehnen, Übersicht aller
/// Läden inklusive Teamgröße und Umsatz, Plattformpreise und Abo-Verwaltung.
/// </summary>
public class AdminService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IServiceProvider serviceProvider,
    ILogger<AdminService> logger,
    Mails.EmailService emailService, TimeProvider? zeit = null)
{
    private readonly TimeProvider uhr = zeit ?? TimeProvider.System;

    /// <summary>Fachliche Mails (optional – in Tests ohne Service-Provider nicht vorhanden).</summary>
    private Mails.MailBenachrichtigungen? MailDienst => serviceProvider?.GetService(typeof(Mails.MailBenachrichtigungen)) as Mails.MailBenachrichtigungen;

    // UserManager lazy holen, um ObjectDisposedException zu vermeiden
    // (UserManager ist scoped; wenn der Scope geschlossen ist, wäre er disposed).
    private UserManager<AppUser> UserManager => serviceProvider.GetRequiredService<UserManager<AppUser>>();
    public record SalonUebersicht(
        Guid SalonId,
        string Name,
        string? Standort,
        string? Land,
        bool IstFreigegeben,
        bool IstAktiv,
        bool HatProbezeit,
        int AnzahlMitglieder,
        decimal GesamtUmsatz,
        DateTime CreatedAtUtc,
        string? BesitzerName,
        string? BesitzerEmail,
        string? ProduktOwnerName,
        string? BesitzerTelefon = null);

    public async Task<List<SalonUebersicht>> GetSalonUebersichtAsync(CancellationToken cancellationToken = default)
        => await GetSalonUebersichtGefiltertAsync(null, null, null, null, cancellationToken);

    /// <summary>
    /// Gefilterte Salon-Übersicht für große Datenmengen. Alle Parameter optional.
    /// suchBegriff filtert Name/Standort (case-insensitiv), nurFreigegebene/nurWartende
    /// schränken den Status ein, land filtert exakt, limit begrenzt die Trefferzahl.
    /// </summary>
    public async Task<List<SalonUebersicht>> GetSalonUebersichtGefiltertAsync(
        string? suchBegriff,
        bool? nurFreigegebene,
        bool? nurWartende,
        string? land,
        CancellationToken cancellationToken = default,
        int limit = 200)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var abfrage = db.Salons.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(suchBegriff))
        {
            var s = suchBegriff.Trim();
            abfrage = abfrage.Where(x => x.Name.Contains(s) || (x.Standort != null && x.Standort.Contains(s)));
        }

        if (nurFreigegebene == true)
        {
            abfrage = abfrage.Where(x => x.IstFreigegeben);
        }

        if (nurWartende == true)
        {
            abfrage = abfrage.Where(x => !x.IstFreigegeben && x.IstAktiv);
        }

        if (!string.IsNullOrWhiteSpace(land))
        {
            abfrage = abfrage.Where(x => x.Land == land);
        }

        var salons = await abfrage
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return await AnreichernAsync(db, salons, uhr.GetUtcNow().UtcDateTime, cancellationToken);
    }

    /// <summary>Ergänzt Läden um Teamgröße, Umsatz, Besitzer und Responsable – nur für die übergebene (kleine) Seite.</summary>
    private static async Task<List<SalonUebersicht>> AnreichernAsync(ApplicationDbContext db, List<Salon> salons, DateTime jetztUtc, CancellationToken cancellationToken)
    {
        var ids = salons.Select(s => s.Id).ToList();
        var mitglieder = await db.UserSalons.AsNoTracking()
            .Where(us => ids.Contains(us.SalonId))
            .GroupBy(us => us.SalonId)
            .Select(g => new { g.Key, Anzahl = g.Count() })
            .ToListAsync(cancellationToken);
        var umsatz = await db.Verkaeufe.AsNoTracking()
            .Where(v => ids.Contains(v.SalonId))
            .GroupBy(v => v.SalonId)
            .Select(g => new { g.Key, Summe = g.Sum(v => v.GesamtSumme) })
            .ToListAsync(cancellationToken);

        // Besitzer = Mitglied mit der Rolle "Besitzer" (nicht nur UserSalons mit PIN).
        var besitzerInfos = await db.UserSalons.AsNoTracking()
            .Where(us => ids.Contains(us.SalonId)
                && db.UserRoles.Any(ur => ur.UserId == us.AppUserId
                    && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == ENote.Core.DataModels.Identity.AppRollen.Besitzer)))
            .GroupBy(us => us.SalonId)
            .Select(g => new
            {
                g.Key,
                Name = g.Select(us => us.AppUser.FullName).FirstOrDefault(),
                Email = g.Select(us => us.AppUser.Email).FirstOrDefault(),
                Telefon = g.Select(us => us.AppUser.TelefonNummer).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var poNamen = await db.ProduktOwnerSalons.AsNoTracking()
            .Where(p => ids.Contains(p.SalonId) && p.GueltigBisUtc == null)
            .GroupBy(p => p.SalonId)
            .Select(g => new { g.Key, Name = g.Select(p => p.ProduktOwner.FullName).FirstOrDefault() })
            .ToListAsync(cancellationToken);

        return salons.Select(s => new SalonUebersicht(
            s.Id,
            s.Name,
            s.Standort,
            s.Land,
            s.IstFreigegeben,
            s.IstAktiv,
            s.ProbeBisUtc is not null && s.ProbeBisUtc > jetztUtc,
            mitglieder.FirstOrDefault(m => m.Key == s.Id)?.Anzahl ?? 0,
            umsatz.FirstOrDefault(u => u.Key == s.Id)?.Summe ?? 0m,
            s.CreatedAtUtc,
            besitzerInfos.FirstOrDefault(x => x.Key == s.Id)?.Name,
            besitzerInfos.FirstOrDefault(x => x.Key == s.Id)?.Email,
            poNamen.FirstOrDefault(x => x.Key == s.Id)?.Name,
            besitzerInfos.FirstOrDefault(x => x.Key == s.Id)?.Telefon)).ToList();
    }

    public sealed record SalonKennzahlen(int Gesamt, int Wartend, int Probe, int Zahlend, int Deaktiviert);

    /// <summary>Für SuperAdmin alle Läden, für Responsables nur aktiv zugewiesene.</summary>
    private static IQueryable<Salon> SichtbareSalons(ApplicationDbContext db, Guid userId, bool istSuperAdmin)
        => istSuperAdmin
            ? db.Salons.AsNoTracking()
            : db.Salons.AsNoTracking().Where(s => db.ProduktOwnerSalons.Any(p => p.ProduktOwnerId == userId && p.SalonId == s.Id && p.GueltigBisUtc == null));

    /// <summary>Kennzahlen über ALLE sichtbaren Läden (per Datenbank gezählt, nicht nur die angezeigte Seite).</summary>
    public async Task<SalonKennzahlen> GetSalonKennzahlenAsync(Guid userId, bool istSuperAdmin, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var basis = SichtbareSalons(db, userId, istSuperAdmin);
        var jetzt = uhr.GetUtcNow().UtcDateTime;
        return new SalonKennzahlen(
            await basis.CountAsync(cancellationToken),
            await basis.CountAsync(s => !s.IstFreigegeben && s.IstAktiv, cancellationToken),
            await basis.CountAsync(s => s.IstAktiv && s.ProbeBisUtc != null && s.ProbeBisUtc > jetzt, cancellationToken),
            await basis.CountAsync(s => s.IstAktiv && (s.ProbeBisUtc == null || s.ProbeBisUtc <= jetzt), cancellationToken),
            await basis.CountAsync(s => !s.IstAktiv, cancellationToken));
    }

    /// <summary>
    /// Läden seitenweise mit Suche (Name, Ort, Besitzer: Name oder E-Mail), Status ("validé"/"attente") und Land –
    /// alles in der Datenbank, damit auch bei tausenden Läden nur die angezeigte Seite geladen wird.
    /// </summary>
    public async Task<(List<SalonUebersicht> Eintraege, bool Mehr)> SucheSalonsAsync(
        Guid userId, bool istSuperAdmin, string? suche, string? status, string? land, int ueberspringen, int anzahl = 50,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var abfrage = SichtbareSalons(db, userId, istSuperAdmin);
        if (!string.IsNullOrWhiteSpace(suche))
        {
            var q = suche.Trim();
            abfrage = abfrage.Where(x => x.Name.Contains(q) || (x.Standort != null && x.Standort.Contains(q))
                || db.UserSalons.Any(us => us.SalonId == x.Id && ((us.AppUser.FullName != null && us.AppUser.FullName.Contains(q))
                    || (us.AppUser.Email != null && us.AppUser.Email.Contains(q)))));
        }
        abfrage = status switch
        {
            "validé" => abfrage.Where(x => x.IstFreigegeben),
            "attente" => abfrage.Where(x => !x.IstFreigegeben && x.IstAktiv),
            _ => abfrage
        };
        if (!string.IsNullOrWhiteSpace(land)) abfrage = abfrage.Where(x => x.Land == land);

        var salons = await abfrage.OrderByDescending(x => x.CreatedAtUtc)
            .Skip(Math.Max(0, ueberspringen)).Take(anzahl + 1).ToListAsync(cancellationToken);
        var mehr = salons.Count > anzahl;
        if (mehr) salons.RemoveAt(salons.Count - 1);
        return (await AnreichernAsync(db, salons, uhr.GetUtcNow().UtcDateTime, cancellationToken), mehr);
    }

    /// <summary>So viele Tage ab heute darf ein Responsable eine laufende Probezeit höchstens verlängern.</summary>
    public const int PoProbeMaxTage = 30;

    public const string NurAdminMeldung = "Cette action est réservée à l'administrateur. Envoyez-lui une demande.";

    /// <summary>Wer handelt? SuperAdmin oder Responsable (ProduktOwner) mit aktiver Zuweisung für diesen Laden.</summary>
    public sealed record Bearbeiter(bool IstAdmin, string Name);

    /// <summary>
    /// Serverseitige Prüfung jeder Ladenaktion: SuperAdmin darf alles, ein Responsable nur bei aktiv
    /// zugewiesenen Läden (abgelaufene Zuweisungen zählen nicht), alle anderen nichts.
    /// </summary>
    public static async Task<Bearbeiter> PruefeBearbeiterAsync(ApplicationDbContext db, Guid bearbeiter, Guid salonId, CancellationToken ct)
    {
        var konto = await db.Users.AsNoTracking()
            .Where(u => u.Id == bearbeiter && !u.IsBlocked)
            .Select(u => new
            {
                u.FullName,
                Admin = db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.SuperAdmin)),
                Po = db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.ProduktOwner))
            })
            .FirstOrDefaultAsync(ct);
        if (konto?.Admin == true)
            return new Bearbeiter(true, konto.FullName ?? "");
        if (konto?.Po == true && await db.ProduktOwnerSalons.AnyAsync(
                p => p.ProduktOwnerId == bearbeiter && p.SalonId == salonId && p.GueltigBisUtc == null, ct))
            return new Bearbeiter(false, konto.FullName ?? "");
        throw new UnauthorizedAccessException("Vous ne disposez pas des droits nécessaires pour cet établissement.");
    }

    /// <summary>Freigabe setzen. Ein Responsable darf nur freigeben (nie entziehen); der Admin wird informiert.</summary>
    public async Task<bool> SetzeFreigabeAsync(Guid bearbeiter, Guid salonId, bool freigegeben, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var wer = await PruefeBearbeiterAsync(db, bearbeiter, salonId, cancellationToken);
        if (!wer.IstAdmin && !freigegeben)
            throw new UnauthorizedAccessException(NurAdminMeldung);

        var salon = await db.Salons.FirstOrDefaultAsync(s => s.Id == salonId, cancellationToken);
        if (salon is null)
        {
            return false;
        }

        salon.IstFreigegeben = freigegeben;
        salon.UpdatedAtUtc = uhr.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);

        // Validierungs-Mail an den Besitzer (nur bei Freigabe, nicht beim Entzug).
        if (freigegeben)
        {
            try
            {
                var besitzer = await db.UserSalons.AsNoTracking()
                    .Where(us => us.SalonId == salonId)
                    .Select(us => us.AppUser)
                    .FirstOrDefaultAsync(u => u.IsSelfRegistered, cancellationToken)
                    ?? await db.UserSalons.AsNoTracking().Where(us => us.SalonId == salonId).Select(us => us.AppUser).FirstOrDefaultAsync(cancellationToken);

                if (besitzer is not null && !string.IsNullOrWhiteSpace(besitzer.Email))
                {
                    await emailService.SendeAsync(
                        besitzer.Email,
                        "Votre inscription a été validée",
                        Mails.TabloSuiteMails.InscriptionValidee(besitzer.Vorname, "eNOTE", MailDienst?.AppUrl ?? BonQr.AppSeite),
                        "inscription-validee",
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Validierungs-Mail für Salon {SalonId} fehlgeschlagen.", salonId);
            }

            if (!wer.IstAdmin && MailDienst is { } m)
                await m.ResponsableMeldungAsync(wer.Name, salon.Name, "Établissement validé par un responsable", null, cancellationToken);
        }
        return true;
    }

    /// <summary>Wählbare Länder (siehe <see cref="LaenderAuswahl"/>).</summary>
    public async Task<List<string>> GetLaenderAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await LaenderAuswahl.GetAsync(db, cancellationToken);
    }

    /// <summary>Land eines Ladens ändern (Ortszeit, Mobile Money) – nur der SuperAdmin.</summary>
    public async Task<bool> SetzeLandAsync(Guid bearbeiter, Guid salonId, string? land, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!(await PruefeBearbeiterAsync(db, bearbeiter, salonId, cancellationToken)).IstAdmin)
            throw new UnauthorizedAccessException(NurAdminMeldung);
        return await LaenderAuswahl.SetzeAsync(db, salonId, land, uhr.GetUtcNow().UtcDateTime, cancellationToken);
    }

    /// <summary>Laden abschalten/einschalten – nur der SuperAdmin (kritisch, auch bei gestohlenen Responsable-Zugangsdaten).</summary>
    public async Task<bool> SetzeAktivAsync(Guid bearbeiter, Guid salonId, bool aktiv, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!(await PruefeBearbeiterAsync(db, bearbeiter, salonId, cancellationToken)).IstAdmin)
            throw new UnauthorizedAccessException(NurAdminMeldung);

        var salon = await db.Salons.FirstOrDefaultAsync(s => s.Id == salonId, cancellationToken);
        if (salon is null)
        {
            return false;
        }

        var geaendert = salon.IstAktiv != aktiv;
        salon.IstAktiv = aktiv;
        salon.UpdatedAtUtc = uhr.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        if (geaendert && MailDienst is { } m)
            await m.LadenStatusAsync(salonId, aktiv, cancellationToken);
        return true;
    }

    /// <summary>
    /// Probezeit ändern. Admin: frei (verlängern, kürzen, entfernen). Responsable: nur eine bestehende
    /// Probezeit verlängern, höchstens <see cref="PoProbeMaxTage"/> Tage ab heute – nie kürzen oder entfernen;
    /// der Admin wird informiert.
    /// </summary>
    public async Task<bool> SetzeProbezeitAsync(Guid bearbeiter, Guid salonId, DateTime? probeBisUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var wer = await PruefeBearbeiterAsync(db, bearbeiter, salonId, cancellationToken);
        var salon = await db.Salons.FirstOrDefaultAsync(s => s.Id == salonId, cancellationToken);
        if (salon is null)
        {
            return false;
        }

        // Datum aus dem Datumsfeld gilt einschließlich des Tages (wie bei der Registrierung: Tagesende).
        var neu = probeBisUtc is DateTime tag && tag.TimeOfDay == TimeSpan.Zero
            ? ENote.Core.Services.VertragsRegeln.ProbeEndeEinschliesslich(tag)
            : probeBisUtc;

        if (!wer.IstAdmin)
        {
            if (salon.ProbeBisUtc is not DateTime bisher || neu is not DateTime gewuenscht)
                throw new UnauthorizedAccessException(NurAdminMeldung);
            if (gewuenscht < bisher)
                throw new UnauthorizedAccessException("Un responsable peut seulement prolonger l'essai, pas le raccourcir.");
            if (gewuenscht > PoProbeGrenze(uhr.GetUtcNow().UtcDateTime))
                throw new UnauthorizedAccessException($"Un responsable peut prolonger l'essai de {PoProbeMaxTage} jours au plus à partir d'aujourd'hui.");
        }

        salon.ProbeBisUtc = neu;
        salon.UpdatedAtUtc = uhr.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);

        if (!wer.IstAdmin && MailDienst is { } m)
            await m.ResponsableMeldungAsync(wer.Name, salon.Name, "Essai prolongé par un responsable",
                $"Nouvelle fin d'essai : {neu:dd.MM.yyyy}", cancellationToken);
        return true;
    }

    /// <summary>Spätestes Probezeit-Ende, das ein Responsable setzen darf (Tagesende, <see cref="PoProbeMaxTage"/> Tage ab heute).</summary>
    public static DateTime PoProbeGrenze(DateTime jetztUtc)
        => ENote.Core.Services.VertragsRegeln.ProbeEndeEinschliesslich(jetztUtc.Date.AddDays(PoProbeMaxTage));

    /// <summary>
    /// Anfrage eines Responsables an den Admin (z. B. Laden deaktivieren oder Probezeit kürzen):
    /// kritische Aktionen führt nur der Admin aus, der Responsable meldet sie per Mail.
    /// </summary>
    public async Task MeldeAnfrageAsync(Guid bearbeiter, Guid salonId, string anliegen, string? grund, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(anliegen))
            throw new ArgumentException("Veuillez choisir l'objet de la demande.");
        if (grund?.Length > 500)
            throw new ArgumentException("Le message est trop long (500 caractères au plus).");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var wer = await PruefeBearbeiterAsync(db, bearbeiter, salonId, cancellationToken);
        var salonName = await db.Salons.AsNoTracking().Where(s => s.Id == salonId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Établissement introuvable.");
        if (MailDienst is not { } m)
            throw new InvalidOperationException("L'envoi d'e-mails n'est pas disponible.");
        await m.ResponsableMeldungAsync(wer.Name, salonName, "Demande d'un responsable : " + anliegen.Trim(), grund?.Trim(), cancellationToken);
    }

    /// <summary>Aktuelle Probezeit eines Ladens lesen.</summary>
    public async Task<DateTime?> GetProbezeitAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Salons
            .AsNoTracking()
            .Where(s => s.Id == salonId)
            .Select(s => s.ProbeBisUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<PlattformPreis>> GetPlattformPreiseAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.PlattformPreise
            .AsNoTracking()
            .Where(p => p.IstAktuell)
            .OrderBy(p => p.Intervall)
            .ToListAsync(cancellationToken);
    }

    /// <summary>SuperAdmin setzt den globalen Monatspreis (serverseitig geprüft).</summary>
    public async Task SetzePlattformPreisAsync(Guid bearbeiter, AboIntervall intervall, decimal preis, CancellationToken cancellationToken = default)
    {
        await using (var pruefung = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            await AdminKontenService.PruefeAsync(pruefung, bearbeiter, false, cancellationToken);
        }

        await SetzePlattformPreisAsync(intervall, preis, cancellationToken);
    }

    public async Task SetzePlattformPreisAsync(AboIntervall intervall, decimal preis, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Bisherige aktuelle Preise des Intervalls historisieren.
        var bisherige = await db.PlattformPreise
            .Where(p => p.Intervall == intervall && p.IstAktuell)
            .ToListAsync(cancellationToken);
        foreach (var alt in bisherige)
        {
            alt.IstAktuell = false;
        }

        var jetzt = uhr.GetUtcNow().UtcDateTime;
        db.PlattformPreise.Add(new PlattformPreis
        {
            Intervall = intervall,
            Preis = preis,
            IstAktuell = true,
            GueltigAbUtc = jetzt
        });

        // Der Monatspreis ist die einzige Basis aller Formeln: auch in der Preis-Historie festhalten,
        // sonst hätte ein älterer globaler Historien-Eintrag Vorrang und die Änderung bliebe wirkungslos.
        if (intervall == AboIntervall.Monatlich)
        {
            db.MonatsPreisStaende.Add(new MonatsPreisStand { SalonId = null, MonatsPreis = preis, GueltigAbUtc = jetzt });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public record LadenTarif(string Name, decimal Global, decimal? Individuell);

    /// <summary>Globaler Monatspreis aus derselben Quelle wie Besitzer-Preise und Tarifdialog.</summary>
    public async Task<decimal> GetGlobalenMonatsPreisAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await AboService.GlobalerMonatsPreisAsync(db, cancellationToken, uhr.GetUtcNow().UtcDateTime);
    }

    public async Task<LadenTarif> GetLadenTarifAsync(
        Guid bearbeiter, Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await AdminKontenService.PruefeAsync(db, bearbeiter, false, cancellationToken);
        var salon = await db.Salons.AsNoTracking()
            .SingleAsync(s => s.Id == salonId, cancellationToken);
        var global = await AboService.GlobalerMonatsPreisAsync(db, cancellationToken, uhr.GetUtcNow().UtcDateTime);
        return new LadenTarif(salon.Name, global, salon.IndividuellerMonatsPreis);
    }

    public async Task<bool> SetzeIndividuellenPreisAsync(
        Guid bearbeiter, Guid salonId, decimal? preis,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await AdminKontenService.PruefeAsync(db, bearbeiter, false, cancellationToken);
        return await SetzeIndividuellenPreisAsync(salonId, preis, cancellationToken);
    }

    public async Task<bool> SetzeIndividuellenPreisAsync(
        Guid salonId, decimal? preis, CancellationToken cancellationToken = default)
    {
        if (preis is <= 0)
            throw new ArgumentOutOfRangeException(nameof(preis),
                "Veuillez saisir un montant positif.");
        // Klicktest 20.11.2026: « 2.500 » im Zahlenfeld = 2,5 – ganze Francs wie im Katalog.
        if (preis is decimal p && PreisEingabe.Pruefe(p) is { } ganze)
            throw new ArgumentOutOfRangeException(nameof(preis), ganze);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var salon = await db.Salons.FirstOrDefaultAsync(s => s.Id == salonId, cancellationToken);
        if (salon is null)
        {
            return false;
        }

        var jetzt = uhr.GetUtcNow().UtcDateTime;
        // Der Standardpreis ist der Mindestpreis: ein individueller Preis darf nicht darunter liegen.
        var standard = await AboService.GlobalerMonatsPreisAsync(db, cancellationToken, uhr.GetUtcNow().UtcDateTime);
        if (preis is decimal eigener && standard > 0 && eigener < standard)
            throw new ArgumentOutOfRangeException(nameof(preis),
                $"Le tarif personnalisé ne peut pas être inférieur au tarif général ({standard:N0} F).");
        salon.IndividuellerMonatsPreis = preis;
        salon.UpdatedAtUtc = jetzt;
        db.MonatsPreisStaende.Add(new MonatsPreisStand
        {
            SalonId = salonId,
            MonatsPreis = preis,
            GueltigAbUtc = jetzt
        });
        // Ist eine Tarifänderung geplant, gilt sie auch für diesen neuen Preis.
        await PreisAnpassungService.PasseGeplanteAnAsync(db, salonId, preis, jetzt, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// SuperAdmin legt einen Besitzer samt Geschäft an (freigegeben & aktiv).
    /// Der Besitzer erhält ein Initialpasswort und kann sich sofort anmelden.
    /// </summary>
    public Task<(bool Erfolg, string? Fehler, AppUser? Besitzer)> LegeBesitzerMitSalonAnAsync(
        string vorname, string nachname, string email, string initialPasswort,
        string salonName, string? standort, string? land = null,
        CancellationToken cancellationToken = default, string? telefon = null)
        => LegeBesitzerMitSalonAnInternAsync(
            vorname, nachname, email, initialPasswort, salonName, standort,
            land, cancellationToken, email, telefon);

    public async Task<(bool Erfolg, string? Fehler, AppUser? Besitzer)> LegeBesitzerMitSalonAnAsync(
        Guid bearbeiter, string vorname, string nachname, string email,
        string initialPasswort, string salonName, string? standort,
        string? land = null, CancellationToken cancellationToken = default,
        string? benutzername = null, string? telefon = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await AdminKontenService.PruefeAsync(db, bearbeiter, false, cancellationToken);
        return await LegeBesitzerMitSalonAnInternAsync(
            vorname, nachname, email, initialPasswort, salonName, standort,
            land, cancellationToken, benutzername ?? email, telefon);
    }

    private async Task<(bool Erfolg, string? Fehler, AppUser? Besitzer)> LegeBesitzerMitSalonAnInternAsync(
        string vorname, string nachname, string email, string initialPasswort,
        string salonName, string? standort, string? land,
        CancellationToken cancellationToken, string benutzername, string? telefon)
    {
        var bereinigtEmail = email.Trim();
        var bereinigtSalon = salonName.Trim();
        var pseudonym = benutzername.Trim();
        // Telefon des Besitzers ist Pflicht (wie bei der öffentlichen Registrierung): man muss ihn leicht erreichen können.
        var (bereinigtTelefon, telefonFehler) = TelefonRegel.Pruefe(telefon, TelefonRegel.MeldungPflichtBesitzer);
        if (telefonFehler is not null) return (false, telefonFehler, null);
        // Klicktest 20.11.2026: überlange Namen scheitern sonst an der Datenbank bzw. später an jedem Verkauf.
        if (KontoProfilService.PruefeNamen(vorname, nachname, pflicht: false) is { } namensFehler) return (false, namensFehler, null);
        if (bereinigtSalon.Length > AuthService.LadenNameMax) return (false, $"Nom de l'établissement : {AuthService.LadenNameMax} caractères au maximum.", null);

        if (string.IsNullOrWhiteSpace(vorname) || string.IsNullOrWhiteSpace(nachname)
            || string.IsNullOrWhiteSpace(bereinigtSalon) || string.IsNullOrWhiteSpace(pseudonym)
            || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(bereinigtEmail))
        {
            return (false, "Veuillez saisir les coordonnées et un pseudonyme valide.", null);
        }

        if (await UserManager.FindByNameAsync(pseudonym) is not null)
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
            UserName = pseudonym,
            Email = bereinigtEmail,
            Vorname = vorname.Trim(),
            Nachname = nachname.Trim(),
            FullName = $"{vorname.Trim()} {nachname.Trim()}",
            TelefonNummer = bereinigtTelefon,
            EmailConfirmed = true
        };

        var erstellen = await UserManager.CreateAsync(besitzer, initialPasswort);
        if (!erstellen.Succeeded)
        {
            var fehler = string.Join(" ", erstellen.Errors.Select(e => e.Description));
            logger.LogWarning("Admin: Besitzer-Anlage fehlgeschlagen für {Email}: {Fehler}", bereinigtEmail, fehler);
            return (false, string.IsNullOrWhiteSpace(fehler) ? "La création a échoué." : fehler, null);
        }

        await UserManager.AddToRoleAsync(besitzer, AppRollen.Besitzer);

        var salon = new Salon
        {
            Name = bereinigtSalon,
            Standort = string.IsNullOrWhiteSpace(standort) ? null : standort.Trim(),
            Land = string.IsNullOrWhiteSpace(land) ? null : land.Trim(),
            IstAktiv = true,
            IstFreigegeben = true, // vom Admin angelegt ? sofort freigegeben
            ProbeBisUtc = AuthService.BerechneProbeEnde(uhr.GetUtcNow().UtcDateTime) // Probezeit wie bei Selbstregistrierung
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
        await db.SaveChangesAsync(cancellationToken);

        // Zugangsmail an den neuen Besitzer – mit Link zum Festlegen des eigenen Passworts.
        if (MailDienst is { } m && !string.IsNullOrWhiteSpace(besitzer.Email))
        {
            string? link = null;
            try
            {
                var token = await UserManager.GeneratePasswordResetTokenAsync(besitzer);
                link = $"{m.AppUrl}/auth/reinitialiser?compte={besitzer.Id}&code={Uri.EscapeDataString(token)}";
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Passwort-Link für neuen Besitzer konnte nicht erzeugt werden.");
            }
            await m.KontoErstelltAsync(besitzer.Email, besitzer.Vorname, besitzer.UserName ?? "", salon.Name, link, cancellationToken);
        }

        return (true, null, besitzer);
    }

    /// <summary>SuperAdmin legt einen ProduktOwner an (Initialpasswort, Rolle).
    /// Login ist das eindeutige Pseudonym; die E-Mail ist nur Kontaktadresse und darf mehrfach verwendet werden.</summary>
    public async Task<(bool Erfolg, string? Fehler, AppUser? ProduktOwner)> LegeProduktOwnerAnAsync(
        string vorname, string nachname, string pseudonym, string email, string initialPasswort, CancellationToken cancellationToken = default)
    {
        var bereinigtPseudonym = pseudonym.Trim();
        if (string.IsNullOrWhiteSpace(bereinigtPseudonym))
        {
            return (false, "Veuillez saisir un pseudonyme.", null);
        }

        // Login = Pseudonym (eindeutig); E-Mail ist nur Kontaktadresse und darf mehrfach sein.
        if (await UserManager.FindByNameAsync(bereinigtPseudonym) is not null)
        {
            return (false, "Ce pseudonyme est déjà utilisé.", null);
        }

        var bereinigtEmail = email.Trim();
        var po = new AppUser
        {
            UserName = bereinigtPseudonym,
            Email = bereinigtEmail,
            Vorname = vorname.Trim(),
            Nachname = nachname.Trim(),
            FullName = $"{vorname.Trim()} {nachname.Trim()}",
            EmailConfirmed = true
        };

        var erstellen = await UserManager.CreateAsync(po, initialPasswort);
        if (!erstellen.Succeeded)
        {
            var fehler = string.Join(" ", erstellen.Errors.Select(e => e.Description));
            logger.LogWarning("Admin: ProduktOwner-Anlage fehlgeschlagen für {Email}: {Fehler}", bereinigtEmail, fehler);
            return (false, string.IsNullOrWhiteSpace(fehler) ? "La création a échoué." : fehler, null);
        }

        await UserManager.AddToRoleAsync(po, AppRollen.ProduktOwner);

        // Zugangsmail an den neuen Responsable (Testabend 18.11.2026: bisher keine) – mit Link zum eigenen Passwort.
        if (MailDienst is { } m && !string.IsNullOrWhiteSpace(po.Email))
        {
            string? link = null;
            try
            {
                var token = await UserManager.GeneratePasswordResetTokenAsync(po);
                link = $"{m.AppUrl}/auth/reinitialiser?compte={po.Id}&code={Uri.EscapeDataString(token)}";
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Passwort-Link für neuen Responsable konnte nicht erzeugt werden.");
            }
            await m.ResponsableAngelegtAsync(po.Email, po.Vorname, po.UserName ?? "", link, cancellationToken);
        }
        return (true, null, po);
    }

    /// <summary>Alle ProduktOwner (für die Admin-Verwaltung).</summary>
    public async Task<List<AppUser>> GetProduktOwnersAsync(CancellationToken cancellationToken = default)
    {
        var poInRolle = await UserManager.GetUsersInRoleAsync(AppRollen.ProduktOwner);
        return poInRolle.OrderBy(u => u.FullName).ToList();
    }

    /// <summary>
    /// Salon-Übersicht gefiltert nach Rolle: SuperAdmin sieht alle, ProduktOwner nur
    /// zugewiesene + selbst angelegte Läden.
    /// </summary>
    public async Task<List<SalonUebersicht>> GetSalonUebersichtFuerAsync(
        Guid userId, bool istSuperAdmin, CancellationToken cancellationToken = default)
    {
        var alle = await GetSalonUebersichtAsync(cancellationToken);
        if (istSuperAdmin)
        {
            return alle;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var verwaltet = await db.ProduktOwnerSalons
            .AsNoTracking()
            .Where(p => p.ProduktOwnerId == userId && p.GueltigBisUtc == null)
            .Select(p => p.SalonId)
            .ToListAsync(cancellationToken);

        var verwaltetSet = verwaltet.ToHashSet();
        return alle.Where(s => verwaltetSet.Contains(s.SalonId)).ToList();
    }
}
