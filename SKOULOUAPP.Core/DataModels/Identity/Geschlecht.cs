namespace SkoulouApp.Core.DataModels.Identity;

/// <summary>
/// Geschlecht eines Benutzers. Wird bei der Registrierung ausgewählt.
/// Standardwert ist <see cref="Divers"/>.
/// </summary>
public enum Geschlecht
{
    /// <summary>Divers / keine Angabe (Standard).</summary>
    Divers = 0,

    /// <summary>Männlich.</summary>
    Maennlich = 1,

    /// <summary>Weiblich.</summary>
    Weiblich = 2
}
