using ENote.Core.DataModels.Finanzen;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Kasse;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Data.Seed;

/// <summary>
/// Nur für Development: legt einen vollständig befüllten Demo-Laden an (Besitzer, Kassierer,
/// Katalog mit Kategorien, Rabatte, Ausgaben und mehrere Wochen Verkaufshistorie), damit
/// Design und Abläufe mit realistischen Daten geprüft werden können. Ein vorhandener
/// Demo-Laden wird vorher vollständig entfernt (wiederholbar, deterministisch).
/// Aufruf: dotnet run -- --database-admin=demo --database-target=localhost/ENOTEDB
/// </summary>
public static class DemoDatenSeeder
{
    public const string LadenName = "Salon Élégance (Démo)";
    public const string BesitzerPseudonym = "demo.proprio";
    public const string Passwort = "demo2026";
    public const string BesitzerPin = "1234";
    private const int TageHistorie = 42;

    private static readonly string[] KassiererPseudonyme = ["demo.awa", "demo.kofi"];

    public static async Task SeedAsync(ApplicationDbContext db, UserManager<AppUser> userManager)
    {
        await EntferneVorhandeneDemoAsync(db, userManager);

        var jetzt = DateTime.UtcNow;
        var zufall = new Random(2026);

        // --- Konten ---
        var besitzer = await LegeKontoAnAsync(userManager, BesitzerPseudonym, "Ama", "Mensah", AppRollen.Besitzer);
        var awa = await LegeKontoAnAsync(userManager, KassiererPseudonyme[0], "Awa", "Koffi", AppRollen.Kassierer);
        var kofi = await LegeKontoAnAsync(userManager, KassiererPseudonyme[1], "Kofi", "Agbeko", AppRollen.Kassierer);

        // --- Laden ---
        var salon = new Salon
        {
            Name = LadenName,
            Standort = "Lomé, Tokoin",
            Land = "Togo",
            Adresse = "Boulevard du 13 Janvier, Lomé",
            TelefonNummer = "+228 90 00 00 00",
            Slogan = "Votre beauté, notre passion",
            IstAktiv = true,
            IstFreigegeben = true,
            ProbeBisUtc = jetzt.AddMonths(2),
            CreatedAtUtc = jetzt.AddDays(-TageHistorie - 5)
        };
        db.Salons.Add(salon);
        db.UserSalons.AddRange(
            new UserSalon
            {
                AppUserId = besitzer.Id, SalonId = salon.Id,
                BesitzerPinHash = KassenPinService.HashPin(BesitzerPin),
                KassenPinHash = KassenPinService.HashPin("1000"),
                KannFinanzenSehen = true, KannLeistungenVerwalten = true, KannRabatteVerwalten = true
            },
            new UserSalon { AppUserId = awa.Id, SalonId = salon.Id, KassenPinHash = KassenPinService.HashPin("2222") },
            new UserSalon { AppUserId = kofi.Id, SalonId = salon.Id, KassenPinHash = KassenPinService.HashPin("3333") });

        // --- Kategorien (zweistufig) ---
        LeistungKategorie Kat(string name, LeistungKategorie? parent = null)
        {
            var k = new LeistungKategorie { SalonId = salon.Id, Name = name, ParentId = parent?.Id };
            db.LeistungKategorien.Add(k);
            return k;
        }
        var coiffure = Kat("Coiffure");
        var femmes = Kat("Femmes", coiffure);
        var hommes = Kat("Hommes", coiffure);
        var beaute = Kat("Soins & beauté");
        var ongles = Kat("Ongles", beaute);
        var produits = Kat("Produits");
        var boissons = Kat("Boissons");

        // --- Katalog ---
        var katalog = new List<Leistung>();
        Leistung L(string name, decimal preis, LeistungKategorie kat, LeistungsTyp typ = LeistungsTyp.Prestation,
            decimal? stock = null, decimal? min = null, decimal? einkauf = null, string? barcode = null, string? beschreibung = null)
        {
            var l = new Leistung
            {
                SalonId = salon.Id, Name = name, Preis = preis, KategorieId = kat.Id, Typ = typ,
                HatStueckzahl = stock is not null, Stueckzahl = stock ?? 0, MindestbestandWarnung = min,
                Kaufpreis = einkauf, StrichCode = barcode, Beschreibung = beschreibung,
                CreatedAtUtc = salon.CreatedAtUtc
            };
            katalog.Add(l);
            return l;
        }
        L("Tresses africaines", 15000, femmes, beschreibung: "Tresses classiques, mèches non comprises");
        L("Box braids", 20000, femmes);
        L("Brushing", 3000, femmes);
        L("Mise en plis", 4000, femmes);
        L("Défrisage", 6000, femmes);
        L("Coloration", 8000, femmes);
        L("Shampoing + soin", 2500, femmes);
        L("Coupe homme", 1500, hommes);
        L("Coupe + barbe", 2500, hommes);
        L("Taille de barbe", 1000, hommes);
        L("Dégradé", 2000, hommes);
        L("Manucure", 3500, ongles);
        L("Pédicure", 4500, ongles);
        L("Pose vernis semi-permanent", 5000, ongles);
        L("Soin du visage", 7000, beaute);
        L("Épilation sourcils", 1000, beaute);
        L("Mèches (paquet)", 2500, produits, LeistungsTyp.Produit, 40, 10, 1500, "6111000000017");
        L("Huile de karité 250 ml", 3000, produits, LeistungsTyp.Produit, 18, 5, 1800, "6111000000024");
        L("Shampoing bio 500 ml", 4000, produits, LeistungsTyp.Produit, 4, 5, 2500, "6111000000031");
        L("Crème coiffante", 3500, produits, LeistungsTyp.Produit, 12, 4, 2000, "6111000000048");
        L("Eau minérale", 300, boissons, LeistungsTyp.Produit, 48, 12, 150, "6111000000055");
        L("Jus de bissap", 500, boissons, LeistungsTyp.Produit, 3, 6, 250, "6111000000062");
        L("Café", 500, boissons, LeistungsTyp.Produit);
        db.Leistungen.AddRange(katalog);

        // --- Rabatte ---
        var fidelite = new Rabatt { SalonId = salon.Id, Name = "Fidélité -10 %", Art = RabattArt.Prozent, Wert = 10, IstAktiv = true };
        var etudiant = new Rabatt { SalonId = salon.Id, Name = "Étudiant -500 F", Art = RabattArt.Festbetrag, Wert = 500, IstAktiv = true, Bedingung = "Carte étudiant présentée" };
        var tresses = new Rabatt { SalonId = salon.Id, Name = "Promo tresses -2000 F", Art = RabattArt.Festbetrag, Wert = 2000, IstAktiv = false, LeistungId = katalog[0].Id };
        db.Rabatte.AddRange(fidelite, etudiant, tresses);

        // --- Ausgaben ---
        var ausgabenKats = new[] { "Loyer", "Produits & fournitures", "Électricité & eau", "Salaires", "Divers" }
            .Select(n => new AusgabenKategorie { SalonId = salon.Id, Name = n })
            .ToDictionary(k => k.Name);
        db.AusgabenKategorien.AddRange(ausgabenKats.Values);

        void Ausgabe(int tageZurueck, string kat, decimal betrag, string? notiz) => db.Ausgaben.Add(new Ausgabe
        {
            SalonId = salon.Id, KategorieId = ausgabenKats[kat].Id, Betrag = betrag, Notiz = notiz,
            DatumUtc = jetzt.Date.AddDays(-tageZurueck).AddHours(10), ErfasstVonId = besitzer.Id,
            CreatedAtUtc = jetzt.Date.AddDays(-tageZurueck).AddHours(10)
        });
        foreach (var monatTage in new[] { 40, 10 })
        {
            Ausgabe(monatTage, "Loyer", 75000, "Loyer mensuel");
            Ausgabe(monatTage - 2, "Électricité & eau", 18500 + zufall.Next(0, 6) * 1000, "Facture CEET / TdE");
            Ausgabe(monatTage - 1, "Salaires", 120000, "Salaires équipe");
        }
        for (var t = TageHistorie; t > 0; t -= zufall.Next(4, 9))
            Ausgabe(t, "Produits & fournitures", 8000 + zufall.Next(0, 15) * 1000, "Réassort produits");
        Ausgabe(3, "Divers", 4500, "Nettoyage");
        Ausgabe(16, "Divers", 2500, "Transport");

        // --- Kassensitzungen + Verkäufe ---
        var flooz = await db.MobileMoneyAnbieter.FirstAsync(a => a.Land == "Togo" && a.Name == "Flooz");
        var mixx = await db.MobileMoneyAnbieter.FirstAsync(a => a.Land == "Togo" && a.Name == "Mixx by Yas");
        var team = new[] { (Konto: besitzer, Name: besitzer.FullName), (Konto: awa, Name: awa.FullName), (Konto: kofi, Name: kofi.FullName) };
        var gewichtet = katalog.SelectMany(l => Enumerable.Repeat(l, l.Preis switch { <= 600 => 3, <= 3000 => 5, <= 8000 => 3, _ => 1 })).ToList();

        for (var tag = TageHistorie; tag >= 1; tag--)
        {
            var datum = jetzt.Date.AddDays(-tag);
            if (datum.DayOfWeek == DayOfWeek.Sunday) continue;

            var kassierer = team[zufall.Next(team.Length)];
            var start = datum.AddHours(8).AddMinutes(zufall.Next(0, 30));
            var sitzung = new KassenSitzung
            {
                SalonId = salon.Id, KassiererId = kassierer.Konto.Id, GestartetUtc = start,
                Startbestand = 20000, Status = KassenSitzungStatus.Abgeschlossen
            };
            db.KassenSitzungen.Add(sitzung);

            // Samstag deutlich mehr Betrieb
            var anzahl = datum.DayOfWeek == DayOfWeek.Saturday ? zufall.Next(14, 22) : zufall.Next(6, 14);
            decimal bar = 0;
            var zeit = start.AddMinutes(20);
            for (var v = 0; v < anzahl; v++)
            {
                zeit = zeit.AddMinutes(zufall.Next(15, 45));
                var verkauf = new Verkauf { KassenSitzungId = sitzung.Id, SalonId = salon.Id, ZeitUtc = zeit };
                var anzahlPos = zufall.Next(1, 4);
                foreach (var leistung in Enumerable.Range(0, anzahlPos).Select(_ => gewichtet[zufall.Next(gewichtet.Count)]).Distinct())
                {
                    var menge = leistung.Typ == LeistungsTyp.Produit ? zufall.Next(1, 3) : 1;
                    var brutto = leistung.Preis * menge;
                    Rabatt? rabatt = zufall.NextDouble() < 0.08 ? fidelite : null;
                    var rabattBetrag = rabatt is null ? 0 : Math.Round(brutto * 0.10m);
                    verkauf.Positionen.Add(new VerkaufsPosition
                    {
                        VerkaufId = verkauf.Id, LeistungId = leistung.Id, LeistungName = leistung.Name,
                        EinzelPreis = leistung.Preis, Menge = menge, RabattId = rabatt?.Id,
                        RabattBetrag = rabattBetrag, ZeilenSumme = brutto - rabattBetrag
                    });
                }
                verkauf.Zwischensumme = verkauf.Positionen.Sum(p => p.EinzelPreis * p.Menge);
                verkauf.RabattSumme = verkauf.Positionen.Sum(p => p.RabattBetrag);
                verkauf.GesamtSumme = verkauf.Zwischensumme - verkauf.RabattSumme;

                // Zahlungsmix wie im Alltag: überwiegend bar, viel Mobile Money, wenig Karte, teils geteilt.
                var wurf = zufall.NextDouble();
                var momo = zufall.NextDouble() < 0.65 ? flooz : mixx;
                void Zahlung(ZahlungsArt art, decimal betrag, MobileMoneyAnbieter? anbieter = null) => verkauf.Zahlungen.Add(new VerkaufZahlung
                {
                    VerkaufId = verkauf.Id, Art = art, Betrag = betrag, Reihenfolge = verkauf.Zahlungen.Count,
                    MobileMoneyAnbieterId = anbieter?.Id, AnbieterName = anbieter?.Name,
                    Referenz = anbieter is null ? null : $"{(anbieter == flooz ? "MP" : "TM")}{zufall.Next(100000, 999999)}"
                });
                void Bar(decimal betrag)
                {
                    var gegeben = Math.Ceiling(betrag / 1000m) * 1000m;
                    verkauf.Gegeben = gegeben;
                    verkauf.Rueckgeld = gegeben - betrag;
                    bar += betrag;
                    Zahlung(ZahlungsArt.Bar, betrag);
                }

                if (wurf < 0.55) Bar(verkauf.GesamtSumme);
                else if (wurf < 0.85) Zahlung(ZahlungsArt.MobileMoney, verkauf.GesamtSumme, momo);
                else if (wurf < 0.95 || verkauf.GesamtSumme < 2000) { verkauf.KarteZahlungBestaetigt = true; Zahlung(ZahlungsArt.Karte, verkauf.GesamtSumme); }
                else
                {
                    var baranteil = Math.Floor(verkauf.GesamtSumme / 2000m) * 1000m;
                    Bar(baranteil);
                    Zahlung(ZahlungsArt.MobileMoney, verkauf.GesamtSumme - baranteil, momo);
                }
                verkauf.ZahlungsArt = KassenRechner.Zusammenfassen(verkauf.Zahlungen.Select(z => z.Art));
                db.Verkaeufe.Add(verkauf);
            }

            var soll = sitzung.Startbestand + bar;
            var differenz = zufall.NextDouble() < 0.15 ? -zufall.Next(1, 6) * 100m : 0m;
            sitzung.AbgeschlossenUtc = zeit.AddMinutes(30);
            sitzung.BareinnahmenSoll = soll;
            sitzung.EndbestandGezaehlt = soll + differenz;
            sitzung.Differenz = differenz;
            sitzung.AbschlussNotiz = differenz < 0 ? "Petit écart de monnaie" : null;
        }

        // --- Hinweise für die Startseite ---
        db.BesitzerHinweise.AddRange(
            new BesitzerHinweis { SalonId = salon.Id, Text = "Stock bas : « Shampoing bio 500 ml » (4 restants).", AusgeloestVonId = awa.Id, AusgeloestVonName = awa.FullName, CreatedAtUtc = jetzt.AddHours(-5) },
            new BesitzerHinweis { SalonId = salon.Id, Text = "Écart de caisse de -300 F lors de la dernière clôture.", AusgeloestVonId = kofi.Id, AusgeloestVonName = kofi.FullName, CreatedAtUtc = jetzt.AddDays(-1) });

        await db.SaveChangesAsync();

        Console.WriteLine($"Demo-Laden « {LadenName} » angelegt.");
        Console.WriteLine($"  Besitzer:   {BesitzerPseudonym} / {Passwort}  (Besitzer-PIN {BesitzerPin}, Kassen-PIN 1000)");
        Console.WriteLine($"  Kassierer:  {KassiererPseudonyme[0]} / {Passwort} (PIN 2222), {KassiererPseudonyme[1]} / {Passwort} (PIN 3333)");
    }

    private static async Task<AppUser> LegeKontoAnAsync(UserManager<AppUser> userManager, string pseudonym, string vorname, string nachname, string rolle)
    {
        var konto = new AppUser
        {
            UserName = pseudonym, Email = $"{pseudonym}@demo.invalid", Vorname = vorname, Nachname = nachname,
            FullName = $"{vorname} {nachname}", EmailConfirmed = true, IsSelfRegistered = rolle == AppRollen.Besitzer
        };
        var ergebnis = await userManager.CreateAsync(konto, Passwort);
        if (!ergebnis.Succeeded)
            throw new InvalidOperationException($"Demo-Konto {pseudonym}: {string.Join(" ", ergebnis.Errors.Select(e => e.Description))}");
        await userManager.AddToRoleAsync(konto, rolle);
        return konto;
    }

    private static async Task EntferneVorhandeneDemoAsync(ApplicationDbContext db, UserManager<AppUser> userManager)
    {
        var salonIds = await db.Salons.Where(s => s.Name == LadenName).Select(s => s.Id).ToListAsync();
        foreach (var id in salonIds)
        {
            await db.VerkaufsPositionen.Where(p => p.Verkauf.SalonId == id).ExecuteDeleteAsync();
            await db.VerkaufZahlungen.Where(z => z.Verkauf.SalonId == id).ExecuteDeleteAsync();
            await db.SalonMobileMoneyAnbieter.Where(x => x.SalonId == id).ExecuteDeleteAsync();
            await db.Verkaeufe.Where(v => v.SalonId == id).ExecuteDeleteAsync();
            await db.KassenSitzungen.Where(k => k.SalonId == id).ExecuteDeleteAsync();
            await db.Ausgaben.Where(a => a.SalonId == id).ExecuteDeleteAsync();
            await db.AusgabenKategorien.Where(a => a.SalonId == id).ExecuteDeleteAsync();
            await db.LeistungFavoriten.Where(f => f.SalonId == id).ExecuteDeleteAsync();
            await db.Rabatte.Where(r => r.SalonId == id).ExecuteDeleteAsync();
            await db.Leistungen.Where(l => l.SalonId == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.BasisLeistungId, (Guid?)null));
            await db.Leistungen.Where(l => l.SalonId == id).ExecuteDeleteAsync();
            await db.LeistungKategorien.Where(k => k.SalonId == id).ExecuteUpdateAsync(s => s.SetProperty(k => k.ParentId, (Guid?)null));
            await db.LeistungKategorien.Where(k => k.SalonId == id).ExecuteDeleteAsync();
            await db.BesitzerHinweise.Where(h => h.SalonId == id).ExecuteDeleteAsync();
            await db.GeraetSalonFavoriten.Where(f => f.SalonId == id).ExecuteDeleteAsync();
            await db.NutzungsAbos.Where(a => a.SalonId == id).ExecuteDeleteAsync();
            await db.UserSalons.Where(u => u.SalonId == id).ExecuteDeleteAsync();
            await db.Salons.Where(s => s.Id == id).ExecuteDeleteAsync();
        }

        foreach (var pseudonym in KassiererPseudonyme.Append(BesitzerPseudonym))
        {
            var konto = await userManager.FindByNameAsync(pseudonym);
            if (konto is null) continue;
            if (await db.UserSalons.AnyAsync(u => u.AppUserId == konto.Id))
                throw new InvalidOperationException($"Konto {pseudonym} ist noch einem anderen Laden zugeordnet – wird nicht gelöscht.");
            await userManager.DeleteAsync(konto);
        }
    }
}
