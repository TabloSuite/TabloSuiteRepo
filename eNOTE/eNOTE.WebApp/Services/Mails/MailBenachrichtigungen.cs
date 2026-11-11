using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.Services;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services.Mails;

/// <summary>
/// Fachliche E-Mail-Benachrichtigungen: wer bekommt wann welche Mail. Alle Methoden sind
/// „best effort“ – ein Mailfehler bricht nie die eigentliche Aktion ab.
/// Zeitgesteuerte Mails (Rechnung, Erinnerung, Sperre, Probe-Ende) prüft <see cref="PruefeAboMailsAsync"/>;
/// jede wird über einen Zeitstempel am Datensatz höchstens einmal versendet.
/// </summary>
public class MailBenachrichtigungen(
    IDbContextFactory<ApplicationDbContext> factory,
    EmailService email,
    IConfiguration configuration,
    ILogger<MailBenachrichtigungen> logger,
    TimeProvider? zeit = null)
{
    private readonly TimeProvider uhr = zeit ?? TimeProvider.System;

    /// <summary>Erinnerung so viele Tage vor der Kassensperre.</summary>
    public const int ErinnerungTageVorSperre = 3;

    /// <summary>Hinweis so viele Tage vor dem Ende der Probezeit.</summary>
    public const int ProbeHinweisTage = 7;

    public string AppUrl => (configuration["App:PublicBaseUrl"] is { Length: > 0 } u ? u : "https://enote.tablosuite.com").TrimEnd('/');
    private string AboUrl => AppUrl + "/abonnement";
    private string AdminEmpfaenger => configuration["Mail:AdminEmpfaenger"] is { Length: > 0 } a ? a : "contact@tablosuite.com";

    private sealed record Besitzer(string Email, string Vorname, string SalonName);

    /// <summary>Besitzer (Rolle „Besitzer“) eines Ladens mit E-Mail-Adresse.</summary>
    private static async Task<Besitzer?> BesitzerAsync(ApplicationDbContext db, Guid salonId, CancellationToken ct)
        => await db.UserSalons.AsNoTracking()
            .Where(us => us.SalonId == salonId && !us.AppUser.IsBlocked && us.AppUser.Email != null
                && db.UserRoles.Any(ur => ur.UserId == us.AppUserId && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.Besitzer)))
            .OrderByDescending(us => us.AppUser.IsSelfRegistered)
            .Select(us => new Besitzer(us.AppUser.Email!, us.AppUser.Vorname, us.Salon.Name))
            .FirstOrDefaultAsync(ct);

    private async Task SicherAsync(string was, Func<Task> aktion)
    {
        try { await aktion(); }
        catch (Exception ex) { logger.LogWarning(ex, "E-Mail-Benachrichtigung « {Was} » fehlgeschlagen.", was); }
    }

    // ------------------------------------------------------------------ Ereignisse

    public Task NeueRegistrierungAsync(string salonName, string besitzerName, string besitzerEmail, string? telefon, string? land, CancellationToken ct = default)
        => SicherAsync("inscription-admin", () => email.SendeAsync(AdminEmpfaenger, $"Nouvel établissement à valider : {salonName}",
            TabloSuiteMails.NeueRegistrierungAdmin(salonName, besitzerName, besitzerEmail, telefon, land, AppUrl + "/admin"), "inscription-admin", ct));

    public Task ZahlungGemeldetAsync(Guid aboId, CancellationToken ct = default)
        => SicherAsync("paiement-signale", async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var abo = await db.NutzungsAbos.AsNoTracking().Include(a => a.Salon).FirstOrDefaultAsync(a => a.Id == aboId, ct);
            if (abo is null) return;
            await email.SendeAsync(AdminEmpfaenger, $"Paiement signalé : {abo.Salon.Name}",
                TabloSuiteMails.ZahlungGemeldetAdmin(abo.Salon.Name, VertragsRegeln.Bezeichnung(abo.Intervall), abo.PreisSnapshot, abo.ZahlungsNotiz, AppUrl + "/admin/facturation"),
                "paiement-signale", ct);
        });

    public Task ZahlungsStatusAsync(Guid aboId, AboZahlungStatus status, CancellationToken ct = default)
        => SicherAsync("paiement-statut", async () =>
        {
            if (status is not (AboZahlungStatus.Bestaetigt or AboZahlungStatus.Abgelehnt)) return;
            await using var db = await factory.CreateDbContextAsync(ct);
            var abo = await db.NutzungsAbos.AsNoTracking().FirstOrDefaultAsync(a => a.Id == aboId, ct);
            if (abo is null || await BesitzerAsync(db, abo.SalonId, ct) is not { } b) return;
            if (status == AboZahlungStatus.Bestaetigt)
                await email.SendeAsync(b.Email, "Paiement confirmé – merci !",
                    TabloSuiteMails.PaiementConfirme(b.Vorname, b.SalonName, abo.PreisSnapshot, abo.ZeitraumVonUtc, abo.ZeitraumBisUtc, AboUrl), "paiement-confirme", ct);
            else
                await email.SendeAsync(b.Email, "Paiement non retrouvé",
                    TabloSuiteMails.PaiementRefuse(b.Vorname, b.SalonName, abo.PreisSnapshot, AboUrl), "paiement-refuse", ct);
        });

    public Task LadenStatusAsync(Guid salonId, bool aktiv, CancellationToken ct = default)
        => SicherAsync("etablissement-statut", async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            if (await BesitzerAsync(db, salonId, ct) is not { } b) return;
            await email.SendeAsync(b.Email, aktiv ? "Votre établissement est réactivé" : "Votre établissement est désactivé",
                TabloSuiteMails.EtablissementStatut(b.Vorname, b.SalonName, aktiv, AppUrl), "etablissement-statut", ct);
        });

    public Task KontoErstelltAsync(string empfaenger, string vorname, string pseudonym, string salonName, string? passwortLink, CancellationToken ct = default)
        => SicherAsync("compte-cree", () => email.SendeAsync(empfaenger, "Votre compte eNOTE est prêt",
            TabloSuiteMails.KontoErstellt(vorname, pseudonym, salonName, AppUrl, passwortLink), "compte-cree", ct));

    public Task KassiererAngelegtAsync(string empfaenger, string vorname, string pseudonym, Guid salonId, CancellationToken ct = default)
        => SicherAsync("caissier-bienvenue", async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var b = await BesitzerAsync(db, salonId, ct);
            var salonName = b?.SalonName ?? await db.Salons.Where(s => s.Id == salonId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "votre établissement";
            var besitzerName = b is null ? "Le propriétaire" : await db.Users.Where(u => u.Email == b.Email).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "Le propriétaire";
            await email.SendeAsync(empfaenger, $"Bienvenue dans l'équipe {salonName}",
                TabloSuiteMails.KassiererWillkommen(vorname, pseudonym, salonName, besitzerName, AppUrl), "caissier-bienvenue", ct);
        });

    public Task PasswortGeaendertAsync(Guid userId, CancellationToken ct = default)
        => SicherAsync("mot-de-passe", async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var u = await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => new { x.Email, x.Vorname, x.UserName }).FirstOrDefaultAsync(ct);
            if (u?.Email is not { Length: > 0 } adresse) return;
            await email.SendeAsync(adresse, "Votre mot de passe a été modifié",
                TabloSuiteMails.PasswortGeaendert(u.Vorname, u.UserName ?? "", uhr.GetUtcNow().UtcDateTime, AppUrl), "mot-de-passe", ct);
        });

    /// <summary>Sicherheitshinweis an die bisherige Adresse nach einer Änderung der E-Mail-Adresse.</summary>
    public Task EmailGeaendertAsync(Guid userId, string alteAdresse, string neueAdresse, CancellationToken ct = default)
        => SicherAsync("email-modifiee", async () =>
        {
            if (string.IsNullOrWhiteSpace(alteAdresse)) return;
            await using var db = await factory.CreateDbContextAsync(ct);
            var u = await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => new { x.Vorname, x.UserName }).FirstOrDefaultAsync(ct);
            if (u is null) return;
            await email.SendeAsync(alteAdresse, "Votre adresse e-mail a été modifiée",
                TabloSuiteMails.EmailGeaendert(u.Vorname, u.UserName ?? "", MaskiereEmail(neueAdresse), uhr.GetUtcNow().UtcDateTime, AppUrl), "email-modifiee", ct);
        });

    /// <summary>„ama.mensah@exemple.com“ → „a•••h@exemple.com“ (für Hinweise an die alte Adresse).</summary>
    public static string MaskiereEmail(string adresse)
    {
        var at = adresse.IndexOf('@');
        if (at <= 0) return "•••";
        var lokal = adresse[..at];
        var maskiert = lokal.Length <= 2 ? lokal[0] + "•••" : lokal[0] + "•••" + lokal[^1];
        return maskiert + adresse[at..];
    }

    // ------------------------------------------------------------------ Zeitgesteuert

    /// <summary>
    /// Rechnung, Erinnerung vor der Sperre, Sperrhinweis und Probe-Ende. Idempotent: jede Mail nur einmal
    /// (Zeitstempel am Abo bzw. Laden). Alte Rechnungen (älter als 3 Tage) lösen keine Rechnungsmail mehr aus,
    /// damit beim ersten Einschalten keine Mail-Welle entsteht.
    /// </summary>
    public async Task<int> PruefeAboMailsAsync(CancellationToken ct = default)
    {
        var jetzt = uhr.GetUtcNow().UtcDateTime;
        var anzahl = 0;
        await using var db = await factory.CreateDbContextAsync(ct);

        var offene = await db.NutzungsAbos
            .Include(a => a.Salon)
            .Where(a => a.Salon.IstAktiv && a.PreisSnapshot > 0
                && (a.ZahlungStatus == AboZahlungStatus.Offen || a.ZahlungStatus == AboZahlungStatus.Abgelehnt)
                && (a.RechnungMailUtc == null || a.ErinnerungMailUtc == null || a.SperrMailUtc == null))
            .ToListAsync(ct);

        foreach (var abo in offene)
        {
            if (await BesitzerAsync(db, abo.SalonId, ct) is not { } b) continue;
            var formel = VertragsRegeln.Bezeichnung(abo.Intervall);
            var faellig = abo.FaelligUtc ?? abo.ZeitraumVonUtc;

            if (abo.RechnungMailUtc is null)
            {
                if (abo.CreatedAtUtc >= jetzt.AddDays(-3) && await email.IstAktivAsync("facture", ct))
                {
                    await email.SendeAsync(b.Email, $"Votre facture eNOTE – {TabloSuiteMails.Betrag(abo.PreisSnapshot)}",
                        TabloSuiteMails.NouvelleFacture(b.Vorname, b.SalonName, formel, abo.ZeitraumVonUtc, abo.ZeitraumBisUtc,
                            abo.PreisSnapshot, faellig, abo.KassenSperreAbUtc, AboUrl), "facture", ct);
                    anzahl++;
                }
                abo.RechnungMailUtc = jetzt;
            }

            if (abo.KassenSperreAbUtc is DateTime sperre)
            {
                if (abo.SperrMailUtc is null && jetzt >= sperre)
                {
                    await email.SendeAsync(b.Email, "Votre caisse est suspendue",
                        TabloSuiteMails.CaisseSuspendue(b.Vorname, b.SalonName, abo.PreisSnapshot, AboUrl), "caisse-suspendue", ct);
                    abo.SperrMailUtc = jetzt;
                    abo.ErinnerungMailUtc ??= jetzt;
                    anzahl++;
                }
                else if (abo.ErinnerungMailUtc is null && jetzt < sperre && jetzt >= sperre.AddDays(-ErinnerungTageVorSperre))
                {
                    // Abgeschaltet: als „erledigt“ markieren, damit beim späteren Einschalten keine alten Erinnerungen nachkommen.
                    if (await email.IstAktivAsync("rappel-paiement", ct))
                    {
                        await email.SendeAsync(b.Email, "Rappel : paiement en attente",
                            TabloSuiteMails.RappelPaiement(b.Vorname, b.SalonName, abo.PreisSnapshot, sperre, AboUrl), "rappel-paiement", ct);
                        anzahl++;
                    }
                    abo.ErinnerungMailUtc = jetzt;
                }
            }
        }

        // Probezeit endet in ≤ 7 Tagen
        var grenze = jetzt.AddDays(ProbeHinweisTage);
        var probe = await db.Salons
            .Where(s => s.IstAktiv && s.IstFreigegeben && s.ProbeEndeMailUtc == null && s.ProbeBisUtc != null
                && s.ProbeBisUtc > jetzt && s.ProbeBisUtc <= grenze)
            .ToListAsync(ct);
        foreach (var salon in probe)
        {
            if (await email.IstAktivAsync("fin-essai", ct) && await BesitzerAsync(db, salon.Id, ct) is { } b)
            {
                var intervall = salon.NaechstesVertragsIntervall ?? salon.VertragsIntervall;
                var preis = (await new AboService(factory).GetFormelPreisAsync(salon.Id, intervall, ct))?.Preis ?? 0m;
                await email.SendeAsync(b.Email, "Votre essai gratuit se termine bientôt",
                    TabloSuiteMails.FinEssai(b.Vorname, salon.Name, salon.ProbeBisUtc!.Value, VertragsRegeln.Bezeichnung(intervall), preis, AboUrl), "fin-essai", ct);
                anzahl++;
            }
            salon.ProbeEndeMailUtc = jetzt;
        }

        await db.SaveChangesAsync(ct);
        return anzahl;
    }
}

/// <summary>
/// Hintergrunddienst: alle 5 Minuten fehlgeschlagene Mails erneut senden, stündlich die
/// zeitgesteuerten Abo-Mails prüfen. Läuft nur, wenn ein Versandweg konfiguriert ist.
/// </summary>
public class MailVersandDienst(IServiceScopeFactory scopes, ILogger<MailVersandDienst> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch (OperationCanceledException) { return; }
        var letztePruefung = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var email = scope.ServiceProvider.GetRequiredService<EmailService>();
                if (email.IstKonfiguriert)
                {
                    var erneut = await email.WiederholeAusstehendeAsync(stoppingToken);
                    if (erneut > 0) logger.LogInformation("{Anzahl} E-Mail(s) nachgesendet.", erneut);

                    if (DateTime.UtcNow - letztePruefung >= TimeSpan.FromHours(1))
                    {
                        letztePruefung = DateTime.UtcNow;
                        var neu = await scope.ServiceProvider.GetRequiredService<MailBenachrichtigungen>().PruefeAboMailsAsync(stoppingToken);
                        if (neu > 0) logger.LogInformation("{Anzahl} Abo-E-Mail(s) versendet.", neu);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Mail-Hintergrunddienst fehlgeschlagen.");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
