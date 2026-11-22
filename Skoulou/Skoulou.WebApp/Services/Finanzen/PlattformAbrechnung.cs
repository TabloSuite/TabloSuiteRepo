using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Finanzen;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Finanzen;

/// <summary>
/// Erzeugt die Plattformrechnungen je Abrechnungsperiode (Bauplan §5 Regel 7) – idempotent über den eindeutigen Index
/// (Periode, Schüler) der nicht stornierten Positionen. Später eingeschriebene Schüler ergeben eine Nachtragsrechnung.
/// Der automatische Lauf berechnet einen Schüler nicht erneut, dessen Position der Admin storniert hat; « Facturer
/// maintenant » (Admin) tut es ausdrücklich. Erinnerungen bei Fälligkeit und 7 Tage danach (Entscheidung 24).
/// </summary>
public sealed class PlattformAbrechnung(
    IDbContextFactory<SkoulouDbContext> dbFactory, NummernkreisService nummern, AuditService audit, FinanzMails mails,
    TimeProvider zeit, ILogger<PlattformAbrechnung> logger)
{
    private DateOnly Heute => new SchulZeit(zeit).Heute;

    private sealed record NeueRechnung(Guid Id, string Nummer, string Schule, Guid SchuleId, string Periode, int Anzahl, long Betrag, DateOnly Faellig);

    /// <summary>Rechnet eine Periode ab; null, wenn niemand (mehr) abzurechnen ist.</summary>
    /// <param name="akteurId">Admin bei « Facturer maintenant », null beim automatischen Lauf.</param>
    public async Task<Guid?> PeriodeAbrechnenAsync(Guid periodeId, Guid? akteurId, CancellationToken ct = default)
    {
        var manuell = akteurId is not null;
        var ergebnis = await dbFactory.TransaktionAsync<NeueRechnung?>(async db =>
        {
            var p = await db.Perioden.AsNoTracking().FirstOrDefaultAsync(x => x.Id == periodeId, ct) ?? throw new FachFehler("Cette période est introuvable.");
            if (!p.ZaehltFuerJahr) throw new FachFehler("Cette période ne compte pas pour l'année : elle n'est pas facturée.");
            var jahr = await db.Schuljahre.FirstAsync(j => j.Id == p.SchuljahrId, ct);
            if (p.Beginn > Heute) throw new FachFehler("Cette période n'a pas encore commencé : elle ne peut pas encore être facturée.");
            var schule = await db.Schulen.AsNoTracking().FirstAsync(s => s.Id == p.SchuleId, ct);
            // Probezeit (18.11.2026): diese Perioden stellt SKOULOU nicht in Rechnung – auch nicht über « Facturer maintenant ».
            if ((await EssaiService.ProbePeriodenAsync(db, schule, ct)).Contains(p.Id))
            {
                if (manuell) throw new FachFehler("Cette période fait partie de la période d'essai de l'établissement : elle n'est pas facturée.");
                return null;
            }
            if (VorAnmeldung(p, schule))
            {
                if (manuell) throw new FachFehler(MeldungVorAnmeldung);
                return null;
            }
            var einstellung = await db.PlattformEinstellungen.AsNoTracking().FirstOrDefaultAsync(ct) ?? new PlattformEinstellung();

            var (anteil, monate, monatspreis, ab) = Anteil(jahr, p, schule, einstellung);
            var kandidaten = await KandidatenAsync(db, p, ab, mitStornierten: !manuell, ct);
            if (kandidaten.Count == 0) return null;
            // Fehlersuche 21.11.2026 (A2), Nutzerentscheidung 22.11.: ein nie gestartetes Jahr mit eingeschriebenen Schülern wird
            // trotzdem abgerechnet – zum aktuellen Preis, der mit der ersten Rechnung für das Jahr eingefroren wird (wie beim Start).
            if (jahr.Status == SchuljahrStatus.Vorbereitung && jahr.PlattformMonatspreis is null)
            {
                jahr.PlattformMonatspreis = monatspreis;
                audit.Erfasse(db, akteurId, schule.Id, FinanzAudit.RechnungErstellt, nameof(Schuljahr), jahr.Id,
                    $"Année {jahr.Bezeichnung} non démarrée mais déjà utilisée : prix par élève et par mois figé à {SkoulouKultur.Fcfa(monatspreis)} avec la première facture.");
            }

            var heute = Heute;
            var haupt = await db.Plattformrechnungen.Where(r => r.PeriodeId == p.Id && r.ErgaenztId == null && r.Status != RechnungsStatus.Annulee)
                .OrderBy(r => r.Datum).ThenBy(r => r.Nummer).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
            var rechnung = new Plattformrechnung
            {
                Nummer = await nummern.NaechsteNummerAsync(db, null, NummernArt.Plattformrechnung, heute.Year, ct: ct),
                SchuleId = schule.Id, SchuljahrId = jahr.Id, PeriodeId = p.Id, Datum = heute, Faellig = heute.AddDays(Math.Max(0, einstellung.ZahlungszielTage)),
                Anzahl = kandidaten.Count, Betrag = anteil * kandidaten.Count, Status = RechnungsStatus.Ouverte, ErgaenztId = haupt
            };
            db.Plattformrechnungen.Add(rechnung);
            foreach (var k in kandidaten)
                db.Rechnungspositionen.Add(new Rechnungsposition { RechnungId = rechnung.Id, PeriodeId = p.Id, SchuelerId = k.SchuelerId, EinschreibungId = k.EinschreibungId, Betrag = anteil });
            audit.Erfasse(db, akteurId, schule.Id, FinanzAudit.RechnungErstellt, nameof(Plattformrechnung), rechnung.Id,
                $"Facture {rechnung.Nummer}{(haupt is null ? "" : " (complément)")} : {p.Titel} {jahr.Bezeichnung}, {kandidaten.Count} élève(s) × {SkoulouKultur.Fcfa(anteil)} ({monate} mois × {SkoulouKultur.Fcfa(monatspreis)}{(ab > p.Beginn ? $", à partir du {SkoulouKultur.Datum(ab)} – mois d'inscription de l'établissement" : "")}) = {SkoulouKultur.Fcfa(rechnung.Betrag)}{(manuell ? " – facturée par l'administrateur" : "")}.");
            await db.SaveChangesAsync(ct);
            return new NeueRechnung(rechnung.Id, rechnung.Nummer, schule.Name, rechnung.SchuleId, $"{p.Titel} {jahr.Bezeichnung}", rechnung.Anzahl, rechnung.Betrag, rechnung.Faellig);
        }, ct);
        if (ergebnis is null) return null;
        await mails.RechnungAsync(await DirektionsAdressenAsync(ergebnis.SchuleId, ct), ergebnis.Schule, ergebnis.Nummer, ergebnis.Periode,
            ergebnis.Anzahl, ergebnis.Betrag, ergebnis.Faellig, 0, ct);
        return ergebnis.Id;
    }

    public const string MeldungVorAnmeldung = "Cette période s'est terminée avant l'inscription de l'établissement sur SKOULOU : elle n'est pas facturée.";

    /// <summary>
    /// Perioden, die vor der Anmeldung der Schule endeten, stellt SKOULOU nie in Rechnung (wie bei den Kandidaten der
    /// Probezeit). Seit 21.11.2026 beginnen Einschreibungen standardmäßig am Beginn des Schuljahres – eine Schule, die
    /// mitten im Jahr startet, zahlt dadurch nicht rückwirkend für Perioden, in denen sie SKOULOU noch nicht nutzte.
    /// </summary>
    internal static bool VorAnmeldung(Periode p, Schule schule) => p.Ende < Anmeldung(schule);

    /// <summary>Tag der Anmeldung der Schule bei SKOULOU.</summary>
    internal static DateOnly Anmeldung(Schule schule) => DateOnly.FromDateTime(schule.ErstelltAmUtc);

    /// <summary>
    /// Anteil je Schüler für die Periode (Regel 7, seit 19.11.2026): eingefrorener Monatspreis × Monate der Periode,
    /// sowie die Monate, der Monatspreis und der erste berechnete Tag (für Audit, Rechnung, PDF und Dialog). Seit 21.11.2026
    /// zählt die Periode der Anmeldung erst ab dem Anmeldemonat (<see cref="PlattformAnteil.AbrechnungsBeginn"/>).
    /// </summary>
    internal static (long Anteil, int Monate, long Monatspreis, DateOnly Ab) Anteil(Schuljahr jahr, Periode p, Schule schule, PlattformEinstellung einstellung)
    {
        var preis = jahr.PlattformMonatspreis ?? schule.MonatspreisOverride ?? einstellung.MonatspreisProSchueler;
        var ab = PlattformAnteil.AbrechnungsBeginn(p.Beginn, p.Ende, Anmeldung(schule));
        return (PlattformAnteil.Anteil(preis, ab, p.Ende), PlattformAnteil.Monate(ab, p.Ende), preis, ab);
    }

    /// <summary>Abrechnungspflichtige, noch nicht abgerechnete Schüler der Periode.</summary>
    /// <param name="ab">Erster berechneter Tag (<see cref="Anteil"/>): in der Periode der Anmeldung der 1. des Anmeldemonats. Wer davor
    /// schon ausgetreten war, schneidet den berechneten Zeitraum nicht (Klicktest 21.11.2026: Austritt 15.10., Anmeldung 23.11. → berechnet).</param>
    internal static async Task<IReadOnlyList<AbrechnungsKandidat>> KandidatenAsync(SkoulouDbContext db, Periode p, DateOnly ab, bool mitStornierten, CancellationToken ct)
    {
        var beginn = ab > p.Beginn ? ab : p.Beginn;
        var einschreibungen = await (from e in SchulAbfragen.EinschreibungenImZeitraum(db, p.SchuljahrId, beginn, p.Ende)
                                     join k in db.Klassen on e.KlasseId equals k.Id
                                     join s in db.Perioden on k.PeriodeId equals (Guid?)s.Id into sj
                                     from s in sj.DefaultIfEmpty()
                                     select new EinschreibungsZeitraum(e.SchuelerId, e.Id, e.Von, e.Bis,
                                         s == null ? (DateOnly?)null : s.Beginn, s == null ? (DateOnly?)null : s.Ende)).ToListAsync(ct);
        var positionen = db.Rechnungspositionen.Where(x => x.PeriodeId == p.Id);
        if (!mitStornierten) positionen = positionen.Where(x => x.StorniertGrund == null);
        var bereits = (await positionen.Select(x => x.SchuelerId).ToListAsync(ct)).ToHashSet();
        return Abrechnungspflicht.Kandidaten(einschreibungen, beginn, p.Ende, bereits);
    }

    /// <summary>Täglicher Lauf: alle beendeten Abrechnungsperioden aktiver (oder suspendierter) Schulen der letzten zwei Jahre.</summary>
    public async Task<int> FaelligeAbrechnenAsync(CancellationToken ct = default)
    {
        var heute = Heute;
        List<Guid> perioden;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            perioden = await (from p in db.Perioden
                              join j in db.Schuljahre on p.SchuljahrId equals j.Id
                              join s in db.Schulen on p.SchuleId equals s.Id
                              where p.ZaehltFuerJahr && p.Ende < heute && p.Ende >= heute.AddYears(-2)
                                    && (s.Status == SchulStatus.Active || s.Status == SchulStatus.Suspendue)
                              orderby p.Ende
                              select p.Id).ToListAsync(ct);
        }
        var neu = 0;
        foreach (var id in perioden)
        {
            try
            {
                if (await PeriodeAbrechnenAsync(id, null, ct) is not null) neu++;
            }
            catch (Exception ex) when (ex is FachFehler or DbUpdateException)
            {
                logger.LogWarning(ex, "Periode {Periode} nicht abgerechnet.", id);
            }
        }
        return neu;
    }

    /// <summary>Zahlungserinnerungen: bei Fälligkeit und 7 Tage danach, nur für offene Rechnungen (sperrt nichts).</summary>
    public async Task<int> ErinnernAsync(CancellationToken ct = default)
    {
        var heute = Heute;
        var spaet = heute.AddDays(-7);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var faellig = await (from r in db.Plattformrechnungen
                             where r.Status == RechnungsStatus.Ouverte
                                   && ((r.Erinnerungen == 0 && r.Faellig <= heute) || (r.Erinnerungen == 1 && r.Faellig <= spaet))
                             join s in db.Schulen on r.SchuleId equals s.Id
                             join p in db.Perioden on r.PeriodeId equals p.Id
                             select new { r, Schule = s.Name, Periode = p.Titel }).Take(200).ToListAsync(ct);
        var gesendet = 0;
        foreach (var x in faellig)
        {
            x.r.Erinnerungen = x.r.Faellig <= spaet && x.r.Erinnerungen == 0 ? 2 : x.r.Erinnerungen + 1;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                continue;
            }
            await mails.RechnungAsync(await DirektionsAdressenAsync(x.r.SchuleId, ct), x.Schule, x.r.Nummer, x.Periode, x.r.Anzahl, x.r.Betrag,
                x.r.Faellig, x.r.Erinnerungen, ct);
            gesendet++;
        }
        return gesendet;
    }

    /// <summary>Adressen der Direktion (Direktor, Koordinator mit Rechnungsrecht) und der Schule.</summary>
    internal async Task<List<string?>> DirektionsAdressenAsync(Guid schuleId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var konten = await db.Users.Where(k => k.SchuleId == schuleId && k.Status == KontoStatus.Actif
                                               && (k.Rolle == Rollen.Direktor || k.Rolle == Rollen.Koordinator))
            .Select(k => new { k.Rolle, k.KoordinatorRechte, k.Email }).ToListAsync(ct);
        var liste = konten.Where(k => k.Rolle == Rollen.Direktor || k.KoordinatorRechte.HasFlag(KoordinatorRechte.RechnungSkoulou)).Select(k => k.Email).ToList();
        liste.Add(await db.Schulen.Where(s => s.Id == schuleId).Select(s => s.Email).FirstOrDefaultAsync(ct));
        return liste;
    }
}

/// <summary>Hintergrunddienst: einmal täglich Rechnungen nach Periodenende und Erinnerungen (jeder Lauf idempotent).</summary>
public sealed class RechnungsDienst(IServiceScopeFactory scopes, ILogger<RechnungsDienst> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SkoulouDbContext>>();
                await using (var kontext = await db.CreateDbContextAsync(stoppingToken))
                    await FinanzKategorien.SicherstellenAsync(kontext, stoppingToken);
                var abrechnung = scope.ServiceProvider.GetRequiredService<PlattformAbrechnung>();
                var neu = await abrechnung.FaelligeAbrechnenAsync(stoppingToken);
                var erinnert = await abrechnung.ErinnernAsync(stoppingToken);
                if (neu + erinnert > 0) logger.LogInformation("Facturation SKOULOU : {Neu} facture(s), {Erinnert} rappel(s).", neu, erinnert);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Lauf der Plattformabrechnung fehlgeschlagen.");
            }

            try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
