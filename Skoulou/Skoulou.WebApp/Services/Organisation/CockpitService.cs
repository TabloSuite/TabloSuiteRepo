using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Organisation;

/// <summary>Ein Schritt der Einrichtung einer neuen Schule (Cockpit « Prochaines étapes »).</summary>
public sealed record CockpitSchritt(string Titel, string Text, string Href, bool Erledigt);

/// <summary>Kennzahlen des laufenden Schuljahres für die Startseite der Direktion.</summary>
public sealed record CockpitDaten(
    Guid? JahrId, string? Jahr, SchuljahrStatus? JahrStatus,
    int SchuelerAktiv, int AValider, int OhneKlasse, int Klassen, int Lehrer, int LehrerOhneAuftrag,
    int AbsencesHeute, int RetardsHeute, int KlassenMitAppellHeute, int ElevesEnClasseHeute,
    IReadOnlyList<CockpitSchritt> Schritte)
{
    public bool EinrichtungFertig => Schritte.All(s => s.Erledigt);
}

/// <summary>Cockpit der Direktion (Bauplan §10 E3): echte Zahlen per COUNT, Checkliste für neue Schulen.</summary>
public sealed class CockpitService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, TimeProvider zeit)
{
    public async Task<CockpitDaten> LadeAsync(Guid akteurId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var schulZone = await db.Schulen.AsNoTracking().Where(s => s.Id == a.SchuleId).Select(s => s.Zeitzone).FirstAsync(ct);
        var heute = new SchulZeit(zeit, schulZone).Heute;
        var jahr = await SchulAbfragen.LaufendesSchuljahrAsync(db, a.SchuleId, ct);
        var jahrId = jahr?.Id;

        var schueler = db.Users.AsNoTracking().Where(k => k.SchuleId == a.SchuleId && k.Rolle == Rollen.Schueler);
        var aktive = SchulAbfragen.LaufendeEinschreibungen(db, heute);
        var schuelerAktiv = await schueler.CountAsync(k => k.Status == KontoStatus.Actif, ct);
        var aValider = await schueler.CountAsync(k => k.Status == KontoStatus.EnAttente, ct);
        var ohneKlasse = await schueler.CountAsync(k => k.Status == KontoStatus.Actif && !aktive.Any(e => e.SchuelerId == k.Id), ct);

        var klassen = jahrId is null ? 0 : await db.Klassen.CountAsync(k => k.SchuljahrId == jahrId && k.Aktiv, ct);
        var faecher = await db.Faecher.CountAsync(f => f.SchuleId == a.SchuleId && f.Aktiv, ct);
        var klassenfaecher = jahrId is null ? 0 : await db.Klassenfaecher.CountAsync(kf => db.Klassen.Any(k => k.Id == kf.KlasseId && k.SchuljahrId == jahrId), ct);

        // Ein Direktor/Koordinator zählt nicht als Lehrer (Bauplan §11.2).
        var lehrer = db.Users.AsNoTracking().Where(k => k.SchuleId == a.SchuleId && k.Rolle == Rollen.Lehrer && k.Status == KontoStatus.Actif);
        var lehrerAnzahl = await lehrer.CountAsync(ct);
        var auftraegeDesJahres = from l in db.Lehrauftraege
                                 join kf in db.Klassenfaecher on l.KlassenfachId equals kf.Id
                                 join k in db.Klassen on kf.KlasseId equals k.Id
                                 where k.SchuljahrId == jahrId
                                 select l.LehrerId;
        var ohneAuftrag = await lehrer.CountAsync(k => !auftraegeDesJahres.Contains(k.Id), ct);

        var heuteEintraege = db.Anwesenheiten.AsNoTracking().Where(x => x.SchuleId == a.SchuleId && x.Datum == heute);
        var absences = await heuteEintraege.CountAsync(x => x.Status == AnwesenheitsStatus.Absent, ct);
        var retards = await heuteEintraege.CountAsync(x => x.Status == AnwesenheitsStatus.Retard, ct);
        var klassenMitAppell = await (from x in heuteEintraege
                                      join e in db.Einschreibungen on x.EinschreibungId equals e.Id
                                      select e.KlasseId).Distinct().CountAsync(ct);
        var eingeschrieben = jahrId is null ? 0 : await aktive.CountAsync(e => e.SchuljahrId == jahrId, ct);

        var schritte = new List<CockpitSchritt>
        {
            new("Créer l'année scolaire", jahr is null ? "Dates, périodes (trimestres, semestres…) puis démarrage." : $"{jahr.Bezeichnung} est créée.",
                "/direction/annee", jahr is not null),
            new("Démarrer l'année", jahr?.Status == SchuljahrStatus.Laufend ? "L'année est en cours." : "Démarrez l'année quand l'organisation est prête.",
                "/direction/annee", jahr?.Status == SchuljahrStatus.Laufend),
            new("Créer les classes", klassen == 0 ? "Niveaux (6e, 5e…) et classes de l'année." : $"{klassen} classe{S(klassen)} active{S(klassen)}.",
                "/direction/classes", klassen > 0),
            new("Ajouter les matières", faecher == 0 ? "Matières, groupes du bulletin et coefficients par classe." : $"{faecher} matière{S(faecher)}, {klassenfaecher} dans les classes.",
                klassen > 0 && faecher > 0 ? "/direction/classes" : "/direction/matieres", faecher > 0 && klassenfaecher > 0),
            new("Constituer l'équipe", lehrerAnzahl == 0 ? "Comptes des enseignants et affectations." : $"{lehrerAnzahl} enseignant{S(lehrerAnzahl)} actif{S(lehrerAnzahl)}.",
                "/direction/equipe", lehrerAnzahl > 0),
            new("Inscrire les élèves", eingeschrieben == 0 ? "Un par un ou par import Excel, avec leur classe." : $"{eingeschrieben} élève{S(eingeschrieben)} inscrit{S(eingeschrieben)} dans une classe.",
                "/direction/eleves", eingeschrieben > 0),
        };

        return new CockpitDaten(jahrId, jahr?.Bezeichnung, jahr?.Status, schuelerAktiv, aValider, ohneKlasse, klassen, lehrerAnzahl, ohneAuftrag,
            absences, retards, klassenMitAppell, eingeschrieben, schritte);
    }

    private static string S(int n) => n > 1 ? "s" : string.Empty;
}
