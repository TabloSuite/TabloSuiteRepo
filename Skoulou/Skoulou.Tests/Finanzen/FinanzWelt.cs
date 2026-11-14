using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.Tests.Organisation;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Finanzen;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;

namespace Skoulou.Tests.Finanzen;

/// <summary>
/// Testwelt des Bereichs Finanzen auf der <see cref="OrgWelt"/> (Collège Alpha, Jahr 2026-2027, drei Trimester, Preis 12.500):
/// Posten « Inscription » (10.000, fällig 01/09/2026, alle) und « Scolarité 6e » (60.000 in drei Raten: 01/10/2026,
/// 10/01/2027, 10/04/2027). KoordZiel hat alle Finanzrechte, Koordinator keine. Heute = 13/11/2026.
/// </summary>
internal sealed class FinanzWelt
{
    private FinanzWelt(OrgWelt org, TestZeit zeit)
    {
        Org = org;
        Zeit = zeit;
        var db = org.D.Db;
        var zugriff = new Zugriff(db);
        var audit = new AuditService(zeit);
        Nummern = new NummernkreisService(zeit);
        var mailAusgang = new MailAusgangService(db, org.D.Versender, new MailSignal(), zeit, NullLogger<MailAusgangService>.Instance);
        var app = new OptionsWrapper<AppOptionen>(new AppOptionen { BaseUrl = "https://skoulou.test" });
        var mails = new FinanzMails(mailAusgang, app, NullLogger<FinanzMails>.Instance);
        Gebuehren = new GebuehrenService(db, zugriff, audit, zeit);
        Zahlungen = new ZahlungService(db, zugriff, audit, Nummern, mails, zeit);
        Quittungen = new QuittungService(db, zugriff, org.D.Speicher, zeit);
        Journal = new JournalService(db, zugriff, audit, Nummern, org.D.Speicher, zeit, NullLogger<JournalService>.Instance);
        Bericht = new BerichtService(db, zugriff, org.D.Speicher, zeit);
        Abrechnung = new PlattformAbrechnung(db, Nummern, audit, mails, zeit, NullLogger<PlattformAbrechnung>.Instance);
        Rechnungen = new RechnungService(db, zugriff, audit, Nummern, Abrechnung, org.D.Speicher, app, zeit, NullLogger<RechnungService>.Instance);
        Anteile = new AnteilService(db, zugriff, audit, zeit);
        Tarife = new TarifService(db, zugriff, audit);
        Familie = new FamilienFinanzService(db, zugriff, zeit);
    }

    public OrgWelt Org { get; }
    public TestZeit Zeit { get; }
    public IDbContextFactory<SkoulouDbContext> Db => Org.D.Db;
    public NummernkreisService Nummern { get; }
    public GebuehrenService Gebuehren { get; }
    public ZahlungService Zahlungen { get; }
    public QuittungService Quittungen { get; }
    public JournalService Journal { get; }
    public BerichtService Bericht { get; }
    public PlattformAbrechnung Abrechnung { get; }
    public RechnungService Rechnungen { get; }
    public AnteilService Anteile { get; }
    public TarifService Tarife { get; }
    public FamilienFinanzService Familie { get; }

    public Guid Inscription, Scolarite;

    /// <summary>Direktor, Koordinator mit allen Finanzrechten, Koordinator ohne Rechte, Lehrer, Schüler, Eltern, Responsable, Admin, fremder Direktor.</summary>
    public Guid D => Org.Direktor;
    public Guid KMit => Org.KoordZiel;
    public Guid KOhne => Org.Koordinator;

    public static Task<FinanzWelt> ErstelleAsync() => ErstelleAsync(TestDbContextFactory.Create(), new TestZeit());

    public static async Task<FinanzWelt> ErstelleAsync(IDbContextFactory<SkoulouDbContext> fabrik, TestZeit zeit)
    {
        var org = await OrgWelt.ErstelleAsync(fabrik, zeit);
        var w = new FinanzWelt(org, zeit);
        await using (var db = await fabrik.CreateDbContextAsync())
        {
            await FinanzKategorien.SicherstellenAsync(db);
            var k = await db.Users.FirstAsync(x => x.Id == org.KoordZiel);
            k.KoordinatorRechte = KoordinatorRechte.Alle;
            await db.SaveChangesAsync();
        }
        w.Inscription = await w.Gebuehren.PostenSpeichernAsync(org.Direktor, org.Jahr, null,
            new PostenEingabe("Inscription", 10_000, null, null, true, [new RateEingabe(10_000, new(2026, 9, 1))]));
        w.Scolarite = await w.Gebuehren.PostenSpeichernAsync(org.Direktor, org.Jahr, null,
            new PostenEingabe("Scolarité 6e", 60_000, org.Stufe6, null, true,
                [new RateEingabe(20_000, new(2026, 10, 1)), new RateEingabe(20_000, new(2027, 1, 10)), new RateEingabe(20_000, new(2027, 4, 10))]));
        return w;
    }

    /// <summary>Zahlung des Direktors mit neuer Vorgangs-Id.</summary>
    public Task<ZahlungErgebnis> ZahlenAsync(Guid schueler, long betrag, ZahlungsKanal kanal = ZahlungsKanal.Especes, DateOnly? datum = null,
        Guid? akteur = null, bool guthaben = false)
        => Zahlungen.ErfassenAsync(akteur ?? D, Org.Jahr, new ZahlungEingabe(Guid.NewGuid(), schueler, betrag, datum ?? new(2026, 11, 12), kanal,
            kanal == ZahlungsKanal.Especes ? null : "TX-" + Guid.NewGuid().ToString("N")[..6], null, guthaben));

    public async Task<T> MitDbAsync<T>(Func<SkoulouDbContext, Task<T>> abfrage)
    {
        await using var db = await Db.CreateDbContextAsync();
        return await abfrage(db);
    }

    /// <summary>Uhr auf einen lokalen Tag (Lomé = UTC) um 9 Uhr stellen.</summary>
    public void Heute(DateOnly tag) => Zeit.Jetzt = new DateTimeOffset(tag.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
}
