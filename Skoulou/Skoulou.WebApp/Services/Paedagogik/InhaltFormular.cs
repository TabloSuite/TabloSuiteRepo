using Skoulou.Core.Entitaeten;
using Skoulou.Core.Paedagogik;

namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>Leçon oder Examen.</summary>
public enum InhaltsArt
{
    Lecon,
    Pruefung
}

/// <summary>Bearbeitungsmodell des Editors (Leçon oder Examen) – wird komplett gespeichert.</summary>
public sealed class InhaltFormular
{
    public Guid? Id { get; set; }
    public InhaltsArt Art { get; set; }

    /// <summary>Lehrkraft, der der Inhalt gehört (bei « Saisir pour un enseignant » nicht der Akteur).</summary>
    public Guid AutorId { get; set; }

    public string Titel { get; set; } = string.Empty;
    public Guid? StufeId { get; set; }
    public Guid? FachId { get; set; }
    public InhaltsStatus Status { get; set; } = InhaltsStatus.Brouillon;

    // Leçon
    public string? Zusammenfassung { get; set; }
    public string? Thema { get; set; }
    public Guid? VideoMediumId { get; set; }
    public string? VideoName { get; set; }
    public List<SeiteFormular> Seiten { get; set; } = [];

    // Examen
    public string? Beschreibung { get; set; }
    public int DauerMinuten { get; set; } = 60;
    public decimal Bestehensgrenze { get; set; } = 50;

    public List<BlockFormular> Bloecke { get; set; } = [];

    // Nur zur Anzeige
    public string? AutorName { get; set; }
    public string? ErfasstVonName { get; set; }
    public int AnzahlZuweisungen { get; set; }
}

/// <summary>Eine A4-Seite einer Leçon.</summary>
public sealed class SeiteFormular
{
    public string Schluessel { get; } = Guid.NewGuid().ToString("N");
    public string Html { get; set; } = string.Empty;
}

/// <summary>Aufgabenblock (« Exercice n ») mit Fragen.</summary>
public sealed class BlockFormular
{
    public string Schluessel { get; } = Guid.NewGuid().ToString("N");
    public Guid? Id { get; set; }
    public string? Titel { get; set; }
    public string? Consigne { get; set; }
    public List<FrageFormular> Fragen { get; set; } = [];
}

/// <summary>Frage im Editor; <see cref="HatAntworten"/> = Änderung erzeugt eine neue Revision.</summary>
public sealed class FrageFormular
{
    public string Schluessel { get; } = Guid.NewGuid().ToString("N");
    public Guid? Id { get; set; }
    public FragenTyp Typ { get; set; } = FragenTyp.ChoixUnique;
    public string TextHtml { get; set; } = string.Empty;
    public decimal Punkte { get; set; } = 1;
    public string? AudioText { get; set; }
    public string Sprache { get; set; } = "fr-FR";

    /// <summary>Sprachniveau (GER) des vorgelesenen Textes – bestimmt das Sprechtempo; siehe <see cref="SprechNiveaus"/>.</summary>
    public string Niveau { get; set; } = SprechNiveaus.Standard;

    public List<string> Varianten { get; set; } = [];
    public List<List<string>> Luecken { get; set; } = [];
    public List<string> Wortbank { get; set; } = [];
    public bool AkzenteBeachten { get; set; }
    public List<OptionFormular> Optionen { get; set; } = [];

    public int Revision { get; set; } = 1;
    public bool HatAntworten { get; set; }

    /// <summary>Neue Frage mit sinnvollen Vorgaben je Typ.</summary>
    public static FrageFormular Neu(FragenTyp typ = FragenTyp.ChoixUnique)
    {
        var f = new FrageFormular { Typ = typ };
        f.TypVorbereiten();
        return f;
    }

    /// <summary>Ergänzt fehlende Pflichtteile nach einem Typwechsel (bestehende Eingaben bleiben).</summary>
    public void TypVorbereiten()
    {
        if (Core.Paedagogik.FragenTypen.IstAuswahl(Typ))
        {
            while (Optionen.Count < 2) Optionen.Add(new OptionFormular { Richtig = Optionen.Count == 0 });
        }
        if (Typ == FragenTyp.TexteATrous && Luecken.Count == 0) Luecken.Add([string.Empty]);
        if (Core.Paedagogik.FragenTypen.IstText(Typ) && Varianten.Count == 0) Varianten.Add(string.Empty);
    }
}

/// <summary>Antwortmöglichkeit im Editor.</summary>
public sealed class OptionFormular
{
    public string Schluessel { get; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = string.Empty;
    public bool Richtig { get; set; }
}

/// <summary>Formular einer Zuweisung (Zeiten in UTC; die Seite rechnet über <c>SchulZeit</c> um).</summary>
public sealed class ZuweisungFormular
{
    public Guid InhaltId { get; set; }
    public InhaltsArt Art { get; set; }
    public Guid? KlasseId { get; set; }
    public DateTime AbUtc { get; set; }
    public DateTime? FristUtc { get; set; }
    public int? MaxVersuche { get; set; }
    public VersuchsWertung Wertung { get; set; } = VersuchsWertung.Meilleur;
    public LoesungsSichtbarkeit Loesungen { get; set; } = LoesungsSichtbarkeit.Immediat;
}
