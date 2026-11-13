using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Skoulou.Core.Fehler;
using Skoulou.Core.Werkzeuge;

namespace Skoulou.WebApp.Services.Organisation.Import;

/// <summary>Was importiert wird.</summary>
public enum ImportArt
{
    Eleves,
    Enseignants,
    Parents
}

/// <summary>Spalte der Importvorlage: Schlüssel, Titel in der Datei, Pflicht, erkannte Varianten, Beispielwert.</summary>
internal sealed record ImportSpalte(string Schluessel, string Titel, bool Pflicht, string[] Aliase, string Beispiel, string Erklaerung);

/// <summary>Eine gelesene Zeile (Zeilennummer wie in Excel) mit den Werten je Spaltenschlüssel.</summary>
internal sealed record RohZeile(int Nr, IReadOnlyDictionary<string, string?> Werte)
{
    public string? this[string schluessel] => Werte.TryGetValue(schluessel, out var w) ? w : null;
}

/// <summary>
/// Spalten der drei Vorlagen und das Lesen einer XLSX-Datei (nur erstes Blatt, höchstens 2 Mo und 2 000 Zeilen;
/// Kopfzeilen tolerant gegenüber Akzenten, Groß-/Kleinschreibung und Varianten wie « Prenom », « Vorname »).
/// </summary>
internal static class ImportVorlage
{
    public const long MaxBytes = 2 * 1024 * 1024;
    public const int MaxZeilen = 2000;

    private static readonly ImportSpalte Vorname = new("prenom", "Prénom", true, ["prenom", "prenoms", "vorname", "firstname"], "Ama", "Obligatoire.");
    private static readonly ImportSpalte Nachname = new("nom", "Nom", true, ["nom", "nomdefamille", "nachname", "lastname"], "MENSAH", "Obligatoire.");
    private static readonly ImportSpalte Identifiant = new("identifiant", "Identifiant", false, ["identifiant", "login", "utilisateur"], "",
        "Facultatif : vide = proposé automatiquement (prenom.nom).");
    private static readonly ImportSpalte Email = new("email", "E-mail", false, ["email", "mail", "courriel", "adresseemail"], "", "Facultatif.");
    private static readonly ImportSpalte Telefon = new("telephone", "Téléphone", false, ["telephone", "tel", "telefon", "portable", "contact"], "+228 90 00 00 00", "Facultatif.");
    private static readonly ImportSpalte Geschlecht = new("sexe", "Sexe", false, ["sexe", "genre", "geschlecht"], "F", "Facultatif : F ou M.");

    public static IReadOnlyList<ImportSpalte> Spalten(ImportArt art) => art switch
    {
        ImportArt.Eleves =>
        [
            Vorname, Nachname,
            new("classe", "Classe", false, ["classe", "klasse", "class"], "6e A", "Nom exact d'une classe de l'année (voir l'onglet « Classes »). Vide = sans classe."),
            Geschlecht,
            new("naissance", "Date de naissance", false, ["datedenaissance", "naissance", "nele", "nee", "geburtsdatum", "datenaissance"], "14/03/2014", "Facultatif : jj/mm/aaaa."),
            new("lieu", "Lieu de naissance", false, ["lieudenaissance", "lieu", "geburtsort", "nea"], "Lomé", "Facultatif."),
            new("nationalite", "Nationalité", false, ["nationalite", "nationalitaet"], "Togolaise", "Facultatif."),
            new("matricule", "Matricule", false, ["matricule", "matrikel", "numero"], "", "Facultatif : vide = attribué automatiquement."),
            Identifiant, Email, Telefon,
            new("telparent", "Téléphone parent", false, ["telephoneparent", "telparent", "telephonedesparents", "contactparent"], "+228 91 00 00 00", "Facultatif."),
            new("emailparent", "E-mail parent", false, ["emailparent", "mailparent", "emaildesparents"], "", "Facultatif."),
            new("redoublant", "Redoublant", false, ["redoublant", "redoublante", "wiederholer"], "non", "Facultatif : oui ou non."),
        ],
        ImportArt.Enseignants => [Vorname, Nachname, Geschlecht, Identifiant, Email, Telefon],
        _ =>
        [
            Vorname, Nachname, Geschlecht, Identifiant, Email, Telefon,
            new("matricules", "Matricule enfant", false, ["matriculeenfant", "matriculesenfants", "matriculedelenfant", "matricules", "matricule", "enfant", "enfants"],
                "CDP260001", "Matricule de l'enfant ; plusieurs enfants séparés par une virgule."),
            new("lien", "Lien de parenté", false, ["liendeparente", "lien", "parente", "relation"], "Mère", "Mère, Père, Tuteur, Tutrice ou Autre."),
        ]
    };

    public static string Bezeichnung(ImportArt art) => art switch
    {
        ImportArt.Eleves => "élèves",
        ImportArt.Enseignants => "enseignants",
        _ => "parents"
    };

    public static string DateiName(ImportArt art) => $"modele-import-{Bezeichnung(art)}.xlsx";

    private static string KopfSchluessel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var basis = TextNormalisierung.OhneAkzente(text.Replace("*", string.Empty)).ToLowerInvariant();
        var sb = new StringBuilder(basis.Length);
        foreach (var c in basis)
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Liest das erste Blatt; Fehler bei fehlenden Pflichtspalten, zu vielen Zeilen oder keiner gültigen XLSX-Datei.</summary>
    public static List<RohZeile> Lese(byte[] inhalt, ImportArt art)
    {
        XLWorkbook mappe;
        try
        {
            mappe = new XLWorkbook(new MemoryStream(inhalt, writable: false));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new FachFehler("Ce fichier n'est pas un classeur Excel (.xlsx) lisible. Enregistrez-le au format .xlsx puis réessayez.");
        }
        using (mappe)
        {
            var blatt = mappe.Worksheets.FirstOrDefault() ?? throw new FachFehler("Le classeur ne contient aucune feuille.");
            var benutzt = blatt.RangeUsed();
            if (benutzt is null) throw new FachFehler("La première feuille du classeur est vide.");
            var ersteZeile = benutzt.FirstRow().RowNumber();
            var letzteZeile = benutzt.LastRow().RowNumber();
            if (letzteZeile - ersteZeile > MaxZeilen)
                throw new FachFehler($"Le fichier contient trop de lignes ({MaxZeilen} au maximum par import). Découpez-le en plusieurs fichiers.");

            var spalten = Spalten(art);
            var zuordnung = new Dictionary<int, ImportSpalte>();
            foreach (var zelle in blatt.Row(ersteZeile).CellsUsed())
            {
                var kopf = KopfSchluessel(zelle.GetString());
                var spalte = spalten.FirstOrDefault(s => s.Aliase.Contains(kopf) || KopfSchluessel(s.Titel) == kopf);
                if (spalte is not null && !zuordnung.ContainsValue(spalte)) zuordnung[zelle.Address.ColumnNumber] = spalte;
            }
            var fehlend = spalten.Where(s => s.Pflicht && !zuordnung.ContainsValue(s)).Select(s => $"« {s.Titel} »").ToList();
            if (fehlend.Count > 0)
                throw new FachFehler($"Colonne{(fehlend.Count > 1 ? "s" : "")} introuvable{(fehlend.Count > 1 ? "s" : "")} dans la première ligne : {string.Join(", ", fehlend)}. Utilisez le modèle à télécharger.");

            var zeilen = new List<RohZeile>();
            for (var nr = ersteZeile + 1; nr <= letzteZeile; nr++)
            {
                var werte = new Dictionary<string, string?>();
                foreach (var (spaltenNr, spalte) in zuordnung)
                    werte[spalte.Schluessel] = ZellText(blatt.Cell(nr, spaltenNr));
                if (werte.Values.All(string.IsNullOrWhiteSpace)) continue;
                zeilen.Add(new RohZeile(nr, werte));
            }
            if (zeilen.Count == 0) throw new FachFehler("Le fichier ne contient aucune ligne de données sous la ligne d'en-tête.");
            return zeilen;
        }
    }

    private static string? ZellText(IXLCell zelle)
    {
        var wert = zelle.Value;
        string? text = wert.Type switch
        {
            XLDataType.Blank => null,
            XLDataType.DateTime => DateOnly.FromDateTime(wert.GetDateTime()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            XLDataType.Number => wert.GetNumber().ToString("0.##########", CultureInfo.InvariantCulture),
            XLDataType.Boolean => wert.GetBoolean() ? "oui" : "non",
            _ => zelle.GetString()
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>Leere Vorlage: Blatt 1 nur Kopfzeile, Blatt « Mode d'emploi » mit Erklärung und Beispiel, für Schüler Blatt « Classes ».</summary>
    public static byte[] Erzeuge(ImportArt art, IReadOnlyList<string> klassen)
    {
        using var mappe = new XLWorkbook();
        var spalten = Spalten(art);
        var blatt = mappe.Worksheets.Add(art switch { ImportArt.Eleves => "Élèves", ImportArt.Enseignants => "Enseignants", _ => "Parents" });
        for (var i = 0; i < spalten.Count; i++)
        {
            var zelle = blatt.Cell(1, i + 1);
            zelle.Value = spalten[i].Titel;
            zelle.Style.Font.Bold = true;
            zelle.Style.Fill.BackgroundColor = spalten[i].Pflicht ? XLColor.FromHtml("#DBEAFE") : XLColor.FromHtml("#F1F5F9");
            blatt.Column(i + 1).Width = Math.Max(14, spalten[i].Titel.Length + 4);
            blatt.Column(i + 1).Style.NumberFormat.Format = "@";
        }
        blatt.SheetView.FreezeRows(1);

        var hilfe = mappe.Worksheets.Add("Mode d'emploi");
        hilfe.Cell(1, 1).Value = $"Import des {Bezeichnung(art)} – SKOULOU";
        hilfe.Cell(1, 1).Style.Font.Bold = true;
        hilfe.Cell(2, 1).Value = "Remplissez la première feuille, une personne par ligne, sans modifier la ligne d'en-tête. Les colonnes en bleu sont obligatoires.";
        hilfe.Cell(4, 1).Value = "Colonne";
        hilfe.Cell(4, 2).Value = "Explication";
        hilfe.Cell(4, 3).Value = "Exemple";
        hilfe.Range(4, 1, 4, 3).Style.Font.Bold = true;
        for (var i = 0; i < spalten.Count; i++)
        {
            hilfe.Cell(5 + i, 1).Value = spalten[i].Titel + (spalten[i].Pflicht ? " *" : string.Empty);
            hilfe.Cell(5 + i, 2).Value = spalten[i].Erklaerung;
            hilfe.Cell(5 + i, 3).Value = spalten[i].Beispiel;
        }
        hilfe.Column(1).Width = 22;
        hilfe.Column(2).Width = 80;
        hilfe.Column(3).Width = 20;

        if (art == ImportArt.Eleves)
        {
            var liste = mappe.Worksheets.Add("Classes");
            liste.Cell(1, 1).Value = "Classes de l'année";
            liste.Cell(1, 1).Style.Font.Bold = true;
            for (var i = 0; i < klassen.Count; i++) liste.Cell(2 + i, 1).Value = klassen[i];
            liste.Column(1).Width = 24;
        }

        using var ziel = new MemoryStream();
        mappe.SaveAs(ziel);
        return ziel.ToArray();
    }

    /// <summary>Fehlerliste als Excel-Datei (Ligne, Personne, Erreur).</summary>
    public static byte[] FehlerDatei(IReadOnlyList<ImportFehler> fehler)
    {
        using var mappe = new XLWorkbook();
        var blatt = mappe.Worksheets.Add("Erreurs");
        blatt.Cell(1, 1).Value = "Ligne";
        blatt.Cell(1, 2).Value = "Personne";
        blatt.Cell(1, 3).Value = "Erreur";
        blatt.Range(1, 1, 1, 3).Style.Font.Bold = true;
        for (var i = 0; i < fehler.Count; i++)
        {
            blatt.Cell(2 + i, 1).Value = fehler[i].Zeile;
            blatt.Cell(2 + i, 2).Value = fehler[i].Person;
            blatt.Cell(2 + i, 3).Value = fehler[i].Meldung;
        }
        blatt.Column(1).Width = 8;
        blatt.Column(2).Width = 28;
        blatt.Column(3).Width = 90;
        using var ziel = new MemoryStream();
        mappe.SaveAs(ziel);
        return ziel.ToArray();
    }
}
