using Microsoft.AspNetCore.Identity;
using Skoulou.Core.Rechte;

namespace Skoulou.Core.Entitaeten;

/// <summary>
/// Benutzerkonto. <c>UserName</c> ist das Identifiant (Anmeldeschlüssel), die E-Mail nur Kontakt
/// (mehrfach erlaubt). Genau eine Rolle je Konto: <see cref="Rolle"/> ist führend, die Identity-Rolle
/// wird für Claims und [Authorize] mitgeführt.
/// </summary>
public class Konto : IdentityUser<Guid>, IZeitgestempelt
{
    public string Vorname { get; set; } = string.Empty;
    public string Nachname { get; set; } = string.Empty;
    public string? Telefon { get; set; }
    public Geschlecht Geschlecht { get; set; }
    public DateOnly? Geburtsdatum { get; set; }

    /// <summary>Hauptrolle (Werte aus <see cref="Rollen"/>).</summary>
    public string Rolle { get; set; } = string.Empty;

    public KontoStatus Status { get; set; } = KontoStatus.Actif;

    /// <summary>Schule für alle Schulrollen; null für SuperAdmin und Responsable.</summary>
    public Guid? SchuleId { get; set; }
    public Schule? Schule { get; set; }

    /// <summary>Zusatzrechte eines Koordinators (nur vom Direktor gesetzt).</summary>
    public KoordinatorRechte KoordinatorRechte { get; set; }

    public bool MussPasswortAendern { get; set; }

    /// <summary>Einzig gültige Sitzung; eine neue Anmeldung ersetzt sie.</summary>
    public Guid? AktiveSitzungId { get; set; }

    public Guid? FotoMediumId { get; set; }
    public string? AgbVersion { get; set; }
    public DateTime? AgbAkzeptiertAmUtc { get; set; }
    public DateTime? LetzteAnmeldungUtc { get; set; }
    public Guid? ErstelltVonId { get; set; }
    public bool SelbstRegistriert { get; set; }
    /// <summary>
    /// Hauptadministrator (nur ein SuperAdmin-Konto): verwaltet die anderen Administratoren und kann dort selbst
    /// nicht geändert werden. Fehlt die Kennzeichnung, erhält sie das älteste aktive Administratorkonto.
    /// </summary>
    public bool IstHauptAdmin { get; set; }

    public DateTime ErstelltAmUtc { get; set; }
    public DateTime GeaendertAmUtc { get; set; }

    /// <summary>« Prénom Nom » für Anzeigen.</summary>
    public string VollerName => $"{Vorname} {Nachname}".Trim();
}

/// <summary>Identity-Rolle mit französischer Beschreibung.</summary>
public class Rolle : IdentityRole<Guid>, IZeitgestempelt
{
    public string? Beschreibung { get; set; }
    public DateTime ErstelltAmUtc { get; set; }
    public DateTime GeaendertAmUtc { get; set; }
}
