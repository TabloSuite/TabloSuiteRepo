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
    private readonly TimeProvider uhr = zeit ?? ENote.Core.Services.AppUhr.Provider;

    /// <summary>Erinnerung so viele Tage vor der Kassensperre.</summary>
    public const int ErinnerungTageVorSperre = 3;

    /// <summary>Hinweis so viele Tage vor dem Ende der Probezeit.</summary>
    public const int ProbeHinweisTage = 7;

    public string AppUrl => (configuration["App:PublicBaseUrl"] is { Length: > 0 } u ? u : "https://enote.tablosuite.com").TrimEnd('/');
    private string AboUrl => AppUrl + "/abonnement";
    private string AdminEmpfaenger => configuration["Mail:AdminEmpfaenger"] is { Length: > 0 } a ? a : "contact@tablosuite.com";

    private sealed record Besitzer(string Email, string Vorname, string SalonName, string Name);

    /// <summary>Besitzer (Rolle „Besitzer“) eines Ladens mit E-Mail-Adresse.</summary>
    private static async Task<Besitzer?> BesitzerAsync(ApplicationDbContext db, Guid salonId, CancellationToken ct)
        => await db.UserSalons.AsNoTracking()
            .Where(us => us.SalonId == salonId && !us.AppUser.IsBlocked && us.AppUser.Email != null
                && db.UserRoles.Any(ur => ur.UserId == us.AppUserId && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.Besitzer)))
            .OrderByDescending(us => us.AppUser.IsSelfRegistered)
            .Select(us => new Besitzer(us.AppUser.Email!, us.AppUser.Vorname, us.Salon.Name, us.AppUser.FullName))
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

    /// <summary>Nachricht aus « Aide &amp; contact » an TabloSuite. Liefert false, wenn sie nicht versendet werden konnte.</summary>
    public async Task<bool> KontaktAnfrageAsync(string thema, string nachricht, string name, string pseudonym, string? absenderEmail,
        string? telefon, string? laden, CancellationToken ct = default)
    {
        try
        {
            return await email.SendeAsync(AdminEmpfaenger, $"Contact : {thema}",
                TabloSuiteMails.KontaktAnfrageAdmin(thema, nachricht, name, pseudonym, absenderEmail, telefon, laden), "contact", ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kontaktnachricht konnte nicht versendet werden.");
            return false;
        }
    }

    /// <summary>Aktion oder Anfrage eines Responsables an das TabloSuite-Team melden.</summary>
    public Task ResponsableMeldungAsync(string responsable, string salonName, string titel, string? detail, CancellationToken ct = default)
        => SicherAsync("responsable-meldung", () => email.SendeAsync(AdminEmpfaenger, $"{titel} : {salonName}",
            TabloSuiteMails.ResponsableMeldungAdmin(titel, responsable, salonName, detail, AppUrl + "/admin"), "responsable-meldung", ct));

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

    public Task KontoErstelltAsync(string empfaenger, string vorname, string pseudonym, string salonName, string? passwortLink, CancellationToken ct = default,
        string? erstelltVon = null)
        => SicherAsync("compte-cree", () => email.SendeAsync(empfaenger, "Votre compte eNOTE est prêt",
            TabloSuiteMails.KontoErstellt(vorname, pseudonym, salonName, AppUrl, passwortLink, erstelltVon), "compte-cree", ct));

    public Task ResponsableAngelegtAsync(string empfaenger, string vorname, string pseudonym, string? passwortLink, CancellationToken ct = default)
        => SicherAsync("compte-responsable", () => email.SendeAsync(empfaenger, "Votre compte de responsable est prêt",
            TabloSuiteMails.ResponsableKontoErstellt(vorname, pseudonym, AppUrl, passwortLink), "compte-responsable", ct));

    public Task KassiererAngelegtAsync(string empfaenger, string vorname, string pseudonym, Guid salonId, CancellationToken ct = default)
        => SicherAsync("caissier-bienvenue", async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var b = await BesitzerAsync(db, salonId, ct);
            var salonName = b?.SalonName ?? await db.Salons.Where(s => s.Id == salonId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "votre établissement";
            // Name direkt über den Laden (E-Mail-Adressen sind nicht eindeutig).
            var besitzerName = string.IsNullOrWhiteSpace(b?.Name) ? "Le propriétaire" : b.Name;
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

    /// <summary>Kündigung: Bestätigung an den Besitzer und Info an das Team.</summary>
    public Task KuendigungAsync(Guid salonId, Guid besitzerId, CancellationToken ct = default)
        => SicherAsync("resiliation", async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var salon = await db.Salons.AsNoTracking().FirstOrDefaultAsync(s => s.Id == salonId, ct);
            var u = await db.Users.AsNoTracking().Where(x => x.Id == besitzerId).Select(x => new { x.Email, x.Vorname, x.FullName }).FirstOrDefaultAsync(ct);
            if (salon?.GekuendigtZumUtc is not DateTime zum || u is null) return;
            if (!string.IsNullOrWhiteSpace(u.Email))
                await email.SendeAsync(u.Email, "Résiliation de votre abonnement eNOTE", TabloSuiteMails.KuendigungBestaetigt(u.Vorname, salon.Name, zum, AboUrl), "resiliation", ct);
            await email.SendeAsync(AdminEmpfaenger, $"Résiliation : {salon.Name}",
                TabloSuiteMails.KuendigungAdmin(salon.Name, u.FullName, "Résiliation de l'abonnement", zum, salon.KuendigungGrund, AppUrl + "/admin"), "resiliation-admin", ct);
        });

    /// <summary>Kontolöschung beantragt: Sicherheitsbestätigung an den Besitzer und Info an das Team.</summary>
    public Task LoeschungGeplantAsync(Guid salonId, Guid besitzerId, CancellationToken ct = default)
        => SicherAsync("suppression-compte", async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var salon = await db.Salons.AsNoTracking().FirstOrDefaultAsync(s => s.Id == salonId, ct);
            var u = await db.Users.AsNoTracking().Where(x => x.Id == besitzerId).Select(x => new { x.Email, x.Vorname, x.FullName }).FirstOrDefaultAsync(ct);
            if (salon?.LoeschungAbUtc is not DateTime ab || u is null) return;
            if (!string.IsNullOrWhiteSpace(u.Email))
                await email.SendeAsync(u.Email, "Suppression de votre compte eNOTE programmée", TabloSuiteMails.LoeschungGeplant(u.Vorname, salon.Name, ab, AboUrl), "suppression-compte", ct);
            await email.SendeAsync(AdminEmpfaenger, $"Suppression demandée : {salon.Name}",
                TabloSuiteMails.KuendigungAdmin(salon.Name, u.FullName, "Suppression du compte demandée", ab, salon.KuendigungGrund, AppUrl + "/admin"), "resiliation-admin", ct);
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

    /// <summary>
    /// Formation der Responsables: Mails « session ouverte », « rappel » (7 Tage vor der Frist) und an das Team bei
    /// « non réussie » / « manquée ». Jede höchstens einmal (Zeitstempel an der Runde).
    /// </summary>
    public async Task<int> PruefeFormationMailsAsync(Formation.FormationService formation, CancellationToken ct = default)
    {
        var anzahl = 0;
        var url = AppUrl + "/formation";
        await using var db = await factory.CreateDbContextAsync(ct);
        foreach (var m in await formation.GetOffeneMeldungenAsync(ct))
        {
            var po = await db.Users.AsNoTracking().Where(u => u.Id == m.PoId).Select(u => new { u.Email, u.Vorname, u.FullName, u.IsBlocked }).FirstOrDefaultAsync(ct);
            try
            {
                if (po is not null && !po.IsBlocked)
                {
                    var gesendet = m.Art switch
                    {
                        "ouverte" when po.Email is not null => await email.SendeAsync(po.Email, "Votre évaluation est ouverte",
                            TabloSuiteMails.FormationOuverte(po.Vorname, m.FristUtc, m.Grenze, m.Fragen, url), "formation-session", ct),
                        "rappel" when po.Email is not null => await email.SendeAsync(po.Email, "Rappel : votre évaluation",
                            TabloSuiteMails.FormationRappel(po.Vorname, m.FristUtc, m.Note, m.Grenze, url), "formation-rappel", ct),
                        "resultat" => await email.SendeAsync(AdminEmpfaenger, $"Formation : {po.FullName}",
                            TabloSuiteMails.FormationResultatAdmin(po.FullName, m.Note is null, m.Note, m.Grenze, AppUrl + "/admin?tab=formation"), "formation-resultat", ct),
                        _ => false
                    };
                    if (gesendet) anzahl++;
                }
                // Auch abgeschaltete Mails / fehlende Adresse: nicht stündlich neu versuchen.
                await formation.MarkiereGemeldetAsync(m.RundeId, m.Art, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Formation-Mail « {Art} » für {PoId} fehlgeschlagen.", m.Art, m.PoId);
            }
        }
        return anzahl;
    }
}

/// <summary>
/// Hintergrunddienst und einziger regulärer Versender: neue Mails aus der Warteschlange sofort nach dem
/// <see cref="MailWeckruf"/> (spätestens nach einer Minute), Fehlversuche alle 5 Minuten erneut, stündlich die
/// zeitgesteuerten Abo- und Formation-Mails. Läuft nur, wenn ein Versandweg konfiguriert ist.
/// </summary>
public class MailVersandDienst(IServiceScopeFactory scopes, ILogger<MailVersandDienst> logger, MailWeckruf? weckruf = null) : BackgroundService
{
    /// <summary>Wartezeit nach dem App-Start, bevor neue Mails versendet werden.</summary>
    public TimeSpan StartVerzoegerung { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Fehlversuche, Abo- und Formation-Mails erst so lange nach dem Start (App-Start nicht belasten).</summary>
    public TimeSpan PruefVerzoegerung { get; init; } = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan WiederholAbstand = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PruefAbstand = TimeSpan.FromHours(1);
    private static readonly TimeSpan HoechsteRuhe = TimeSpan.FromMinutes(1);

    /// <summary>Höchstens so viele volle Stapel direkt hintereinander, dann eine Ruhepause (Schutz vor Endlosschleifen).</summary>
    private const int MaxStapelAmStueck = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var weck = weckruf ?? new MailWeckruf();
        try { await Task.Delay(StartVerzoegerung, stoppingToken); } catch (OperationCanceledException) { return; }
        var naechsteWiederholung = ENote.Core.Services.AppUhr.UtcNow + PruefVerzoegerung;
        var naechstePruefung = naechsteWiederholung;
        var stapelAmStueck = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var mehr = false;
            try
            {
                using var scope = scopes.CreateScope();
                var email = scope.ServiceProvider.GetRequiredService<EmailService>();
                if (email.IstKonfiguriert)
                {
                    var (gesendet, weitere) = await email.VersendeNeueAsync(stoppingToken);
                    mehr = weitere;
                    if (gesendet > 0) logger.LogInformation("{Anzahl} E-Mail(s) versendet.", gesendet);

                    if (ENote.Core.Services.AppUhr.UtcNow >= naechsteWiederholung)
                    {
                        naechsteWiederholung = ENote.Core.Services.AppUhr.UtcNow + WiederholAbstand;
                        var erneut = await email.WiederholeAusstehendeAsync(stoppingToken);
                        if (erneut > 0) logger.LogInformation("{Anzahl} E-Mail(s) nachgesendet.", erneut);
                    }

                    if (ENote.Core.Services.AppUhr.UtcNow >= naechstePruefung)
                    {
                        naechstePruefung = ENote.Core.Services.AppUhr.UtcNow + PruefAbstand;
                        var neu = await scope.ServiceProvider.GetRequiredService<MailBenachrichtigungen>().PruefeAboMailsAsync(stoppingToken);
                        if (neu > 0) logger.LogInformation("{Anzahl} Abo-E-Mail(s) eingereiht.", neu);
                        var formation = await scope.ServiceProvider.GetRequiredService<MailBenachrichtigungen>()
                            .PruefeFormationMailsAsync(scope.ServiceProvider.GetRequiredService<Formation.FormationService>(), stoppingToken);
                        if (formation > 0) logger.LogInformation("{Anzahl} Formation-E-Mail(s) eingereiht.", formation);
                        // Die eingereihten Abo-/Formation-Mails gleich im nächsten Durchgang versenden.
                        mehr |= neu + formation > 0;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Mail-Hintergrunddienst fehlgeschlagen.");
                mehr = false;
            }

            if (mehr && ++stapelAmStueck < MaxStapelAmStueck) continue;
            stapelAmStueck = 0;
            try { await weck.WarteAsync(HoechsteRuhe, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
