using System.Diagnostics;
using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Finanzen;
using ENote.Core.DataModels.Kasse;
using ENote.Core.Services;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;

namespace ENote.Tests.Simulation;

internal sealed partial class Simulation
{
    public List<string> Verletzungen { get; } = [];

    private void Verletzt(string text)
    {
        lock (Verletzungen) Verletzungen.Add(text);
    }

    private void Invariante(string name, int verletzungenVorher)
    {
        var neu = Verletzungen.Count - verletzungenVorher;
        Geprueft.Add(neu == 0 ? name : $"{name} – {neu} Verletzung(en)");
    }

    public async Task InvariantenAsync()
    {
        var aktive = Laeden.Where(l => l.Eingerichtet && !l.Geloescht).ToList();

        // 1) Verkäufe und Erstattungen je Laden = eigene Buchführung der Simulation.
        var v = Verletzungen.Count;
        var proLaden = await d.MitDbAsync(db => db.Verkaeufe.AsNoTracking().GroupBy(x => x.SalonId)
            .Select(g => new { g.Key, Anzahl = g.Count(x => x.StornoVonVerkaufId == null), Summe = g.Sum(x => x.GesamtSumme),
                Erstattungen = g.Count(x => x.StornoVonVerkaufId != null) }).ToListAsync());
        foreach (var l in aktive)
        {
            var x = proLaden.FirstOrDefault(p => p.Key == l.Id);
            if ((x?.Anzahl ?? 0) != l.Verkaeufe || (x?.Summe ?? 0) != l.Umsatz || (x?.Erstattungen ?? 0) != l.Erstattungen)
                Verletzt($"{l.Name}: DB {x?.Anzahl} Verkäufe / {x?.Summe:N0} F / {x?.Erstattungen} Erstattungen ≠ Simulation {l.Verkaeufe} / {l.Umsatz:N0} F / {l.Erstattungen}.");
        }
        Invariante("Verkäufe, Umsatz und Erstattungen je Laden = Buchführung der Simulation", v);

        // 2) Belege: Summe der Zahlungsanteile = Summe der Zeilen = Gesamtbetrag; jeder Beleg in einer Sitzung seines Ladens, in deren Zeitfenster.
        v = Verletzungen.Count;
        var falsch = await d.MitDbAsync(db => db.Verkaeufe.AsNoTracking()
            .Where(x => x.Zahlungen.Sum(z => z.Betrag) != x.GesamtSumme || x.Positionen.Sum(p => p.ZeilenSumme) != x.GesamtSumme
                || x.Zwischensumme - x.RabattSumme != x.GesamtSumme || x.KassenSitzung.SalonId != x.SalonId
                || x.ZeitUtc < x.KassenSitzung.GestartetUtc || (x.KassenSitzung.AbgeschlossenUtc != null && x.ZeitUtc > x.KassenSitzung.AbgeschlossenUtc))
            .Select(x => new { x.Id, x.SalonId, x.GesamtSumme }).Take(20).ToListAsync());
        foreach (var f in falsch) Verletzt($"Beleg {f.Id} ({f.GesamtSumme:N0} F): Anteile/Zeilen/Sitzung passen nicht.");
        var doppelt = await d.MitDbAsync(db => db.Verkaeufe.GroupBy(x => x.Id).CountAsync(g => g.Count() > 1));
        if (doppelt > 0) Verletzt($"{doppelt} doppelte Beleg-Ids.");
        Invariante("Belege: Anteile = Zeilen = Gesamt, Beleg in Sitzung des Ladens und im Zeitfenster, Ids eindeutig", v);

        // 3) Erstattungen: verweisen auf einen Beleg desselben Ladens, negativ, nie mehr als das Original.
        v = Verletzungen.Count;
        var storni = await d.MitDbAsync(db => db.Verkaeufe.AsNoTracking().Where(x => x.StornoVonVerkaufId != null)
            .GroupBy(x => x.StornoVonVerkaufId).Select(g => new { Original = g.Key, Summe = g.Sum(x => x.GesamtSumme), Max = g.Max(x => x.GesamtSumme) })
            .Join(db.Verkaeufe, s => s.Original, o => o.Id, (s, o) => new { s.Original, s.Summe, s.Max, o.GesamtSumme, o.SalonId, o.StornoVonVerkaufId,
                Laeden = db.Verkaeufe.Where(x => x.StornoVonVerkaufId == s.Original).Select(x => x.SalonId).Distinct().Count() })
            .ToListAsync());
        foreach (var s in storni.Where(s => s.Max >= 0 || -s.Summe > s.GesamtSumme || s.StornoVonVerkaufId != null || s.Laeden != 1).Take(20))
            Verletzt($"Erstattung zu {s.Original}: {-s.Summe:N0} F von {s.GesamtSumme:N0} F.");
        var ohneOriginal = await d.MitDbAsync(db => db.Verkaeufe.CountAsync(x => x.StornoVonVerkaufId != null && !db.Verkaeufe.Any(o => o.Id == x.StornoVonVerkaufId && o.SalonId == x.SalonId)));
        if (ohneOriginal > 0) Verletzt($"{ohneOriginal} Erstattungen ohne Original im selben Laden.");
        Invariante($"Erstattungen ({storni.Count:N0} Originale): negativ, ≤ Original, gleicher Laden", v);

        // 4) Kassenstand: Soll = Startbestand + Baranteile + Bewegungen in der Kasse; Écart = gezählt − Soll; keine offene Kasse.
        v = Verletzungen.Count;
        var sitzungen = await d.MitDbAsync(db => db.KassenSitzungen.AsNoTracking().Where(s => s.Status == KassenSitzungStatus.Abgeschlossen)
            .Select(s => new
            {
                s.Id, s.Startbestand, s.BareinnahmenSoll, s.EndbestandGezaehlt, s.Differenz,
                Bar = s.Verkaeufe.SelectMany(x => x.Zahlungen).Where(z => z.Art == ZahlungsArt.Bar).Sum(z => (decimal?)z.Betrag) ?? 0,
                Geld = db.GeldBewegungen.Where(g => g.KassenSitzungId == s.Id && g.Ort == GeldOrt.Kasse)
                    .Sum(g => (decimal?)(g.Art == GeldBewegungArt.Einlage ? g.Betrag : -g.Betrag)) ?? 0,
            }).ToListAsync());
        foreach (var s in sitzungen.Where(s => s.BareinnahmenSoll != s.Startbestand + s.Bar + s.Geld || s.Differenz != s.EndbestandGezaehlt - s.BareinnahmenSoll).Take(20))
            Verletzt($"Sitzung {s.Id}: Soll {s.BareinnahmenSoll:N0} ≠ {s.Startbestand:N0} + {s.Bar:N0} + {s.Geld:N0} oder Écart falsch.");
        var offen = await d.MitDbAsync(db => db.KassenSitzungen.CountAsync(s => s.Status == KassenSitzungStatus.Offen));
        if (offen > 0) Verletzt($"{offen} Kassen am Ende noch offen.");
        Invariante($"Kassenstand = Summe der Bewegungen ({sitzungen.Count:N0} Abschlüsse), Écart = gezählt − Soll", v);

        // 5) Berichte: Finances je Monat und gesamt = Verkäufe − Erstattungen; Tagesumsätze = Gesamt.
        v = Verletzungen.Count;
        var finanz = d.Hole<FinanzService>();
        var berichte = 0;
        foreach (var l in aktive.Where(l => l.Verkaeufe > 0))
        {
            foreach (var ((jahr, monat), erwartet) in l.MonatsUmsatz)
            {
                var von = new DateTime(jahr, monat, 1, 0, 0, 0, DateTimeKind.Utc);
                var u = await finanz.GetFinanzUebersichtAsync(l.Id, von, von.AddMonths(1).AddTicks(-1));
                berichte++;
                if (u.EinnahmenGesamt != erwartet || u.EinnahmenBar + u.EinnahmenKarte + u.EinnahmenMobileMoney != u.EinnahmenGesamt)
                    Verletzt($"{l.Name} {monat:00}/{jahr}: Finances {u.EinnahmenGesamt:N0} F ≠ Verkäufe−Erstattungen {erwartet:N0} F.");
            }
            var alles = await finanz.GetFinanzUebersichtAsync(l.Id, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc));
            if (alles.EinnahmenGesamt != l.Umsatz || alles.AnzahlVerkaeufe != l.Verkaeufe || alles.Erstattungen != l.Erstattet)
                Verletzt($"{l.Name}: Finances gesamt {alles.EinnahmenGesamt:N0} F / {alles.AnzahlVerkaeufe} / Erstattungen {alles.Erstattungen:N0} ≠ {l.Umsatz:N0} / {l.Verkaeufe} / {l.Erstattet:N0}.");
            var tage = await finanz.GetTagesUmsaetzeImZeitraumAsync(l.Id, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc), default, l.Versatz);
            if (tage.Sum(t => t.Gesamt) != l.Umsatz || tage.Sum(t => t.AnzahlVerkaeufe) != l.Verkaeufe)
                Verletzt($"{l.Name}: Tagesumsätze {tage.Sum(t => t.Gesamt):N0} F / {tage.Sum(t => t.AnzahlVerkaeufe)} ≠ {l.Umsatz:N0} F / {l.Verkaeufe}.");
        }
        Invariante($"Finances je Monat ({berichte} Monatsberichte), gesamt und Tagesumsätze = Verkäufe − Erstattungen", v);

        // 6) Bestände = Anfangsbestand + Zugänge − Abgänge (Buchführung der Simulation).
        v = Verletzungen.Count;
        var bestand = await d.MitDbAsync(db => db.Leistungen.AsNoTracking().Where(x => x.HatStueckzahl).ToDictionaryAsync(x => x.Id, x => x.Stueckzahl));
        var produkte = 0;
        foreach (var l in aktive)
            foreach (var (id, erwartet) in l.Lager)
            {
                produkte++;
                if (bestand.GetValueOrDefault(id) != erwartet)
                    Verletzt($"{l.Name}: Bestand « {l.Katalog.First(a => a.Id == id).Name} » {bestand.GetValueOrDefault(id):0.##} ≠ erwartet {erwartet:0.##}.");
            }
        Invariante($"Bestände ({produkte} Produkte) = Zugänge − Abgänge", v);

        // 7) Kundenumsätze (Liste « Clients ») = Summe ihrer Verkäufe.
        v = Verletzungen.Count;
        var kunden = d.Hole<KundenService>();
        var geprueft = 0;
        foreach (var l in aktive.Where(l => l.KundenUmsatz.Count > 0))
        {
            var (liste, _) = await kunden.SucheListeAsync(l.Besitzer, l.Id, null, 0, 5000);
            foreach (var z in liste)
            {
                geprueft++;
                if (z.Umsatz != l.KundenUmsatz.GetValueOrDefault(z.Id))
                    Verletzt($"{l.Name}: Kunde {z.Name}: Umsatz {z.Umsatz:N0} F ≠ {l.KundenUmsatz.GetValueOrDefault(z.Id):N0} F.");
            }
            if (liste.Count != l.Kunden.Count) Verletzt($"{l.Name}: Kundenliste {liste.Count} ≠ {l.Kunden.Count} angelegte Kunden.");
        }
        Invariante($"Kundenumsätze ({geprueft:N0} Kunden) = Summe ihrer Verkäufe", v);

        // 8) Abo-Rechnungen: je Laden und Beginn höchstens einmal, lückenlos, nie > 0 F fehlend, Preis zum Periodenbeginn.
        v = Verletzungen.Count;
        var abos = await d.MitDbAsync(db => db.NutzungsAbos.AsNoTracking().OrderBy(a => a.ZeitraumVonUtc).ToListAsync());
        var salons = await d.MitDbAsync(db => db.Salons.AsNoTracking().ToDictionaryAsync(s => s.Id));
        foreach (var g in abos.GroupBy(a => a.SalonId))
        {
            var l = Laeden.FirstOrDefault(x => x.Id == g.Key);
            var salon = salons[g.Key];
            var liste = g.ToList();
            if (liste.GroupBy(a => a.ZeitraumVonUtc).Any(x => x.Count() > 1)) Verletzt($"{salon.Name}: Rechnung doppelt für denselben Beginn.");
            for (var i = 1; i < liste.Count; i++)
                if (liste[i].ZeitraumVonUtc != liste[i - 1].ZeitraumBisUtc && salon.GekuendigtZumUtc is null)
                    Verletzt($"{salon.Name}: Lücke/Überlappung {liste[i - 1].ZeitraumBisUtc:dd/MM/yyyy} → {liste[i].ZeitraumVonUtc:dd/MM/yyyy}.");
            if (salon.ProbeBisUtc is DateTime probe && liste[0].ZeitraumVonUtc < VertragsRegeln.BeginnNachProbe(probe))
                Verletzt($"{salon.Name}: Rechnung vor Ende der Probezeit.");
            if (salon.GekuendigtZumUtc is DateTime zum && liste.Any(a => a.ZeitraumVonUtc >= zum)) Verletzt($"{salon.Name}: Rechnung nach der Kündigung.");
            foreach (var a in liste)
            {
                if (a.PreisSnapshot <= 0) Verletzt($"{salon.Name}: Rechnung über {a.PreisSnapshot} F.");
                var basis = MonatsBasis(g.Key, a.ZeitraumVonUtc);
                var erwartet = a.IstTeilperiode ? VertragsRegeln.Teilpreis(basis, a.ZeitraumVonUtc) : VertragsRegeln.Preis(basis, a.Intervall);
                if (a.PreisSnapshot != erwartet || a.MonatsBasisSnapshot != basis)
                    Verletzt($"{salon.Name}: Rechnung ab {a.ZeitraumVonUtc:dd/MM/yyyy} ({a.Intervall}): {a.PreisSnapshot:N0} F (Basis {a.MonatsBasisSnapshot:N0}) ≠ erwartet {erwartet:N0} F (Basis {basis:N0}).");
            }
            if (l is null) continue;
        }
        Invariante($"Abo-Rechnungen ({abos.Count:N0}): je Laden/Beginn einmal, lückenlos, nach der Probezeit, Preis/Tarif zum Periodenbeginn", v);

        // 9) Provisionen nur aus bestätigten Zahlungen, Betrag = Preis × % + Festbetrag (Festbetrag höchstens einmal je Laden und Monat).
        v = Verletzungen.Count;
        var eingaenge = await d.MitDbAsync(db => db.PoZahlungseingaenge.AsNoTracking().Include(e => e.NutzungsAbo).ToListAsync());
        foreach (var e in eingaenge)
        {
            if (e.NutzungsAbo.ZahlungStatus != AboZahlungStatus.Bestaetigt) Verletzt($"Provision aus unbestätigter Zahlung ({e.Laden}).");
            if (e.Betrag != e.NutzungsAbo.PreisSnapshot || e.Provision != Math.Round(e.Betrag * e.ProzentProvision / 100 + e.FestProvision, 2))
                Verletzt($"Provision {e.Laden}: {e.Provision:N2} F aus {e.Betrag:N0} F ({e.ProzentProvision} % + {e.FestProvision}).");
        }
        foreach (var g in eingaenge.Where(e => e.FestProvision > 0).GroupBy(e => (e.ProduktOwnerId, e.NutzungsAbo.SalonId, e.EingangUtc.Year, e.EingangUtc.Month)))
            if (g.Count() > 1) Verletzt($"Festbetrag mehrfach im Monat: {g.First().Laden}.");
        var anforderungen = await d.MitDbAsync(db => db.PoZahlungsanforderungen.AsNoTracking().Select(a => new { a.Id, a.Betrag, Summe = db.PoZahlungseingaenge.Where(e => e.AnforderungId == a.Id).Sum(e => e.Betrag) }).ToListAsync());
        foreach (var a in anforderungen.Where(a => a.Betrag != a.Summe)) Verletzt($"Anforderung {a.Id}: {a.Betrag:N0} ≠ Summe der Positionen {a.Summe:N0}.");
        var auszahlungen = await d.MitDbAsync(db => db.ProduktOwnerAuszahlungen.AsNoTracking().ToListAsync());
        foreach (var a in auszahlungen)
        {
            var von = new DateTime(a.Jahr, a.Monat, 1, 0, 0, 0, DateTimeKind.Utc);
            var erwartet = await d.MitDbAsync(async db =>
            {
                var zuweisungen = await db.ProduktOwnerSalons.AsNoTracking().Where(p => p.ProduktOwnerId == a.ProduktOwnerId && p.CreatedAtUtc < von.AddMonths(1)
                    && (p.GueltigBisUtc == null || p.GueltigBisUtc >= von)).ToListAsync();
                decimal summe = 0;
                foreach (var z in zuweisungen)
                {
                    var einnahmen = await db.NutzungsAbos.Where(n => n.SalonId == z.SalonId && n.ZahlungStatus == AboZahlungStatus.Bestaetigt
                        && n.ZahlungBestaetigtUtc >= von && n.ZahlungBestaetigtUtc < von.AddMonths(1) && !db.PoZahlungseingaenge.Any(e => e.NutzungsAboId == n.Id))
                        .SumAsync(n => (decimal?)n.PreisSnapshot) ?? 0;
                    if (einnahmen > 0) summe += Math.Round(einnahmen * (z.ProzentProvision ?? 0) / 100 + (z.FestbetragProvision ?? 0), 2);
                }
                return summe;
            });
            if (a.Betrag != erwartet) Verletzt($"Auszahlung {a.Monat:00}/{a.Jahr}: {a.Betrag:N2} F ≠ {erwartet:N2} F aus bestätigten Zahlungen.");
        }
        Invariante($"Provisionen ({eingaenge.Count} Eingänge, {anforderungen.Count} Anforderungen, {auszahlungen.Count} Auszahlungen) nur aus bestätigten Zahlungen", v);

        // 10) Kündigung/Löschung: keine Kasse nach dem Stichtag; gelöschter Laden ohne Personendaten.
        v = Verletzungen.Count;
        foreach (var l in Laeden.Where(l => l.GekuendigtZum is not null))
        {
            var danach = await d.MitDbAsync(db => db.KassenSitzungen.CountAsync(s => s.SalonId == l.Id && s.GestartetUtc >= l.GekuendigtZum));
            if (danach > 0) Verletzt($"{l.Name}: {danach} Kassensitzungen nach der Kündigung.");
        }
        foreach (var l in Laeden.Where(l => l.Geloescht))
        {
            var rest = await d.MitDbAsync(async db => await db.Verkaeufe.CountAsync(x => x.SalonId == l.Id) + await db.Kunden.CountAsync(x => x.SalonId == l.Id)
                + await db.KassenSitzungen.CountAsync(x => x.SalonId == l.Id) + await db.Leistungen.CountAsync(x => x.SalonId == l.Id));
            var konten = await d.MitDbAsync(db => db.UserSalons.Where(us => us.SalonId == l.Id).CountAsync(us => !us.AppUser.UserName!.StartsWith("supprime-")));
            if (rest > 0 || konten > 0) Verletzt($"{l.Name}: nach der Löschung noch {rest} Datensätze / {konten} nicht anonymisierte Konten.");
        }
        Invariante("Gekündigte Läden ohne Kasse nach dem Stichtag; gelöschte Läden ohne Personendaten, Konten anonymisiert", v);

        // 11) Nichts nach außen: alle Mails nur in der Warteschlange.
        v = Verletzungen.Count;
        var gesendet = await d.MitDbAsync(db => db.MailNachrichten.CountAsync(m => m.GesendetUtc != null));
        if (gesendet > 0) Verletzt($"{gesendet} Mails als gesendet markiert.");
        Invariante("Keine Mail versendet (nur Warteschlange)", v);
    }

    // ------------------------------------------------------------------ Antwortzeiten

    public async Task ZeitenMessenAsync()
    {
        var groesster = await d.MitDbAsync(db => db.Verkaeufe.GroupBy(x => x.SalonId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync());
        var l = Laeden.FirstOrDefault(x => x.Id == groesster);
        var besitzer = l?.Besitzer ?? await d.MitDbAsync(db => db.UserSalons.Where(us => us.SalonId == groesster && us.KannRabatteVerwalten && us.BesitzerPinHash != null).Select(us => us.AppUserId).FirstAsync());
        var kassierer = l?.Team[0].Id ?? besitzer;
        var admin = Admin != Guid.Empty ? Admin : await d.MitDbAsync(db => db.Users.Where(u => u.UserName == SimDienste.AdminPseudonym).Select(u => u.Id).SingleAsync());
        var po = await d.MitDbAsync(db => db.ProduktOwnerSalons.GroupBy(p => p.ProduktOwnerId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync());
        var bis = Uhr.Jetzt;
        var finanz = d.Hole<FinanzService>();

        async Task Messe(string name, Func<Task> aktion)
        {
            var werte = new List<double>();
            for (var i = 0; i < 3; i++)
            {
                var sw = Stopwatch.StartNew();
                try { await aktion(); }
                catch (Exception ex) { Befund($"Messung {name}: {ex.GetType().Name}: {ex.Message}"); return; }
                werte.Add(sw.Elapsed.TotalMilliseconds);
            }
            Zeiten.Add((name, werte.Order().ElementAt(1)));
        }

        await Messe("Finances: Übersicht 12 Monate", () => finanz.GetFinanzUebersichtAsync(groesster, bis.AddMonths(-12), bis));
        await Messe("Finances: Übersicht Monat", () => finanz.GetFinanzUebersichtAsync(groesster, bis.AddMonths(-1), bis));
        await Messe("Finances: Par jour (12 Monate)", () => finanz.GetTagesUmsaetzeImZeitraumAsync(groesster, bis.AddMonths(-12), bis));
        await Messe("Finances: Dernières ventes (Seite 1)", () => finanz.SucheVerkaeufeAsync(groesster, null, null, null, null, 0));
        await Messe("Finances: Ventes mit Suche", () => finanz.SucheVerkaeufeAsync(groesster, null, null, "Coupe", null, 0));
        await Messe("Finances: Ventes Seite 40", () => finanz.SucheVerkaeufeAsync(groesster, null, null, null, null, 2000));
        await Messe("Finances: Clôtures de caisse", () => finanz.GetAbschluesseAsync(groesster));
        await Messe("Finances: Mouvements 12 Monate", () => d.Hole<BestandsService>().GetBewegungenAsync(groesster, bis.AddMonths(-12), bis));
        await Messe("Accueil: Dashboard", () => d.Hole<DashboardService>().LadeAsync(besitzer, groesster, bis.Date, TimeSpan.Zero));
        await Messe("Accueil: Tagesumsätze 14 Tage", () => finanz.GetTagesUmsaetzeAsync(groesster));
        await Messe("Caisse: Kassierer-Liste", () => d.Hole<KassenService>().GetKassiererAsync(groesster));
        await Messe("Caisse: Kundensuche", () => d.Hole<KundenService>().SucheAsync(kassierer, groesster, "a"));
        await Messe("Clients: Liste (Seite 1)", () => d.Hole<KundenService>().SucheListeAsync(besitzer, groesster, null, 0));
        await Messe("Clients: Kennzahlen", () => d.Hole<KundenService>().GetKennzahlenAsync(besitzer, groesster));
        await Messe("Clients présents", () => d.Hole<KundenBesuchService>().GetAnwesendeAsync(kassierer, groesster));
        await Messe("Équipe", () => d.Hole<TeamService>().GetTeamAsync(groesster));
        await Messe("Abonnement (Vertrag)", () => d.Hole<VertragsService>().GetLadenVertragAsync(besitzer, groesster));
        await Messe("Admin: Établissements (Seite 1)", () => d.Hole<AdminService>().SucheSalonsAsync(admin, true, null, null, null, 0));
        await Messe("Admin: Kennzahlen", () => d.Hole<AdminService>().GetSalonKennzahlenAsync(admin, true));
        await Messe("Admin: Établissements (Übersicht alle)", () => d.Hole<AdminService>().GetSalonUebersichtAsync());
        await Messe("Facturation: Liste (Seite 1)", () => d.Hole<AboService>().SucheAbosAsync(admin, null, null, 0));
        await Messe("Facturation: Kennzahlen", () => d.Hole<AboService>().GetKennzahlenAsync(admin, new DateTime(bis.Year, bis.Month, 1, 0, 0, 0, DateTimeKind.Utc)));
        await Messe("Facturation: Zahlungen 12 Monate", () => d.Hole<VertragsService>().GetZahlungenAsync(admin, bis.AddMonths(-12), bis));
        await Messe("Responsable: Mes établissements", () => d.Hole<ProduktOwnerService>().GetDashboardAsync(po));
        await Messe("Admin: Abrechnung Responsables", () => d.Hole<PoAbrechnungService>().GetKontenAsync(admin, bis.Date));
        await Messe("Admin: Bericht Besitzer (12 Monate)", () => d.Hole<BesitzerFinanzService>().GetBerichtAsync(admin, besitzer, groesster, bis.AddMonths(-12), bis));

        // Kasse auf vollen Daten: öffnen, verkaufen, schließen (wie ein Klick « Encaisser »).
        if (l is not null)
        {
            Uhr.Global = bis.Date.AddDays(1).AddHours(8);
            var kassen = d.Hole<KassenService>();
            var start = await kassen.StarteSitzungAsync(groesster, kassierer, 0);
            if (start.Erfolg)
            {
                var artikel = l.Katalog.First(a => a.Verkaufbar && !a.Produkt);
                await Messe("Caisse: Encaisser (Verkauf buchen)", async () =>
                {
                    var r = await kassen.BucheVerkaufAsync(start.Sitzung!.Id, [new KassenService.VerkaufsPositionInput(artikel.Id, 1, null, false)],
                        [new KassenService.ZahlungInput(ENote.Core.DataModels.Kasse.ZahlungsArt.Karte)]);
                    if (r.Erfolg) Buche(l, r.Verkauf!, null);
                });
                await Messe("Caisse: Soll für den Abschluss", () => kassen.GetBarSollAufstellungAsync(start.Sitzung!.Id));
                var ab = await kassen.SchliesseSitzungAsync(start.Sitzung!.Id, 0, null);
                if (!ab.Erfolg) Befund("Messung: Abschluss " + ab.Fehler);
            }
            else Befund("Messung: Kasse öffnen " + start.Fehler);
        }
    }
}
