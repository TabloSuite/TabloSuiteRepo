using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Noten;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Noten;

public sealed record VorlageZeile(Guid Id, Guid? StufeId, string Geltung, string Name, string Farbe);

/// <summary>
/// Bulletin-Vorlagen (Bauplan §4.3): eine Standardvorlage der Schule (Stufe leer) und höchstens eine je Stufe; Schalter, Farbe,
/// Schrift, Rand, Fußtext wie in der Alt-App. Änderungen wirken nur auf künftige Bulletins. Ohne Vorlage gelten die Standardwerte.
/// </summary>
public sealed partial class VorlagenService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, TimeProvider zeit)
{
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex FarbMuster();

    public async Task<IReadOnlyList<VorlageZeile>> VorlagenAsync(Guid akteurId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.BulletinsVeroeffentlichen, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var liste = await (from v in db.BulletinVorlagen.AsNoTracking()
                           join s in db.Stufen on v.StufeId equals s.Id into st
                           from s in st.DefaultIfEmpty()
                           where v.SchuleId == a.SchuleId
                           select new { v.Id, v.StufeId, Stufe = s == null ? null : s.Name, Reihenfolge = s == null ? -1 : s.Reihenfolge, v.Name, v.Farbe }).ToListAsync(ct);
        return liste.OrderBy(v => v.Reihenfolge)
            .Select(v => new VorlageZeile(v.Id, v.StufeId, v.Stufe is null ? "Toutes les classes (modèle standard)" : $"Niveau {v.Stufe}", v.Name, v.Farbe)).ToList();
    }

    /// <summary>Vorlage zum Bearbeiten (Kopie, nicht verfolgt); ohne Id eine neue mit Standardwerten.</summary>
    public async Task<BulletinVorlage> VorlageAsync(Guid akteurId, Guid? vorlageId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.BulletinsVeroeffentlichen, schreiben: false, ct);
        if (vorlageId is not { } id) return new BulletinVorlage { SchuleId = a.SchuleId };
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await db.BulletinVorlagen.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new FachFehler("Ce modèle est introuvable.");
        a.PruefeSchule(v.SchuleId);
        return v;
    }

    public async Task<Guid> SpeichernAsync(Guid akteurId, BulletinVorlage e, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.BulletinsVeroeffentlichen, ct: ct);
        var name = NotenFormat.Text(e.Name, 120, "Le nom") ?? throw new FachFehler("Indiquez le nom du modèle.", "name");
        if (!FarbMuster().IsMatch(e.Farbe ?? string.Empty)) throw new FachFehler("Choisissez une couleur au format #RRVVBB.", "farbe");
        if (e.SchriftGroesse is < 8 or > 12) throw new FachFehler("La taille du texte est comprise entre 8 et 12.", "schrift");
        if (e.SeitenRand is < 15 or > 50) throw new FachFehler("Les marges sont comprises entre 15 et 50 mm.", "rand");
        var fuss = NotenFormat.Text(e.Fusstext, 300, "Le texte de bas de page");
        return await dbFactory.TransaktionAsync(async db =>
        {
            if (e.StufeId is { } stufe && !await db.Stufen.AnyAsync(s => s.Id == stufe && s.SchuleId == a.SchuleId, ct))
                throw new FachFehler("Ce niveau est introuvable.");
            var v = await db.BulletinVorlagen.FirstOrDefaultAsync(x => x.Id == e.Id, ct);
            if (v is null)
            {
                v = new BulletinVorlage { SchuleId = a.SchuleId };
                db.BulletinVorlagen.Add(v);
            }
            else a.PruefeSchule(v.SchuleId);
            if (await db.BulletinVorlagen.AnyAsync(x => x.SchuleId == a.SchuleId && x.StufeId == e.StufeId && x.Id != v.Id, ct))
                throw new FachFehler(e.StufeId is null ? "Le modèle standard existe déjà : modifiez-le." : "Ce niveau a déjà son modèle : modifiez-le.", "stufe");
            v.StufeId = e.StufeId;
            v.Name = name;
            v.Farbe = e.Farbe!.ToLowerInvariant();
            (v.ZeigeKoeffizienten, v.ZeigeRang, v.ZeigeVorperioden, v.ZeigeUnterschriften, v.ZeigeLehrkraefte, v.ZeigeAnwesenheit) =
                (e.ZeigeKoeffizienten, e.ZeigeRang, e.ZeigeVorperioden, e.ZeigeUnterschriften, e.ZeigeLehrkraefte, e.ZeigeAnwesenheit);
            (v.ZeigeTeilnoten, v.ZeigePunkte, v.ZeigeFachRang, v.ZeigeAppreciation, v.ZeigeSignaturSpalte, v.ZeigeFachGruppen) =
                (e.ZeigeTeilnoten, e.ZeigePunkte, e.ZeigeFachRang, e.ZeigeAppreciation, e.ZeigeSignaturSpalte, e.ZeigeFachGruppen);
            (v.ZeigeKlassenStatistik, v.ZeigeMentionen, v.ZeigeJahresBilanz, v.ZeigeFoto, v.ZeigeNoteInWorten) =
                (e.ZeigeKlassenStatistik, e.ZeigeMentionen, e.ZeigeJahresBilanz, e.ZeigeFoto, e.ZeigeNoteInWorten);
            (v.SchriftGroesse, v.SeitenRand, v.Fusstext) = (e.SchriftGroesse, e.SeitenRand, fuss);
            await db.SaveChangesAsync(ct);
            return v.Id;
        }, ct);
    }

    public async Task LoeschenAsync(Guid akteurId, Guid vorlageId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.BulletinsVeroeffentlichen, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await db.BulletinVorlagen.FirstOrDefaultAsync(x => x.Id == vorlageId, ct) ?? throw new FachFehler("Ce modèle est introuvable.");
        a.PruefeSchule(v.SchuleId);
        db.BulletinVorlagen.Remove(v);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Vorschau einer gespeicherten Vorlage mit einem erfundenen Schüler und dem echten Schulkopf.</summary>
    public async Task<BulletinDruck> BeispielAsync(Guid akteurId, Guid? vorlageId, CancellationToken ct = default)
    {
        var vorlage = await VorlageAsync(akteurId, vorlageId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var schule = await db.Schulen.AsNoTracking().FirstAsync(s => s.Id == vorlage.SchuleId, ct);
        var heute = new SchulZeit(zeit, schule.Zeitzone).Heute;
        return new BulletinDruck(new BulletinSnapshot(1, Beispiel(schule, vorlage),
            new SnapshotDokument("APERÇU — EXEMPLE", SkoulouKultur.Datum(heute), null, null, false, null, true)), schule.LogoMediumId, null, BulletinStatus.Publie, "apercu-modele.pdf");
    }

    /// <summary>Erfundener Togo-Schüler (3e trimestre, mit Bilan) für die Vorschau.</summary>
    internal static BulletinInhalt Beispiel(Schule schule, BulletinVorlage vorlage)
    {
        (string Fach, string Gruppe, string Lehrer, decimal Koeff, decimal Moy, string Rang, string Teil)[] daten =
        [
            ("Français", "Lettres", "A. Lawson", 3, 13.25m, "4e", "12 · 14,5 · 13"), ("Anglais", "Lettres", "A. Lawson", 2, 15.5m, "2e", "16 · 15"),
            ("Histoire-Géographie", "Lettres", "K. Amegah", 2, 11.75m, "9e", "11 · 12,5"), ("Mathématiques", "Sciences", "Y. Kpodar", 3, 14m, "3e ex", "13 · 15 · 14"),
            ("SVT", "Sciences", "Y. Kpodar", 2, 12.5m, "6e", "12,5 · 12,5"), ("EPS", "Autres", "M. Ahadji", 1, 16m, "1er", "16"),
        ];
        var faecher = daten.Select(d => new SnapshotFach(d.Fach, d.Gruppe, d.Lehrer, d.Koeff, d.Moy, NotenTexte.Zahl(d.Moy), d.Moy * d.Koeff, d.Rang, d.Teil,
            NotenTexte.Appreciation(d.Moy), NotenTexte.Zahl(d.Moy - 0.4m))).ToList();
        var summeK = daten.Sum(d => d.Koeff);
        var summeP = daten.Sum(d => d.Moy * d.Koeff);
        var moyenne = summeP / summeK;
        var vorperioden = new List<SnapshotPeriode> { new("1er trimestre", "12,40", "7e"), new("2e trimestre", "13,05", "5e") };
        return new BulletinInhalt(BulletinBerechnung.SchulKopf(schule), "2026-2027", "3e trimestre", NotenTexte.BulletinTitel(PeriodenArt.Trimestre, true), "5e A", 42,
            new SnapshotSchueler("Amegah", "Essi", "CDP260004", "14/03/2014", "Lomé", "F", "Togolaise", false, false, false),
            faecher, [new("Lettres", "13,38", 7), new("Sciences", "13,40", 5), new("Autres", "16,00", 1)], moyenne, NotenTexte.Zahl(moyenne),
            NoteInWorten.Text(moyenne), "5e", summeK, summeP, NotenTexte.Appreciation(moyenne),
            new SnapshotStatistik("11,62", "6,85", "16,40", "71,43", 42), vorperioden,
            new SnapshotBilan("13,02", "5e", NotenTexte.Entscheidung(JahresEntscheidungsArt.Admis), [.. vorperioden, new("3e trimestre", NotenTexte.Zahl(moyenne), "5e")]),
            Distinktion.TableauHonneur, NotenTexte.DistinktionText(Distinktion.TableauHonneur), Sanktionen.Keine, [],
            "Bon trimestre, élève sérieuse et appliquée. Continuez ainsi.", new SnapshotAnwesenheit(true, 3, 2, 1, 15), "Afi Lawson",
            SnapshotLayout.Aus(vorlage));
    }
}
