using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Infrastruktur;
using ENote.Core.DataModels.Kasse;
using ENote.WebApp.Services;
using ENote.WebApp.Services.Mails;
using Microsoft.EntityFrameworkCore;

namespace ENote.Tests.Simulation;

internal sealed partial class Simulation
{
    private readonly HashSet<Guid> einmalAbgelehnt = [];

    // ------------------------------------------------------------------ Abo-Rechnungen

    private async Task AbrechnungAsync(DateOnly tag)
    {
        var jetzt = Uhr.Jetzt;
        var offene = await d.MitDbAsync(db => db.NutzungsAbos.AsNoTracking()
            .Where(a => a.ZahlungStatus != AboZahlungStatus.Bestaetigt && (a.FaelligUtc ?? a.ZeitraumVonUtc) <= jetzt)
            .Select(a => new { a.Id, a.SalonId, a.ZahlungStatus, Faellig = a.FaelligUtc ?? a.ZeitraumVonUtc, a.ZahlungGemeldetUtc })
            .ToListAsync());
        foreach (var a in offene)
        {
            var l = Laeden.FirstOrDefault(x => x.Id == a.SalonId);
            if (l is null || l.Geloescht) continue;
            var tage = (jetzt - a.Faellig).TotalDays;
            if (a.ZahlungStatus is AboZahlungStatus.Offen or AboZahlungStatus.Abgelehnt)
            {
                var melden = a.ZahlungStatus == AboZahlungStatus.Abgelehnt || (l.Zahler == 1 ? tage >= 17 + l.Nr % 4 : tage >= 1 + l.Nr % 4);
                if (!melden) continue;
                await SollAsync($"Zahlung melden {l.Name}", () => d.Hole<VertragsService>().MeldeAsync(l.Besitzer, a.Id, $"Virement Mobile Money réf. MM{a.Id.ToString()[..6]}"));
                Zaehle("Zahlungen gemeldet (« J'ai payé »)");
            }
            else if (a.ZahlungStatus == AboZahlungStatus.Gemeldet && a.ZahlungGemeldetUtc <= jetzt.AddHours(-20))
            {
                if (l.Zahler == 2 && einmalAbgelehnt.Add(a.Id))
                {
                    await SollAsync("Zahlung ablehnen", () => d.Hole<AboService>().SetzeZahlungsStatusAsync(Admin, a.Id, AboZahlungStatus.Abgelehnt));
                    Zaehle("Zahlungen abgelehnt (« Paiement non retrouvé »)");
                }
                else
                {
                    await SollAsync("Zahlung bestätigen", () => d.Hole<AboService>().SetzeZahlungsStatusAsync(l.Nr % 3 == 0 ? Admin2 : Admin, a.Id, AboZahlungStatus.Bestaetigt));
                    Zaehle("Zahlungen bestätigt (Admin)");
                }
            }
        }
    }

    // ------------------------------------------------------------------ Plattform-Ereignisse

    private async Task EreignisseAsync(DateOnly tag)
    {
        var i = tag.DayNumber - Beginn.DayNumber;
        var admin = d.Hole<AdminService>();
        var jetzt = Uhr.Jetzt;

        if (i == 10)
        {
            var r = await d.Hole<MobileMoneyService>().SpeichereAsync(Admin, new MobileMoneyAnbieter
            { Id = Guid.Empty, Name = "Moov Money", Land = "Togo", Farbe = "#0b5cad", IstAktiv = true, Reihenfolge = 3 });
            if (!r.Erfolg) Befund("Mobile Money anlegen: " + r.Fehler);
            anbieterCache.Clear();
            Zaehle("Mobile-Money-Anbieter angelegt");
        }
        if (i == 60 || i == 280 || i == 450)
        {
            await SollAsync("Annonce", () => d.Hole<AnkuendigungService>().VeroeffentlicheAsync(Admin, new AnkuendigungService.Entwurf(
                i == 280 ? AnkuendigungArt.Information : AnkuendigungArt.Nouveaute, i == 60 ? AnkuendigungZiel.Tous : AnkuendigungZiel.ProprietairesEtCaissiers,
                i == 60 ? "Nouveau : les clients présents" : "Conseil : clôturez la caisse chaque soir",
                "Retrouvez la nouvelle fonction dans le menu. Les vidéos d'aide expliquent chaque étape.", null, null, jetzt.AddDays(30), i == 450)));
            Zaehle("Annonces publiées");
        }
        if (i == 250)
        {
            var von = jetzt.Date.AddDays(3).AddHours(22);
            await SollAsync("Wartung", () => d.Hole<AnkuendigungService>().VeroeffentlicheAsync(Admin, new AnkuendigungService.Entwurf(
                AnkuendigungArt.Maintenance, AnkuendigungZiel.Tous, "Mise à jour planifiée",
                AnkuendigungService.FuelleWartung(AnkuendigungService.WartungsVorlagen[0].Text, von, von.AddHours(1)), null, null, von.AddHours(1), false,
                von, von.AddHours(1), true)));
            Zaehle("Annonces publiées");
        }

        // Tarife: Erhöhung zum Monatsanfang, aufgehobene Erhöhung, Senkung sofort; individuelle Tarife.
        if (i == 196) await TarifAsync(4000m, new DateTime(2025, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        if (i == 370) await TarifAsync(4500m, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        if (i == 380)
        {
            var geplant = (await d.Hole<PreisAnpassungService>().GetStandAsync(Admin)).Geplant;
            if (geplant is null) Befund("Geplante Tarifänderung fehlt.");
            else
            {
                await SollAsync("Tarif aufheben", () => d.Hole<PreisAnpassungService>().AufhebenAsync(Admin, geplant.Id));
                GlobalPreise.RemoveAll(p => p.Ab > jetzt);
                foreach (var liste in IndividuellePreise.Values) liste.RemoveAll(p => p.Ab > jetzt);
                Zaehle("Tarifänderung aufgehoben");
            }
        }
        if (i == 395) await TarifAsync(3800m, null);
        if (i == 300)
            foreach (var l in Laeden.Where(l => l.Nr is 5 or 9 or 14 && l.Freigegeben))
            {
                if (await SollAsync($"Tarif personnalisé {l.Name}", () => admin.SetzeIndividuellenPreisAsync(Admin, l.Id, 5000m)))
                    IndividuellePreise[l.Id] = [(jetzt, 5000m)];
                Zaehle("Tarifs personnalisés");
            }

        // Formelwechsel (Trimestriel, Annuel, Semestriel) nach einigen Monaten.
        foreach (var l in Laeden.Where(l => l.Betrieb && l.Nr < 100 && l.Start.AddDays(160) == tag && l.Nr % 6 is 1 or 2 or 4))
        {
            var formel = (l.Nr % 6) switch { 1 => AboIntervall.DreiMonate, 2 => AboIntervall.Jaehrlich, _ => AboIntervall.SechsMonate };
            var art = await SollAsync($"Formelwechsel {l.Name}", () => d.Hole<VertragsService>().WechsleAsync(l.Besitzer, l.Id, formel));
            Zaehle($"Formelwechsel ({art})");
        }

        // Kündigungen: in der Probezeit, zahlend, zurückgenommen; Löschung mit Bedenkzeit.
        var kuendigung = d.Hole<KuendigungService>();
        foreach (var l in Laeden.Where(l => l.Eingerichtet && !l.Geloescht && l.Nr < 100))
        {
            if ((l.Nr == 7 && l.Start.AddDays(40) == tag) || (l.Nr == 12 && i == 420) || (l.Nr == 16 && i == 430))
            {
                var zum = await SollAsync($"Kündigung {l.Name}", () => kuendigung.KuendigeAsync(l.Besitzer, l.Id, "Je ferme mon activité"));
                if (zum != default) { l.GekuendigtZum = zum; Notiere($"{l.Name}: gekündigt zum {zum:dd/MM/yyyy}."); Zaehle("Kündigungen"); }
            }
            if (l.Nr == 16 && i == 440)
            {
                await SollAsync("Kündigung zurücknehmen", () => kuendigung.NimmKuendigungZurueckAsync(l.Besitzer, l.Id));
                l.GekuendigtZum = null;
                Zaehle("Kündigungen zurückgenommen");
            }
            if (l.Nr == 20 && i >= 480 && !l.Deaktiviert)
            {
                try
                {
                    await kuendigung.BeantrageLoeschungAsync(l.Besitzer, l.Id, l.BesitzerPasswort);
                    l.Deaktiviert = true;
                    Notiere($"{l.Name}: Löschung beantragt (30 Tage Bedenkzeit).");
                    Zaehle("Löschungen beantragt");
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    Zaehle("Löschung abgelehnt (unbezahlte Rechnung, erwartet)");
                }
            }
        }

        // Admin: Laden vorübergehend deaktivieren und reaktivieren; Probezeit verlängern; Admin sperren.
        var neun = Laeden.First(l => l.Nr == 9);
        if (i == 330 && neun.Betrieb)
        {
            await SollAsync("Deaktivieren", () => admin.SetzeAktivAsync(Admin, neun.Id, false));
            neun.Deaktiviert = true;
            var s = await d.Hole<KassenService>().StarteSitzungAsync(neun.Id, neun.Team[0].Id, 0);
            Pruefe(!s.Erfolg, "deaktivierter Laden kann die Kasse nicht öffnen");
        }
        if (i == 340 && neun.Deaktiviert)
        {
            await SollAsync("Reaktivieren", () => admin.SetzeAktivAsync(Admin, neun.Id, true));
            neun.Deaktiviert = false;
        }
        var l25 = Laeden.First(l => l.Nr == 25);
        if (l25.Id != Guid.Empty && l25.Start.AddDays(20) == tag)
        {
            var bis = await admin.GetProbezeitAsync(l25.Id);
            if (bis is DateTime b) await SollAsync("Probezeit verlängern", () => admin.SetzeProbezeitAsync(Admin, l25.Id, b.Date.AddMonths(1)));
            Zaehle("Probezeiten verlängert");
        }
        if (i == 200)
        {
            await SollAsync("Admin sperren", () => d.Hole<AdminKontenService>().SetzeGesperrtAsync(Admin, Admin3, true));
            Zaehle("Admins gesperrt");
        }

        // Kontaktformular, Nachricht an TabloSuite, manuelle Mails.
        if (tag.Day == 7)
        {
            var fehler = await d.Hole<KontaktService>().SendeOeffentlichAsync($"{SimNamen.Vornamen[i % 40]} {SimNamen.Nachnamen[i % 30]}", "visiteur@enote-sim.tg",
                "+228 90 00 00 00", KontaktService.Themen[i % KontaktService.Themen.Count], "Bonjour, je souhaite une démonstration d'eNOTE pour mon salon.", null, default, jetzt);
            if (fehler is not null) Befund("Kontaktformular: " + fehler); else Zaehle("Kontaktformular");
        }
        if (tag.Day == 12 && Laeden.FirstOrDefault(l => l.Betrieb) is { } k)
        {
            var fehler = await d.Hole<KontaktService>().SendeAnTabloSuiteAsync(k.Besitzer, k.Id, KontaktService.Themen[0], "Bonjour, comment imprimer un ticket sur une imprimante Bluetooth ?");
            if (fehler is not null) Befund("Nachricht an TabloSuite: " + fehler); else Zaehle("Nachrichten an TabloSuite");
        }
        if (i == 150 || i == 520)
        {
            var nachrichten = d.Hole<NachrichtenService>();
            var empfaenger = await nachrichten.ErmittleAsync(Admin, NachrichtenService.Gruppe.AlleBesitzer, [], []);
            var r = await nachrichten.SendeAsync(Admin, empfaenger, "Nouveautés eNOTE", "Bonjour, découvrez les nouveautés du mois.", null, null);
            Zaehle("Manuelle Mails (Outbox)", r.Gesendet + r.Fehlgeschlagen);
        }
    }

    private async Task TarifAsync(decimal neu, DateTime? ab)
    {
        var jetzt = Uhr.Jetzt;
        var alt = GlobalPreis(jetzt);
        var r = await SollAsync($"Tarif {neu}", () => d.Hole<PreisAnpassungService>().PlaneAsync(Admin, neu, ab, true));
        if (r is null) return;
        var gueltig = r.AbUtc;
        if (ab is DateTime erwartet && gueltig != erwartet) Befund($"Tarifänderung gilt ab {gueltig:dd/MM/yyyy}, erwartet {erwartet:dd/MM/yyyy}.");
        GlobalPreise.Add((gueltig, neu));
        if (neu > alt)
            foreach (var (salon, liste) in IndividuellePreise)
                if (liste.LastOrDefault().Preis is decimal p) liste.Add((gueltig, p + (neu - alt)));
        Zaehle(neu > alt ? "Tariferhöhungen geplant" : "Tarifsenkungen (sofort)");
        Notiere($"Tarif {alt:N0} → {neu:N0} F ab {gueltig:dd/MM/yyyy}, {r.AngepassteLaeden} individuelle Tarife angepasst, {r.Mails} Mails.");
    }

    public decimal GlobalPreis(DateTime zeit) => GlobalPreise.Where(p => p.Ab <= zeit).OrderBy(p => p.Ab).Last().Preis;

    /// <summary>Erwartete Monatsbasis eines Ladens zum Periodenbeginn (individuell vor global).</summary>
    public decimal MonatsBasis(Guid salon, DateTime zeit)
    {
        if (IndividuellePreise.TryGetValue(salon, out var liste) && liste.Where(p => p.Ab <= zeit).OrderBy(p => p.Ab).LastOrDefault() is { Preis: decimal p } )
            return p;
        return GlobalPreis(zeit);
    }

    // ------------------------------------------------------------------ Provisionen der Responsables

    private async Task ProvisionenAsync(DateOnly tag)
    {
        // Seit 17.11.2026: Provision entsteht automatisch je bestätigter Zahlung (PoProvisionen). Am 5. markiert der Admin die
        // Provisionen des Vormonats als « versées », der Responsable bestätigt den Erhalt (selten « Pas reçue »).
        var dienst = d.Hole<PoProvisionService>();
        var stichtag = tag.AddDays(-tag.Day + 1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var offen = await d.MitDbAsync(db => db.PoProvisionen.Where(p => p.VerseeUtc == null && p.ZahlungBestaetigtUtc < stichtag)
            .Select(p => new { p.Id, p.ProduktOwnerId }).ToListAsync());
        if (offen.Count > 0)
        {
            await SollAsync("Commissions versées", () => dienst.MarkiereVerseeAsync(Admin, offen.Select(o => o.Id).ToList(), Uhr.Jetzt.Date, $"VIR-{tag:yyyyMM}"));
            Zaehle("Commissions versées (Admin)");
            foreach (var g in offen.GroupBy(o => o.ProduktOwnerId))
            {
                await SollAsync("Commission reçue", () => dienst.BestaetigeErhaltAsync(g.Key, g.Select(o => o.Id).ToList()));
                Zaehle("Commissions reçues (Responsable)");
            }
        }
    }

    // ------------------------------------------------------------------ Rechte (Stichproben)

    private async Task RechteAsync(DateOnly tag)
    {
        var aktiv = Laeden.Where(l => l.Betrieb && l.Team.Count > 0 && l.Kunden.Count > 0).ToList();
        if (aktiv.Count < 2) return;
        var z = new Zufall(Zufall.Kombiniere(Startwert, tag.DayNumber, 77));
        var a = z.Wahl(aktiv);
        var b = z.Wahl(aktiv.Where(x => x != a).ToList());
        var emp = z.Wahl(a.Team).Id;
        var kundeB = z.Wahl(b.Kunden);

        await VerweigertAsync("Employé sucht Kunden eines fremden Ladens", () => d.Hole<KundenService>().SucheAsync(emp, b.Id, "a"));
        await VerweigertAsync("Employé sieht Kundenliste (nur Besitzer)", () => d.Hole<KundenService>().GetListeAsync(emp, a.Id, null));
        await VerweigertAsync("Employé sieht Anwesende eines fremden Ladens", () => d.Hole<KundenBesuchService>().GetAnwesendeAsync(emp, b.Id));
        await VerweigertAsync("Employé sieht geparkte Tickets eines fremden Ladens", () => d.Hole<TicketParkService>().GetAsync(emp, b.Id));
        await VerweigertAsync("Employé sieht Aufenthalte eines fremden Ladens", () => d.Hole<AufenthaltService>().GetStaendeAsync(emp, b.Id));
        await VerweigertAsync("Employé sieht Provisionen eines Responsable", () => d.Hole<PoProvisionService>().GetFuerPoAsync(emp, null));
        await VerweigertAsync("Besitzer liest Kundenfiche eines fremden Ladens", () => d.Hole<KundenService>().GetDetailAsync(a.Besitzer, kundeB));
        await VerweigertAsync("Besitzer sieht Vertrag eines fremden Ladens", () => d.Hole<VertragsService>().GetLadenVertragAsync(a.Besitzer, b.Id));
        await VerweigertAsync("Besitzer kündigt fremden Laden", () => d.Hole<KuendigungService>().KuendigeAsync(a.Besitzer, b.Id, null));
        await VerweigertAsync("Besitzer meldet Raum in fremdem Laden an", () => d.Hole<KundenBesuchService>().RaumAnlegenAsync(a.Besitzer, b.Id, "Fremd"));
        var fremderPo = Responsables.FirstOrDefault(p => p != b.Responsable);
        await VerweigertAsync("Responsable gibt fremden Laden frei", () => d.Hole<AdminService>().SetzeFreigabeAsync(fremderPo, b.Id, true));
        await VerweigertAsync("Responsable ändert Probezeit eines fremden Ladens", () => d.Hole<AdminService>().SetzeProbezeitAsync(fremderPo, b.Id, Uhr.Jetzt.AddDays(10)));
        if (a.Responsable is Guid eigenerPo)
            await VerweigertAsync("Responsable entzieht Freigabe (nur Admin)", () => d.Hole<AdminService>().SetzeFreigabeAsync(eigenerPo, a.Id, false));
        if (tag.DayNumber - Beginn.DayNumber > 200)
            await VerweigertAsync("Gesperrter Admin deaktiviert Laden", () => d.Hole<AdminService>().SetzeAktivAsync(Admin3, a.Id, false));
        var offen = await d.MitDbAsync(db => db.NutzungsAbos.Where(x => x.SalonId == b.Id).Select(x => x.Id).FirstOrDefaultAsync());
        if (offen != Guid.Empty)
            await VerweigertAsync("Besitzer meldet Zahlung eines fremden Ladens", () => d.Hole<VertragsService>().MeldeAsync(a.Besitzer, offen, null));
        Zaehle("Rechte-Stichproben (Runden)");
    }
}
