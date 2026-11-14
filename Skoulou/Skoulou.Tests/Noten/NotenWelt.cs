using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skoulou.Core.Entitaeten;
using Skoulou.Tests.Organisation;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;
using Skoulou.WebApp.Services.Noten;

namespace Skoulou.Tests.Noten;

/// <summary>Teilnehmer-Attrappe des Jahreswechsels (zählt Aufrufe).</summary>
internal sealed class TeilnehmerAttrappe : IJahreswechselTeilnehmer
{
    public List<JahreswechselKontext> Aufrufe { get; } = [];

    public Task NachWechselAsync(SkoulouDbContext db, JahreswechselKontext kontext, CancellationToken ct)
    {
        Aufrufe.Add(kontext);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Testwelt des Bereichs Noten auf der <see cref="OrgWelt"/>: Collège Alpha, 2026-2027 mit drei Trimestres, 6e A mit Mathématiques
/// (Koeff. 3, Lehrauftrag « prof ») und Français (Koeff. 2, ohne Lehrauftrag), zwei Schüler (eleve, eleveziel); heute 13/11/2026.
/// </summary>
internal sealed class NotenWelt
{
    private NotenWelt(OrgWelt o, TestZeit zeit)
    {
        O = o;
        Zeit = zeit;
        var db = o.D.Db;
        var zugriff = o.D.Zugriff;
        var audit = new AuditService(zeit);
        var app = new OptionsWrapper<AppOptionen>(new AppOptionen { BaseUrl = "https://skoulou.test" });
        var mails = new NotenMails(new MailAusgangService(db, Versender, new MailSignal(), zeit, NullLogger<MailAusgangService>.Instance), app, NullLogger<NotenMails>.Instance);
        Noten = new NotenService(db, zugriff, audit, zeit);
        Bewertungen = new BewertungService(db, zugriff);
        Conseil = new KlassenratService(db, zugriff, zeit);
        Bulletins = new BulletinService(db, zugriff, audit, new NummernkreisService(zeit), mails, zeit);
        Lesen = new BulletinLeseService(db, zugriff, zeit);
        Vorlagen = new VorlagenService(db, zugriff, zeit);
        Wechsel = new JahreswechselService(db, zugriff, audit, zeit, [Teilnehmer]);
        Pdf = new BulletinPdfService(db, o.D.Speicher, Lesen, Vorlagen, app);
    }

    public OrgWelt O { get; }
    public TestZeit Zeit { get; }
    public FakeVersender Versender { get; } = new();
    public TeilnehmerAttrappe Teilnehmer { get; } = new();
    public NotenService Noten { get; }
    public BewertungService Bewertungen { get; }
    public KlassenratService Conseil { get; }
    public BulletinService Bulletins { get; }
    public BulletinLeseService Lesen { get; }
    public VorlagenService Vorlagen { get; }
    public JahreswechselService Wechsel { get; }
    public BulletinPdfService Pdf { get; }

    public Guid D => O.Direktor;
    public Guid E1 => O.EinschreibungSchueler;
    public Guid E2 => O.EinschreibungZiel;

    public static async Task<NotenWelt> ErstelleAsync(IDbContextFactory<SkoulouDbContext>? fabrik = null)
    {
        var zeit = new TestZeit();
        var o = await OrgWelt.ErstelleAsync(fabrik ?? TestDbContextFactory.Create(), zeit);
        return new NotenWelt(o, zeit);
    }

    public Task<Guid> BewertungAsync(Guid klassenfach, Guid? periode = null, string titel = "Devoir 1", decimal bareme = 20, decimal gewicht = 1, DateOnly? datum = null)
        => Bewertungen.AnlegenAsync(D, klassenfach, periode ?? O.P1, new BewertungEingabe(titel, BewertungsArt.Devoir, datum, bareme, gewicht));

    public Task<ZellStand> NoteAsync(Guid bewertung, Guid einschreibung, string eingabe, Guid? akteur = null)
        => Noten.NoteSpeichernAsync(akteur ?? D, bewertung, einschreibung, eingabe);

    /// <summary>Vollständige Noten im 1er trimestre: eleve 15 (math) / 10 (fr) = 13; eleveziel 11 / 14 = 12,2.</summary>
    public async Task<(Guid Math, Guid Fr)> VollstaendigAsync()
    {
        var math = await BewertungAsync(O.Kf6AMath, titel: "Interrogation");
        var fr = await BewertungAsync(O.Kf6AFr, titel: "Dictée");
        await NoteAsync(math, E1, "15");
        await NoteAsync(math, E2, "11");
        await NoteAsync(fr, E1, "10");
        await NoteAsync(fr, E2, "14");
        return (math, fr);
    }

    /// <summary>Veröffentlicht 6e A im 1er trimestre (Hash aus der Übersicht).</summary>
    public async Task<VeroeffentlichungsErgebnis> VeroeffentlichenAsync(bool trotz = false, Guid? akteur = null)
    {
        var u = await Conseil.UebersichtAsync(D, O.Klasse6A, O.P1);
        return await Bulletins.VeroeffentlichenAsync(akteur ?? D, O.Klasse6A, O.P1, u.KlassenHash, trotz);
    }

    public Task<T> MitDbAsync<T>(Func<SkoulouDbContext, Task<T>> abfrage) => O.MitDbAsync(abfrage);

    public async Task AendernAsync(Func<SkoulouDbContext, Task> aenderung)
    {
        await using var db = await O.D.Db.CreateDbContextAsync();
        await aenderung(db);
        await db.SaveChangesAsync();
    }
}
