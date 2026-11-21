using Skoulou.Core.Fehler;
using Skoulou.Core.Werkzeuge;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Meldung nach einer Ablehnung des klassischen Absendens: Text (oben) und Feld, das markiert wird.</summary>
public sealed record InscriptionMeldung(string Text, string? Feld = null);

/// <summary>
/// Codes für « ?erreur= » nach /auth/inscription/envoyer (21.11.2026). Wird das Formular abgeschickt, bevor die Seite
/// interaktiv ist (langsames Netz), entscheidet allein der Server; statt nur « saisie » nennt der Code jetzt den Grund
/// (z. B. « identifiant-pris ») und das Feld. Nur feste Texte – nie Text aus der Adresse anzeigen.
/// </summary>
public static class InscriptionFehler
{
    public const string Allgemein = "L'inscription n'a pas pu être enregistrée. Vérifiez vos informations et réessayez.";

    /// <summary>Felder des Formulars mit festem Hinweis (Name wie im Formular und im Service).</summary>
    private static readonly IReadOnlyDictionary<string, string> Felder = new Dictionary<string, string>
    {
        ["prenom"] = "Indiquez votre prénom.",
        ["nom"] = "Indiquez votre nom.",
        ["email"] = "Vérifiez l'adresse e-mail.",
        ["telephone"] = "Vérifiez le numéro de téléphone (au moins 8 chiffres).",
        ["motdepasse"] = "Le mot de passe doit contenir au moins 6 caractères, dont un chiffre et une minuscule.",
        ["confirmation"] = PasswortService.MeldungBestaetigung,
        ["ecole-nom"] = "Vérifiez le nom de l'établissement.",
        ["ville"] = "Indiquez la ville.",
        ["pays"] = "Vérifiez le pays.",
        ["ecole"] = RegistrierungService.MeldungSchuleWaehlen,
        ["naissance"] = "La date de naissance n'est pas plausible.",
        ["parent-tel"] = "Vérifiez le numéro de téléphone du parent.",
        ["parent-email"] = "L'adresse e-mail du parent n'est pas valide.",
        ["identifiant"] = IdentifiantRegel.MeldungFormat,
    };

    /// <summary>Code für die Rückleitung; bekannte Meldungen genau, sonst das Feld, sonst « saisie ».</summary>
    public static string Code(FachFehler f) => f.Message switch
    {
        RegistrierungsBremse.Meldung => "bremse",
        IdentifiantRegel.MeldungVergeben => "identifiant-pris",
        RegistrierungService.MeldungSchuleVorhanden => "ecole-existe",
        RegistrierungService.MeldungSchuleGeschlossen => "ecole-fermee",
        _ when f.Feld == "agb" => "agb",
        _ when f.Feld is { } feld && Felder.ContainsKey(feld) => "champ-" + feld,
        _ => "saisie"
    };

    /// <summary>Meldung zu einem Code (null = kein Fehler).</summary>
    public static InscriptionMeldung? Meldung(string? code) => code switch
    {
        null => null,
        "limite" => new(AnmeldeRegeln.MeldungLimite),
        "bremse" => new(RegistrierungsBremse.Meldung),
        "session" => new("La page a expiré. Veuillez vérifier vos informations et réessayer."),
        "agb" => new(RegistrierungService.MeldungAgb, "agb"),
        "identifiant-pris" => new(IdentifiantRegel.MeldungVergeben, "identifiant"),
        "ecole-existe" => new(RegistrierungService.MeldungSchuleVorhanden, "ecole-nom"),
        "ecole-fermee" => new(RegistrierungService.MeldungSchuleGeschlossen, "ecole"),
        _ when code.StartsWith("champ-", StringComparison.Ordinal) && Felder.TryGetValue(code[6..], out var text) => new(text, code[6..]),
        _ => new(Allgemein)
    };
}
