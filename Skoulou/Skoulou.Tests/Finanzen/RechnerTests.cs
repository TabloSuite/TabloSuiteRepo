using Skoulou.Core.Entitaeten;
using Skoulou.Core.Finanzen;
using Skoulou.Core.Werkzeuge;

namespace Skoulou.Tests.Finanzen;

public class PlattformAnteilTests
{
    [Fact]
    public void Jahrespreis_auf_drei_Trimester()
        => Assert.Equal([4_166L, 4_167L, 4_167L], PlattformAnteil.Anteile(12_500, 3));

    [Theory]
    [InlineData(12_500, 1)]
    [InlineData(12_500, 2)]
    [InlineData(12_500, 3)]
    [InlineData(10_001, 4)]
    [InlineData(7, 3)]
    [InlineData(0, 3)]
    [InlineData(999_999, 7)]
    public void Summe_ergibt_immer_den_Jahrespreis(long preis, int perioden)
    {
        var anteile = PlattformAnteil.Anteile(preis, perioden);
        Assert.Equal(preis, anteile.Sum());
        Assert.True(anteile.Max() - anteile.Min() <= 1, "Anteile unterscheiden sich höchstens um 1 FCFA.");
    }

    [Fact]
    public void Ungueltige_Angaben_werden_abgelehnt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlattformAnteil.Anteil(12_500, 0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlattformAnteil.Anteil(12_500, 4, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlattformAnteil.Anteil(12_500, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlattformAnteil.Anteil(-1, 1, 3));
    }

    [Fact]
    public void Responsable_Anteil_wird_abgerundet()
    {
        Assert.Equal(1_249, ResponsableVerguetung.Betrag(12_499, 10));
        Assert.Equal(833, ResponsableVerguetung.Betrag(8_332, 10m));
        Assert.Equal(0, ResponsableVerguetung.Betrag(8_332, 0));
        Assert.Equal(1_041, ResponsableVerguetung.Betrag(8_332, 12.5m));
    }
}

public class AbrechnungspflichtTests
{
    private static readonly DateOnly Beginn = new(2026, 9, 1), Ende = new(2026, 12, 18);
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

    [Fact]
    public void Klassenwechsel_wird_je_Periode_einmal_berechnet_mit_der_neuesten_Einschreibung()
    {
        var alt = Guid.NewGuid();
        var neu = Guid.NewGuid();
        var kandidaten = Abrechnungspflicht.Kandidaten(
        [
            new(A, alt, new(2026, 9, 1), new(2026, 10, 15)),
            new(A, neu, new(2026, 10, 16), null),
        ], Beginn, Ende);
        Assert.Equal(new AbrechnungsKandidat(A, neu), Assert.Single(kandidaten));
    }

    [Fact]
    public void Nur_wer_die_Periode_schneidet_und_noch_nicht_abgerechnet_ist()
    {
        var kandidaten = Abrechnungspflicht.Kandidaten(
        [
            new(A, Guid.NewGuid(), new(2026, 9, 1), null),
            new(B, Guid.NewGuid(), new(2026, 9, 1), new(2026, 8, 31)),   // vor der Periode ausgetreten
            new(C, Guid.NewGuid(), new(2026, 12, 19), null),              // nach der Periode eingetreten
            new(D, Guid.NewGuid(), new(2026, 12, 18), null),              // am letzten Tag eingetreten: voller Anteil
        ], Beginn, Ende, new HashSet<Guid> { A });
        Assert.Equal([D], kandidaten.Select(k => k.SchuelerId));
    }

    [Fact]
    public void Schneidet_beachtet_die_Grenzen()
    {
        Assert.True(Abrechnungspflicht.Schneidet(Ende, null, Beginn, Ende));
        Assert.True(Abrechnungspflicht.Schneidet(new(2026, 1, 1), Beginn, Beginn, Ende));
        Assert.False(Abrechnungspflicht.Schneidet(new(2026, 1, 1), Beginn.AddDays(-1), Beginn, Ende));
    }
}

public class GebuehrenRechnerTests
{
    private static readonly DateOnly JahrBeginn = new(2026, 9, 1);
    private static readonly Guid Insc = Guid.NewGuid(), Scol = Guid.NewGuid();

    private static readonly PostenDaten Inscription = new(Insc, "Inscription", 10_000, true, 1, [new(1, 10_000, new(2026, 9, 1))]);
    private static readonly PostenDaten Scolarite = new(Scol, "Scolarité", 60_000, true, 2,
        [new(1, 20_000, new(2026, 10, 1)), new(2, 20_000, new(2027, 1, 10)), new(3, 20_000, new(2027, 4, 10))]);

    private static ZahlungDaten Zahlung(long betrag, int tag, int monat = 11, int jahr = 2026)
        => new(Guid.NewGuid(), betrag, new(jahr, monat, tag), new DateTime(jahr, monat, tag, 10, 0, 0, DateTimeKind.Utc));

    private static GebuehrenKonto Konto(IReadOnlyList<ZahlungDaten> zahlungen, IReadOnlyList<ErmaessigungDaten>? ermaessigungen = null, long vortrag = 0,
        DateOnly? heute = null)
        => GebuehrenRechner.Berechne([Scolarite, Inscription], ermaessigungen ?? [], zahlungen, vortrag, heute ?? new(2026, 11, 13), JahrBeginn);

    [Fact]
    public void Zahlungen_decken_die_Raten_nach_Faelligkeit()
    {
        var k = Konto([Zahlung(25_000, 5)]);
        Assert.Equal(70_000, k.Soll);
        Assert.Equal(25_000, k.Bezahlt);
        Assert.Equal(45_000, k.Offen);
        Assert.Equal(["Inscription", "Scolarité", "Scolarité", "Scolarité"], k.Zeilen.Select(z => z.Bezeichnung));
        Assert.Equal([10_000L, 15_000, 0, 0], k.Zeilen.Select(z => z.Bezahlt));
        // Fällig bis heute: 10.000 + 20.000, gedeckt 25.000 → 5.000 en retard.
        Assert.Equal(5_000, k.EnRetardBetrag);
        Assert.True(k.Zeilen[1].EnRetard);
        Assert.False(k.Zeilen[2].EnRetard);
    }

    [Fact]
    public void Faellig_heute_ist_noch_nicht_en_retard()
    {
        var k = Konto([Zahlung(10_000, 1, 9)], heute: new(2026, 10, 1));
        Assert.Equal(0, k.EnRetardBetrag);
        Assert.Equal(20_000, Konto([Zahlung(10_000, 1, 9)], heute: new(2026, 10, 2)).EnRetardBetrag);
    }

    [Fact]
    public void Ueberzahlung_wird_Guthaben()
    {
        var k = Konto([Zahlung(50_000, 1), Zahlung(30_000, 2)]);
        Assert.Equal(0, k.Offen);
        Assert.Equal(10_000, k.Guthaben);
        Assert.Equal(-10_000, k.Saldo);
        Assert.Equal(0, k.EnRetardBetrag);
    }

    [Fact]
    public void Rueckstand_des_Vorjahres_wird_zuerst_gedeckt_und_ist_sofort_en_retard()
    {
        var k = Konto([Zahlung(15_000, 1)], vortrag: 12_000);
        Assert.Equal(GebuehrenRechner.VortragText, k.Zeilen[0].Bezeichnung);
        Assert.Equal(82_000, k.Soll);
        Assert.Equal([12_000L, 3_000, 0, 0, 0], k.Zeilen.Select(z => z.Bezahlt));
        Assert.Equal(12_000, k.VortragRest);
        Assert.Equal(7_000 + 20_000, k.EnRetardBetrag);

        var unbezahlt = Konto([], vortrag: 12_000, heute: JahrBeginn.AddDays(-30));
        Assert.True(unbezahlt.Zeilen[0].EnRetard);
    }

    [Fact]
    public void Guthaben_des_Vorjahres_wird_wie_eine_erste_Zahlung_verteilt()
    {
        var k = Konto([], vortrag: -12_000);
        Assert.Equal(70_000, k.Soll);
        Assert.Equal(0, k.Bezahlt);
        Assert.Equal(58_000, k.Offen);
        Assert.Equal(12_000, k.VortragGuthaben);
        Assert.Equal(Guid.Empty, k.Zuordnungen[0].ZahlungId);
        Assert.Equal(58_000, k.Saldo);
    }

    [Fact]
    public void Ermaessigungen_in_Prozent_und_Betrag_kuerzen_die_spaetesten_Raten()
    {
        var k = Konto([], [new ErmaessigungDaten(Scol, ErmaessigungsArt.Pourcentage, 50), new ErmaessigungDaten(Insc, ErmaessigungsArt.Montant, 2_500)]);
        Assert.Equal(70_000, k.Brutto);
        Assert.Equal(32_500, k.Ermaessigungen);
        Assert.Equal(37_500, k.Soll);
        var scol = k.Zeilen.Where(z => z.PostenId == Scol).Select(z => z.Betrag).ToList();
        Assert.Equal([20_000L, 10_000, 0], scol);
        Assert.Equal(7_500, k.Zeilen.Single(z => z.PostenId == Insc).Betrag);
    }

    [Fact]
    public void Ermaessigung_ohne_Posten_und_nie_unter_null()
    {
        // 10 % auf alles = 1.000 + 6.000; pauschal 100.000 → alles erlassen, nie negativ.
        var prozent = Konto([], [new ErmaessigungDaten(null, ErmaessigungsArt.Pourcentage, 10)]);
        Assert.Equal(63_000, prozent.Soll);
        var alles = Konto([Zahlung(5_000, 1)], [new ErmaessigungDaten(null, ErmaessigungsArt.Montant, 100_000)]);
        Assert.Equal(0, alles.Soll);
        Assert.Equal(5_000, alles.Guthaben);
        Assert.All(alles.Zeilen, z => Assert.True(z.Betrag >= 0));
        // Pauschalbetrag kürzt die späteste Rate zuerst.
        var pauschal = Konto([], [new ErmaessigungDaten(null, ErmaessigungsArt.Montant, 25_000)]);
        Assert.Equal([10_000L, 20_000, 15_000, 0], pauschal.Zeilen.Select(z => z.Betrag));
    }

    [Fact]
    public void Posten_ohne_Raten_ist_zum_Jahresbeginn_faellig()
    {
        var tenue = new PostenDaten(Guid.NewGuid(), "Tenue", 8_000, false, 3, []);
        var k = GebuehrenRechner.Berechne([tenue], [], [], 0, new(2026, 9, 2), JahrBeginn);
        var zeile = Assert.Single(k.Zeilen);
        Assert.Equal(JahrBeginn, zeile.Faellig);
        Assert.False(zeile.Pflicht);
        Assert.True(zeile.EnRetard);
    }

    [Fact]
    public void Zuordnung_je_Zahlung_fuer_die_Quittung()
    {
        var erste = Zahlung(15_000, 1, 10);
        var zweite = Zahlung(20_000, 3, 10);
        var k = Konto([zweite, erste]);
        Assert.Equal([10_000L, 5_000], k.Zuordnungen.Where(z => z.ZahlungId == erste.Id).Select(z => z.Betrag));
        Assert.Equal([15_000L, 5_000], k.Zuordnungen.Where(z => z.ZahlungId == zweite.Id).Select(z => z.Betrag));
    }

    [Fact]
    public void Saldo_fuer_den_Vortrag()
    {
        Assert.Equal(40_000, GebuehrenRechner.Saldo([Scolarite, Inscription], [], [Zahlung(30_000, 1)], 0, JahrBeginn));
        Assert.Equal(-5_000, GebuehrenRechner.Saldo([Inscription], [], [Zahlung(15_000, 1)], 0, JahrBeginn));
        Assert.Equal(0, GebuehrenKonto.Leer(JahrBeginn).Soll);
    }
}

public class KasseUndWortenTests
{
    [Fact]
    public void Kassensaldo_und_Kategorien_mit_Gegenbuchung()
    {
        var scol = Guid.NewGuid();
        var loyer = Guid.NewGuid();
        var buchungen = new[]
        {
            new KategorieBuchung(scol, "Scolarité", BuchungsRichtung.Recette, BuchungsRichtung.Recette, 50_000),
            new KategorieBuchung(scol, "Scolarité", BuchungsRichtung.Recette, BuchungsRichtung.Depense, 5_000),
            new KategorieBuchung(loyer, "Loyer", BuchungsRichtung.Depense, BuchungsRichtung.Depense, 20_000),
        };
        Assert.Equal(25_000, Kasse.Saldo(buchungen.Select(b => new BuchungsBetrag(b.Richtung, b.Betrag))));
        Assert.Equal((50_000L, 25_000L), Kasse.Summen(buchungen.Select(b => new BuchungsBetrag(b.Richtung, b.Betrag))));
        var summen = Kasse.NachKategorie(buchungen);
        Assert.Equal(45_000, summen.Single(s => s.KategorieId == scol).Betrag);
        Assert.Equal(20_000, summen.Single(s => s.KategorieId == loyer).Betrag);
    }

    [Theory]
    [InlineData(25_000, "vingt-cinq mille")]
    [InlineData(1_500, "mille cinq cents")]
    [InlineData(80_000, "quatre-vingt mille")]
    [InlineData(200_000, "deux cent mille")]
    [InlineData(1_250_000, "un million deux cent cinquante mille")]
    [InlineData(71, "soixante et onze")]
    [InlineData(1_001, "mille un")]
    [InlineData(0, "zéro")]
    [InlineData(21, "vingt et un")]
    [InlineData(80, "quatre-vingts")]
    [InlineData(81, "quatre-vingt-un")]
    [InlineData(91, "quatre-vingt-onze")]
    [InlineData(180, "cent quatre-vingts")]
    [InlineData(201, "deux cent un")]
    [InlineData(71_000, "soixante et onze mille")]
    [InlineData(2_000_000, "deux millions")]
    [InlineData(200_000_000, "deux cents millions")]
    [InlineData(4_166, "quatre mille cent soixante-six")]
    [InlineData(999_999_999, "neuf cent quatre-vingt-dix-neuf millions neuf cent quatre-vingt-dix-neuf mille neuf cent quatre-vingt-dix-neuf")]
    public void Zahl_in_Worten(long zahl, string text) => Assert.Equal(text, ZahlInWorten.Text(zahl));

    [Fact]
    public void Betrag_in_Worten_mit_Waehrung_und_Grenze()
    {
        Assert.Equal("vingt-cinq mille francs CFA", ZahlInWorten.Fcfa(25_000));
        Assert.Throws<ArgumentOutOfRangeException>(() => ZahlInWorten.Text(1_000_000_000));
    }

    [Fact]
    public void Nummernformate()
    {
        Assert.Equal("REC-2026-00042", NummernFormat.Formatiere(NummernArt.Quittung, 2026, 42));
        Assert.Equal("ECR-2026-00001", NummernFormat.Formatiere(NummernArt.Ecriture, 2026, 1));
        Assert.Equal("SKL-2027-0001", NummernFormat.Formatiere(NummernArt.Plattformrechnung, 2027, 1));
        Assert.True(NummernFormat.IstGlobal(NummernArt.Plattformrechnung));
        Assert.False(NummernFormat.IstGlobal(NummernArt.Quittung));
    }
}
