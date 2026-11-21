using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Data.Seed;

/// <summary>
/// Demo-Daten des Bereichs E2 Plattform (nach der Grund-Demo-Schule; nur erfundene Daten): eine wartende
/// Schulregistrierung, eine zweite Schule desselben Responsable, Annonces (auch für Besucher) und einige
/// Nachrichten der Messagerie.
/// </summary>
public static class DemoSeedPlattform
{
    private const string WartendeSchule = "Institut Démo Horizon";
    private const string ZweiteSchule = "École Primaire Démo Les Cocotiers";

    /// <param name="dienste">Dienste der App (DbContextFactory, UserManager, Bereichsdienste …).</param>
    /// <param name="schuleId">Demo-Schule « Collège Démo Les Palmiers ».</param>
    /// <param name="neueKonten">Neu angelegte Demo-Konten (Passwörter landen nur in demo-konten.json).</param>
    public static async Task ErgaenzenAsync(IServiceProvider dienste, Guid schuleId, List<DemoKonto> neueKonten, CancellationToken ct)
    {
        var dbFactory = dienste.GetRequiredService<IDbContextFactory<SkoulouDbContext>>();
        var users = dienste.GetRequiredService<UserManager<Konto>>();
        var jetzt = dienste.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var schluessel = TextNormalisierung.Schluessel(WartendeSchule);
        if (await db.Schulen.AnyAsync(s => s.NameNormalisiert == schluessel, ct)) return;
        var demo = await db.Schulen.FirstAsync(s => s.Id == schuleId, ct);
        var agb = await db.PlattformEinstellungen.Select(p => p.AgbVersion).FirstOrDefaultAsync(ct);

        // 1. Wartende Registrierung (Schule + Direktion warten auf die Validierung).
        var wartend = new Schule
        {
            Name = WartendeSchule, NameNormalisiert = TextNormalisierung.Schluessel(WartendeSchule), Ort = "Kpalimé", Land = "Togo",
            Status = SchulStatus.EnAttente, MatrikelPraefix = NummernFormat.PraefixAusName(WartendeSchule)
        };
        // 2. Zweite aktive Schule desselben Responsable.
        var zweite = new Schule
        {
            Name = ZweiteSchule, NameNormalisiert = TextNormalisierung.Schluessel(ZweiteSchule), Ort = "Aného", Land = "Togo",
            Status = SchulStatus.Active, ResponsableId = demo.ResponsableId, Telefon = "+228 00 00 00 99",
            MatrikelPraefix = NummernFormat.PraefixAusName(ZweiteSchule), ErstelltAmUtc = DemoSeed.Angemeldet
        };
        db.Schulen.AddRange(wartend, zweite);
        await db.SaveChangesAsync(ct);

        var yao = await KontoAsync(users, neueKonten, "Yao", "Atchou", wartend.Id, KontoStatus.EnAttente, selbst: true, agb);
        var afiwa = await KontoAsync(users, neueKonten, "Afiwa", "Kodjovi", zweite.Id, KontoStatus.Actif, selbst: false, agb: null);
        db.AuditEintraege.Add(new AuditEintrag
        {
            ZeitUtc = jetzt.AddHours(-20), AkteurId = yao.Id, SchuleId = wartend.Id, Aktion = PlattformAudit.SchuleRegistriert,
            ZielTyp = nameof(Schule), ZielId = wartend.Id, Text = $"Inscription en ligne de « {WartendeSchule} » (Kpalimé) par {yao.VollerName}."
        });

        // 3. Annonces (eine auch für Besucher der öffentlichen Seiten).
        db.Ankuendigungen.AddRange(
            new Ankuendigung
            {
                Titel = "Bienvenue sur la nouvelle version de SKOULOU",
                InhaltHtml = HtmlBereiniger.TextAlsHtml("Plus rapide, plus simple, utilisable sur téléphone. Découvrez les vidéos d'aide pour bien démarrer."),
                Stufe = AnkuendigungsStufe.Info, Zielrollen = ZielRollen.Alle, StartUtc = jetzt.AddDays(-1), EndeUtc = jetzt.AddDays(30)
            },
            new Ankuendigung
            {
                Titel = "Facture SKOULOU du 1er trimestre",
                InhaltHtml = HtmlBereiniger.TextAlsHtml("Les factures du 1er trimestre seront disponibles après la fin de la période dans Finances › Facturation SKOULOU."),
                Stufe = AnkuendigungsStufe.Wichtig, Zielrollen = ZielRollen.Direktor | ZielRollen.Koordinator, StartUtc = jetzt.AddDays(-1), EndeUtc = jetzt.AddDays(14)
            },
            new Ankuendigung
            {
                Titel = "Conseils de classe du 1er trimestre",
                InhaltHtml = HtmlBereiniger.TextAlsHtml("Les conseils de classe ont lieu la semaine du 14 décembre. Les bulletins seront publiés ensuite."),
                Stufe = AnkuendigungsStufe.Info, Zielrollen = ZielRollen.Schueler | ZielRollen.Eltern | ZielRollen.Lehrer, SchuleId = schuleId,
                StartUtc = jetzt.AddDays(-2), EndeUtc = jetzt.AddDays(21)
            });

        // 4. Messagerie: zugeordnet über das Absenderkonto, ohne Schule, und eine beantwortete Nachricht.
        var direktorMail = await db.Users.Where(k => k.SchuleId == schuleId && k.Rolle == Rollen.Direktor).Select(k => k.Email).FirstOrDefaultAsync(ct)
                           ?? "direction@" + DemoSeed.Domain;
        var frage = new Nachricht
        {
            Richtung = NachrichtRichtung.Eingang, Von = afiwa.Email!, An = SkoulouMarke.KontaktEmail, Betreff = "Ajouter un deuxième coordinateur",
            TextHtml = HtmlBereiniger.TextAlsHtml("Bonjour,\nPouvons-nous avoir deux coordinateurs dans notre école ?\nMerci."),
            SchuleId = zweite.Id, GelesenAmUtc = jetzt.AddDays(-3), PostmarkId = "demo-" + Guid.NewGuid().ToString("N")
        };
        db.Nachrichten.AddRange(
            new Nachricht
            {
                Richtung = NachrichtRichtung.Eingang, Von = direktorMail, An = SkoulouMarke.KontaktEmail, Betreff = "Question sur l'import des élèves",
                TextHtml = HtmlBereiniger.TextAlsHtml("Bonjour,\nNotre fichier Excel contient deux classes de 6e. Faut-il faire deux imports ?\nCordialement."),
                SchuleId = schuleId, PostmarkId = "demo-" + Guid.NewGuid().ToString("N")
            },
            new Nachricht
            {
                Richtung = NachrichtRichtung.Eingang, Von = "parent.curieux@" + DemoSeed.Domain, An = SkoulouMarke.KontaktEmail, Betreff = "[Contact] Inscription en 6e",
                TextHtml = HtmlBereiniger.TextAlsHtml("Bonjour, comment inscrire mon fils dans une école qui utilise SKOULOU ?")
            },
            frage,
            new Nachricht
            {
                Richtung = NachrichtRichtung.Ausgang, Von = SkoulouMarke.KontaktEmail, An = afiwa.Email!, Betreff = "[SKOULOU] Re: Ajouter un deuxième coordinateur",
                TextHtml = HtmlBereiniger.TextAlsHtml("Bonjour,\nOui : ajoutez-les dans Direction › Équipe ; chaque coordinateur a ses propres droits.\nBonne journée."),
                SchuleId = zweite.Id, AntwortAufId = frage.Id, GelesenAmUtc = jetzt.AddDays(-2)
            });
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Konto> KontoAsync(UserManager<Konto> users, List<DemoKonto> neu, string vorname, string nachname, Guid schuleId,
        KontoStatus status, bool selbst, string? agb)
    {
        var identifiant = IdentifiantRegel.Grundform(vorname, nachname);
        if (await users.FindByNameAsync(identifiant) is { } vorhanden) return vorhanden;
        var passwort = StartPasswort.Erzeuge();
        var konto = new Konto
        {
            UserName = identifiant, Vorname = vorname, Nachname = nachname, SchuleId = schuleId, Email = $"{identifiant}@{DemoSeed.Domain}",
            Status = status, SelbstRegistriert = selbst, AgbVersion = agb, AgbAkzeptiertAmUtc = agb is null ? null : DateTime.UtcNow
        };
        var ergebnis = await KontoAnlage.AnlegenAsync(users, konto, Rollen.Direktor, passwort);
        if (!ergebnis.Succeeded) throw new InvalidOperationException($"Compte de démonstration non créé : {KontoAnlage.Fehlertext(ergebnis)}");
        neu.Add(new DemoKonto(Rollen.Direktor, konto.VollerName, identifiant, passwort));
        return konto;
    }
}
