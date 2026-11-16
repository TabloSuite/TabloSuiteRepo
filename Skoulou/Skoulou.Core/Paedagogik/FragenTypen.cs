using Skoulou.Core.Entitaeten;

namespace Skoulou.Core.Paedagogik;

/// <summary>Eigenschaften und Bezeichnungen der acht Fragetypen.</summary>
public static class FragenTypen
{
    public static readonly IReadOnlyList<FragenTyp> Alle = Enum.GetValues<FragenTyp>();

    public static bool IstAuswahl(FragenTyp typ) => typ is FragenTyp.ChoixUnique or FragenTyp.ChoixMultiple
        or FragenTyp.AudioChoixUnique or FragenTyp.AudioChoixMultiple;

    public static bool IstMehrfach(FragenTyp typ) => typ is FragenTyp.ChoixMultiple or FragenTyp.AudioChoixMultiple;

    public static bool IstAudio(FragenTyp typ) => typ is FragenTyp.AudioTexte or FragenTyp.AudioChoixUnique or FragenTyp.AudioChoixMultiple;

    /// <summary>Freitext mit Musterlösungen (Texte, Construction de phrase, Audio → texte).</summary>
    public static bool IstText(FragenTyp typ) => typ is FragenTyp.Texte or FragenTyp.ConstructionPhrase or FragenTyp.AudioTexte;

    /// <summary>Wird die Frage automatisch bewertet (sonst korrigiert die Lehrkraft)?</summary>
    public static bool HatAutomatischeBewertung(FragenTyp typ, IEnumerable<string> akzeptierteAntworten)
        => !IstText(typ) || akzeptierteAntworten.Any(v => AntwortVergleich.Normalisiere(v).Length > 0);

    public static string Bezeichnung(FragenTyp typ) => typ switch
    {
        FragenTyp.ChoixUnique => "Choix unique",
        FragenTyp.ChoixMultiple => "Choix multiple",
        FragenTyp.Texte => "Réponse écrite",
        FragenTyp.ConstructionPhrase => "Construction de phrase",
        FragenTyp.TexteATrous => "Texte à trous",
        FragenTyp.AudioTexte => "Audio → réponse écrite",
        FragenTyp.AudioChoixUnique => "Audio → choix unique",
        FragenTyp.AudioChoixMultiple => "Audio → choix multiple",
        _ => typ.ToString()
    };

    /// <summary>Kurze Anleitung für den Schüler.</summary>
    public static string Anleitung(FragenTyp typ) => typ switch
    {
        FragenTyp.ChoixUnique or FragenTyp.AudioChoixUnique => "Choisissez une seule réponse.",
        FragenTyp.ChoixMultiple or FragenTyp.AudioChoixMultiple => "Plusieurs réponses peuvent être justes.",
        FragenTyp.ConstructionPhrase => "Touchez les mots dans le bon ordre.",
        FragenTyp.TexteATrous => "Complétez les trous.",
        FragenTyp.AudioTexte => "Écoutez puis écrivez ce que vous entendez.",
        _ => "Écrivez votre réponse."
    };

    /// <summary>
    /// Sprachen des vorgelesenen Textes (Azure-Sprachausgabe, sonst Browser-Stimme); die erste ist der Standard.
    /// Zu jeder gehört eine Stimme in <c>VorleseRegeln.Stimmen</c> (Test prüft die Vollständigkeit).
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Sprachen =
    [
        ("fr-FR", "Français"), ("en-US", "Anglais"), ("de-DE", "Allemand"), ("es-ES", "Espagnol"),
        ("it-IT", "Italien"), ("pt-PT", "Portugais"), ("ar-SA", "Arabe"), ("zh-CN", "Chinois (mandarin)")
    ];
}
