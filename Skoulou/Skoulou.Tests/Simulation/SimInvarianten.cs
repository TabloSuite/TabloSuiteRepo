using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Finanzen;
using Skoulou.Core.Noten;
using Skoulou.WebApp.Services.Finanzen;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.Tests.Simulation;

/// <summary>Invarianten auf dem Endstand (Bauplan §9.4); jede Verletzung mit klarer Meldung.</summary>
internal sealed partial class Simulation
{
    public List<string> Verletzungen { get; } = [];
    public List<string> Geprueft { get; } = [];

    private void Pruefe(bool ok, string meldung)
    {
        if (!ok && Verletzungen.Count < 400) Verletzungen.Add(meldung);
    }

    public async Task InvariantenAsync()
    {
        d.Heute(Ende);
        foreach (var (name, pruefung) in new (string, Func<Task>)[]
                 {
                     ("Plattformanteile", PlattformanteileAsync), ("Nummern", NummernAsync), ("Kasse", KasseAsync), ("Familiensalden", FamilienSaldenAsync),
                     ("Ränge", RaengeAsync), ("Anteile", AnteileAsync), ("Rechte", RechteAsync)
                 })
        {
            try
            {
                await pruefung();
            }
            catch (Exception ex) when (ex is FachFehler or KeinZugriffFehler or InvalidOperationException)
            {
                Verletzungen.Add($"Prüfung « {name} » abgebrochen: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    // ---------- 1 + 2: Jahrespreis je Schüler, höchstens einmal je Periode, Vollständigkeit

    private async Task PlattformanteileAsync()
    {
        var daten = await d.MitDbAsync(async db => new
        {
            Jahre = await db.Schuljahre.Select(j => new { j.Id, j.SchuleId, j.Status, j.PlattformJahrespreis, j.Bezeichnung }).ToListAsync(),
            Perioden = await db.Perioden.Where(p => p.ZaehltFuerJahr).Select(p => new { p.Id, p.SchuljahrId, p.Nr, p.Beginn, p.Ende, p.Titel }).ToListAsync(),
            Rechnungen = await db.Plattformrechnungen.Select(r => new { r.Id, r.Nummer, r.PeriodeId, r.SchuljahrId, r.Status, r.Betrag, r.Anzahl }).ToListAsync(),
            Positionen = await db.Rechnungspositionen.Select(p => new { p.RechnungId, p.PeriodeId, p.SchuelerId, p.Betrag, p.StorniertGrund }).ToListAsync(),
            // Session-Klassen (E7b): die Einschreibung zählt nur innerhalb ihrer Session (unabhängig nachgerechnet).
            Einschreibungen = await (from e in db.Einschreibungen
                                     where e.StorniertAmUtc == null
                                     join k in db.Klassen on e.KlasseId equals k.Id
                                     join s in db.Perioden on k.PeriodeId equals (Guid?)s.Id into sj
                                     from s in sj.DefaultIfEmpty()
                                     select new
                                     {
                                         e.SchuelerId, e.SchuljahrId, e.Von, e.Bis,
                                         SBeginn = s == null ? (DateOnly?)null : s.Beginn, SEnde = s == null ? (DateOnly?)null : s.Ende
                                     }).ToListAsync(),
            Schulen = await db.Schulen.Select(s => new { s.Id, s.Status }).ToDictionaryAsync(s => s.Id, s => s.Status)
        });
        var rechnungen = daten.Rechnungen.ToDictionary(r => r.Id);
        var periodenJeJahr = daten.Perioden.GroupBy(p => p.SchuljahrId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Beginn).ThenBy(p => p.Nr).ToList());
        var gueltig = daten.Positionen.Where(p => p.StorniertGrund == null).ToList();

        foreach (var j in daten.Jahre.Where(j => j.PlattformJahrespreis is not null))
        {
            var preis = j.PlattformJahrespreis!.Value;
            var perioden = periodenJeJahr.GetValueOrDefault(j.Id) ?? [];
            if (perioden.Count == 0) continue;
            Pruefe(PlattformAnteil.Anteile(preis, perioden.Count).Sum() == preis, $"Aufteilung von {preis} auf {perioden.Count} Perioden ergibt nicht den Jahrespreis.");
            var anteil = perioden.Select((p, i) => (p.Id, Betrag: PlattformAnteil.Anteil(preis, i + 1, perioden.Count))).ToDictionary(x => x.Id, x => x.Betrag);
            var jePosition = gueltig.Where(p => anteil.ContainsKey(p.PeriodeId)).ToList();
            foreach (var p in jePosition)
                Pruefe(p.Betrag == anteil[p.PeriodeId], $"Position {p.Betrag} statt {anteil[p.PeriodeId]} (Jahr {j.Bezeichnung}, Preis {preis}).");
            foreach (var g in jePosition.GroupBy(p => p.SchuelerId).Where(g => g.Select(p => p.PeriodeId).Distinct().Count() == perioden.Count))
                Pruefe(g.Sum(p => p.Betrag) == preis, $"Schüler {g.Key}: Summe {g.Sum(p => p.Betrag)} ≠ Jahrespreis {preis} ({j.Bezeichnung}).");
        }
        Geprueft.Add($"Jahrespreis-Aufteilung: {daten.Jahre.Count(j => j.PlattformJahrespreis is not null)} Schuljahre, {gueltig.Count} gültige Positionen");

        foreach (var g in gueltig.GroupBy(p => (p.PeriodeId, p.SchuelerId)).Where(g => g.Count() > 1))
            Pruefe(false, $"Schüler {g.Key.SchuelerId} in Periode {g.Key.PeriodeId} {g.Count()}× abgerechnet.");
        foreach (var p in daten.Positionen)
        {
            var r = rechnungen[p.RechnungId];
            if (r.Status == RechnungsStatus.Annulee) Pruefe(p.StorniertGrund is not null, $"Rechnung {r.Nummer} storniert, Position aber nicht.");
            else Pruefe(p.StorniertGrund is null, $"Rechnung {r.Nummer} gültig, Position aber storniert.");
        }
        foreach (var r in daten.Rechnungen)
        {
            var pos = daten.Positionen.Where(p => p.RechnungId == r.Id).ToList();
            Pruefe(pos.Count == r.Anzahl && pos.Sum(p => p.Betrag) == r.Betrag, $"Rechnung {r.Nummer}: Anzahl/Betrag passt nicht zu den Positionen.");
        }
        Geprueft.Add($"Keine Doppelabrechnung je Periode ({daten.Rechnungen.Count} Rechnungen, {daten.Positionen.Count} Positionen)");

        // Vollständigkeit: jede beendete Abrechnungsperiode eines gestarteten Jahres einer aktiven/suspendierten Schule.
        var abgerechnet = daten.Positionen.GroupBy(p => p.PeriodeId).ToDictionary(g => g.Key, g => g.Select(p => p.SchuelerId).ToHashSet());
        var geprueftePerioden = 0;
        foreach (var p in daten.Perioden.Where(p => p.Ende < Ende))
        {
            var jahr = daten.Jahre.First(j => j.Id == p.SchuljahrId);
            if (jahr.Status == SchuljahrStatus.Vorbereitung || daten.Schulen[jahr.SchuleId] is not (SchulStatus.Active or SchulStatus.Suspendue)) continue;
            var soll = daten.Einschreibungen.Where(e => e.SchuljahrId == p.SchuljahrId)
                .Where(e =>
                {
                    var von = e.SBeginn is { } sb && sb > e.Von ? sb : e.Von;
                    var bis = e.SEnde is { } se && (e.Bis is null || se < e.Bis) ? se : e.Bis;
                    return von <= p.Ende && (bis is null || (bis >= p.Beginn && bis >= von));
                })
                .Select(e => e.SchuelerId).ToHashSet();
            var ist = abgerechnet.GetValueOrDefault(p.Id) ?? [];
            var fehlt = soll.Except(ist).Count();
            var zuviel = ist.Except(soll).Count();
            Pruefe(fehlt == 0 && zuviel == 0, $"Periode {p.Titel} ({jahr.Bezeichnung}, Schule {jahr.SchuleId}): {fehlt} nicht abgerechnet, {zuviel} ohne Einschreibung abgerechnet.");
            geprueftePerioden++;
        }
        Geprueft.Add($"Vollständige Abrechnung: {geprueftePerioden} beendete Perioden (Session-Klassen nur in ihrer Session)");
    }

    // ---------- 3: Nummernkreise ohne Lücken und Dubletten

    private static readonly Regex NummerMuster = new(@"^(?<p>[A-Z]+-\d{4})-(?<n>\d+)$", RegexOptions.Compiled);

    private async Task NummernAsync()
    {
        var reihen = await d.MitDbAsync(async db => new (string Art, List<(Guid? Schule, string Nummer)> Werte)[]
        {
            ("Quittung", (await db.Zahlungen.Select(z => new { z.SchuleId, z.Quittungsnummer }).ToListAsync()).Select(x => ((Guid?)x.SchuleId, x.Quittungsnummer)).ToList()),
            ("Écriture", (await db.Buchungen.Select(b => new { b.SchuleId, b.Nummer }).ToListAsync()).Select(x => ((Guid?)x.SchuleId, x.Nummer)).ToList()),
            ("Bulletin", (await db.Bulletins.Select(b => new { b.SchuleId, b.Nummer }).ToListAsync()).Select(x => ((Guid?)x.SchuleId, x.Nummer)).ToList()),
            ("Facture", (await db.Plattformrechnungen.Select(r => r.Nummer).ToListAsync()).Select(n => ((Guid?)null, n)).ToList())
        });
        var doppelt = await d.MitDbAsync(db => db.Bulletins.Where(b => b.Status == BulletinStatus.Publie)
            .GroupBy(b => new { b.PeriodeId, b.EinschreibungId }).CountAsync(g => g.Count() > 1));
        Pruefe(doppelt == 0, $"{doppelt} Schüler haben für dieselbe Periode mehr als ein gültiges Bulletin.");
        foreach (var (art, werte) in reihen)
        {
            var kreise = 0;
            foreach (var g in werte.GroupBy(w => (w.Schule, Praefix: NummerMuster.Match(w.Nummer).Groups["p"].Value)))
            {
                kreise++;
                Pruefe(g.Key.Praefix.Length > 0, $"{art}: Nummer ohne Format ({g.First().Nummer}).");
                var zahlen = g.Select(w => long.Parse(NummerMuster.Match(w.Nummer).Groups["n"].Value)).OrderBy(n => n).ToList();
                Pruefe(zahlen.Distinct().Count() == zahlen.Count, $"{art} {g.Key.Praefix}: Dubletten ({zahlen.Count - zahlen.Distinct().Count()}).");
                Pruefe(zahlen.Count == 0 || (zahlen[0] == 1 && zahlen[^1] == zahlen.Count), $"{art} {g.Key.Praefix} (Schule {g.Key.Schule}): Lücke – {zahlen.Count} Nummern, höchste {zahlen.LastOrDefault()}.");
            }
            Geprueft.Add($"Nummern {art}: {werte.Count} in {kreise} Kreisen lückenlos und eindeutig");
        }
    }

    // ---------- 4: Kassensaldo = Summe des Journals, Zahlungen und Meldungen im Journal

    private async Task KasseAsync()
    {
        foreach (var s in Schulen)
        {
            var ist = await d.MitDbAsync(async db =>
            {
                var b = db.Buchungen.Where(x => x.SchuleId == s.Id);
                long Signiert(IEnumerable<(BuchungsRichtung R, long B)> l) => l.Sum(x => Kasse.MitVorzeichen(x.R, x.B));
                var alle = (await b.Select(x => new { x.Richtung, x.Betrag, x.Herkunft, x.HerkunftId, x.StornoVonId }).ToListAsync());
                var manuelleIds = (await b.Where(x => x.Herkunft == BuchungsHerkunft.Manuelle).Select(x => x.Id).ToListAsync()).ToHashSet();
                return new
                {
                    Saldo = Signiert(alle.Select(x => (x.Richtung, x.Betrag))),
                    Zahlungen = Signiert(alle.Where(x => x.Herkunft is BuchungsHerkunft.Paiement or BuchungsHerkunft.AnnulationPaiement).Select(x => (x.Richtung, x.Betrag))),
                    Facture = alle.Where(x => x.Herkunft == BuchungsHerkunft.FactureSkoulou).Sum(x => x.Betrag),
                    Manuell = Signiert(alle.Where(x => x.Herkunft == BuchungsHerkunft.Manuelle
                        || (x.Herkunft == BuchungsHerkunft.Annulation && x.StornoVonId is { } v && manuelleIds.Contains(v))).Select(x => (x.Richtung, x.Betrag))),
                    GueltigeZahlungen = await db.Zahlungen.Where(z => z.SchuleId == s.Id && z.StorniertAmUtc == null).SumAsync(z => (long?)z.Betrag) ?? 0,
                    Meldungen = await db.Zahlungsmeldungen.Where(m => db.Plattformrechnungen.Any(r => r.Id == m.RechnungId && r.SchuleId == s.Id)).SumAsync(m => (long?)m.Betrag) ?? 0
                };
            });
            var kasse = await api.Journal.KasseAsync(s.Direktor, s.Jahr.Id);
            Pruefe(kasse.Saldo == ist.Saldo, $"{s.P.Name}: Kassensaldo {kasse.Saldo} ≠ Summe des Journals {ist.Saldo}.");
            Pruefe(ist.Zahlungen == ist.GueltigeZahlungen, $"{s.P.Name}: Zahlungen im Journal {ist.Zahlungen} ≠ gültige Zahlungen {ist.GueltigeZahlungen}.");
            Pruefe(ist.Facture == ist.Meldungen, $"{s.P.Name}: Ausgaben « Facture SKOULOU » {ist.Facture} ≠ gemeldete Zahlungen {ist.Meldungen}.");
            Pruefe(ist.Manuell == s.ManuellNetto, $"{s.P.Name}: manuelle Buchungen netto {ist.Manuell} ≠ erfasst {s.ManuellNetto}.");
            var bezahlt = s.Schueler.Values.Sum(x => x.Konten.Values.Sum(k => k.Bezahlt));
            Pruefe(bezahlt == ist.GueltigeZahlungen, $"{s.P.Name}: Zahlungen laut Simulation {bezahlt} ≠ gültige Zahlungen {ist.GueltigeZahlungen}.");
        }
        Geprueft.Add($"Kassensaldo = Journal, Zahlungen/Meldungen/manuelle Buchungen im Journal ({Schulen.Count} Schulen)");
    }

    // ---------- 5: Familiensalden = Soll − bezahlt (+ Vortrag)

    private async Task FamilienSaldenAsync()
    {
        var z = new Zufall(Startwert + 5);
        var anzahl = 0;
        var eltern = 0;
        foreach (var s in Schulen)
        {
            var stichprobe = z.Mische(s.Schueler.Values.Where(x => x.Konten.Count > 0)).Take(Math.Max(10, s.Schueler.Count / 15)).ToList();
            foreach (var x in stichprobe)
            {
                var jahr = s.Jahre.Last(j => x.Konten.ContainsKey(j.Id));
                var erwartet = s.Saldo(x, jahr.Id);
                var konto = (await api.Gebuehren.KontoAsync(s.Direktor, jahr.Id, x.Id)).Konto;
                Pruefe(konto.Saldo == erwartet, $"{s.P.Name}, {x.Nachname} {x.Vorname} ({jahr.Bezeichnung}): Saldo {konto.Saldo} ≠ erwartet {erwartet} (Vortrag {konto.Vortrag}).");
                Pruefe(konto.Offen - konto.Guthaben == erwartet, $"{s.P.Name}, {x.Nachname}: Offen {konto.Offen} − Avoir {konto.Guthaben} ≠ {erwartet}.");
                anzahl++;
                if (x.Eltern is { } e && x.Aktiv && jahr == s.Jahr)
                {
                    await ErsteAnmeldungAsync(e);
                    var familie = await api.Familie.KontoAsync(e, x.Id);
                    Pruefe(familie?.Konto.Saldo == erwartet, $"{s.P.Name}, Eltern von {x.Nachname}: Saldo {familie?.Konto.Saldo} ≠ {erwartet}.");
                    eltern++;
                }
            }
        }
        Geprueft.Add($"Familiensalden: {anzahl} Schülerkonten (davon {eltern} auch aus Elternsicht) = Soll − bezahlt + Vortrag");
    }

    // ---------- 6: Ränge passen zu den Mitteln; Mittel unabhängig nachgerechnet (Stichprobe)

    private async Task RaengeAsync()
    {
        var z = new Zufall(Startwert + 6);
        var klassen = 0;
        var nachgerechnet = 0;
        foreach (var s in Schulen.Where(s => s.Jahre.Count > 1))
        {
            var jahr = s.Jahre[^2];
            foreach (var (k, p) in z.Mische(jahr.Klassen.SelectMany(k => jahr.Perioden.Where(p => k.Session == 0 || k.Session == p.Nr).Select(p => (k, p)))).Take(3))
            {
                var u = await api.Conseil.UebersichtAsync(s.Direktor, k.Id, p.Id);
                var werte = u.Zeilen.Select(r => r.Moyenne.Wert is { } w ? Math.Round(w, 2, MidpointRounding.AwayFromZero) : (decimal?)null).ToList();
                for (var i = 0; i < u.Zeilen.Count; i++)
                {
                    var r = u.Zeilen[i];
                    var wert = werte[i];
                    Pruefe((r.Rang.Rang is null) == (wert is null), $"{s.P.Name} {k.Name} {p.Titel}: {r.Name} klassiert ≠ vollständig.");
                    if (wert is null) continue;
                    var besser = werte.Count(w => w > wert);
                    var gleich = werte.Count(w => w == wert);
                    Pruefe(r.Rang.Rang == besser + 1 && r.Rang.Ex == (gleich > 1), $"{s.P.Name} {k.Name} {p.Titel}: {r.Name} Rang {r.Rang.Rang}{(r.Rang.Ex ? " ex" : "")} bei Mittel {wert} (erwartet {besser + 1}).");
                }
                nachgerechnet += await MittelNachrechnenAsync(s, k, p, u);
                klassen++;
            }
        }
        Geprueft.Add($"Ränge/Mittel: {klassen} Klassen-Perioden, {nachgerechnet} Mittel unabhängig nachgerechnet");
    }

    private async Task<int> MittelNachrechnenAsync(SimSchule s, SimKlasse k, SimPeriode p, Skoulou.WebApp.Services.Noten.KlassenratUebersicht u)
    {
        var daten = await d.MitDbAsync(async db => new
        {
            Faecher = await db.Klassenfaecher.Where(kf => kf.KlasseId == k.Id).Select(kf => new { kf.Id, kf.Koeffizient }).ToListAsync(),
            Bewertungen = await db.Bewertungen.Where(b => b.PeriodeId == p.Id && db.Klassenfaecher.Any(kf => kf.Id == b.KlassenfachId && kf.KlasseId == k.Id))
                .Select(b => new { b.Id, b.KlassenfachId, b.Bareme, b.Gewicht }).ToListAsync(),
            Noten = await db.Noten.Where(n => db.Bewertungen.Any(b => b.Id == n.BewertungId && b.PeriodeId == p.Id
                    && db.Klassenfaecher.Any(kf => kf.Id == b.KlassenfachId && kf.KlasseId == k.Id)))
                .Select(n => new { n.BewertungId, n.EinschreibungId, n.Punkte, n.Status }).ToListAsync(),
            Einschreibungen = await db.Einschreibungen.Where(e => e.KlasseId == k.Id && e.StorniertAmUtc == null)
                .Select(e => new { e.Id, e.Von, e.Bis }).ToListAsync(),
            NullBeiAbsenz = await db.Schulen.Where(x => x.Id == s.Id).Select(x => x.AbsenzOhneEntschuldigungZaehltNull).SingleAsync()
        });
        var ganz = daten.Einschreibungen.Where(e => e.Von <= p.Beginn && (e.Bis is null || e.Bis >= p.Ende)).Select(e => e.Id).ToHashSet();
        var n = 0;
        foreach (var zeile in u.Zeilen.Where(r => ganz.Contains(r.EinschreibungId)))
        {
            decimal summe = 0, koeffizienten = 0;
            var vollstaendig = true;
            foreach (var kf in daten.Faecher)
            {
                var bew = daten.Bewertungen.Where(b => b.KlassenfachId == kf.Id).ToList();
                if (bew.Count == 0) continue;
                decimal punkte = 0, gewichte = 0;
                var fehlt = false;
                foreach (var b in bew)
                {
                    var note = daten.Noten.FirstOrDefault(x => x.BewertungId == b.Id && x.EinschreibungId == zeile.EinschreibungId);
                    if (note is null) { fehlt = true; break; }
                    if (note.Status == NotenStatus.Notee) { punkte += note.Punkte!.Value / b.Bareme * 20 * b.Gewicht; gewichte += b.Gewicht; }
                    else if (note.Status == NotenStatus.Absent && daten.NullBeiAbsenz) gewichte += b.Gewicht;
                }
                if (fehlt) { vollstaendig = false; break; }
                if (gewichte == 0) continue;
                summe += punkte / gewichte * kf.Koeffizient;
                koeffizienten += kf.Koeffizient;
            }
            decimal? erwartet = vollstaendig && koeffizienten > 0 ? Math.Round(summe / koeffizienten, 2, MidpointRounding.AwayFromZero) : null;
            decimal? ist = zeile.Moyenne.Wert is { } w ? Math.Round(w, 2, MidpointRounding.AwayFromZero) : null;
            Pruefe(erwartet == ist, $"{s.P.Name} {k.Name} {p.Titel}: Mittel von {zeile.Name} {ist} ≠ nachgerechnet {erwartet}.");
            n++;
        }
        return n;
    }

    // ---------- 8: Anteile der Responsables nur für bestätigte Rechnungen, Ausgleich nach Storno

    private async Task AnteileAsync()
    {
        var daten = await d.MitDbAsync(async db => new
        {
            Anteile = await db.ResponsableAnteile.Select(a => new { a.Id, a.RechnungId, a.ResponsableId, a.Prozent, a.Betrag, a.AusgleichFuerId }).ToListAsync(),
            Rechnungen = await db.Plattformrechnungen.Select(r => new { r.Id, r.Nummer, r.Status, r.Betrag, r.SchuleId }).ToListAsync(),
            Schulen = await db.Schulen.Select(s => new { s.Id, s.ResponsableId, s.ResponsableProzent }).ToDictionaryAsync(s => s.Id),
            Standard = await db.PlattformEinstellungen.Select(p => p.ResponsableProzentStandard).SingleAsync()
        });
        var jeRechnung = daten.Anteile.ToLookup(a => a.RechnungId);
        var negative = 0;
        foreach (var r in daten.Rechnungen)
        {
            var anteile = jeRechnung[r.Id].ToList();
            var positiv = anteile.Where(a => a.AusgleichFuerId is null).ToList();
            switch (r.Status)
            {
                case RechnungsStatus.Payee:
                    var schule = daten.Schulen[r.SchuleId];
                    var prozent = schule.ResponsableProzent ?? daten.Standard;
                    var soll = schule.ResponsableId is null ? 0 : ResponsableVerguetung.Betrag(r.Betrag, prozent);
                    Pruefe(positiv.Count == (soll > 0 ? 1 : 0) && anteile.Count == positiv.Count, $"Rechnung {r.Nummer} (bezahlt): {positiv.Count} Anteil(e), erwartet {(soll > 0 ? 1 : 0)}.");
                    if (positiv.Count == 1) Pruefe(positiv[0].Betrag == soll, $"Rechnung {r.Nummer}: Anteil {positiv[0].Betrag} ≠ {soll} ({prozent} %).");
                    break;
                case RechnungsStatus.Annulee:
                    Pruefe(anteile.Sum(a => a.Betrag) == 0, $"Rechnung {r.Nummer} storniert: Anteile summieren sich auf {anteile.Sum(a => a.Betrag)} statt 0.");
                    negative += anteile.Count(a => a.Betrag < 0);
                    break;
                default:
                    Pruefe(anteile.Count == 0, $"Rechnung {r.Nummer} ({r.Status}) hat {anteile.Count} Anteil(e) ohne Bestätigung.");
                    break;
            }
        }
        Geprueft.Add($"Responsable-Anteile: {daten.Anteile.Count} (davon {negative} Ausgleich nach Storno) nur für bestätigte Rechnungen");
    }
}
