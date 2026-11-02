namespace SkoulouApp.WebApp.Services;

public sealed record BulletinFach(string Fach, string Lehrer, decimal Koeffizient, decimal? Note);
public sealed record BulletinVorperiode(string Titel, decimal? Note);
public sealed record BulletinSnapshot(string Schule, string? Slogan, string? Adresse, string? Republik, string? Ministerium,
    string? Motto, string? Zulassung, string? Kontakt, string Schuljahr, string Titel, string Klasse, string Schueler,
    string? Matrikelnummer, string? Geburtsort, string? Geburtsdatum, bool IstWiederholer,
    List<BulletinFach> Faecher, decimal? Durchschnitt, int? Rang, int AnzahlSchueler,
    List<BulletinVorperiode> Vorperioden, decimal? JahresNote, string Bemerkung,
    string Farbe, bool Koeffizienten, bool RangAnzeigen, bool VorperiodenAnzeigen, bool Unterschriften, string? FussText,
    string BlockReihenfolge = "Schueler,Noten,Ergebnisse,Bemerkung,Unterschriften", byte[]? Logo = null, byte[]? Siegel = null,
    int ErfassteTage = 0, int FehlTage = 0, int EntschuldigteFehlTage = 0, int Verspaetungen = 0, int VerspaetungsMinuten = 0,
    int SchriftGroesse = 10, int SeitenRand = 25, bool ZeigeLehrkraefte = true, bool ZeigeAnwesenheit = true);
public sealed record BulletinListenZeile(Guid Id, Guid SchulID, string Titel, string Schueler, int Version, bool IstVeroeffentlicht);
