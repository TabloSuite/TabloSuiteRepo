using SkoulouApp.Core.DataModels.Unterricht;

namespace SkoulouApp.WebApp.Services;

/// <summary>Fiktives Beispiel-Bulletin für die Vorlagen-Vorschau (keine echten Schülerdaten).</summary>
public static class BulletinBeispiel
{
    public static BulletinSnapshot Erzeugen(bool jahresabschluss, BulletinLayout layout, string farbe = "#1e3a8a")
    {
        BulletinFach Fach(string name, string gruppe, decimal coef, decimal classe, decimal compo, int rang, string lehrer)
        {
            var moy = (classe + compo) / 2m;
            return new BulletinFach(name, lehrer, coef, moy, gruppe,
                [new BulletinTeilnote("CLASSE", classe), new BulletinTeilnote("COMPO", compo)], rang, BulletinTexte.Appreciation(moy),
                jahresabschluss ? moy + 0.4m : null, jahresabschluss ? rang + 1 : null);
        }

        List<BulletinFach> faecher =
        [
            Fach("Français", "Lettres", 3, 11, 9, 28, "Mme Adjo Mensah"),
            Fach("Anglais", "Lettres", 2, 12, 11, 16, "M. Koffi Agbeko"),
            Fach("Histoire-Géographie", "Lettres", 2, 10.5m, 11, 21, "M. Yao Lawson"),
            Fach("Mathématiques", "Sciences", 4, 12, 11.1m, 13, "M. Kodjo Amouzou"),
            Fach("Physique-Chimie", "Sciences", 4, 10, 11, 17, "Mme Afi Tchalla"),
            Fach("SVT", "Sciences", 4, 11.4m, 11.4m, 12, "M. Komi Djossou"),
            Fach("EPS", "Autres", 1, 11, 11.5m, 20, "M. Kossi Ayité"),
            Fach("Conduite", "Autres", 1, 14, 14, 1, "Vie scolaire")
        ];
        return new BulletinSnapshot("COLLÈGE LES PALMIERS", "Travail · Discipline · Réussite", "BP 1234 Lomé · Quartier Bè", "RÉPUBLIQUE TOGOLAISE",
            "Ministère des Enseignements Primaire, Secondaire et Technique", "Travail – Liberté – Patrie", "336130119418",
            "Tél. 22 21 00 00 · contact@palmiers.tg", "2026-2027", jahresabschluss ? "3e trimestre" : "1er trimestre", "2nde C1",
            "Mensah Kossi Koffi", "15036143F", "Lomé", "14/10/2009", false, faecher, 11.02m, 17, 46,
            jahresabschluss ? [new BulletinVorperiode("1er trimestre", 10.92m), new BulletinVorperiode("2e trimestre", 11.35m)] : [],
            jahresabschluss ? 11.16m : null, "Peut mieux faire en français.", farbe, true, true, true, true,
            "Le présent bulletin doit être conservé ; aucun duplicata ne sera délivré.",
            ErfassteTage: 60, FehlTage: 3, EntschuldigteFehlTage: 2, Verspaetungen: 1, VerspaetungsMinuten: 10,
            SchriftGroesse: 10, SeitenRand: 22, DokumentNummer: "EXEMPLE", DokumentDatumUtc: DateTime.UtcNow,
            Layout: layout, Direction: "Direction régionale de l'éducation Lomé-Golfe", Statut: "Privé laïc", Ort: "Lomé", Geschlecht: "M",
            Nationalitaet: "Togolaise", IstAffecte: true, TeilnotenSpalten: ["CLASSE", "COMPO"],
            KlassenMittel: 9.82m, KlassenMin: 5.17m, KlassenMax: 14.85m,
            Distinktion: null, Sanktionen: [], RatsBemerkung: "Trimestre moyen, des efforts sont attendus.",
            Entscheidung: jahresabschluss ? BulletinTexte.EntscheidungText(Jahresentscheidung.Admis) : null,
            JahresRang: jahresabschluss ? 19 : null, IstJahresabschluss: jahresabschluss);
    }

    /// <summary>Beispiel mit Kopfdaten und Layout der echten Schule/Vorlage, Schülerdaten bleiben fiktiv.</summary>
    public static BulletinSnapshot FuerVorlage(BulletinVorlage vorlage, SkoulouApp.Core.DataModels.Schools.School schule, byte[]? logo, bool jahresabschluss)
    {
        var layout = new BulletinLayout(vorlage.ZeigeTeilnoten, vorlage.ZeigePunkte, vorlage.ZeigeFachRang, vorlage.ZeigeAppreciation,
            vorlage.ZeigeSignaturSpalte, vorlage.ZeigeFachGruppen, vorlage.ZeigeKlassenStatistik, vorlage.ZeigeMentionen,
            vorlage.ZeigeJahresBilanz, vorlage.ZeigeFoto, vorlage.ZeigeNoteInWorten);
        var kontakt = string.Join(" · ", new[] { schule.DokumentTelefon, schule.DokumentEmail }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return Erzeugen(jahresabschluss, layout, vorlage.AkzentFarbe) with
        {
            Schule = schule.OffizielleBezeichnung ?? schule.Name,
            Slogan = schule.Slogan,
            Adresse = schule.DokumentAdresse,
            Republik = schule.RepublikZeile,
            Ministerium = schule.Ministerium,
            Motto = schule.StaatsMotto,
            Zulassung = schule.Zulassungsnummer,
            Kontakt = kontakt.Length > 0 ? kontakt : null,
            Direction = schule.DokumentDirection,
            Statut = schule.DokumentStatut,
            Ort = schule.City,
            Logo = logo,
            Koeffizienten = vorlage.ZeigeKoeffizienten,
            RangAnzeigen = vorlage.ZeigeRang,
            VorperiodenAnzeigen = vorlage.ZeigeVorperioden,
            Unterschriften = vorlage.ZeigeUnterschriften,
            FussText = vorlage.FussText,
            SchriftGroesse = vorlage.SchriftGroesse,
            SeitenRand = vorlage.SeitenRand,
            ZeigeLehrkraefte = vorlage.ZeigeLehrkraefte,
            ZeigeAnwesenheit = vorlage.ZeigeAnwesenheit
        };
    }
}
