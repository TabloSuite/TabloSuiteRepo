using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Fehlersuche 21.11.2026 + Nutzerentscheidung 22.11.: « Coupe », « Coupe » und « coupe » bzw. « Fidélité »/« fidélité »
/// lagen gleichzeitig aktiv im Katalog und waren an der Kasse nicht zu unterscheiden; ein Anfangsbestand von −3 ließ sich
/// eintippen. Jetzt: aktive Artikel gleichen Typs und Remises mit überlappendem Zeitraum brauchen verschiedene Namen,
/// kein negativer Bestand per Eingabe; Bruchteile (2,5 m) bleiben erlaubt.
/// </summary>
public class KatalogDublettenTests
{
    private static async Task<(KatalogService Dienst, Guid SalonId)> WeltAsync()
    {
        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Id = Guid.NewGuid(), Name = "Dubletten " + Guid.NewGuid().ToString("N")[..6] };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            await db.SaveChangesAsync();
        }
        return (new KatalogService(factory, null!, null!), salon.Id);
    }

    private static Leistung Neu(Guid salonId, string name, LeistungsTyp typ = LeistungsTyp.Prestation)
        => new() { Id = Guid.Empty, SalonId = salonId, Name = name, Typ = typ, Preis = 1000 };

    [Fact]
    public async Task Gleichnamige_Aktive_Prestation_Wird_Abgelehnt_Auch_Bei_Anderer_Schreibweise()
    {
        var (dienst, salonId) = await WeltAsync();
        await dienst.SpeichereLeistungAsync(Neu(salonId, "Coupe"));

        var fehler = await Assert.ThrowsAsync<InvalidOperationException>(() => dienst.SpeichereLeistungAsync(Neu(salonId, "  coupe ")));
        Assert.Contains("porte déjà ce nom", fehler.Message);
    }

    [Fact]
    public async Task Gleicher_Name_Erlaubt_Bei_Anderem_Typ_Oder_Wenn_Der_Alte_Deaktiviert_Ist()
    {
        var (dienst, salonId) = await WeltAsync();
        var alt = await dienst.SpeichereLeistungAsync(Neu(salonId, "Shampoing"));
        await dienst.SpeichereLeistungAsync(Neu(salonId, "Shampoing", LeistungsTyp.Produit));

        alt.IstAktiv = false;
        await dienst.SpeichereLeistungAsync(alt);
        await dienst.SpeichereLeistungAsync(Neu(salonId, "Shampoing"));

        // Den alten wieder aktivieren: jetzt gibt es zwei aktive « Shampoing » gleichen Typs → abgelehnt
        alt.IstAktiv = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => dienst.SpeichereLeistungAsync(alt));
    }

    [Fact]
    public async Task Bearbeiten_Ohne_Namensaenderung_Geht_Weiter()
    {
        var (dienst, salonId) = await WeltAsync();
        var l = await dienst.SpeichereLeistungAsync(Neu(salonId, "Tresses"));
        l.Preis = 5000;
        var gespeichert = await dienst.SpeichereLeistungAsync(l);
        Assert.Equal(5000, gespeichert.Preis);
    }

    [Fact]
    public async Task Negativer_Bestand_Wird_Abgelehnt_Bruchteile_Gehen_Durch()
    {
        var (dienst, salonId) = await WeltAsync();
        var minus = Neu(salonId, "Gel", LeistungsTyp.Produit);
        minus.HatStueckzahl = true;
        minus.Stueckzahl = -3;
        var fehler = await Assert.ThrowsAsync<InvalidOperationException>(() => dienst.SpeichereLeistungAsync(minus));
        Assert.Contains("négatif", fehler.Message);

        var tissu = Neu(salonId, "Tissu wax (m)", LeistungsTyp.Produit);
        tissu.HatStueckzahl = true;
        tissu.Stueckzahl = 2.5m;
        Assert.Equal(2.5m, (await dienst.SpeichereLeistungAsync(tissu)).Stueckzahl);
    }

    [Fact]
    public async Task Durch_Verkaeufe_Negativer_Bestand_Blockiert_Das_Bearbeiten_Nicht()
    {
        var (dienst, salonId) = await WeltAsync();
        var l = Neu(salonId, "Mèches", LeistungsTyp.Produit);
        l.HatStueckzahl = true;
        l.Stueckzahl = 1;
        l = await dienst.SpeichereLeistungAsync(l);
        // Stand nach Verkäufen −2 (z. B. Ware verkauft, bevor sie erfasst war); Formular zeigt −2, nur der Preis ändert sich.
        l.Stueckzahl = -2;
        var gespeichert = await dienst.SpeichereLeistungAsync(new Leistung
        {
            Id = l.Id, SalonId = salonId, Name = l.Name, Typ = l.Typ, Preis = 2000, HatStueckzahl = true, Stueckzahl = -2
        }, default, stueckzahlBeimOeffnen: -2);
        Assert.Equal(2000, gespeichert.Preis);
        // Wer im Formular einen negativen Wert neu eintippt, bekommt die Meldung
        await Assert.ThrowsAsync<InvalidOperationException>(() => dienst.SpeichereLeistungAsync(new Leistung
        {
            Id = l.Id, SalonId = salonId, Name = l.Name, Typ = l.Typ, Preis = 2000, HatStueckzahl = true, Stueckzahl = -5
        }, default, stueckzahlBeimOeffnen: -2));
    }

    private static Rabatt Remise(Guid salonId, string name, DateTime? von = null, DateTime? bis = null)
        => new() { Id = Guid.Empty, SalonId = salonId, Name = name, Art = RabattArt.Prozent, Wert = 10, IstAktiv = true, GueltigVonUtc = von, GueltigBisUtc = bis };

    [Fact]
    public async Task Gleichnamige_Remise_Nur_Bei_Getrennten_Zeitraeumen()
    {
        var (dienst, salonId) = await WeltAsync();
        await dienst.SpeichereRabattAsync(Remise(salonId, "Fidélité"));
        var fehler = await Assert.ThrowsAsync<ArgumentException>(() => dienst.SpeichereRabattAsync(Remise(salonId, "fidélité")));
        Assert.Contains("porte déjà ce nom", fehler.Message);

        await dienst.SpeichereRabattAsync(Remise(salonId, "Soldes", new DateTime(2027, 1, 1), new DateTime(2027, 1, 31)));
        await dienst.SpeichereRabattAsync(Remise(salonId, "Soldes", new DateTime(2027, 7, 1), new DateTime(2027, 7, 31)));
        // « Au » einschließlich: 31.01. überschneidet sich mit einer Remise ab 31.01.
        await Assert.ThrowsAsync<ArgumentException>(() => dienst.SpeichereRabattAsync(Remise(salonId, "SOLDES", new DateTime(2027, 1, 31), null)));
    }

    [Fact]
    public async Task Reaktivieren_Einer_Gleichnamigen_Remise_Wird_Abgelehnt()
    {
        var (dienst, salonId) = await WeltAsync();
        var alt = await dienst.SpeichereRabattAsync(Remise(salonId, "Étudiant"));
        await dienst.SetzeRabattAktivAsync(alt.Id, salonId, false);
        await dienst.SpeichereRabattAsync(Remise(salonId, "Étudiant"));

        await Assert.ThrowsAsync<ArgumentException>(() => dienst.SetzeRabattAktivAsync(alt.Id, salonId, true));
        // Deaktivieren geht immer
        Assert.True(await dienst.SetzeRabattAktivAsync(alt.Id, salonId, false));
    }
}
