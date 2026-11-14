using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Finanzen;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Medien;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Finanzen;

public sealed record RechnungZeile(Guid Id, string Nummer, Guid SchuleId, string Schule, string Periode, string Schuljahr, DateOnly Datum,
    DateOnly Faellig, int Anzahl, long Betrag, RechnungsStatus Status, string? Grund, bool IstNachtrag, int Erinnerungen)
{
    public bool Ueberfaellig(DateOnly heute) => Status == RechnungsStatus.Ouverte && Faellig < heute;
}
public sealed record PositionZeile(string Name, string? Matrikel, string? Klasse, long Betrag, string? StorniertGrund);
public sealed record MeldungZeile(long Betrag, DateOnly Datum, string? Referenz, Guid? BuchungId, bool HatBeleg, string GemeldetVon, DateTime AmUtc);
public sealed record AnteilKurz(string Responsable, decimal Prozent, long Betrag, DateTime? AusgezahltAmUtc);
public sealed record RechnungDetail(RechnungZeile Kopf, long ProSchueler, IReadOnlyList<PositionZeile> Positionen, IReadOnlyList<MeldungZeile> Meldungen,
    IReadOnlyList<AnteilKurz> Anteile, string? BestaetigtVon, DateTime? BestaetigtAmUtc, string? ErgaenztNummer);
public sealed record RechnungsZaehler(int Alle, int Ouvertes, int Declarees, int Contestees, int Payees, int Annulees, int EnRetard, long ResteAPayer);
public sealed record MeldungEingabe(long? Betrag, DateOnly? Datum, string? Referenz);
public sealed record AbrechenbarePeriode(Guid PeriodeId, string Titel, string Schuljahr, DateOnly Beginn, DateOnly Ende, bool Beendet, int Abrechenbar, long ProSchueler)
{
    public bool Begonnen(DateOnly heute) => Beginn <= heute;
}

/// <summary>
/// Plattformrechnungen (SKOULOU → Schule) aus Sicht der Schule (lesen, Zahlung melden, anfechten), des Admins (alle, bestätigen,
/// Anfechtung beantworten, stornieren, « Facturer maintenant ») und des Responsable (seine Schulen, nur lesen).
/// Statuswechsel sind über die RowVersion der Rechnung gegen parallele Änderungen geschützt.
/// </summary>
public sealed class RechnungService(
    IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, NummernkreisService nummern,
    PlattformAbrechnung abrechnung, IMedienSpeicher speicher, Microsoft.Extensions.Options.IOptions<AppOptionen> app, TimeProvider zeit,
    ILogger<RechnungService> logger)
{
    public const string FilterOuvertes = "ouvertes";
    public const string FilterDeclarees = "declarees";
    public const string FilterContestees = "contestees";
    public const string FilterPayees = "payees";
    public const string FilterAnnulees = "annulees";
    public const string FilterRetard = "retard";
    public const string MeldungKonflikt = "La facture vient d'être modifiée par quelqu'un d'autre. Rechargez la page.";

    private static readonly Faehigkeit[] SchuleLesen = [Faehigkeit.RechnungSkoulouBearbeiten, Faehigkeit.FinanzenEinsehen];

    private DateOnly Heute => new SchulZeit(zeit).Heute;

    // ---------- Lesen ----------

    /// <summary>Welche Schulen darf der Akteur sehen? null = alle (Admin).</summary>
    private async Task<(Akteur Akteur, Guid[]? Schulen)> SichtAsync(Guid akteurId, CancellationToken ct)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        if (akteur.IstDirektion)
        {
            var a = await zugriff.DirektionAsync(akteurId, false, ct, SchuleLesen);
            return (akteur, [a.SchuleId]);
        }
        Zugriff.Pruefe(akteur, Faehigkeit.RechnungSkoulouBearbeiten);
        if (akteur.IstAdmin) return (akteur, null);
        if (akteur.Rolle != Rollen.Responsable) throw new KeinZugriffFehler();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (akteur, await db.Schulen.Where(s => s.ResponsableId == akteur.Id).Select(s => s.Id).ToArrayAsync(ct));
    }

    public async Task<Seite<RechnungZeile>> ListeAsync(Guid akteurId, string? filter, Guid? schuleId, string? suche, int ueberspringen, int anzahl,
        CancellationToken ct = default)
    {
        var (_, schulen) = await SichtAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = Gefiltert(db, schulen, schuleId, filter);
        if (Eingabe.Suche(suche) is { } s)
        {
            var muster = $"%{s}%";
            q = q.Where(r => EF.Functions.Like(r.Nummer, muster) || db.Schulen.Any(x => x.Id == r.SchuleId && EF.Functions.Like(x.Name, muster)));
        }
        return await Seite<RechnungZeile>.LadeAsync(Projektion(db, q.OrderByDescending(r => r.Datum).ThenByDescending(r => r.Nummer)), ueberspringen, anzahl, ct);
    }

    public async Task<RechnungsZaehler> ZaehlerAsync(Guid akteurId, Guid? schuleId = null, CancellationToken ct = default)
    {
        var (_, schulen) = await SichtAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = Gefiltert(db, schulen, schuleId, null);
        var heute = Heute;
        var gruppen = await q.GroupBy(r => r.Status).Select(g => new { g.Key, Anzahl = g.Count(), Summe = g.Sum(r => r.Betrag) }).ToListAsync(ct);
        int Zahl(RechnungsStatus s) => gruppen.FirstOrDefault(g => g.Key == s)?.Anzahl ?? 0;
        return new RechnungsZaehler(gruppen.Sum(g => g.Anzahl), Zahl(RechnungsStatus.Ouverte), Zahl(RechnungsStatus.PaiementDeclare),
            Zahl(RechnungsStatus.Contestee), Zahl(RechnungsStatus.Payee), Zahl(RechnungsStatus.Annulee),
            await q.CountAsync(r => r.Status == RechnungsStatus.Ouverte && r.Faellig < heute, ct),
            gruppen.Where(g => g.Key is RechnungsStatus.Ouverte or RechnungsStatus.Contestee or RechnungsStatus.PaiementDeclare).Sum(g => g.Summe));
    }

    private IQueryable<Plattformrechnung> Gefiltert(SkoulouDbContext db, Guid[]? schulen, Guid? schuleId, string? filter)
    {
        var q = db.Plattformrechnungen.AsQueryable();
        if (schulen is not null) q = q.Where(r => schulen.Contains(r.SchuleId));
        if (schuleId is { } s) q = q.Where(r => r.SchuleId == s);
        var heute = Heute;
        return filter switch
        {
            FilterOuvertes => q.Where(r => r.Status == RechnungsStatus.Ouverte),
            FilterDeclarees => q.Where(r => r.Status == RechnungsStatus.PaiementDeclare),
            FilterContestees => q.Where(r => r.Status == RechnungsStatus.Contestee),
            FilterPayees => q.Where(r => r.Status == RechnungsStatus.Payee),
            FilterAnnulees => q.Where(r => r.Status == RechnungsStatus.Annulee),
            FilterRetard => q.Where(r => r.Status == RechnungsStatus.Ouverte && r.Faellig < heute),
            _ => q
        };
    }

    private static IQueryable<RechnungZeile> Projektion(SkoulouDbContext db, IQueryable<Plattformrechnung> q)
        => from r in q
           join s in db.Schulen on r.SchuleId equals s.Id
           join p in db.Perioden on r.PeriodeId equals p.Id
           join j in db.Schuljahre on r.SchuljahrId equals j.Id
           select new RechnungZeile(r.Id, r.Nummer, r.SchuleId, s.Name, p.Titel, j.Bezeichnung, r.Datum, r.Faellig, r.Anzahl, r.Betrag, r.Status,
               r.Grund, r.ErgaenztId != null, r.Erinnerungen);

    public async Task<RechnungDetail> DetailAsync(Guid akteurId, Guid rechnungId, CancellationToken ct = default)
    {
        var (_, schulen) = await SichtAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var kopf = await Projektion(db, db.Plattformrechnungen.Where(r => r.Id == rechnungId)).FirstOrDefaultAsync(ct)
                   ?? throw new FachFehler("Cette facture est introuvable.");
        if (schulen is not null && !schulen.Contains(kopf.SchuleId)) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
        var r = await db.Plattformrechnungen.AsNoTracking().FirstAsync(x => x.Id == rechnungId, ct);
        var positionen = await (from p in db.Rechnungspositionen
                                where p.RechnungId == rechnungId
                                join k in db.Users on p.SchuelerId equals k.Id
                                join e in db.Einschreibungen on p.EinschreibungId equals e.Id
                                join kl in db.Klassen on e.KlasseId equals kl.Id
                                join pr in db.SchuelerProfile on k.Id equals pr.KontoId into prj
                                from pr in prj.DefaultIfEmpty()
                                orderby kl.Name, k.Nachname, k.Vorname
                                select new PositionZeile(k.Nachname + " " + k.Vorname, pr == null ? null : pr.Matrikel, kl.Name, p.Betrag, p.StorniertGrund))
            .ToListAsync(ct);
        var meldungen = await (from m in db.Zahlungsmeldungen
                               where m.RechnungId == rechnungId
                               join k in db.Users on m.GemeldetVonId equals k.Id
                               orderby m.ErstelltAmUtc
                               select new MeldungZeile(m.Betrag, m.Datum, m.Referenz, m.BuchungId, m.BelegMediumId != null, k.Vorname + " " + k.Nachname, m.ErstelltAmUtc))
            .ToListAsync(ct);
        var anteile = await (from a in db.ResponsableAnteile
                             where a.RechnungId == rechnungId
                             join k in db.Users on a.ResponsableId equals k.Id
                             orderby a.ErstelltAmUtc
                             select new AnteilKurz(k.Vorname + " " + k.Nachname, a.Prozent, a.Betrag, a.AusgezahltAmUtc)).ToListAsync(ct);
        var bestaetigt = r.BestaetigtVonId is { } b ? await db.Users.Where(k => k.Id == b).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefaultAsync(ct) : null;
        var ergaenzt = r.ErgaenztId is { } eid ? await db.Plattformrechnungen.Where(x => x.Id == eid).Select(x => x.Nummer).FirstOrDefaultAsync(ct) : null;
        return new RechnungDetail(kopf, r.Anzahl == 0 ? 0 : r.Betrag / r.Anzahl, positionen, meldungen, anteile, bestaetigt, r.BestaetigtAmUtc, ergaenzt);
    }

    public async Task<Organisation.Druck.PdfDatei> PdfAsync(Guid akteurId, Guid rechnungId, CancellationToken ct = default)
    {
        var detail = await DetailAsync(akteurId, rechnungId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var schule = await QuittungService.SchulDokumentAsync(db, speicher, detail.Kopf.SchuleId, ct);
        var webseite = Uri.TryCreate(app.Value.BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : "skoulou.tablosuite.com";
        return new Organisation.Druck.PdfDatei(RechnungPdf.Erzeuge(detail, schule, webseite), $"facture-{detail.Kopf.Nummer}.pdf");
    }

    // ---------- Schule ----------

    /// <summary>« Déclarer le paiement »: Meldung + Ausgabe « Facture SKOULOU » im Kassenjournal der Schule.</summary>
    public async Task MeldenAsync(Guid akteurId, Guid rechnungId, MeldungEingabe e, BelegDatei? beleg, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, true, ct, Faehigkeit.RechnungSkoulouBearbeiten);
        var betrag = FinanzEingabe.Betrag(e.Betrag);
        var datum = FinanzEingabe.Datum(e.Datum, Heute);
        var referenz = Eingabe.Pflicht(e.Referenz, 100, "la référence du virement", "referenz");
        Medium? medium = null;
        if (beleg is not null)
        {
            var (inhalt, typ) = await BelegSpeicher.LeseAsync(beleg.Inhalt, ct);
            medium = await BelegSpeicher.SpeichereAsync(speicher, a.SchuleId, a.Id, inhalt, typ, beleg.Dateiname, ct);
        }
        try
        {
            await StatusAsync(async db =>
            {
                var r = await RechnungAsync(db, rechnungId, ct);
                a.PruefeSchule(r.SchuleId);
                if (r.Status != RechnungsStatus.Ouverte) throw new FachFehler($"Cette facture est « {FinanzEingabe.StatusText(r.Status)} » : le paiement ne peut plus être déclaré.");
                var kategorie = await FinanzKategorien.SystemAsync(db, FinanzKategorien.FactureSkoulou, ct);
                var buchung = new Buchung
                {
                    SchuleId = r.SchuleId, Datum = datum, Richtung = BuchungsRichtung.Depense, KategorieId = kategorie.Id, Betrag = betrag,
                    Text = Kassierer.Kuerze($"Facture SKOULOU {r.Nummer} – virement {referenz}"), Herkunft = BuchungsHerkunft.FactureSkoulou,
                    HerkunftId = r.Id, BelegMediumId = medium?.Id, ErfasstVonId = a.Id,
                    Nummer = await nummern.NaechsteNummerAsync(db, r.SchuleId, NummernArt.Ecriture, datum.Year, ct: ct)
                };
                if (medium is not null)
                {
                    db.Medien.Add(medium);
                    db.MedienNutzungen.Add(new MedienNutzung { MediumId = medium.Id, Typ = "Buchung.Beleg", ZielId = buchung.Id });
                }
                db.Buchungen.Add(buchung);
                db.Zahlungsmeldungen.Add(new Zahlungsmeldung
                {
                    RechnungId = r.Id, Betrag = betrag, Datum = datum, Referenz = referenz, BelegMediumId = medium?.Id, GemeldetVonId = a.Id, BuchungId = buchung.Id
                });
                r.Status = RechnungsStatus.PaiementDeclare;
                r.Grund = null;
                audit.Erfasse(db, a.Id, r.SchuleId, AuditAktionen.RechnungStatus, nameof(Plattformrechnung), r.Id,
                    $"Paiement de la facture {r.Nummer} déclaré : {SkoulouKultur.Fcfa(betrag)} le {SkoulouKultur.Datum(datum)} (réf. {referenz}).");
            }, ct);
        }
        catch
        {
            if (medium is not null)
            {
                try { await speicher.LoeschenAsync(medium.BlobName); }
                catch (Exception ex) { logger.LogWarning(ex, "Beleg {Blob} nach Fehler nicht gelöscht.", medium.BlobName); }
            }
            throw;
        }
    }

    public async Task AnfechtenAsync(Guid akteurId, Guid rechnungId, string? grund, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, true, ct, Faehigkeit.RechnungSkoulouBearbeiten);
        var motiv = Eingabe.Pflicht(grund, 500, "le motif de la contestation", "grund");
        await StatusAsync(async db =>
        {
            var r = await RechnungAsync(db, rechnungId, ct);
            a.PruefeSchule(r.SchuleId);
            if (r.Status != RechnungsStatus.Ouverte) throw new FachFehler("Seule une facture à payer peut être contestée.");
            (r.Status, r.Grund) = (RechnungsStatus.Contestee, motiv);
            audit.Erfasse(db, a.Id, r.SchuleId, AuditAktionen.RechnungStatus, nameof(Plattformrechnung), r.Id, $"Facture {r.Nummer} contestée : {motiv}");
        }, ct);
    }

    // ---------- Admin ----------

    private async Task<Akteur> AdminAsync(Guid akteurId, CancellationToken ct)
        => (await zugriff.PruefeAsync(akteurId, Faehigkeit.PlattformVerwalten, null, true, ct)).Akteur;

    /// <summary>Bankeingang bestätigt → « Payée »; der Responsable der Schule erhält seinen Anteil (eingefroren, abgerundet).</summary>
    public async Task BestaetigenAsync(Guid akteurId, Guid rechnungId, CancellationToken ct = default)
    {
        var admin = await AdminAsync(akteurId, ct);
        await StatusAsync(async db =>
        {
            var r = await RechnungAsync(db, rechnungId, ct);
            if (r.Status is not (RechnungsStatus.Ouverte or RechnungsStatus.PaiementDeclare))
                throw new FachFehler($"Une facture « {FinanzEingabe.StatusText(r.Status)} » ne peut pas être confirmée.");
            (r.Status, r.BestaetigtVonId, r.BestaetigtAmUtc, r.Grund) = (RechnungsStatus.Payee, admin.Id, zeit.GetUtcNow().UtcDateTime, null);
            var schule = await db.Schulen.AsNoTracking().FirstAsync(s => s.Id == r.SchuleId, ct);
            var text = $"Facture {r.Nummer} confirmée payée ({SkoulouKultur.Fcfa(r.Betrag)}).";
            if (schule.ResponsableId is { } responsable)
            {
                var standard = await db.PlattformEinstellungen.Select(p => (decimal?)p.ResponsableProzentStandard).FirstOrDefaultAsync(ct) ?? 0;
                var prozent = schule.ResponsableProzent ?? standard;
                var betrag = ResponsableVerguetung.Betrag(r.Betrag, prozent);
                if (betrag > 0)
                {
                    db.ResponsableAnteile.Add(new ResponsableAnteil { ResponsableId = responsable, RechnungId = r.Id, SchuleId = r.SchuleId, Prozent = prozent, Betrag = betrag });
                    text += $" Part du responsable : {prozent:0.##} % = {SkoulouKultur.Fcfa(betrag)}.";
                }
            }
            audit.Erfasse(db, admin.Id, r.SchuleId, AuditAktionen.RechnungStatus, nameof(Plattformrechnung), r.Id, text);
        }, ct);
    }

    /// <summary>Anfechtung beantwortet: die Rechnung bleibt bestehen und ist wieder zu zahlen.</summary>
    public async Task AufrechterhaltenAsync(Guid akteurId, Guid rechnungId, string? antwort, CancellationToken ct = default)
    {
        var admin = await AdminAsync(akteurId, ct);
        var text = Eingabe.Pflicht(antwort, 500, "votre réponse à l'établissement", "grund");
        await StatusAsync(async db =>
        {
            var r = await RechnungAsync(db, rechnungId, ct);
            if (r.Status != RechnungsStatus.Contestee) throw new FachFehler("Cette facture n'est pas contestée.");
            (r.Status, r.Grund) = (RechnungsStatus.Ouverte, $"Contestation examinée : {text}");
            audit.Erfasse(db, admin.Id, r.SchuleId, AuditAktionen.RechnungStatus, nameof(Plattformrechnung), r.Id, $"Facture {r.Nummer} maintenue : {text}");
        }, ct);
    }

    /// <summary>Storno mit Grund: Positionen werden frei (neu abrechenbar); war sie bestätigt, entsteht ein negativer Ausgleichsanteil.</summary>
    public async Task StornierenAsync(Guid akteurId, Guid rechnungId, string? grund, CancellationToken ct = default)
    {
        var admin = await AdminAsync(akteurId, ct);
        var motiv = Eingabe.Pflicht(grund, 300, "le motif de l'annulation", "grund");
        await StatusAsync(async db =>
        {
            var r = await RechnungAsync(db, rechnungId, ct);
            if (r.Status == RechnungsStatus.Annulee) throw new FachFehler("Cette facture est déjà annulée.");
            var warBezahlt = r.Status == RechnungsStatus.Payee;
            (r.Status, r.Grund) = (RechnungsStatus.Annulee, motiv);
            var jetzt = zeit.GetUtcNow().UtcDateTime;
            foreach (var p in await db.Rechnungspositionen.Where(p => p.RechnungId == r.Id && p.StorniertGrund == null).ToListAsync(ct))
                (p.StorniertGrund, p.StorniertAmUtc) = (motiv, jetzt);
            if (warBezahlt)
            {
                var anteile = await db.ResponsableAnteile.Where(x => x.RechnungId == r.Id).ToListAsync(ct);
                var ausgeglichen = anteile.Where(x => x.AusgleichFuerId is not null).Select(x => x.AusgleichFuerId!.Value).ToHashSet();
                foreach (var anteil in anteile.Where(x => x.AusgleichFuerId is null && !ausgeglichen.Contains(x.Id)))
                    db.ResponsableAnteile.Add(new ResponsableAnteil
                    {
                        ResponsableId = anteil.ResponsableId, RechnungId = r.Id, SchuleId = r.SchuleId, Prozent = anteil.Prozent,
                        Betrag = -anteil.Betrag, AusgleichFuerId = anteil.Id
                    });
            }
            audit.Erfasse(db, admin.Id, r.SchuleId, AuditAktionen.RechnungStatus, nameof(Plattformrechnung), r.Id, $"Facture {r.Nummer} annulée : {motiv}");
        }, ct);
    }

    public async Task<Guid?> JetztAbrechnenAsync(Guid akteurId, Guid periodeId, CancellationToken ct = default)
    {
        var admin = await AdminAsync(akteurId, ct);
        return await abrechnung.PeriodeAbrechnenAsync(periodeId, admin.Id, ct);
    }

    /// <summary>Abrechnungsperioden einer Schule mit der Zahl der noch nicht abgerechneten Schüler (Dialog « Facturer maintenant »).</summary>
    public async Task<IReadOnlyList<AbrechenbarePeriode>> AbrechenbareAsync(Guid akteurId, Guid schuleId, CancellationToken ct = default)
    {
        await AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var schule = await db.Schulen.AsNoTracking().FirstOrDefaultAsync(s => s.Id == schuleId, ct) ?? throw new FachFehler("Cet établissement est introuvable.");
        var einstellung = await db.PlattformEinstellungen.AsNoTracking().FirstOrDefaultAsync(ct) ?? new PlattformEinstellung();
        var jahre = await db.Schuljahre.AsNoTracking().Where(j => j.SchuleId == schuleId && j.Status != SchuljahrStatus.Vorbereitung)
            .OrderByDescending(j => j.Beginn).Take(2).ToListAsync(ct);
        var heute = Heute;
        var liste = new List<AbrechenbarePeriode>();
        foreach (var jahr in jahre)
            foreach (var p in await db.Perioden.AsNoTracking().Where(p => p.SchuljahrId == jahr.Id && p.ZaehltFuerJahr).OrderBy(p => p.Beginn).ToListAsync(ct))
            {
                var (anteil, _, _) = await PlattformAbrechnung.AnteilAsync(db, jahr, p, schule, einstellung, ct);
                var kandidaten = await PlattformAbrechnung.KandidatenAsync(db, p, mitStornierten: false, ct);
                liste.Add(new AbrechenbarePeriode(p.Id, p.Titel, jahr.Bezeichnung, p.Beginn, p.Ende, p.Ende < heute, kandidaten.Count, anteil));
            }
        return liste;
    }

    public async Task<IReadOnlyList<AuswahlEintrag>> SchulenAsync(Guid akteurId, CancellationToken ct = default)
    {
        var (_, schulen) = await SichtAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Schulen.Where(s => s.Status == SchulStatus.Active || s.Status == SchulStatus.Suspendue);
        if (schulen is not null) q = q.Where(s => schulen.Contains(s.Id));
        return await q.OrderBy(s => s.Name).Select(s => new AuswahlEintrag(s.Id, s.Name, s.Ort)).ToListAsync(ct);
    }

    // ---------- Hilfen ----------

    private static async Task<Plattformrechnung> RechnungAsync(SkoulouDbContext db, Guid id, CancellationToken ct)
        => await db.Plattformrechnungen.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new FachFehler("Cette facture est introuvable.");

    /// <summary>Statuswechsel in einer Transaktion; parallele Änderungen derselben Rechnung → verständliche Meldung.</summary>
    private async Task StatusAsync(Func<SkoulouDbContext, Task> arbeit, CancellationToken ct)
    {
        try
        {
            await dbFactory.TransaktionAsync(async db =>
            {
                await arbeit(db);
                await db.SaveChangesAsync(ct);
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new FachFehler(MeldungKonflikt);
        }
    }
}
