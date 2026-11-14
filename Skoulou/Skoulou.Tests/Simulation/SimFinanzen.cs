using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Services.Finanzen;

namespace Skoulou.Tests.Simulation;

internal sealed partial class Simulation
{
    private static long Abrunden(long betrag) => betrag >= 500 ? betrag / 500 * 500 : betrag;

    /// <summary>Bis zum Stichtag fälliger Betrag eines Jahres (alle Raten mit Fälligkeit ≤ Tag) nach Ermäßigung.</summary>
    private static long FaelligBis(SimJahr jahr, JahresKonto k, DateOnly tag)
    {
        var faellig = jahr.Posten.Where(p => p.StufeId is null || p.StufeId == k.StufeId)
            .SelectMany(p => p.Raten).Where(r => r.Faellig <= tag).Sum(r => r.Betrag);
        return Math.Max(0, faellig - k.Ermaessigung);
    }

    /// <summary>Zahlungen der Familien je nach Zahlertyp (bar, Mobile Money, Überweisung), gelegentlich mit Storno.</summary>
    private async Task ZahlungenAsync(SimSchule s, Zufall z, DateOnly heute)
    {
        var jahr = s.Jahr;
        foreach (var schueler in s.Aktive.ToList())
        {
            if (!schueler.Konten.TryGetValue(jahr.Id, out var konto)) continue;
            var offen = s.Saldo(schueler, jahr.Id);
            if (offen <= 0) continue;
            var vortrag = offen - (Math.Max(0, jahr.SollFuer(konto.StufeId) - konto.Ermaessigung) - konto.Bezahlt);
            long betrag = schueler.Zahler switch
            {
                0 => FaelligBis(jahr, konto, heute.AddDays(7)) + Math.Max(0, vortrag) - konto.Bezahlt,
                1 => z.Ja(0.14) ? offen * z.Zahl(30, 61) / 100 : 0,
                2 => konto.Bezahlt == 0 && z.Ja(0.7) ? jahr.PostenBetrag(konto.StufeId, "Inscription") : z.Ja(0.05) ? offen / 4 : 0,
                _ => konto.Bezahlt == 0 ? offen : 0
            };
            if (schueler.Zahler == 0 && betrag > 0 && z.Ja(0.15)) betrag = 0; // zahlt erst zum nächsten Stichtag
            betrag = Math.Min(Abrunden(betrag), offen);
            if (betrag <= 0) continue;
            var guthaben = schueler.Zahler == 3 && z.Ja(0.15);
            if (guthaben) betrag += 5_000;
            await ZahlenAsync(s, z, schueler, betrag, heute, guthaben);
        }
    }

    private async Task ZahlenAsync(SimSchule s, Zufall z, SimSchueler schueler, long betrag, DateOnly heute, bool guthaben)
    {
        var jahr = s.Jahr;
        var kanal = z.Anteil() switch { < 0.62 => ZahlungsKanal.Especes, < 0.93 => ZahlungsKanal.MobileMoney, < 0.98 => ZahlungsKanal.Virement, _ => ZahlungsKanal.Cheque };
        var referenz = kanal switch
        {
            ZahlungsKanal.MobileMoney => (z.Ja(0.6) ? "TMoney " : "Flooz ") + z.Zahl(10_000_000, 99_999_999),
            ZahlungsKanal.Virement => "VIR-" + z.Zahl(100_000, 999_999),
            ZahlungsKanal.Cheque => "CHQ " + z.Zahl(1_000_000, 9_999_999),
            _ => null
        };
        var datum = DateOnly.FromDayNumber(Math.Max(jahr.Beginn.DayNumber, heute.AddDays(-z.Zahl(0, 6)).DayNumber));
        var akteur = z.Ja(0.6) ? s.Direktor : s.KoordMit;

        // Gelegentlich falscher Betrag: erfassen, mit Grund stornieren, richtig neu erfassen.
        if (z.Ja(0.01) && betrag > 1_000 && !guthaben)
        {
            var falsch = await SollAsync("Zahlung (falsch)", () => api.Zahlungen.ErfassenAsync(akteur, jahr.Id,
                new ZahlungEingabe(Guid.NewGuid(), schueler.Id, betrag / 2, datum, kanal, referenz, null)));
            if (falsch is not null)
            {
                await SollAsync("Zahlung stornieren", () => api.Zahlungen.StornierenAsync(s.Direktor, falsch.ZahlungId, "Erreur de montant à la saisie."));
                Zaehle("Zahlungen storniert");
            }
        }

        var vorher = s.Saldo(schueler, jahr.Id);
        var ergebnis = await SollAsync($"Zahlung {betrag} ({s.P.Name})", () => api.Zahlungen.ErfassenAsync(akteur, jahr.Id,
            new ZahlungEingabe(Guid.NewGuid(), schueler.Id, betrag, datum, kanal, referenz, null, guthaben)));
        if (ergebnis is null) return;
        if (ergebnis.Rest != Math.Max(0, vorher - betrag))
            Fehler.Enqueue($"{heute:dd/MM/yyyy} Quittung {ergebnis.Quittungsnummer} ({s.P.Name}): Reste à payer {ergebnis.Rest} statt {Math.Max(0, vorher - betrag)}.");
        schueler.Konten[jahr.Id].Bezahlt += betrag;
        Zaehle("Zahlungen");
        Zaehle("Zahlungen Summe", (int)Math.Min(int.MaxValue, betrag / 1_000));
    }

    /// <summary>Monatliche Buchungen im Kassenjournal (Gehälter, Strom/Wasser, Material, gelegentlich Spenden) und eine Gegenbuchung je Jahr.</summary>
    private async Task JournalAsync(SimSchule s, Zufall z, DateOnly heute)
    {
        var journal = api.Journal;
        if (s.Kategorien.Count == 0)
        {
            foreach (var k in await journal.KategorienAsync(s.Direktor)) s.Kategorien[k.Id] = k.Id;
            if (s.Nr % 3 == 0)
            {
                var neu = await SollAsync("Kategorie anlegen", () => journal.KategorieAnlegenAsync(s.KoordMit, "Cantine scolaire", BuchungsRichtung.Recette));
                if (neu != Guid.Empty) s.Kategorien[neu] = neu;
            }
        }
        var kategorien = await journal.KategorienAsync(s.Direktor);
        Guid Kat(string name) => kategorien.First(k => k.Name == name).Id;
        var monatsSoll = s.Aktive.Sum(x => s.Jahr.SollFuer(x.Konten.GetValueOrDefault(s.Jahr.Id)?.StufeId)) / 10;
        var akteur = z.Ja(0.5) ? s.Direktor : s.KoordMit;
        var datum = heute.AddDays(-z.Zahl(1, 10));
        var eintraege = new List<BuchungEingabe>
        {
            new(datum, Kat("Salaires et primes"), Abrunden(monatsSoll * z.Zahl(35, 50) / 100), $"Salaires du personnel – {datum:MM/yyyy}"),
            new(datum, Kat("Eau, électricité, internet"), Abrunden(z.Zahl(15_000, 60_000)), "Facture CEET et TdE")
        };
        if (z.Ja(0.4)) eintraege.Add(new(datum, Kat("Fournitures et matériel"), Abrunden(z.Zahl(5_000, 40_000)), "Craies, registres et cahiers"));
        if (z.Ja(0.1)) eintraege.Add(new(datum, Kat("Dons et subventions"), Abrunden(z.Zahl(50_000, 300_000)), "Don de l'association des anciens élèves"));
        if (kategorien.Any(k => k.Name == "Cantine scolaire") && z.Ja(0.5))
            eintraege.Add(new(datum, Kat("Cantine scolaire"), Abrunden(z.Zahl(20_000, 80_000)), "Recettes de la cantine"));
        Guid letzte = Guid.Empty;
        long letzteNetto = 0;
        foreach (var e in eintraege)
        {
            var id = await SollAsync("Buchung", () => journal.ErfassenAsync(akteur, e, null));
            if (id == Guid.Empty) continue;
            letzte = id;
            letzteNetto = kategorien.First(k => k.Id == e.KategorieId).Richtung == BuchungsRichtung.Recette ? e.Betrag!.Value : -e.Betrag!.Value;
            s.ManuellNetto += letzteNetto;
            Zaehle("Buchungen manuell");
        }
        if (heute.Month == 3 && letzte != Guid.Empty)
        {
            var ok = await SollAsync("Gegenbuchung", async () => { await journal.GegenbuchungAsync(s.Direktor, letzte, "Erreur de saisie : écriture en double."); return true; });
            if (ok) { s.ManuellNetto -= letzteNetto; Zaehle("Gegenbuchungen"); }
        }
        await AbgelehntAsync("Buchung ohne Recht", () => journal.ErfassenAsync(s.KoordOhne, eintraege[0], null));
    }

    // ---------------------------------------------------------------- Plattformrechnungen

    /// <summary>Täglicher Lauf des Rechnungsdienstes; neue Rechnungen bekommen einen geplanten Weg.</summary>
    private async Task AbrechnenAsync(DateOnly heute)
    {
        var neu = await api.Abrechnung.FaelligeAbrechnenAsync();
        if (neu == 0) return;
        await NeueRechnungenAufnehmenAsync(heute);
    }

    private async Task NeueRechnungenAufnehmenAsync(DateOnly heute)
    {
        var bekannt = Rechnungen.Select(r => r.Id).ToHashSet();
        var alle = await d.MitDbAsync(db => db.Plattformrechnungen.Select(r => new { r.Id, r.SchuleId, r.Nummer, r.ErgaenztId }).ToListAsync());
        foreach (var r in alle.Where(r => !bekannt.Contains(r.Id)).OrderBy(r => r.Nummer, StringComparer.Ordinal))
        {
            var schule = Schulen.FirstOrDefault(s => s.Id == r.SchuleId);
            if (schule is null) continue;
            var h = Zufall.Kombiniere(Startwert, r.Nummer.GetHashCode(StringComparison.Ordinal) & 0xFFFF, Rechnungen.Count);
            var wahl = new Zufall(h).Zahl(0, 100);
            var weg = r.ErgaenztId is not null ? RechnungsWeg.Normal : wahl switch
            {
                < 66 => RechnungsWeg.Normal,
                < 74 => RechnungsWeg.Direkt,
                < 81 => RechnungsWeg.AnfechtenAufrecht,
                < 86 => RechnungsWeg.AnfechtenStorno,
                < 89 => RechnungsWeg.StornoNachZahlung,
                _ => RechnungsWeg.Offen
            };
            Rechnungen.Add(new SimRechnung(r.Id, schule, weg, heute) { Naechster = heute.AddDays(14 + wahl % 20) });
            Zaehle("Rechnungen");
            if (r.ErgaenztId is not null) Zaehle("Rechnungen (Nachtrag)");
        }
    }

    /// <summary>Fällige Schritte der Rechnungen: melden, anfechten, bestätigen, aufrechterhalten, stornieren + neu abrechnen.</summary>
    private async Task RechnungswegeAsync(DateOnly heute)
    {
        var rechnung = api.Rechnungen;
        var i = 0;
        foreach (var r in Rechnungen.Where(r => !r.Fertig && r.Naechster <= heute && !r.Schule.Suspendiert).ToList())
        {
            i++;
            var s = r.Schule;
            var admin = AdminFuer(i);
            var melder = s.Nr % 3 == 0 ? s.KoordMit : s.Direktor;
            var info = await d.MitDbAsync(db => db.Plattformrechnungen.Where(x => x.Id == r.Id).Select(x => new { x.Betrag, x.PeriodeId, x.Status }).SingleAsync());
            Task Melden() => SollAsync("Rechnung melden", () => rechnung.MeldenAsync(melder, r.Id,
                new MeldungEingabe(info.Betrag, heute.AddDays(-2), "VIR-SKL-" + (r.Id.GetHashCode() & 0xFFFFF)), null));
            Task Bestaetigen() => SollAsync("Rechnung bestätigen", () => rechnung.BestaetigenAsync(admin, r.Id));
            async Task NeuAbrechnen()
            {
                await SollAsync("Neu abrechnen", () => rechnung.JetztAbrechnenAsync(admin, info.PeriodeId));
                await NeueRechnungenAufnehmenAsync(heute);
            }
            var weiter = 14;
            switch (r.Weg, r.Schritt)
            {
                case (RechnungsWeg.Normal, 0) or (RechnungsWeg.AnfechtenAufrecht, 2) or (RechnungsWeg.StornoNachZahlung, 0) or (RechnungsWeg.Offen, 1):
                    await Melden();
                    break;
                case (RechnungsWeg.Normal, 1) or (RechnungsWeg.Direkt, 0) or (RechnungsWeg.AnfechtenAufrecht, 3) or (RechnungsWeg.StornoNachZahlung, 1) or (RechnungsWeg.Offen, 2):
                    await Bestaetigen();
                    r.Fertig = r.Weg != RechnungsWeg.StornoNachZahlung;
                    weiter = 60;
                    break;
                case (RechnungsWeg.AnfechtenAufrecht, 0) or (RechnungsWeg.AnfechtenStorno, 0):
                    await SollAsync("Rechnung anfechten", () => rechnung.AnfechtenAsync(s.Direktor, r.Id, "Deux élèves partis en début de période ont été facturés."));
                    Zaehle("Rechnungen angefochten");
                    break;
                case (RechnungsWeg.AnfechtenAufrecht, 1):
                    await SollAsync("Anfechtung beantworten", () => rechnung.AufrechterhaltenAsync(admin, r.Id, "Les élèves étaient inscrits pendant la période : la facture est maintenue."));
                    break;
                case (RechnungsWeg.AnfechtenStorno, 1):
                    await SollAsync("Rechnung stornieren", () => rechnung.StornierenAsync(admin, r.Id, "Contestation fondée : facture recalculée."));
                    Zaehle("Rechnungen storniert");
                    await NeuAbrechnen();
                    r.Fertig = true;
                    break;
                case (RechnungsWeg.StornoNachZahlung, 2):
                    await SollAsync("Bezahlte Rechnung stornieren", () => rechnung.StornierenAsync(admin, r.Id, "Erreur de facturation : facture remplacée."));
                    Zaehle("Rechnungen storniert (nach Zahlung)");
                    await NeuAbrechnen();
                    r.Fertig = true;
                    break;
                case (RechnungsWeg.Offen, 0):
                    weiter = 150; // bleibt lange offen
                    break;
            }
            r.Schritt++;
            r.Naechster = heute.AddDays(weiter);
        }
    }

    /// <summary>Jahresende: Auszahlung der offenen Anteile von zwei Responsables (der dritte bleibt offen).</summary>
    private async Task AnteileAuszahlenAsync()
    {
        foreach (var resp in Responsables.Take(2))
        {
            var ids = await d.MitDbAsync(db => db.ResponsableAnteile.Where(a => a.ResponsableId == resp && a.AusgezahltAmUtc == null).Select(a => a.Id).ToListAsync());
            if (ids.Count == 0) continue;
            var n = await SollAsync("Anteile auszahlen", () => api.Anteile.AuszahlenAsync(Admin, ids, d.HeuteDatum, $"VIR-RESP-{d.HeuteDatum:yyyyMM}"));
            Zaehle("Anteile ausgezahlt", n);
        }
    }
}
