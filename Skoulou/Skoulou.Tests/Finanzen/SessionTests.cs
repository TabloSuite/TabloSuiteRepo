using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Finanzen;
using Skoulou.Core.Organisation;
using Skoulou.Tests.Organisation;
using Skoulou.Tests.Sql;
using Skoulou.WebApp.Services.Finanzen;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.Tests.Finanzen;

/// <summary>
/// E7b-Zusatz 1/2: Session-Klassen enden automatisch mit ihrer Session (Abrechnung, Konten, Listen) und Gebührenposten
/// können an eine Session gebunden werden (gelten nur für Schüler, die in dieser Session eingeschrieben sind).
/// </summary>
public class SessionTests
{
    private static readonly DateOnly S1Beginn = new(2026, 9, 1), S1Ende = new(2026, 12, 18);

    [Theory]
    [InlineData("2026-09-01", null, "2026-12-18")]          // offen → endet mit der Session
    [InlineData("2026-09-01", "2026-11-30", "2026-11-30")]  // Austritt vorher bleibt
    [InlineData("2026-09-01", "2027-03-01", "2026-12-18")]  // später geschlossen → Session-Ende
    public void Effektives_Ende_ist_Minimum_aus_Einschreibung_und_Session(string von, string? bis, string erwartet)
    {
        Assert.Equal(DateOnly.Parse(erwartet), SessionRegel.Ende(bis is null ? null : DateOnly.Parse(bis), S1Ende));
        Assert.Equal(DateOnly.Parse(von), SessionRegel.Beginn(DateOnly.Parse(von), S1Beginn));
    }

    [Fact]
    public void Ohne_Session_bleibt_die_Einschreibung_unveraendert()
    {
        Assert.Null(SessionRegel.Ende(null, null));
        Assert.True(SessionRegel.Schneidet(new(2026, 9, 1), null, null, null, new(2027, 4, 1), new(2027, 6, 30)));
        Assert.True(SessionRegel.Laeuft(null, null, new(2027, 6, 1)));
    }

    [Fact]
    public void Session_Klasse_zaehlt_nur_in_ihrer_Session()
    {
        // Vorab eingeschrieben (20/12) für die Session Januar–März: weder in der 1. noch in der 3. Periode.
        Assert.False(SessionRegel.Schneidet(new(2026, 12, 20), null, new(2027, 1, 4), new(2027, 3, 26), S1Beginn, S1Ende));
        Assert.True(SessionRegel.Schneidet(new(2026, 12, 20), null, new(2027, 1, 4), new(2027, 3, 26), new(2027, 1, 4), new(2027, 3, 26)));
        Assert.False(SessionRegel.Schneidet(new(2026, 9, 1), null, S1Beginn, S1Ende, new(2027, 1, 4), new(2027, 3, 26)));
        // Austritt vor Beginn der Session: nie in der Session.
        Assert.False(SessionRegel.Schneidet(new(2026, 12, 1), new(2026, 12, 20), new(2027, 1, 4), new(2027, 3, 26), new(2027, 1, 4), new(2027, 3, 26)));
        Assert.False(SessionRegel.Laeuft(null, S1Ende, new(2026, 12, 19)));
        Assert.True(SessionRegel.Laeuft(null, S1Ende, S1Ende));

        var s = Guid.NewGuid();
        var kandidaten = Abrechnungspflicht.Kandidaten(
            [new EinschreibungsZeitraum(s, Guid.NewGuid(), new(2026, 9, 1), null, S1Beginn, S1Ende)], new(2027, 1, 4), new(2027, 3, 26));
        Assert.Empty(kandidaten);
    }

    [Fact]
    public void Posten_einer_Session_gilt_nur_fuer_Teilnehmer_dieser_Session()
    {
        Guid k1 = Guid.NewGuid(), k2 = Guid.NewGuid(), stufe = Guid.NewGuid();
        var nurS1 = new List<BelegungDaten> { new(k1, stufe, new(2026, 9, 1), S1Ende, true) };
        var s1UndS2 = new List<BelegungDaten> { new(k1, stufe, new(2026, 9, 1), S1Ende, false), new(k2, stufe, new(2027, 1, 4), new(2027, 3, 26), true) };
        var session2 = new GeltungDaten(null, null, new(2027, 1, 4), new(2027, 3, 26));
        Assert.False(PostenGeltung.Gilt(session2, nurS1));
        Assert.True(PostenGeltung.Gilt(session2, s1UndS2));
        Assert.True(PostenGeltung.Gilt(new GeltungDaten(null, k1, S1Beginn, S1Ende), s1UndS2));
        Assert.False(PostenGeltung.Gilt(new GeltungDaten(null, k2, S1Beginn, S1Ende), s1UndS2));
        // Jahresposten: maßgeblich bleibt die aktive bzw. zuletzt begonnene Einschreibung.
        Assert.True(PostenGeltung.Gilt(new GeltungDaten(null, k2), s1UndS2));
        Assert.False(PostenGeltung.Gilt(new GeltungDaten(null, k1), s1UndS2));
        Assert.True(PostenGeltung.Gilt(new GeltungDaten(stufe, null), nurS1));
    }

    /// <summary>6e B läuft nur im 1. Trimester (« Session »); SchuelerZiel sitzt dort ohne Wechsel/Austritt.</summary>
    internal static async Task<FinanzWelt> SessionWeltAsync(FinanzWelt? welt = null)
    {
        var w = welt ?? await FinanzWelt.ErstelleAsync();
        await using var db = await w.Db.CreateDbContextAsync();
        (await db.Klassen.FirstAsync(k => k.Id == w.Org.Klasse6B)).PeriodeId = w.Org.P1;
        (await db.Einschreibungen.FirstAsync(e => e.Id == w.Org.EinschreibungZiel)).KlasseId = w.Org.Klasse6B;
        await db.SaveChangesAsync();
        return w;
    }

    [Fact]
    public async Task Session_Teilnehmer_wird_nach_der_Session_nicht_mehr_abgerechnet() => await AbrechnungAsync(await SessionWeltAsync());

    internal static async Task AbrechnungAsync(FinanzWelt w)
    {
        w.Heute(new(2026, 12, 19));
        await w.Abrechnung.FaelligeAbrechnenAsync();
        w.Heute(new(2027, 3, 27));
        await w.Abrechnung.FaelligeAbrechnenAsync();
        var rechnungen = await w.MitDbAsync(db => db.Plattformrechnungen.Include(r => r.Positionen).ToListAsync());
        var p1 = rechnungen.Single(r => r.PeriodeId == w.Org.P1);
        var p2 = rechnungen.Single(r => r.PeriodeId == w.Org.P2);
        Assert.Contains(p1.Positionen, p => p.SchuelerId == w.Org.SchuelerZiel);
        Assert.DoesNotContain(p2.Positionen, p => p.SchuelerId == w.Org.SchuelerZiel);
        Assert.Equal(1, p2.Anzahl);
    }

    [Fact]
    public async Task Nach_der_Session_ohne_Klasse_in_Listen_und_neu_einschreibbar() => await ListenAsync(await SessionWeltAsync());

    internal static async Task ListenAsync(FinanzWelt w)
    {
        var org = w.Org;
        Assert.Equal("6e B", (await org.D.Schueler.SucheAsync(org.Direktor, null, null, "eleveziel", 0, 10)).Eintraege.Single().Klasse);
        w.Heute(new(2027, 1, 5));
        var ohne = await org.D.Schueler.SucheAsync(org.Direktor, SchuelerService.FilterSansClasse, null, null, 0, 50);
        Assert.Contains(ohne.Eintraege, z => z.KontoId == org.SchuelerZiel);
        Assert.Empty((await org.D.Schueler.SucheAsync(org.Direktor, null, org.Klasse6B, null, 0, 50)).Eintraege);
        // Appell der Session-Klasse: nach der Session ohne Teilnehmer, in der Session mit.
        Assert.Empty((await org.D.Anwesenheit.AppellAsync(org.Direktor, org.Klasse6B, new(2027, 1, 5))).Zeilen);
        Assert.Contains((await org.D.Anwesenheit.AppellAsync(org.Direktor, org.Klasse6B, new(2026, 12, 15))).Zeilen, z => z.SchuelerId == org.SchuelerZiel);

        // « Inscrire » ohne « Changer de classe »: die Session-Einschreibung wird zum Session-Ende abgeschlossen.
        await org.D.Einschreibung.EinschreibenAsync(org.Direktor, org.SchuelerZiel, org.Klasse6A, new(2027, 1, 5), false);
        var alt = await w.MitDbAsync(db => db.Einschreibungen.SingleAsync(e => e.Id == org.EinschreibungZiel));
        Assert.Equal((EinschreibungsStatus.Terminee, (DateOnly?)S1Ende), (alt.Status, alt.Bis));
        Assert.DoesNotContain((await org.D.Schueler.SucheAsync(org.Direktor, SchuelerService.FilterSansClasse, null, null, 0, 50)).Eintraege,
            z => z.KontoId == org.SchuelerZiel);
    }

    [Fact]
    public async Task Gebuehr_je_Session_nur_fuer_eingeschriebene_Teilnehmer() => await GebuehrenAsync(await SessionWeltAsync());

    internal static async Task GebuehrenAsync(FinanzWelt w)
    {
        var org = w.Org;
        var s1 = await w.Gebuehren.PostenSpeichernAsync(w.D, org.Jahr, null, new PostenEingabe("Session 1", 15_000, null, null, true, [], org.P1));
        var s2 = await w.Gebuehren.PostenSpeichernAsync(w.D, org.Jahr, null, new PostenEingabe("Session 2", 20_000, null, null, true, [], org.P2));
        Assert.Equal("Tous les élèves · 2e trimestre seulement", (await w.Gebuehren.PostenAsync(w.D, org.Jahr)).Single(p => p.Id == s2).Geltung);

        async Task<HashSet<Guid?>> PostenVonAsync(Guid schueler)
            => (await w.Gebuehren.KontoAsync(w.D, org.Jahr, schueler)).Konto.Zeilen.Select(z => z.PostenId).ToHashSet();

        var ziel = await PostenVonAsync(org.SchuelerZiel);
        Assert.Contains(s1, ziel);
        Assert.DoesNotContain(s2, ziel);
        Assert.Contains(w.Inscription, ziel);
        Assert.Contains(s2, await PostenVonAsync(org.Schueler));

        // Neu eingeschrieben für die 2. Session → schuldet die Gebühr der 2. Session.
        w.Heute(new(2027, 1, 5));
        await org.D.Einschreibung.EinschreibenAsync(org.Direktor, org.SchuelerZiel, org.Klasse6A, new(2027, 1, 5), false);
        Assert.Contains(s2, await PostenVonAsync(org.SchuelerZiel));
    }

    [Fact]
    public async Task Session_Posten_wird_geprueft()
    {
        var w = await SessionWeltAsync();
        var org = w.Org;
        var fremd = await Assert.ThrowsAsync<FachFehler>(() => w.Gebuehren.PostenSpeichernAsync(w.D, org.Jahr, null,
            new PostenEingabe("Session", 5_000, null, null, true, [], org.PV1)));
        Assert.Equal("periode", fremd.Feld);
        var klasse = await Assert.ThrowsAsync<FachFehler>(() => w.Gebuehren.PostenSpeichernAsync(w.D, org.Jahr, null,
            new PostenEingabe("Session", 5_000, null, org.Klasse6B, true, [], org.P2)));
        Assert.Contains("n'a pas lieu", klasse.Message, StringComparison.Ordinal);
        var auswahl = await w.Gebuehren.GeltungsAuswahlAsync(w.D, org.Jahr);
        Assert.Equal(3, auswahl.Perioden.Count);
    }
}

/// <summary>Nur mit SKOULOU_SQL_TESTS=1: dieselben Session-Regeln auf SQL Server (Übersetzbarkeit der Abfragen).</summary>
public class SqlSessionTests(SqlDatenbank datenbank) : IClassFixture<SqlDatenbank>
{
    private async Task<FinanzWelt> WeltAsync()
        => await SessionTests.SessionWeltAsync(await FinanzWelt.ErstelleAsync(new SqlFabrik(datenbank), new TestZeit()));

    [SqlFact]
    public async Task Session_Regeln_auf_SQL_Server()
    {
        await SessionTests.AbrechnungAsync(await WeltAsync());
        await SessionTests.ListenAsync(await WeltAsync());
        await SessionTests.GebuehrenAsync(await WeltAsync());
    }
}