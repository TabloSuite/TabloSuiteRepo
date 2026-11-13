using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Ergebnis einer Anmeldeprüfung.</summary>
public enum AnmeldeErgebnis
{
    Erlaubt,

    /// <summary>Direktor einer noch nicht validierten Schule: Anmeldung auf die Warteseite.</summary>
    Warteseite,

    FalscheDaten,
    Verriegelt,
    KontoGesperrt,
    KontoInPruefung,
    SchuleSuspendiert,
    SchuleInPruefung,
    SchuleAbgelehnt
}

/// <summary>
/// Wer darf sich anmelden (bzw. angemeldet bleiben)? Getrennte Meldungen für falsche Daten,
/// gesperrtes Konto, suspendierte Schule und Prüfung der Anmeldung. Eine Kontosperre des Direktors
/// sperrt nie die Schule – das tut nur der Schulstatus.
/// </summary>
public static class AnmeldeRegeln
{
    public static AnmeldeErgebnis Pruefe(KontoStatus status, string rolle, SchulStatus? schulStatus)
    {
        if (status is KontoStatus.Bloque or KontoStatus.Archive) return AnmeldeErgebnis.KontoGesperrt;
        if (Rollen.IstSchulRolle(rolle))
        {
            switch (schulStatus)
            {
                case null: return AnmeldeErgebnis.KontoGesperrt;
                case SchulStatus.Suspendue: return AnmeldeErgebnis.SchuleSuspendiert;
                case SchulStatus.Refusee: return AnmeldeErgebnis.SchuleAbgelehnt;
                case SchulStatus.EnAttente:
                    return rolle == Rollen.Direktor ? AnmeldeErgebnis.Warteseite : AnmeldeErgebnis.SchuleInPruefung;
            }
        }
        if (status == KontoStatus.EnAttente) return AnmeldeErgebnis.KontoInPruefung;
        return AnmeldeErgebnis.Erlaubt;
    }

    /// <summary>Darf eine bestehende Sitzung weiterlaufen?</summary>
    public static bool IstZugelassen(AnmeldeErgebnis ergebnis) => ergebnis is AnmeldeErgebnis.Erlaubt or AnmeldeErgebnis.Warteseite;

    /// <summary>Code für die Rückleitung auf /auth/connexion?erreur=…</summary>
    public static string Code(AnmeldeErgebnis ergebnis) => ergebnis switch
    {
        AnmeldeErgebnis.FalscheDaten => "identifiants",
        AnmeldeErgebnis.Verriegelt => "verrouille",
        AnmeldeErgebnis.KontoGesperrt => "compte-bloque",
        AnmeldeErgebnis.KontoInPruefung => "compte-en-attente",
        AnmeldeErgebnis.SchuleSuspendiert => "ecole-suspendue",
        AnmeldeErgebnis.SchuleInPruefung => "ecole-en-attente",
        AnmeldeErgebnis.SchuleAbgelehnt => "ecole-refusee",
        _ => string.Empty
    };

    /// <summary>Französische Meldung zu einem Fehlercode der Anmeldeseite.</summary>
    public static string? Meldung(string? code) => code switch
    {
        "identifiants" => "L'identifiant ou le mot de passe est incorrect.",
        "verrouille" => "Trop de tentatives incorrectes. Pour votre sécurité, la connexion est bloquée pendant 15 minutes.",
        "compte-bloque" => "Votre compte a été bloqué. Contactez la direction de votre établissement.",
        "compte-en-attente" => "Votre inscription n'a pas encore été validée par votre établissement.",
        "ecole-suspendue" => "L'accès à votre établissement est suspendu. Veuillez contacter la direction de votre établissement.",
        "ecole-en-attente" => "Votre établissement est en cours de validation par SKOULOU. Vous pourrez vous connecter dès sa validation.",
        "ecole-refusee" => "L'inscription de votre établissement n'a pas été acceptée. Contactez SKOULOU pour en savoir plus.",
        "limite" => "Trop de tentatives en peu de temps. Patientez une minute, puis réessayez.",
        "session" => "Votre page a expiré. Veuillez réessayer.",
        _ => null
    };
}
