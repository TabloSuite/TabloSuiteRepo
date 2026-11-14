using ENote.Core.DataModels.Finanzen;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static ENote.WebApp.Services.KassenService;

namespace ENote.Tests.Simulation;

internal sealed partial class Simulation
{
    // ------------------------------------------------------------------ Registrierung & Einrichtung

    private int zusatz;

    private async Task RegistrierungenAsync(DateOnly tag)
    {
        foreach (var l in Laeden.Where(l => l.Start == tag && l.Id == Guid.Empty).ToList())
            await RegistriereAsync(l);

        // Ablehnungen (Spam) und eine wartende Anmeldung.
        var i = tag.DayNumber - Beginn.DayNumber;
        if (i is 40 or 130 or 500)
        {
            zusatz++;
            var l = new SimLaden
            {
                Nr = 100 + zusatz, V = SimNamen.Vorlagen[zusatz % SimNamen.Vorlagen.Length], Kanal = 0, Start = tag,
                Name = i == 500 ? "Salon Belle Vue Kégué" : $"Test boutique {zusatz}", Ort = "Lomé",
            };
            Laeden.Add(l);
            await RegistriereAsync(l);
            if (i == 500) { l.Wartend = true; Notiere($"{l.Name}: Anmeldung bleibt offen (wartend)."); }
            else
            {
                await SollAsync("Ablehnung", () => d.Hole<AdminService>().SetzeAktivAsync(Admin2, l.Id, false));
                l.Abgelehnt = true;
                l.Wartend = false;
                Zaehle("Anmeldungen abgelehnt");
            }
        }

        // Zweite Läden bestehender Besitzer (nach deren Probezeit).
        foreach (var (nr, tage) in new[] { (1, 125), (4, 150) })
        {
            var basis = Laeden.First(x => x.Nr == nr);
            if (basis.Id == Guid.Empty || basis.Start.AddDays(tage) != tag) continue;
            await ZweiterLadenAsync(basis);
        }

        // Freigaben wartender Anmeldungen (1–3 Tage nach der Anmeldung).
        foreach (var l in Laeden.Where(l => l.Wartend && !l.Abgelehnt && l.Nr < 100 && tag >= l.Start.AddDays(1 + l.Nr % 3)).ToList())
        {
            var wer = l.Kanal == 2 && l.Responsable is Guid po ? po : (l.Nr % 2 == 0 ? Admin : Admin2);
            if (await SollAsync($"Freigabe {l.Name}", () => d.Hole<AdminService>().SetzeFreigabeAsync(wer, l.Id, true)))
            {
                l.Wartend = false;
                l.Freigegeben = true;
                Zaehle(wer == Admin || wer == Admin2 ? "Freigaben durch Admin" : "Freigaben durch Responsable");
            }
        }

        foreach (var l in Laeden.Where(l => l.Freigegeben && !l.Eingerichtet && !l.Abgelehnt).ToList())
        {
            Uhr.SetzeLokal(Uhr.Global);
            await EinrichtenAsync(l, new Zufall(Zufall.Kombiniere(Startwert, l.Nr, 1)));
            Uhr.SetzeLokal(null);
        }
    }

    private async Task RegistriereAsync(SimLaden l)
    {
        var z = new Zufall(Zufall.Kombiniere(Startwert, l.Nr));
        var vn = z.Wahl(SimNamen.Vornamen);
        var nn = z.Wahl(SimNamen.Nachnamen);
        l.BesitzerPseudonym = $"proprio{l.Nr:000}.sim";
        l.BesitzerPasswort = Passwort(l.Nr);
        var email = $"proprio{l.Nr:000}@enote-sim.tg";
        switch (l.Kanal)
        {
            case 0:
                var r = await d.Hole<AuthService>().RegisterBesitzerAsync(vn, nn, l.BesitzerPseudonym, email, l.BesitzerPasswort, l.Name, l.Ort, "Bienvenue chez nous");
                if (!r.Succeeded) { Befund($"Registrierung {l.Name}: {r.ErrorMessage}"); return; }
                l.Wartend = true;
                break;
            case 1:
                var a = await d.Hole<AdminService>().LegeBesitzerMitSalonAnAsync(l.Nr % 2 == 0 ? Admin : Admin2, vn, nn, email, l.BesitzerPasswort,
                    l.Name, l.Ort, l.Land, benutzername: l.BesitzerPseudonym);
                if (!a.Erfolg) { Befund($"Admin-Anlage {l.Name}: {a.Fehler}"); return; }
                l.Freigegeben = true;
                break;
            default:
                var po = Responsables[l.Nr % Responsables.Count];
                var p = await d.Hole<ProduktOwnerService>().LegeSalonSelbstAnAsync(po, vn, nn, l.BesitzerPseudonym, email, l.BesitzerPasswort,
                    l.Name, l.Ort, l.Land, d.Hole<UserManager<ENote.Core.DataModels.Identity.AppUser>>());
                if (!p.Erfolg) { Befund($"Responsable-Anlage {l.Name}: {p.Fehler}"); return; }
                l.Responsable = po;
                l.Wartend = true;
                break;
        }
        (l.Id, l.Besitzer, l.BesitzerUserSalon) = await d.MitDbAsync(async db =>
        {
            var x = await db.UserSalons.Where(us => us.Salon.Name == l.Name && us.AppUser.UserName == l.BesitzerPseudonym)
                .Select(us => new { us.SalonId, us.AppUserId, us.Id }).SingleAsync();
            return (x.SalonId, x.AppUserId, x.Id);
        });
        Zaehle($"Läden angelegt ({(l.Kanal switch { 0 => "Registrierung", 1 => "Admin", 2 => "Responsable", _ => "zweiter Laden" })})");

        // Responsable für einen Teil der übrigen Läden (Provision % oder % + Festbetrag).
        if (l.Responsable is null && l.Nr < 100 && l.Nr % 3 != 0)
        {
            var po = Responsables[l.Nr % Responsables.Count];
            if (await SollAsync("Zuweisung", () => d.Hole<ProduktOwnerService>().WeiseSalonZuAsync(po, l.Id, true, l.Nr % 2 == 0)))
                l.Responsable = po;
        }
        if (l.Responsable is Guid r2)
        {
            var zuweisung = await d.MitDbAsync(db => db.ProduktOwnerSalons.Where(p => p.SalonId == l.Id && p.ProduktOwnerId == r2 && p.GueltigBisUtc == null)
                .Select(p => p.Id).SingleAsync());
            await SollAsync("Provision", () => d.Hole<ProduktOwnerService>().SetzeProvisionAsync(zuweisung, 10 + l.Nr % 3 * 5, l.Nr % 4 == 0 ? 500 : null));
        }
    }

    private async Task ZweiterLadenAsync(SimLaden basis)
    {
        var nr = 200 + basis.Nr;
        var (ort, land) = SimNamen.Orte[30 + basis.Nr % 6];
        var l = new SimLaden
        {
            Nr = nr, V = basis.V, Kanal = 3, Name = $"{basis.V.Praefix} {ort}", Ort = ort, Land = land, Start = Heute,
            BesitzerPseudonym = $"proprio{nr}.sim", BesitzerPasswort = Passwort(nr), BesitzerPin = basis.BesitzerPin,
        };
        Pruefe(await d.Hole<SalonService>().DarfWeiterenLadenAnlegenAsync(basis.Besitzer), $"{basis.Name} darf nach der Probezeit einen zweiten Laden anlegen");
        var r = await d.Hole<SalonService>().FuegeSalonHinzuAsync(basis.Besitzer, l.Name, ort, land, "Kokou", "Ayivi", l.BesitzerPseudonym,
            $"proprio{basis.Nr:000}@enote-sim.tg", l.BesitzerPasswort);
        if (!r.Erfolg) { Befund($"Zweiter Laden für {basis.Name}: {r.Fehler}"); return; }
        l.Id = r.Salon!.Id;
        l.Besitzer = r.Konto!.Id;
        l.BesitzerUserSalon = await d.MitDbAsync(db => db.UserSalons.Where(us => us.SalonId == l.Id && us.AppUserId == l.Besitzer).Select(us => us.Id).SingleAsync());
        l.Wartend = true;
        Laeden.Add(l);
        Zaehle("Läden angelegt (zweiter Laden)");
        Notiere($"Zweiter Laden « {l.Name} » für den Besitzer von {basis.Name}.");
    }

    private async Task EinrichtenAsync(SimLaden l, Zufall z)
    {
        var team = d.Hole<TeamService>();
        var katalog = d.Hole<KatalogService>();
        if (string.IsNullOrEmpty(l.BesitzerPin)) l.BesitzerPin = $"{5000 + l.Nr * 37 % 4000:0000}";
        await PruefeErgebnis("Besitzer-PIN", team.SetzeBesitzerPinAsync(l.BesitzerUserSalon, l.BesitzerPin));
        await SollAsync("Kontaktdaten", () => d.Hole<SalonService>().SetzeKontaktdatenAsync(l.Id, "Votre satisfaction, notre fierté",
            SimNamen.Telefon(l.Nr, 1), $"Rue {l.Nr}, {l.Ort}", null));

        // Équipe: 4–7 Employés, verschiedene Rechte, eigene Kassen-PIN.
        var anzahl = 4 + l.Nr % 4;
        for (var k = 0; k < anzahl; k++)
        {
            var vn = z.Wahl(SimNamen.Vornamen);
            var nn = z.Wahl(SimNamen.Nachnamen);
            var pin = $"{1000 + (l.Nr * 53 + k * 111) % 8000:0000}";
            if (pin == l.BesitzerPin || l.Team.Any(t => t.Pin == pin)) pin = $"{9000 + k:0000}";
            var r = await team.LegeKassiererAnAsync(l.Id, vn, nn, $"emp{l.Nr:000}.{k + 1}.sim", $"emp{l.Nr:000}.{k + 1}@enote-sim.tg", Passwort(l.Nr * 10 + k), pin);
            if (!r.Erfolg) { Befund($"Employé {l.Name}: {r.Fehler}"); continue; }
            var us = await d.MitDbAsync(db => db.UserSalons.Where(x => x.SalonId == l.Id && x.AppUserId == r.Kassierer!.Id).Select(x => x.Id).SingleAsync());
            var m = new SimMitarbeiter { Id = r.Kassierer!.Id, UserSalonId = us, Pin = pin, Name = $"{vn} {nn}", Erstatten = k % 3 == 0 };
            await SollAsync("Rechte", () => team.SetzeRechteAsync(us, k == 0, k <= 1, k == 0, m.Erstatten));
            l.Team.Add(m);
        }
        Zaehle("Employés", l.Team.Count);

        // Katalog mit Kategorien (Farbe, Unterkategorie), Favoriten, Lager, Sammel-Artikel, Barcodes.
        var kategorien = new Dictionary<string, Guid>();
        foreach (var kv in l.V.Kategorien)
        {
            Guid katId;
            if (kv.Unter is null)
            {
                var k = await katalog.SpeichereKategorieAsync(l.Id, kv.Name);
                katId = k.Id;
                kategorien[kv.Name] = katId;
                if (kv.Farbe is not null)
                    await PruefeErgebnis("Kategoriefarbe", katalog.AktualisiereKategorieAsync(katId, l.Id, kv.Name, kv.Farbe));
            }
            else
            {
                var u = await katalog.SpeichereUnterkategorieAsync(l.Id, kategorien[kv.Name], kv.Unter);
                if (!u.Erfolg) { Befund($"Unterkategorie {kv.Unter}: {u.Fehler}"); continue; }
                katId = u.Kategorie!.Id;
            }
            Zaehle("Kategorien");
            var j = 0;
            foreach (var a in kv.Artikel)
            {
                var basis = a.Basis is null ? null : l.Katalog.FirstOrDefault(x => x.Name == a.Basis);
                var code = j == 0 ? await katalog.GeneriereNeuenBarcodeAsync(l.Id) : null;
                var gespeichert = await SollAsync($"Leistung {a.Name}", () => katalog.SpeichereLeistungAsync(new Leistung
                {
                    Id = Guid.Empty, SalonId = l.Id, Name = a.Name, Typ = a.Produkt || basis is not null ? LeistungsTyp.Produit : LeistungsTyp.Prestation,
                    Preis = a.Preis, Kaufpreis = a.Einkauf, KategorieId = katId, HatStueckzahl = a.Produkt, Stueckzahl = a.Lager,
                    MindestbestandWarnung = a.Mindest, BasisLeistungId = basis?.Id, BasisAbzugMenge = a.Abzug, IstAktiv = true, StrichCode = code,
                }));
                if (gespeichert is null) continue;
                var art = new SimArtikel { Id = gespeichert.Id, Name = a.Name, Preis = a.Preis, Produkt = a.Produkt, BasisId = basis?.Id, Abzug = a.Abzug ?? 1, Favorit = j < 2 };
                l.Katalog.Add(art);
                if (a.Produkt) l.Lager[art.Id] = a.Lager;
                if (art.Favorit) await katalog.ToggleFavoritAsync(l.Id, art.Id, l.Besitzer);
                Zaehle(a.Produkt ? "Produits (avec stock)" : "Prestations");
                j++;
            }
        }

        await d.Hole<KassenService>().StelleStandardArtikelSicherAsync(l.Id);
        l.DiversId = await d.MitDbAsync(db => db.Leistungen.Where(x => x.SalonId == l.Id && x.IstStandardArtikel).Select(x => x.Id).SingleAsync());

        // Remises: Prozent mit Bedingung, Festbetrag, auf eine Leistung begrenzt.
        var erste = l.Katalog.First(a => a.Verkaufbar && a.Preis >= 1000);
        foreach (var (name, art, wert, leistung, bedingung) in new[]
                 {
                     ("Fidélité -10 %", RabattArt.Prozent, 10m, (Guid?)null, "Client fidèle (5e passage)"),
                     ("Geste commercial", RabattArt.Festbetrag, 200m, null, (string?)null),
                     ($"Promo {erste.Name}", RabattArt.Prozent, 15m, erste.Id, null),
                 })
        {
            var r = await SollAsync("Remise", () => katalog.SpeichereRabattAsync(new Rabatt
            {
                Id = Guid.Empty, SalonId = l.Id, Name = name, Art = art, Wert = wert, LeistungId = leistung, Bedingung = bedingung, IstAktiv = true,
            }));
            if (r is not null) l.Rabatte.Add(new SimRabatt(r.Id, art, wert, leistung, bedingung is not null));
        }

        // Mobile Money: Standard = alle Anbieter des Landes; jeder fünfte Laden wählt aus.
        if (l.Nr % 5 == 0)
        {
            var alle = await d.Hole<MobileMoneyService>().GetErlaubteAnbieterAsync(l.Id);
            if (alle.Count > 1) await PruefeErgebnis("Mobile Money", d.Hole<MobileMoneyService>().SetzeAuswahlAsync(l.Besitzer, l.Id, [alle[0].Id]));
        }

        Pruefe((await AnbieterAsync(l)).Count > 0, $"{l.Name} ({l.Land ?? "ohne Land"}) kann Mobile Money annehmen");

        foreach (var raum in l.V.Raeume)
            await SollAsync("Salle", () => d.Hole<KundenBesuchService>().RaumAnlegenAsync(l.Besitzer, l.Id, raum));
        foreach (var k in l.V.Ausgaben)
            l.AusgabenKategorien.Add((await d.Hole<AusgabenService>().SpeichereKategorieAsync(l.Id, null, k, true)).Id);

        l.Eingerichtet = true;
        Zaehle("Läden eingerichtet");
    }

    private async Task PruefeErgebnis(string was, Task<(bool Erfolg, string? Fehler)> aufgabe)
    {
        try
        {
            var r = await aufgabe;
            if (!r.Erfolg) Befund($"{was}: {r.Fehler}");
        }
        catch (Exception ex) { Befund($"{was}: {ex.GetType().Name}: {ex.Message}"); }
    }

    // ------------------------------------------------------------------ Tagesbetrieb

    private async Task LadenTagAsync(SimLaden l, DateOnly tag, Zufall z)
    {
        var kassen = d.Hole<KassenService>();
        Uhr.SetzeLokal(tag.ToDateTime(new TimeOnly(7, 45)) - l.Versatz);

        var kassierer = z.Wahl(l.Team);
        if (z.Prozent(1))
        {
            // Vertippt: falsche PIN, dann richtig.
            var falsch = await kassen.FindeZuordnungPerPinAsync(kassierer.Id, kassierer.Pin == "0000" ? "1111" : "0000");
            Pruefe(!falsch.Erfolg, "falsche Kassen-PIN wird abgelehnt");
        }
        var pinOk = await kassen.FindeZuordnungPerPinAsync(kassierer.Id, kassierer.Pin);
        if (!pinOk.Erfolg) { Befund($"{l.Name}: PIN von {kassierer.Name} abgelehnt: {pinOk.Fehler}"); return; }

        var startbestand = 5000m * z.Zahl(2, 7);
        var start = await kassen.StarteSitzungAsync(l.Id, kassierer.Id, startbestand);
        if (!start.Erfolg)
        {
            if (start.Fehler?.Contains("suspendue", StringComparison.Ordinal) == true)
            {
                l.KasseGesperrt++;
                Zaehle("Kassenstart gesperrt (Zahlungsverzug)");
                if (l.Zahler == 0) Befund($"{l.Name}: Kasse gesperrt, obwohl der Laden pünktlich zahlt: {start.Fehler}");
            }
            else if (l.GekuendigtZum is DateTime zum && Uhr.Jetzt >= zum) Zaehle("Kassenstart nach Kündigung abgelehnt (erwartet)");
            else Befund($"{l.Name}: Kasse öffnen: {start.Fehler}");
            return;
        }
        if (l.GekuendigtZum is DateTime k && Uhr.Jetzt >= k) Befund($"{l.Name}: Kasse ließ sich nach Kündigung zum {k:dd/MM/yyyy} öffnen.");
        l.Sitzungen++;
        var sitzung = start.Sitzung!.Id;
        var soll = startbestand;

        var saison = tag.Month == 12 ? 1.3 : tag.Month is 1 or 8 ? 0.85 : 1.0;
        var n = Math.Max(1, (int)Math.Round(l.V.VerkaeufeProTag * faktor * saison * (0.6 + z.Kommazahl() * 0.8)));
        var takt = TimeSpan.FromMinutes(11 * 60.0 / (n + 1));
        Guid? geparkt = null;
        var wechselNach = z.Prozent(12) ? n / 2 : -1;

        for (var i = 0; i < n; i++)
        {
            Uhr.Weiter(takt * (0.5 + z.Kommazahl()));
            if (i == wechselNach && n > 2)
            {
                // Schichtwechsel: abschließen und mit dem gezählten Bestand neu öffnen.
                var zu = await kassen.SchliesseSitzungAsync(sitzung, soll, "Changement d'équipe");
                if (!zu.Erfolg) { Befund($"{l.Name}: Schichtwechsel: {zu.Fehler}"); return; }
                PruefeSoll(l, zu.Sitzung!, soll);
                kassierer = z.Wahl(l.Team);
                var neu = await kassen.StarteSitzungAsync(l.Id, kassierer.Id, soll);
                if (!neu.Erfolg) { Befund($"{l.Name}: Neustart nach Schichtwechsel: {neu.Fehler}"); return; }
                sitzung = neu.Sitzung!.Id;
                l.Sitzungen++;
                Zaehle("Schichtwechsel");
            }

            Guid? kunde = null;
            var mitKunde = l.V.Anwesenheit ? 55 : 15;
            if (z.Prozent(mitKunde)) kunde = await KundeAsync(l, kassierer, z);

            // Anwesenheit: Ankunft vor dem Encaissement (endet automatisch mit dem Verkauf).
            if (kunde is Guid ka && l.V.Anwesenheit && !l.Patienten.ContainsKey(ka) && z.Prozent(35))
            {
                var besuch = await AnkunftAsync(l, kassierer, ka, z);
                if (besuch is Guid b && z.Prozent(8))
                {
                    await SollAsync("Notiz", () => d.Hole<KundenBesuchService>().NotizAsync(kassierer.Id, ka, z.Wahl(SimNamen.Notizen), b));
                    Zaehle("Notizen");
                }
                if (besuch is Guid b2 && z.Prozent(4))
                {
                    // Départ ohne Kauf.
                    await SollAsync("Départ", () => d.Hole<KundenBesuchService>().BeendeAsync(kassierer.Id, b2));
                    Zaehle("Besuche ohne Kauf (Départ)");
                    continue;
                }
            }

            // Ticket « en attente »: parken, später fortsetzen.
            if (geparkt is null && z.Prozent(4))
            {
                var pos = Zeilen(l, z).Select(x => new TicketParkService.PositionInput(x.Artikel.Id, x.Menge)).ToList();
                var p = await SollAsync("Ticket parken", () => d.Hole<TicketParkService>().ParkeAsync(kassierer.Id, l.Id, pos, kunde, "Revient après la pause"));
                if (p is not null) { geparkt = p.TicketId; Zaehle("Tickets en attente"); }
                continue;
            }
            if (geparkt is Guid t && z.Prozent(50))
            {
                geparkt = null;
                var u = await SollAsync("Ticket fortsetzen", () => d.Hole<TicketParkService>().UebernehmeAsync(kassierer.Id, t));
                if (u is null) continue;
                var zeilen = u.Positionen.Select(x => (Artikel: l.Katalog.First(a => a.Id == x.LeistungId), x.Menge, Rabatt: (SimRabatt?)null)).ToList();
                soll += await VerkaufAsync(l, sitzung, zeilen, u.KundeId, z) ?? 0;
                Zaehle("Tickets fortgesetzt");
                continue;
            }

            soll += await VerkaufAsync(l, sitzung, Zeilen(l, z), kunde, z) ?? 0;

            // Rückerstattung (Teil oder ganz, verschiedene Zahlwege, mit/ohne Lager).
            if (z.Prozent(1.4) && l.LetzteVerkaeufe.Count > 3)
                soll += await ErstattungAsync(l, sitzung, kassierer, z, soll);
        }

        if (geparkt is Guid rest && z.Prozent(30))
        {
            await SollAsync("Ticket löschen", () => d.Hole<TicketParkService>().LoescheAsync(kassierer.Id, rest));
            Zaehle("Tickets en attente gelöscht");
        }

        // Patienten (Aufenthalte) im Cabinet: Leistungen erfassen ohne zu kassieren, nach einigen Tagen bezahlen.
        if (l.V.Aufenthalt) soll += await AufenthalteAsync(l, sitzung, kassierer, z);

        // Geldbewegungen in der Kasse.
        if (z.Prozent(3) && soll - startbestand > 20000)
        {
            var betrag = Math.Floor((soll - startbestand) / 2 / 1000) * 1000;
            var r = await d.Hole<BestandsService>().EntnehmeBargeldAsync(kassierer.Id, sitzung, l.BesitzerPin, betrag, "Dépôt à la banque");
            if (r.Erfolg) { soll -= betrag; Zaehle("Retraits (Entnahmen)"); } else Befund($"{l.Name}: Retrait: {r.Fehler}");
        }
        if (z.Prozent(2))
        {
            var r = await d.Hole<BestandsService>().ErfasseEinlageAsync(l.Besitzer, l.Id, 10000, GeldOrt.Kasse, "Monnaie de la banque", null);
            if (r.Erfolg) { soll += 10000; Zaehle("Apports en caisse"); } else Befund($"{l.Name}: Apport: {r.Fehler}");
        }
        if (z.Prozent(1.5)) await LagerEntnahmeAsync(l, sitzung, kassierer, z);

        // Abschluss: meist exakt, manchmal mit Écart.
        var feierabend = tag.ToDateTime(new TimeOnly(19, 30)) - l.Versatz;
        Uhr.SetzeLokal(Uhr.Jetzt > feierabend ? Uhr.Jetzt.AddMinutes(5) : feierabend);
        var gezaehlt = soll + (z.Prozent(8) ? (z.Prozent(50) ? 1 : -1) * 100 * z.Zahl(1, 20) : 0);
        var ab = await kassen.SchliesseSitzungAsync(sitzung, gezaehlt, gezaehlt == soll ? null : "Écart constaté au comptage");
        if (!ab.Erfolg) { Befund($"{l.Name}: Abschluss: {ab.Fehler}"); return; }
        PruefeSoll(l, ab.Sitzung!, soll);
        if (gezaehlt != soll) l.Differenzen++;

        // Ausgaben, Lager, Stammdaten – wie im Alltag des Besitzers.
        if (tag.DayOfWeek == DayOfWeek.Saturday || z.Prozent(5)) await AusgabeAsync(l, z);
        await LagerPflegeAsync(l, z);
        if (tag.Day == 15 && z.Prozent(40)) await PreisAenderungMitLagerAsync(l, sitzung, z);
    }

    private void PruefeSoll(SimLaden l, KassenSitzung s, decimal erwartet)
    {
        if (s.BareinnahmenSoll != erwartet)
            Befund($"{l.Name}: Kassen-Soll {s.BareinnahmenSoll:N0} ≠ erwartet {erwartet:N0} (Sitzung {s.Id}).");
        else Zaehle("Kassenabschlüsse geprüft (Soll = Bewegungen)");
    }

    private static List<(SimArtikel Artikel, int Menge, SimRabatt? Rabatt)> Zeilen(SimLaden l, Zufall z)
    {
        var auswahl = l.Katalog.Where(a => a.Verkaufbar).ToList();
        var favoriten = auswahl.Where(a => a.Favorit).ToList();
        var anzahl = z.Prozent(55) ? 1 : z.Prozent(70) ? 2 : 3;
        var zeilen = new List<(SimArtikel, int, SimRabatt?)>();
        for (var i = 0; i < anzahl; i++)
        {
            var a = favoriten.Count > 0 && z.Prozent(45) ? z.Wahl(favoriten) : z.Wahl(auswahl);
            if (zeilen.Any(x => x.Item1.Id == a.Id)) continue;
            zeilen.Add((a, a.Produkt || a.BasisId is not null ? z.Zahl(1, 4) : 1, null));
        }
        return zeilen;
    }

    private async Task<Guid?> KundeAsync(SimLaden l, SimMitarbeiter m, Zufall z)
    {
        var bekannte = l.Kunden.Where(k => !l.Patienten.ContainsKey(k)).ToList();
        if (bekannte.Count > 8 && z.Prozent(80)) return z.Wahl(bekannte);
        var mitTelefon = z.Prozent(55);
        var name = $"{z.Wahl(SimNamen.Vornamen)} {z.Wahl(SimNamen.Nachnamen)}";
        var nr = Interlocked.Increment(ref l.Telefon);
        var k = await SollAsync("Kunde anlegen", () => d.Hole<KundenService>().LegeAnAsync(m.Id, l.Id, name, mitTelefon ? SimNamen.Telefon(l.Nr, nr) : null, mitTelefon));
        if (k is null) return null;
        l.Kunden.Add(k.Id);
        Zaehle("Kunden angelegt");
        return k.Id;
    }

    private async Task<Guid?> AnkunftAsync(SimLaden l, SimMitarbeiter m, Guid kunde, Zufall z)
    {
        var motiv = l.V.Motive.Length > 0 ? z.Wahl(l.V.Motive) : "Passage";
        var raum = l.V.Raeume.Length > 0 && z.Prozent(70) ? z.Wahl(l.V.Raeume) : null;
        try
        {
            var id = await d.Hole<KundenBesuchService>().AnkunftAsync(m.Id, l.Id, kunde, motiv, raum);
            Zaehle("Arrivées (Clients présents)");
            return id;
        }
        catch (InvalidOperationException) { Zaehle("Ankunft abgelehnt: bereits anwesend (erwartet)"); return null; }
        catch (ArgumentException) { Zaehle("Ankunft abgelehnt: bereits anwesend (erwartet)"); return null; }
    }

    /// <summary>Bucht einen Verkauf und prüft den Betrag gegen die eigene Rechnung. Liefert den Bar-Anteil (für das Soll).</summary>
    private async Task<decimal?> VerkaufAsync(SimLaden l, Guid sitzung, List<(SimArtikel Artikel, int Menge, SimRabatt? Rabatt)> zeilen, Guid? kunde, Zufall z,
        IReadOnlyList<AufenthaltService.Zeile>? aufenthalt = null)
    {
        if (zeilen.Count == 0 && (aufenthalt is null || aufenthalt.Count == 0)) return 0;
        var positionen = new List<VerkaufsPositionInput>();
        decimal erwartet = 0;
        SimRabatt? rabattGenutzt = null;
        foreach (var (a, menge, _) in zeilen)
        {
            SimRabatt? r = null;
            if (rabattGenutzt is null && l.Rabatte.Count > 0 && z.Prozent(9))
            {
                r = z.Wahl(l.Rabatte);
                if (r.LeistungId is Guid nur && nur != a.Id) r = null;
            }
            var betrag = a.Preis * menge;
            var nachlass = r is null ? 0 : betrag - KassenRechner.WendeRabattAn(betrag, r.Art, r.Wert);
            erwartet += betrag - nachlass;
            rabattGenutzt ??= r;
            positionen.Add(new VerkaufsPositionInput(a.Id, menge, r?.Id, r?.Bedingung == true));
        }
        if (z.Prozent(0.7))
        {
            var frei = 500m * z.Zahl(1, 10);
            positionen.Add(new VerkaufsPositionInput(l.DiversId, 1, null, false, frei));
            erwartet += frei;
            Zaehle("Divers (montant libre)");
        }
        foreach (var e in aufenthalt ?? [])
        {
            positionen.Add(new VerkaufsPositionInput(e.LeistungId ?? Guid.Empty, e.Menge, null, false, null, e.Id));
            erwartet += e.Summe;
        }
        erwartet = Math.Round(erwartet, 2);

        var anbieter = await AnbieterAsync(l);
        var p = z.Kommazahl() * 100;
        List<ZahlungInput> zahlungen;
        string art;
        if (p < 48 || erwartet <= 0) { zahlungen = [new(ZahlungsArt.Bar, Gegeben: z.Prozent(40) ? erwartet : Math.Ceiling(erwartet / 1000) * 1000)]; art = "Bar"; }
        else if (p < 78 && anbieter.Count > 0) { zahlungen = [new(ZahlungsArt.MobileMoney, AnbieterId: z.Wahl(anbieter), Referenz: $"TX{z.Zahl(100000, 999999)}")]; art = "Mobile Money"; }
        else if (p < 87) { zahlungen = [new(ZahlungsArt.Karte)]; art = "Karte"; }
        else if (anbieter.Count > 0 && erwartet >= 1000)
        {
            var mm = Math.Floor(erwartet * 0.4m / 100) * 100;
            zahlungen = [new(ZahlungsArt.MobileMoney, mm, AnbieterId: z.Wahl(anbieter)), new(ZahlungsArt.Bar, Gegeben: Math.Ceiling(erwartet / 500) * 500)];
            art = "Gemischt";
        }
        else { zahlungen = [new(ZahlungsArt.Karte, erwartet / 2), new(ZahlungsArt.Bar, Gegeben: erwartet)]; art = "Gemischt"; }

        var r2 = await d.Hole<KassenService>().BucheVerkaufAsync(sitzung, positionen, zahlungen, default, kunde);
        if (!r2.Erfolg)
        {
            if (r2.Fehler?.Contains("suspendue", StringComparison.Ordinal) == true) { Zaehle("Verkauf gesperrt (Zahlungsverzug)"); return null; }
            Befund($"{l.Name}: Verkauf ({art}, {positionen.Count} Zeilen, {erwartet:N0} F): {r2.Fehler}");
            return null;
        }
        var v = r2.Verkauf!;
        if (v.GesamtSumme != erwartet) Befund($"{l.Name}: Verkauf {v.Id}: Summe {v.GesamtSumme:N2} ≠ erwartet {erwartet:N2}.");
        if (v.Zahlungen.Sum(x => x.Betrag) != v.GesamtSumme) Befund($"{l.Name}: Verkauf {v.Id}: Zahlungsanteile ≠ Summe.");
        Zaehle($"Verkäufe {art}");
        if (rabattGenutzt is not null) Zaehle("Verkäufe mit Remise");
        if (kunde is not null) Zaehle("Verkäufe mit Kunde");
        Buche(l, v, kunde);
        foreach (var (a, menge, _) in zeilen)
            if (a.BasisId is Guid b) l.Lager[b] -= menge * a.Abzug;
            else if (a.Produkt) l.Lager[a.Id] -= menge;
        lock (l.LetzteVerkaeufe)
        {
            l.LetzteVerkaeufe.Add(new SimVerkauf(v.Id, v.ZeitUtc, kunde));
            if (l.LetzteVerkaeufe.Count > 40) l.LetzteVerkaeufe.RemoveAt(0);
        }
        return v.Zahlungen.Where(x => x.Art == ZahlungsArt.Bar).Sum(x => x.Betrag);
    }

    private static void Buche(SimLaden l, Verkauf v, Guid? kunde)
    {
        if (v.StornoVonVerkaufId is null) l.Verkaeufe++;
        l.Umsatz += v.GesamtSumme;
        var monat = (v.ZeitUtc.Year, v.ZeitUtc.Month);
        l.MonatsUmsatz[monat] = l.MonatsUmsatz.GetValueOrDefault(monat) + v.GesamtSumme;
        if (kunde is Guid k) l.KundenUmsatz[k] = l.KundenUmsatz.GetValueOrDefault(k) + v.GesamtSumme;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, List<Guid>> anbieterCache = new();

    private async Task<List<Guid>> AnbieterAsync(SimLaden l)
    {
        if (anbieterCache.TryGetValue(l.Id, out var liste)) return liste;
        liste = (await d.Hole<MobileMoneyService>().GetErlaubteAnbieterAsync(l.Id)).Select(a => a.Id).ToList();
        anbieterCache[l.Id] = liste;
        return liste;
    }

    private async Task<decimal> ErstattungAsync(SimLaden l, Guid sitzung, SimMitarbeiter m, Zufall z, decimal soll)
    {
        SimVerkauf original;
        lock (l.LetzteVerkaeufe) original = z.Wahl(l.LetzteVerkaeufe);
        var storno = d.Hole<StornoService>();
        var vorlage = await storno.GetVorlageAsync(original.Id, sitzung, m.Id);
        if (vorlage is null) { Befund($"{l.Name}: Vorlage zur Erstattung fehlt ({original.Id})."); return 0; }
        var offen = vorlage.Positionen.Where(p => p.Offen > 0 && p.ZeilenSumme - p.ErstattetBetrag > 0).ToList(); // 0-F-Zeilen (offert) zeigt die Kasse ohne Betrag
        var wege = vorlage.Wege.Where(w => w.Offen > 0 && (w.Art != ZahlungsArt.Bar || w.Offen <= soll)).ToList();
        if (offen.Count == 0 || wege.Count == 0) return 0;
        var pos = z.Wahl(offen);
        var menge = z.Prozent(50) ? pos.Offen : 1;
        var weg = z.Wahl(wege);
        var insLager = pos.HatLager && z.Prozent(70);
        var r = await storno.ErstatteAsync(m.Id, sitzung, original.Id, [new StornoService.StornoPositionInput(pos.PositionId, menge)], weg.Schluessel,
            insLager, z.Wahl(StornoService.GrundVorschlaege), vorlage.BrauchtPin ? l.BesitzerPin : null);
        if (!r.Erfolg)
        {
            if (r.Fehler?.Contains("espèces", StringComparison.OrdinalIgnoreCase) == true) { Zaehle("Erstattung bar abgelehnt (zu wenig Bargeld, erwartet)"); return 0; }
            if (r.Fehler?.Contains("2e remboursement", StringComparison.Ordinal) == true) { Zaehle("Erstattung auf den Rest eines Zahlwegs begrenzt (Regel, erwartet)"); return 0; }
            Befund($"{l.Name}: Erstattung: {r.Fehler}");
            return 0;
        }
        l.Erstattungen++;
        l.Erstattet += r.Betrag;
        Buche(l, r.Storno!, original.KundeId);
        Zaehle(vorlage.BrauchtPin ? "Erstattungen mit Besitzer-PIN" : "Erstattungen ohne PIN (Recht « rembourser »)");
        if (insLager)
        {
            var leistung = await d.MitDbAsync(db => db.VerkaufsPositionen.Where(p => p.Id == pos.PositionId).Select(p => p.LeistungId).SingleAsync());
            var a = l.Katalog.First(x => x.Id == leistung);
            if (a.BasisId is Guid b) l.Lager[b] += menge * a.Abzug;
            else if (l.Lager.ContainsKey(a.Id)) l.Lager[a.Id] += menge;
            Zaehle("Erstattungen mit Rückgabe ins Lager");
        }
        return r.Storno!.Zahlungen.Where(x => x.Art == ZahlungsArt.Bar).Sum(x => x.Betrag);
    }

    private async Task<decimal> AufenthalteAsync(SimLaden l, Guid sitzung, SimMitarbeiter m, Zufall z)
    {
        decimal bar = 0;
        var aufenthalt = d.Hole<AufenthaltService>();
        // Neue Patientin/neuer Patient.
        if (l.Patienten.Count < 4 && z.Prozent(35))
        {
            var kunde = await KundeAsync(l, m, z);
            if (kunde is Guid k && !l.Patienten.ContainsKey(k))
            {
                try
                {
                    var b = await d.Hole<KundenBesuchService>().AnkunftAsync(m.Id, l.Id, k, "Journée d'hospitalisation", z.Wahl(l.V.Raeume.Skip(1).ToList()));
                    l.Patienten[k] = (b, 2 + z.Zahl(4));
                    Zaehle("Aufenthalte begonnen");
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { Zaehle("Ankunft abgelehnt: bereits anwesend (erwartet)"); }
            }
        }
        foreach (var (kunde, (besuch, tage)) in l.Patienten.ToList())
        {
            if (tage > 0)
            {
                // Leistungen des Tages fest erfassen (Lagerabgang sofort).
                var auswahl = l.Katalog.Where(a => a.Name is "Journée d'hospitalisation" or "Perfusion" or "Soluté 500 ml" or "Injection" or "Kit de perfusion").ToList();
                var zeilen = auswahl.Where(_ => z.Prozent(55)).Select(a => new AufenthaltService.PositionInput(a.Id, a.Produkt ? z.Zahl(1, 3) : 1)).ToList();
                if (zeilen.Count == 0) zeilen.Add(new AufenthaltService.PositionInput(auswahl[0].Id, 1));
                var r = await SollAsync("Aufenthalt erfassen", () => aufenthalt.ErfasseAsync(m.Id, l.Id, kunde, zeilen));
                if (r is not null)
                {
                    Zaehle("Prestations enregistrées sans encaisser", r.Zeilen);
                    foreach (var zeile in zeilen)
                        if (l.Lager.ContainsKey(zeile.LeistungId)) l.Lager[zeile.LeistungId] -= zeile.Menge;
                }
                // Gelegentlich eine Zeile zurücknehmen (Erreur de saisie), Ware zurück ins Lager.
                if (z.Prozent(10))
                {
                    var konto = await aufenthalt.GetKontoAsync(m.Id, l.Id, kunde);
                    var zeile = konto.Zeilen.LastOrDefault(x => x.Offen);
                    if (zeile is not null)
                    {
                        var ok = await SollAsync("Zurücknehmen", async () =>
                        {
                            await aufenthalt.NimmZurueckAsync(m.Id, zeile.Id, "Erreur de saisie", true, zeile.DarfSofortZuruecknehmen ? null : l.BesitzerPin);
                            return true;
                        });
                        if (ok && zeile.HatLager && zeile.LeistungId is Guid lid && l.Lager.ContainsKey(lid)) l.Lager[lid] += zeile.Menge;
                        if (ok) Zaehle("Aufenthalt: Zeile zurückgenommen");
                    }
                }
                l.Patienten[kunde] = (besuch, tage - 1);
                continue;
            }

            // Entlassung: alle offenen Zeilen + Abschlusskonsultation kassieren.
            var offen = (await aufenthalt.GetKontoAsync(m.Id, l.Id, kunde)).Zeilen.Where(x => x.Offen).ToList();
            var abschluss = l.Katalog.First(a => a.Name == "Consultation de suivi");
            var b2 = await VerkaufAsync(l, sitzung, [(abschluss, 1, null)], kunde, z, offen);
            if (b2 is decimal betrag)
            {
                bar += betrag;
                l.Patienten.Remove(kunde);
                Zaehle("Aufenthalte bezahlt (« À la caisse »)");
                var noch = await d.MitDbAsync(db => db.KundenBesuche.AnyAsync(b => b.Id == besuch && b.EndeUtc == null));
                Pruefe(!noch, "Besuch endet mit dem Encaissement");
            }
        }
        return bar;
    }

    private async Task LagerEntnahmeAsync(SimLaden l, Guid sitzung, SimMitarbeiter m, Zufall z)
    {
        var produkte = l.Katalog.Where(a => a.Produkt && l.Lager[a.Id] > 5).ToList();
        if (produkte.Count == 0) return;
        var a = z.Wahl(produkte);
        var r = await d.Hole<BestandsService>().EntnehmeWareAsync(m.Id, sitzung, l.BesitzerPin, a.Id, 1, "Usage personnel");
        if (r.Erfolg) { l.Lager[a.Id] -= 1; Zaehle("Entnahmen Ware"); } else Befund($"{l.Name}: Entnahme Ware: {r.Fehler}");
    }

    private async Task AusgabeAsync(SimLaden l, Zufall z)
    {
        if (l.AusgabenKategorien.Count == 0) return;
        (string, string, Stream)? beleg = z.Prozent(10) ? ("recu.pdf", "application/pdf", new MemoryStream("%PDF-1.4 sim"u8.ToArray())) : null;
        var r = await d.Hole<AusgabenService>().ErfasseAusgabeAsync(l.Id, z.Wahl(l.AusgabenKategorien), 1000m * z.Zahl(2, 40),
            Uhr.Jetzt.Date, "Dépense courante", l.Besitzer, beleg);
        if (r.Erfolg) Zaehle(beleg is null ? "Dépenses" : "Dépenses avec justificatif"); else Befund($"{l.Name}: Ausgabe: {r.Fehler}");
    }

    /// <summary>Nachbestellen unter Mindestbestand (Formular: Bestand + Lieferung), gelegentlich Abschreibung.</summary>
    private async Task LagerPflegeAsync(SimLaden l, Zufall z)
    {
        var katalog = d.Hole<KatalogService>();
        foreach (var a in l.Katalog.Where(a => a.Produkt && l.Lager[a.Id] < 15).ToList())
        {
            var leistung = await katalog.GetLeistungAsync(a.Id);
            var beimOeffnen = leistung?.Stueckzahl;
            if (leistung is null) { Befund($"{l.Name}: {a.Name} fehlt."); continue; }
            var lieferung = 20m * z.Zahl(2, 8);
            leistung.Stueckzahl += lieferung;
            await SollAsync("Nachbestellen", () => katalog.SpeichereLeistungAsync(leistung, default, beimOeffnen));
            l.Lager[a.Id] += lieferung;
            Zaehle("Réassorts (Lager)");
        }
        if (z.Prozent(2))
        {
            var produkte = l.Katalog.Where(a => a.Produkt && l.Lager[a.Id] > 3).ToList();
            if (produkte.Count == 0) return;
            var a = z.Wahl(produkte);
            var r = await d.Hole<BestandsService>().SchreibeAbAsync(l.Besitzer, a.Id, 1, z.Prozent(50) ? AbschreibungsGrund.Casse : AbschreibungsGrund.Peremption, null);
            if (r.Erfolg) { l.Lager[a.Id] -= 1; Zaehle("Abschreibungen"); } else Befund($"{l.Name}: Abschreibung: {r.Fehler}");
        }
    }

    /// <summary>
    /// Alltag: Der Besitzer öffnet « Modifier » eines Produkts (Formular mit dem Bestand von jetzt), währenddessen verkauft
    /// die Kasse dasselbe Produkt; dann speichert er nur einen neuen Preis.
    /// </summary>
    private async Task PreisAenderungMitLagerAsync(SimLaden l, Guid geschlosseneSitzung, Zufall z)
    {
        var a = l.Katalog.FirstOrDefault(x => x.Produkt && x.Verkaufbar && l.Lager[x.Id] > 10);
        if (a is null) return;
        var katalog = d.Hole<KatalogService>();
        var formular = await katalog.GetLeistungAsync(a.Id);
        var formularBestand = formular?.Stueckzahl;
        if (formular is null) return;
        var kassen = d.Hole<KassenService>();
        var kassierer = z.Wahl(l.Team);
        var s = await kassen.StarteSitzungAsync(l.Id, kassierer.Id, 0);
        if (!s.Erfolg) return;
        var bar = await VerkaufAsync(l, s.Sitzung!.Id, [(a, 2, null)], null, z) ?? 0;
        var zu = await kassen.SchliesseSitzungAsync(s.Sitzung.Id, bar, null);
        if (zu.Erfolg) PruefeSoll(l, zu.Sitzung!, bar);
        formular.Preis = Math.Round(a.Preis * 1.1m / 50) * 50;
        await SollAsync("Preis ändern", () => katalog.SpeichereLeistungAsync(formular, default, formularBestand));
        a.Preis = formular.Preis;
        Zaehle("Preisänderungen während des Kassenbetriebs");
    }
}
