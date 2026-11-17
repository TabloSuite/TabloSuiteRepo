namespace ENote.Core.DataModels.Identity;

/// <summary>
/// Eine Anmeldung auf einem Gerät (« Appareils connectés », 17.11.2026). Besitzer-Konten dürfen auf mehreren Geräten
/// gleichzeitig angemeldet sein (Kasse 1 + Kasse 2 im Laden); für andere Konten bleibt es bei einer Anmeldung.
/// Gültig nur, solange <see cref="Stempel"/> dem <see cref="AppUser.ActiveSessionId"/> des Kontos entspricht: Passwortänderung,
/// Sperre, « tout déconnecter » usw. ändern den Stempel und beenden so alle Geräte auf einmal.
/// </summary>
public class AnmeldeSitzung
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>Konto-Stempel zum Zeitpunkt der Anmeldung (= ActiveSessionId).</summary>
    public Guid Stempel { get; set; }

    /// <summary>Kurzbeschreibung aus dem Browser, z. B. « Chrome · Android ».</summary>
    public string Geraet { get; set; } = "";

    public DateTime ErstelltUtc { get; set; }
    public DateTime ZuletztUtc { get; set; }
    public DateTime? BeendetUtc { get; set; }
}
