namespace ENote.Core.Services.Katalog;

/// <summary>
/// Regeln für Artikel-Barcodes: Hersteller-Codes (EAN-13/EAN-8/UPC) werden geprüft, interne Codes
/// werden als gültige EAN-13 im Präfixbereich „2“ (laden-intern, keine Kollision mit Herstellern) erzeugt.
/// </summary>
public static class BarcodeRegeln
{
    public const int MinLaenge = 4;

    /// <summary>Lang genug für Original-QR-Codes der Hersteller (meist eine Web-Adresse, z. B. GS1 Digital Link).</summary>
    public const int MaxLaenge = 500;

    /// <summary>Ab dieser Länge druckt das Etikett einen QR-Code statt Code 128 (sonst zu breit).</summary>
    public const int QrAbLaenge = 40;

    public enum Art { Leer, Ean13, Ean8, UpcA, Intern, Frei, Ungueltig }

    /// <summary>Ergebnis der Prüfung einer Eingabe (für Hinweise direkt am Eingabefeld).</summary>
    public sealed record Pruefung(Art Art, string? Code, string Hinweis, bool Fehler, bool Warnung);

    /// <summary>Entfernt Leerzeichen und Bindestriche (Scanner/Abtippen), Großbuchstaben für alphanumerische Codes.</summary>
    public static string? Normalisiere(string? eingabe)
    {
        if (string.IsNullOrWhiteSpace(eingabe)) return null;
        var code = new string(eingabe.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        return code.Length == 0 ? null : code;
    }

    /// <summary>Prüfziffer nach GS1 (Gewichte 3/1 von rechts) für EAN-8/EAN-13/UPC-A ohne letzte Ziffer.</summary>
    public static int Pruefziffer(string ziffernOhnePruefziffer)
    {
        var summe = 0;
        for (var i = 0; i < ziffernOhnePruefziffer.Length; i++)
        {
            var ziffer = ziffernOhnePruefziffer[ziffernOhnePruefziffer.Length - 1 - i] - '0';
            summe += i % 2 == 0 ? ziffer * 3 : ziffer;
        }
        return (10 - summe % 10) % 10;
    }

    /// <summary>
    /// Gleicher Code? Gespeicherter und gescannter/eingetippter Code werden gleich normalisiert
    /// (Leerzeichen, Bindestriche, Groß-/Kleinschreibung) – so findet die Kasse auch « ABC-123 » oder eine QR-Adresse.
    /// </summary>
    public static bool Gleich(string? gespeichert, string? eingabe)
        => Normalisiere(gespeichert) is { } a && a == Normalisiere(eingabe);

    /// <summary>Sieht der Code wie der Inhalt eines Hersteller-QR-Codes aus (Web-Adresse oder sehr lang)?</summary>
    public static bool IstQrInhalt(string code)
        => code.Contains("://", StringComparison.Ordinal) || code.StartsWith("WWW.", StringComparison.OrdinalIgnoreCase) || code.Length > QrAbLaenge;

    public static bool HatGueltigePruefziffer(string code)
        => code.Length >= 2 && code.All(char.IsDigit) && Pruefziffer(code[..^1]) == code[^1] - '0';

    /// <summary>Interner Code: „2“ + 11 Ziffern + Prüfziffer = gültige EAN-13.</summary>
    public static string ErzeugeIntern(Random zufall)
    {
        var basis = "2" + string.Concat(Enumerable.Range(0, 11).Select(_ => (char)('0' + zufall.Next(10))));
        return basis + Pruefziffer(basis);
    }

    public static Pruefung Pruefe(string? eingabe)
    {
        var code = Normalisiere(eingabe);
        if (code is null)
            return new(Art.Leer, null, "Laissez vide : un code-barres sera créé automatiquement à l'enregistrement.", false, false);
        if (code.Length is < MinLaenge or > MaxLaenge)
            return new(Art.Ungueltig, code, $"Le code doit contenir entre {MinLaenge} et {MaxLaenge} caractères.", true, false);
        // Alle druckbaren ASCII-Zeichen (auch « : / ? . = & » aus QR-Codes), aber keine Akzente oder Sonderschrift.
        if (!code.All(c => c is >= '!' and <= '~'))
            return new(Art.Ungueltig, code, "Le code contient des caractères non pris en charge (accents, emoji…). Scannez-le à nouveau ou saisissez-le sans accents.", true, false);

        if (code.All(char.IsDigit) && code.Length is 8 or 12 or 13)
        {
            var art = code.Length switch { 13 => Art.Ean13, 12 => Art.UpcA, _ => Art.Ean8 };
            var name = art switch { Art.Ean13 => "EAN-13", Art.UpcA => "UPC-A", _ => "EAN-8" };
            if (!HatGueltigePruefziffer(code))
                return new(art, code, $"Format {name}, mais la chiffre de contrôle ne correspond pas : vérifiez la saisie (enregistrement possible).", false, true);
            var intern = art == Art.Ean13 && code[0] == '2';
            return new(intern ? Art.Intern : art, code, intern ? "Code interne de l'établissement (EAN-13 valide)." : $"Code fabricant {name} valide.", false, false);
        }

        return IstQrInhalt(code)
            ? new(Art.Frei, code, "Code QR du fabricant accepté : scannez-le directement à la caisse.", false, false)
            : new(Art.Frei, code, "Code libre accepté (étiquettes imprimées en Code 128).", false, false);
    }
}
