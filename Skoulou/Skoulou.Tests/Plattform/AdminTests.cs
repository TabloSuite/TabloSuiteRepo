using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Plattform;

/// <summary>Admin: Rechte-Matrix je Service-Methode, Schulstatus mit Mail und Audit, Konten, Responsables.</summary>
public class AdminTests
{
    private sealed record Welt(PlattformUmgebung U, Konto Admin, Konto Responsable, Konto Direktor, Konto Lehrer, Schule Eigene, Schule Fremde);

    private static async Task<Welt> WeltAsync(PlattformUmgebung u)
    {
        var admin = await u.StartAsync();
        var resp = await u.Basis.KontoAsync("resp", Rollen.Responsable, email: "resp@exemple.tg");
        var eigene = await u.Basis.SchuleAsync("Eigene", responsable: resp.Id);
        var fremde = await u.Basis.SchuleAsync("Fremde");
        var dir = await u.Basis.KontoAsync("dir", Rollen.Direktor, eigene.Id, email: "dir@exemple.tg");
        var lehrer = await u.Basis.KontoAsync("lehrer", Rollen.Lehrer, fremde.Id);
        return new Welt(u, admin, resp, dir, lehrer, eigene, fremde);
    }

    /// <summary>Nur der SuperAdmin – Responsable, Direktor und Lehrer werden abgewiesen, bevor etwas gelesen wird.</summary>
    [Fact]
    public async Task Rechte_Matrix_Adminfunktionen()
    {
        await using var u = new PlattformUmgebung();
        var w = await WeltAsync(u);
        var nurAdmin = new (string Name, Func<Guid, Task> Aufruf)[]
        {
            ("Schulen.Suche", a => u.Schulen.SucheAsync(a, null, null)),
            ("Schulen.Validieren", a => u.Schulen.ValidierenAsync(a, w.Fremde.Id)),
            ("Schulen.Suspendieren", a => u.Schulen.SuspendierenAsync(a, w.Eigene.Id, "Motif de test")),
            ("Schulen.Responsable", a => u.Schulen.ResponsableZuweisenAsync(a, w.Fremde.Id, null)),
            ("Konten.Suche", a => u.Konten.SucheAsync(a, null, null)),
            ("Konten.Sperren", a => u.Konten.SperrenAsync(a, w.Lehrer.Id, null)),
            ("Konten.Passwort", a => u.Konten.PasswortZuruecksetzenAsync(a, w.Lehrer.Id)),
            ("Konten.ResponsableAnlegen", a => u.Konten.ResponsableAnlegenAsync(a, new ResponsableEingabe("A", "B", null, "a@exemple.tg", null))),
            ("Konten.SchulenZuweisen", a => u.Konten.SchulenZuweisenAsync(a, w.Responsable.Id, [w.Fremde.Id])),
            ("Annonces.Suche", a => u.Annonces.SucheAsync(a, null, null)),
            ("Messagerie.Zuordnen", a => u.Messagerie.ZuordnenAsync(a, Guid.NewGuid(), null)),
            ("Journal.Fehler", a => u.Journal.FehlerAsync(a, new ProtokollFilter())),
            ("Journal.Audit", a => u.Journal.AuditAsync(a, new ProtokollFilter(), null)),
            ("Medien.Summen", a => u.Medien.SummenAsync(a)),
            ("Uebersicht.Zahlen", a => u.Uebersicht.ZahlenAsync(a)),
        };
        foreach (var (name, aufruf) in nurAdmin)
        {
            foreach (var k in new[] { w.Responsable, w.Direktor, w.Lehrer })
            {
                var ex = await Record.ExceptionAsync(() => aufruf(k.Id));
                Assert.True(ex is KeinZugriffFehler, $"{name} für {k.Rolle}: {ex?.GetType().Name ?? "kein Fehler"}");
            }
        }
        // Betreuung: Responsable ja (eigene Schulen), Schulrollen nein.
        await u.Messagerie.SucheAsync(w.Responsable.Id, Postfach.Eingang, null, null);
        Assert.Empty(await u.Uebersicht.MeineSchulenAsync(w.Admin.Id));
        Assert.Single(await u.Uebersicht.MeineSchulenAsync(w.Responsable.Id));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Messagerie.SucheAsync(w.Direktor.Id, Postfach.Eingang, null, null));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Uebersicht.MeineSchulenAsync(w.Lehrer.Id));
        // Admin darf alles lesen.
        Assert.Equal(2, (await u.Schulen.SucheAsync(w.Admin.Id, null, null)).Eintraege.Count);
        Assert.Equal(2, (await u.Uebersicht.ZahlenAsync(w.Admin.Id)).Schulen.Active);
    }

    [Fact]
    public async Task Schulstatus_validieren_ablehnen_suspendieren_mit_Mail_und_Audit()
    {
        await using var u = new PlattformUmgebung();
        var w = await WeltAsync(u);
        var reg = await u.Registrierung.RegistrierenAsync(PlattformUmgebung.Direction(), null);
        var neu = await u.LiesAsync(db => db.Schulen.SingleAsync(s => s.Name == "Collège Horizon"));

        await Assert.ThrowsAsync<FachFehler>(() => u.Schulen.AblehnenAsync(w.Admin.Id, neu.Id, " "));
        await Assert.ThrowsAsync<FachFehler>(() => u.Schulen.SuspendierenAsync(w.Admin.Id, neu.Id, "Pas encore active"));
        await u.Schulen.ValidierenAsync(w.Admin.Id, neu.Id);
        var direktor = await u.LiesAsync(db => db.Users.SingleAsync(k => k.UserName == reg.Identifiant));
        Assert.Equal(KontoStatus.Actif, direktor.Status);
        Assert.Contains(await u.MailsAsync("dir@exemple.tg"), m => m.Betreff.Contains("validé"));
        await Assert.ThrowsAsync<FachFehler>(() => u.Schulen.ValidierenAsync(w.Admin.Id, neu.Id));

        await u.Schulen.SuspendierenAsync(w.Admin.Id, neu.Id, "Factures impayées");
        var s = await u.LiesAsync(db => db.Schulen.SingleAsync(x => x.Id == neu.Id));
        Assert.Equal((SchulStatus.Suspendue, "Factures impayées"), (s.Status, s.StatusGrund));
        Assert.Equal(AnmeldeErgebnis.SchuleSuspendiert, AnmeldeRegeln.Pruefe(direktor.Status, Rollen.Direktor, s.Status));
        Assert.Contains(await u.MailsAsync("dir@exemple.tg"), m => m.Text.Contains("Factures impayées"));
        await u.Schulen.ReaktivierenAsync(w.Admin.Id, neu.Id);

        var zweite = await u.Registrierung.RegistrierenAsync(PlattformUmgebung.Direction("Lycée Refus", "refus@exemple.tg"), null);
        var refus = await u.LiesAsync(db => db.Schulen.SingleAsync(x => x.Name == "Lycée Refus"));
        await u.Schulen.AblehnenAsync(w.Admin.Id, refus.Id, "Établissement introuvable");
        Assert.Equal(SchulStatus.Refusee, (await u.LiesAsync(db => db.Schulen.SingleAsync(x => x.Id == refus.Id))).Status);
        Assert.Contains(await u.MailsAsync("refus@exemple.tg"), m => m.Text.Contains("Établissement introuvable"));
        Assert.Equal(4, await u.LiesAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion.StartsWith("schule.") && a.Aktion != PlattformAudit.SchuleRegistriert)));
        Assert.NotNull(zweite);
    }

    [Fact]
    public async Task Konten_sperren_entsperren_passwort_und_Responsable_nur_eigene_Schulen()
    {
        await using var u = new PlattformUmgebung();
        var w = await WeltAsync(u);

        await Assert.ThrowsAsync<FachFehler>(() => u.Konten.SperrenAsync(w.Admin.Id, w.Admin.Id, null));
        var admin2 = await u.Basis.KontoAsync("admin2", Rollen.SuperAdmin);
        await Assert.ThrowsAsync<FachFehler>(() => u.Konten.SperrenAsync(w.Admin.Id, admin2.Id, null));

        await u.Konten.SperrenAsync(w.Admin.Id, w.Direktor.Id, "Test");
        await u.Konten.SperrenAsync(w.Admin.Id, w.Lehrer.Id, null);
        var gesperrt = await u.LiesAsync(db => db.Users.SingleAsync(k => k.Id == w.Direktor.Id));
        Assert.Equal(KontoStatus.Bloque, gesperrt.Status);
        Assert.NotEqual(w.Direktor.AktiveSitzungId, gesperrt.AktiveSitzungId);

        // Responsable: nur Konten seiner Schulen freigeben, nie sperren.
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Konten.EntsperrenAsync(w.Responsable.Id, w.Lehrer.Id));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Konten.SperrenAsync(w.Responsable.Id, w.Direktor.Id, null));
        await u.Konten.EntsperrenAsync(w.Responsable.Id, w.Direktor.Id);
        Assert.Equal(KontoStatus.Actif, (await u.LiesAsync(db => db.Users.SingleAsync(k => k.Id == w.Direktor.Id))).Status);
        await Assert.ThrowsAsync<FachFehler>(() => u.Konten.EntsperrenAsync(w.Admin.Id, w.Direktor.Id)); // nicht gesperrt

        var zugang = await u.Konten.PasswortZuruecksetzenAsync(w.Admin.Id, w.Lehrer.Id);
        Assert.True(StartPasswortLesbar(zugang.Passwort));
        Assert.True((await u.LiesAsync(db => db.Users.SingleAsync(k => k.Id == w.Lehrer.Id))).MussPasswortAendern);
        await Assert.ThrowsAsync<FachFehler>(() => u.Konten.PasswortZuruecksetzenAsync(w.Admin.Id, w.Admin.Id));
        var aktionen = await u.LiesAsync(db => db.AuditEintraege.Select(a => a.Aktion).ToListAsync());
        Assert.Contains(AuditAktionen.KontoGesperrt, aktionen);
        Assert.Contains(PlattformAudit.KontoEntsperrt, aktionen);
        Assert.Contains(AuditAktionen.PasswortZurueckgesetzt, aktionen);

        // Liste: Suche über Name/Identifiant/E-Mail, nie mit Telefonnummer.
        Assert.Single((await u.Konten.SucheAsync(w.Admin.Id, "dir@exemple", null)).Eintraege);
        Assert.Equal(2, (await u.Konten.SucheAsync(w.Admin.Id, null, Rollen.SuperAdmin)).Eintraege.Count);
        Assert.DoesNotContain(typeof(KontoZeile).GetProperties(), p => p.Name.Contains("Telefon"));
        Assert.DoesNotContain(typeof(ResponsableZeile).GetProperties(), p => p.Name.Contains("Telefon"));
    }

    private static bool StartPasswortLesbar(string p) => Skoulou.Core.Werkzeuge.StartPasswort.IstLesbar(p);

    [Fact]
    public async Task Responsable_anlegen_und_Schulen_zuweisen()
    {
        await using var u = new PlattformUmgebung();
        var w = await WeltAsync(u);

        Assert.Equal("email", (await Assert.ThrowsAsync<FachFehler>(() =>
            u.Konten.ResponsableAnlegenAsync(w.Admin.Id, new ResponsableEingabe("Akou", "Tete", null, null, "+228 90 00 00 01")))).Feld);
        var zugang = await u.Konten.ResponsableAnlegenAsync(w.Admin.Id, new ResponsableEingabe("Akou", "Tete", null, "akou@exemple.tg", "+228 90 00 00 01"));
        Assert.Equal("akou.tete", zugang.Identifiant);
        var mail = Assert.Single(await u.MailsAsync("akou@exemple.tg"));
        Assert.DoesNotContain(zugang.Passwort, mail.Html);

        // Zuweisen: « Fremde » wechselt zu Akou, « Eigene » bleibt beim ersten Responsable.
        await u.Konten.SchulenZuweisenAsync(w.Admin.Id, zugang.KontoId, [w.Fremde.Id]);
        Assert.Equal([w.Fremde.Id], await u.Konten.SchulenDesResponsableAsync(w.Admin.Id, zugang.KontoId));
        await u.Konten.SchulenZuweisenAsync(w.Admin.Id, zugang.KontoId, [w.Fremde.Id, w.Eigene.Id]);
        Assert.Empty(await u.Konten.SchulenDesResponsableAsync(w.Admin.Id, w.Responsable.Id));
        await u.Konten.SchulenZuweisenAsync(w.Admin.Id, zugang.KontoId, []);
        Assert.True(await u.LiesAsync(db => db.Schulen.AllAsync(s => s.ResponsableId == null)));

        await u.Schulen.ResponsableZuweisenAsync(w.Admin.Id, w.Eigene.Id, w.Responsable.Id);
        await Assert.ThrowsAsync<FachFehler>(() => u.Schulen.ResponsableZuweisenAsync(w.Admin.Id, w.Eigene.Id, w.Direktor.Id));
        var liste = (await u.Konten.ResponsablesAsync(w.Admin.Id, null)).Eintraege;
        Assert.Equal(["Eigene"], liste.Single(r => r.Id == w.Responsable.Id).Schulen);
    }
}
