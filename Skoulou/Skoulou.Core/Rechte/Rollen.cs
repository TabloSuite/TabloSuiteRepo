namespace Skoulou.Core.Rechte;

/// <summary>Die sieben Rollen (genau eine je Konto) mit französischen Bezeichnungen.</summary>
public static class Rollen
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Responsable = "Responsable";
    public const string Direktor = "Direktor";
    public const string Koordinator = "Koordinator";
    public const string Lehrer = "Lehrer";
    public const string Schueler = "Schueler";
    public const string Eltern = "Eltern";

    /// <summary>Alle Rollen in Rangfolge (für Anzeigen und Seed).</summary>
    public static readonly IReadOnlyList<string> Alle =
        [SuperAdmin, Responsable, Direktor, Koordinator, Lehrer, Schueler, Eltern];

    /// <summary>Rollen mit genau einer Schule.</summary>
    public static readonly IReadOnlyList<string> SchulRollen = [Direktor, Koordinator, Lehrer, Schueler, Eltern];

    /// <summary>Direktion = Direktor und Koordinator.</summary>
    public static readonly IReadOnlyList<string> Direktion = [Direktor, Koordinator];

    /// <summary>Wer unterrichten darf (Lehraufträge).</summary>
    public static readonly IReadOnlyList<string> Lehrkraefte = [Lehrer, Direktor, Koordinator];

    public static bool IstSchulRolle(string? rolle) => rolle is not null && SchulRollen.Contains(rolle);

    public static bool IstPlattformRolle(string? rolle) => rolle is SuperAdmin or Responsable;

    public static bool IstDirektion(string? rolle) => rolle is Direktor or Koordinator;

    /// <summary>Bezeichnung für die Oberfläche.</summary>
    public static string Bezeichnung(string? rolle) => rolle switch
    {
        SuperAdmin => "Administrateur",
        Responsable => "Responsable SKOULOU",
        Direktor => "Directeur",
        Koordinator => "Coordinateur",
        Lehrer => "Enseignant",
        Schueler => "Élève",
        Eltern => "Parent",
        _ => "Utilisateur"
    };

    /// <summary>Beschreibung der Rolle (Seed der Identity-Rollen).</summary>
    public static string Beschreibung(string rolle) => rolle switch
    {
        SuperAdmin => "Administre la plateforme : établissements, comptes, tarifs, facturation, messagerie.",
        Responsable => "Accompagne les établissements qui lui sont attribués.",
        Direktor => "Dirige un établissement : comptes, organisation, notes, bulletins, finances.",
        Koordinator => "Assiste la direction ; les finances seulement avec les droits accordés.",
        Lehrer => "Enseigne dans les classes de ses affectations.",
        Schueler => "Suit les cours, exercices et résultats de sa classe.",
        Eltern => "Suit la scolarité des enfants liés à son compte.",
        _ => string.Empty
    };

    /// <summary>Startseite nach der Anmeldung.</summary>
    public static string Startseite(string? rolle) => rolle switch
    {
        SuperAdmin => "/admin",
        Responsable => "/responsable",
        Direktor or Koordinator => "/direction",
        Lehrer => "/enseignant",
        Schueler => "/eleve",
        Eltern => "/parents",
        _ => "/"
    };
}
