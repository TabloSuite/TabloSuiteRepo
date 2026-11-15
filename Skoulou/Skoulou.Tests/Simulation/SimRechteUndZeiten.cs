using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Finanzen;
using Skoulou.WebApp.Services.Noten;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Simulation;

internal sealed partial class Simulation
{
    // ---------- 7: jede Rolle bekommt « refusé » für Daten anderer Schulen (Stichprobe)

    private async Task RechteAsync()
    {
        var a = Schulen[8];
        var b = Schulen[9];
        var x = await d.MitDbAsync(async db => new
        {
            Schueler = b.Aktive.First().Id,
            Zahlung = await db.Zahlungen.Where(z => z.SchuleId == b.Id).Select(z => z.Id).FirstAsync(),
            Rechnung = await db.Plattformrechnungen.Where(r => r.SchuleId == b.Id).Select(r => r.Id).FirstAsync(),
            Bulletin = await db.Bulletins.Where(r => r.SchuleId == b.Id).Select(r => r.Id).FirstAsync(),
            Bewertung = await db.Bewertungen.Where(r => r.SchuleId == b.Id).Select(r => new { r.Id, r.KlassenfachId, r.PeriodeId }).FirstAsync(),
            Einschreibung = await db.Einschreibungen.Where(e => e.SchuleId == b.Id && e.Status == EinschreibungsStatus.Active).Select(e => e.Id).FirstAsync(),
            Zuweisung = await db.Zuweisungen.Where(z => z.SchuleId == b.Id).Select(z => z.Id).FirstOrDefaultAsync(),
            Buchung = await db.Buchungen.Where(r => r.SchuleId == b.Id).Select(r => r.Id).FirstAsync()
        });
        var lehrerA = a.Lehrer[0];
        var schuelerA = a.Aktive.First(s => s.Eltern is not null);
        var elternA = schuelerA.Eltern!.Value;
        var fremderResp = Responsables.First(r => r != b.Responsable);
        foreach (var k in new[] { schuelerA.Id, elternA }) await ErsteAnmeldungAsync(k);
        var vorher = Fehler.Count;

        var faelle = new (string Rolle, string Was, Func<Task> Aufruf)[]
        {
            ("Direction", "élève d'une autre école", () => api.Schueler.DetailAsync(a.Direktor, x.Schueler)),
            ("Direction", "frais d'un élève d'une autre école", () => api.Gebuehren.KontoAsync(a.Direktor, b.Jahr.Id, x.Schueler)),
            ("Direction", "annuler un paiement d'une autre école", () => api.Zahlungen.StornierenAsync(a.Direktor, x.Zahlung, "Test de droits.")),
            ("Direction", "facture SKOULOU d'une autre école", () => api.Rechnungen.DetailAsync(a.Direktor, x.Rechnung)),
            ("Direction", "bulletin d'une autre école", () => api.BulletinLesen.DruckAsync(a.Direktor, x.Bulletin)),
            ("Direction", "grille de notes d'une autre école", () => api.Noten.RasterAsync(a.Direktor, x.Bewertung.KlassenfachId, x.Bewertung.PeriodeId)),
            ("Direction", "contre-passer une écriture d'une autre école", () => api.Journal.GegenbuchungAsync(a.Direktor, x.Buchung, "Test de droits.")),
            ("Coordination sans droit", "liste des paiements", () => api.Zahlungen.SucheAsync(a.KoordOhne, a.Jahr.Id, null, null, 0, 50)),
            ("Coordination", "élève d'une autre école", () => api.Schueler.DetailAsync(a.KoordMit, x.Schueler)),
            ("Coordination", "paiement pour un élève d'une autre école", () => api.Zahlungen.ErfassenAsync(a.KoordMit, a.Jahr.Id,
                new ZahlungEingabe(Guid.NewGuid(), x.Schueler, 1_000, Ende, ZahlungsKanal.Especes, null, null))),
            ("Enseignant", "note dans une autre école", () => api.Noten.NoteSpeichernAsync(lehrerA, x.Bewertung.Id, x.Einschreibung, "10")),
            ("Enseignant", "frais d'un élève", () => api.Gebuehren.KontoAsync(lehrerA, a.Jahr.Id, schuelerA.Id)),
            ("Élève", "bulletins d'un autre élève", () => api.BulletinLesen.EigeneAsync(schuelerA.Id, x.Schueler)),
            ("Élève", "contenu attribué dans une autre école", () => x.Zuweisung == Guid.Empty ? throw new KeinZugriffFehler() : api.Lernen.OeffnenAsync(schuelerA.Id, x.Zuweisung)),
            ("Parent", "frais d'un enfant d'une autre famille", () => api.Familie.KontoAsync(elternA, x.Schueler)),
            ("Parent", "notes d'un enfant d'une autre famille", () => api.BulletinLesen.LiveNotenAsync(elternA, x.Schueler, null)),
            ("Responsable", "facture d'une école qu'il ne suit pas", () => api.Rechnungen.DetailAsync(fremderResp, x.Rechnung)),
            ("Responsable", "valider une école", () => api.Schulen.ValidierenAsync(fremderResp, WartendeSchule)),
            ("Administrateur", "enregistrer un paiement", () => api.Zahlungen.ErfassenAsync(Admin, b.Jahr.Id,
                new ZahlungEingabe(Guid.NewGuid(), x.Schueler, 1_000, Ende, ZahlungsKanal.Especes, null, null))),
        };
        foreach (var (rolle, was, aufruf) in faelle)
            await AbgelehntAsync($"{rolle}: {was}", aufruf);
        Pruefe(Fehler.Count == vorher, $"Rechte: {Fehler.Count - vorher} Aufruf(e) nicht mit « refusé » abgelehnt (siehe Befunde).");
        Geprueft.Add($"Rechte: {faelle.Length} fremde Zugriffe (Direction, Coordination, Enseignant, Élève, Parent, Responsable, Administrateur) abgelehnt");
    }

    // ---------- Antwortzeiten der Hauptlisten

    public List<(string Liste, double Ms)> Zeiten { get; } = [];

    /// <summary>Misst auf dem Endstand der Datenbank (auch ohne Simulationsobjekt, z. B. auf der behaltenen SkoulouDB_SIM).</summary>
    public async Task ZeitenMessenAsync()
    {
        d.Heute(Ende);
        var x = await d.MitDbAsync(async db =>
        {
            var laufend = await db.Schuljahre.Where(j => j.Status == SchuljahrStatus.Laufend)
                .OrderByDescending(j => db.Einschreibungen.Count(e => e.SchuljahrId == j.Id && e.Status == EinschreibungsStatus.Active))
                .Select(j => new { j.Id, j.SchuleId, j.Beginn }).FirstAsync();
            var vorjahr = await db.Schuljahre.Where(j => j.SchuleId == laufend.SchuleId && j.Beginn < laufend.Beginn).OrderByDescending(j => j.Beginn).Select(j => j.Id).FirstAsync();
            var klasse = await db.Klassen.Where(k => k.SchuljahrId == vorjahr).OrderBy(k => k.Name).Select(k => k.Id).FirstAsync();
            return new
            {
                Jahr = laufend.Id, Vorjahr = vorjahr, Klasse = klasse,
                Direktor = await db.Users.Where(u => u.SchuleId == laufend.SchuleId && u.Rolle == Skoulou.Core.Rechte.Rollen.Direktor).Select(u => u.Id).FirstAsync(),
                Klassenfach = await db.Klassenfaecher.Where(kf => kf.KlasseId == klasse).OrderBy(kf => kf.Reihenfolge).Select(kf => kf.Id).FirstAsync(),
                Periode = await db.Perioden.Where(p => p.SchuljahrId == vorjahr).OrderBy(p => p.Nr).Select(p => p.Id).FirstAsync(),
                Admin = await db.Users.Where(u => u.Rolle == Skoulou.Core.Rechte.Rollen.SuperAdmin).OrderByDescending(u => u.IstHauptAdmin).ThenBy(u => u.UserName).Select(u => u.Id).FirstAsync(),
                Sprache = await db.GebuehrenPosten.Where(p => p.PeriodeId != null && db.Schuljahre.Any(j => j.Id == p.SchuljahrId && j.Status == SchuljahrStatus.Laufend))
                    .Select(p => new
                    {
                        p.SchuljahrId,
                        Direktor = db.Users.Where(u => u.SchuleId == p.SchuleId && u.Rolle == Skoulou.Core.Rechte.Rollen.Direktor).Select(u => u.Id).First()
                    }).FirstOrDefaultAsync(),
                VorjahrZeitraum = await db.Schuljahre.Where(j => j.Id == vorjahr).Select(j => new { j.Beginn, j.Ende }).SingleAsync(),
                Rechnung = await db.Plattformrechnungen.Where(r => r.SchuleId == laufend.SchuleId).OrderByDescending(r => r.Anzahl).Select(r => r.Id).FirstAsync(),
                Zahlung = await db.Zahlungen.Where(z => z.SchuleId == laufend.SchuleId).OrderBy(z => z.Quittungsnummer).Select(z => z.Id).FirstAsync()
            };
        });
        if (Admin == Guid.Empty) Admin = x.Admin;
        var (jahr, dir) = (x.Jahr, x.Direktor);
        var vorjahr = new { Id = x.Vorjahr };
        var k = new { Id = x.Klasse };
        var kf = new { Id = x.Klassenfach };
        var p = new { Id = x.Periode };
        var messungen = new (string, Func<Task>)[]
        {
            ("Élèves (direction, 1re page)", () => api.Schueler.SucheAsync(dir, null, null, null, 0, 50)),
            ("Élèves (recherche « ko »)", () => api.Schueler.SucheAsync(dir, null, null, "ko", 0, 50)),
            ("Parents (direction)", () => api.Eltern.SucheAsync(dir, null, null, 0, 50)),
            ("Paiements (année en cours)", () => api.Zahlungen.SucheAsync(dir, jahr, null, null, 0, 50)),
            ("Paiements (année précédente)", () => api.Zahlungen.SucheAsync(dir, vorjahr.Id, null, null, 0, 50)),
            ("Comptes élèves / frais", () => api.Gebuehren.KontenAsync(dir, jahr, null, null, null, 0, 50)),
            ("Comptes élèves « en retard »", () => api.Gebuehren.KontenAsync(dir, jahr, null, GebuehrenService.FilterEnRetard, null, 0, 50)),
            ("Compteurs des frais", () => api.Gebuehren.ZaehlerAsync(dir, jahr, null)),
            ("Journal de caisse", () => api.Journal.SucheAsync(dir, new JournalFilter(null, null, null, null, null), 0, 50)),
            ("Grille de notes", () => api.Noten.RasterAsync(dir, kf.Id, p.Id)),
            ("Conseil de classe", () => api.Conseil.UebersichtAsync(dir, k.Id, p.Id)),
            ("Bulletins (année précédente)", () => api.BulletinLesen.SucheAsync(dir, vorjahr.Id, null, null, null, null, 0, 50)),
            ("Cockpit direction", () => api.Cockpit.LadeAsync(dir)),
            ("Classes (direction)", () => api.Klassen.KlassenAsync(dir, jahr)),
            ("Journal de l'école (direction)", () => api.Protokoll.SchulJournalAsync(dir, new ProtokollFilter(), null)),
            ("Journal de l'école (recherche « paiement »)", () => api.Protokoll.SchulJournalAsync(dir, new ProtokollFilter("paiement"), null)),
            ("Journal de l'école (Finances)", () => api.Protokoll.SchulJournalAsync(dir, new ProtokollFilter(), "finanzen")),
            ("Comptes élèves (école de langues, frais par session)", () => x.Sprache is { } sp ? api.Gebuehren.KontenAsync(sp.Direktor, sp.SchuljahrId, null, null, null, 0, 50) : Task.CompletedTask),
            ("Administrateurs (admin principal)", () => api.Administratoren.ListeAsync(Admin)),
            ("Factures SKOULOU (admin)", () => api.Rechnungen.ListeAsync(Admin, null, null, null, 0, 50)),
            ("Factures SKOULOU (école)", () => api.Rechnungen.ListeAsync(dir, null, null, null, 0, 50)),
            ("Compteurs factures (admin)", () => api.Rechnungen.ZaehlerAsync(Admin)),
            ("Écoles (admin)", () => api.Schulen.SucheAsync(Admin, null, null)),
            ("Comptes (admin)", () => api.AdminKonten.SucheAsync(Admin, null, null)),
            ("Comptes (admin, recherche « mensah »)", () => api.AdminKonten.SucheAsync(Admin, "mensah", null)),
            ("Parts des responsables (admin)", () => api.Anteile.ListeAsync(Admin, null, null, 0, 50)),
            ("Tableau de bord admin", () => d.Hole<PlattformUebersicht>().ZahlenAsync(Admin)),
            ("PDF bulletins d'une classe", () => d.Hole<BulletinPdfService>().KlasseAsync(dir, k.Id, p.Id, "https://skoulou.simulation")),
            ("PDF rapport financier (année précédente)", () => api.Bericht.PdfAsync(dir, x.VorjahrZeitraum.Beginn, x.VorjahrZeitraum.Ende)),
            ("PDF facture SKOULOU", () => api.Rechnungen.PdfAsync(dir, x.Rechnung)),
            ("PDF reçu", () => d.Hole<QuittungService>().PdfAsync(dir, x.Zahlung)),
            ("Audit (admin)", () => d.Hole<ProtokollService>().AuditAsync(Admin, new ProtokollFilter(), null)),
            ("Audit (admin, recherche « paiement »)", () => d.Hole<ProtokollService>().AuditAsync(Admin, new ProtokollFilter("paiement"), null)),
        };
        foreach (var (name, aufruf) in messungen)
        {
            try
            {
                await aufruf();
                var werte = new List<double>();
                for (var i = 0; i < 3; i++)
                {
                    var sw = Stopwatch.StartNew();
                    await aufruf();
                    werte.Add(sw.Elapsed.TotalMilliseconds);
                }
                Zeiten.Add((name, werte.Order().ElementAt(1)));
            }
            catch (Exception ex) when (ex is FachFehler or KeinZugriffFehler or InvalidOperationException or DbUpdateException)
            {
                Fehler.Enqueue($"Messung {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
