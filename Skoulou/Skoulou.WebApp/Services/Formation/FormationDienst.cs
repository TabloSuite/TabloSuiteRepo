using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;

namespace Skoulou.WebApp.Services.Formation;

/// <summary>DI der Formation (Lernpfad + Prüfung der Responsables).</summary>
public static class FormationRegistrierung
{
    public static IServiceCollection AddFormation(this IServiceCollection services)
    {
        services.AddScoped<FormationService>();
        services.AddScoped<FormationMails>();
        services.AddHostedService<FormationDienst>();
        return services;
    }
}

/// <summary>
/// Mails der Formation: « Évaluation ouverte » und « Rappel » an den Responsable, « non réussie / manquée » an alle aktiven
/// Administrateure. Jede höchstens einmal (Zeitstempel an der Runde), auch wenn keine Adresse da ist.
/// </summary>
public sealed class FormationMails(FormationService formation, MailAusgangService mails, IDbContextFactory<SkoulouDbContext> dbFactory,
    IOptions<AppOptionen> app, ILogger<FormationMails> logger)
{
    private static string Fett(string text) => $"<strong>{WebUtility.HtmlEncode(text)}</strong>";

    public async Task<int> VersendeAsync(CancellationToken ct = default)
    {
        var anzahl = 0;
        var url = app.Value.Url("/formation");
        var meldungen = await formation.GetOffeneMeldungenAsync(ct);
        if (meldungen.Count == 0) return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var admins = await db.Users.AsNoTracking().Where(k => k.Rolle == Rollen.SuperAdmin && k.Status == KontoStatus.Actif && k.Email != null)
            .Select(k => k.Email!).Distinct().ToListAsync(ct);
        foreach (var m in meldungen)
        {
            try
            {
                var po = await db.Users.AsNoTracking().Where(k => k.Id == m.KontoId).Select(k => new { k.Email, k.Vorname, k.Nachname, k.Status }).FirstOrDefaultAsync(ct);
                if (po is not null && po.Status == KontoStatus.Actif)
                {
                    var frist = m.FristUtc.ToString("dd/MM/yyyy");
                    switch (m.Art)
                    {
                        case "ouverte" when MailAusgangService.IstAdresse(po.Email):
                            await mails.EinreihenAsync(po.Email!, "Votre évaluation est ouverte",
                                MailLayout.Absatz($"Bonjour {po.Vorname},")
                                + MailLayout.Absatz("Une nouvelle session d'évaluation de votre formation de Responsable SKOULOU est ouverte.")
                                + MailLayout.InfoKasten($"<p style=\"margin:0;\">Jusqu'au {Fett(frist)} · {m.Fragen} questions · réussite à partir de {m.Grenze} % · 3 essais au plus, le dernier compte.</p>")
                                + MailLayout.Absatz("Relisez les leçons si besoin : elles sont mises à jour avec l'application. Après chaque essai, vous voyez votre note et vos erreurs.")
                                + MailLayout.Knopf(url, "Ouvrir ma formation"), "formation-session", ct: ct);
                            anzahl++;
                            break;
                        case "rappel" when MailAusgangService.IstAdresse(po.Email):
                            await mails.EinreihenAsync(po.Email!, "Rappel : votre évaluation",
                                MailLayout.Absatz($"Bonjour {po.Vorname},")
                                + MailLayout.Absatz(m.Note is null
                                    ? $"Votre session d'évaluation se termine le {frist} et vous ne l'avez pas encore faite."
                                    : $"Votre dernier essai a obtenu {m.Note} % (réussite à partir de {m.Grenze} %). Vous pouvez encore réessayer jusqu'au {frist}.")
                                + MailLayout.Knopf(url, "Ouvrir ma formation"), "formation-rappel", ct: ct);
                            anzahl++;
                            break;
                        case "resultat":
                            var name = $"{po.Vorname} {po.Nachname}".Trim();
                            foreach (var admin in admins)
                            {
                                await mails.EinreihenAsync(admin, $"Formation : {name}",
                                    MailLayout.Absatz(m.Note is null ? $"{name} n'a pas fait son évaluation avant la fin du délai." : $"{name} n'a pas réussi son évaluation ({m.Note} %, réussite à partir de {m.Grenze} %).")
                                    + MailLayout.Absatz("Vous décidez : ouvrir une nouvelle session, prolonger, dispenser, ou retirer ses écoles et bloquer son compte.")
                                    + MailLayout.Knopf(app.Value.Url("/admin/formation"), "Ouvrir Administration › Formation"), "formation-resultat", ct: ct);
                                anzahl++;
                            }
                            break;
                    }
                }
                await formation.MarkiereGemeldetAsync(m.RundeId, m.Art, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Formation-Mail « {Art} » für {KontoId} fehlgeschlagen.", m.Art, m.KontoId);
            }
        }
        return anzahl;
    }
}

/// <summary>Stündlich: abgelaufene Runden abschließen, fällige öffnen, Mails einreihen.</summary>
public sealed class FormationDienst(IServiceScopeFactory scopes, ILogger<FormationDienst> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch (OperationCanceledException) { return; }
        using var takt = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<FormationService>().AktualisiereAlleAsync(stoppingToken);
                var n = await scope.ServiceProvider.GetRequiredService<FormationMails>().VersendeAsync(stoppingToken);
                if (n > 0) logger.LogInformation("{Anzahl} Formation-E-Mail(s) eingereiht.", n);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Formation-Dienst fehlgeschlagen.");
            }
        }
        while (await takt.WaitForNextTickAsync(stoppingToken));
    }
}
