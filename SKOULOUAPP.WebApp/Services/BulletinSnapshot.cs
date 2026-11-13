namespace SkoulouApp.WebApp.Services;

/// <summary>Teilnote eines Fachs je Bewertungsbestandteil (z. B. « Classe », « Compo »), auf 20 umgerechnet.</summary>
public sealed record BulletinTeilnote(string Bezeichnung, decimal? Note);

public sealed record BulletinFach(string Fach, string Lehrer, decimal Koeffizient, decimal? Note,
    string? Gruppe = null, List<BulletinTeilnote>? Teilnoten = null, int? Rang = null, string? Appreciation = null,
    decimal? JahresNote = null, int? JahresRang = null);

public sealed record BulletinVorperiode(string Titel, decimal? Note);

/// <summary>
/// Schalter des allgemeinen Bulletin-Layouts (seit 11.11.2026). Ältere Snapshots ohne Layout
/// werden unverändert mit dem bisherigen Layout gedruckt.
/// </summary>
public sealed record BulletinLayout(bool Teilnoten, bool Punkte, bool FachRang, bool Appreciation, bool SignaturSpalte,
    bool FachGruppen, bool KlassenStatistik, bool Mentionen, bool JahresBilanz, bool Foto, bool NoteInWorten);

public sealed record BulletinSnapshot(string Schule, string? Slogan, string? Adresse, string? Republik, string? Ministerium,
    string? Motto, string? Zulassung, string? Kontakt, string Schuljahr, string Titel, string Klasse, string Schueler,
    string? Matrikelnummer, string? Geburtsort, string? Geburtsdatum, bool IstWiederholer,
    List<BulletinFach> Faecher, decimal? Durchschnitt, int? Rang, int AnzahlSchueler,
    List<BulletinVorperiode> Vorperioden, decimal? JahresNote, string Bemerkung,
    string Farbe, bool Koeffizienten, bool RangAnzeigen, bool VorperiodenAnzeigen, bool Unterschriften, string? FussText,
    string BlockReihenfolge = "Schueler,Noten,Ergebnisse,Bemerkung,Unterschriften", byte[]? Logo = null, byte[]? Siegel = null,
    int ErfassteTage = 0, int FehlTage = 0, int EntschuldigteFehlTage = 0, int Verspaetungen = 0, int VerspaetungsMinuten = 0,
    int SchriftGroesse = 10, int SeitenRand = 25, bool ZeigeLehrkraefte = true, bool ZeigeAnwesenheit = true,
    string? DokumentNummer = null, DateTime? DokumentDatumUtc = null, string? PruefToken = null, string? ErsetztDokumentNummer = null,
    // --- Allgemeines Layout ---
    BulletinLayout? Layout = null, string? Direction = null, string? Statut = null, string? Ort = null, string? Geschlecht = null,
    string? Nationalitaet = null, bool IstInterne = false, bool IstAffecte = false, byte[]? Foto = null,
    List<string>? TeilnotenSpalten = null, decimal? KlassenMittel = null, decimal? KlassenMin = null, decimal? KlassenMax = null,
    string? Distinktion = null, List<string>? Sanktionen = null, string? RatsBemerkung = null, string? Entscheidung = null,
    int? JahresRang = null, bool IstJahresabschluss = false);

public sealed record BulletinListenZeile(Guid Id, Guid SchulID, string Titel, string Schueler, int Version, bool IstVeroeffentlicht,
    bool IstEndgueltig = false, Guid? ErsetztDurchID = null);

/// <summary>Filter der gespeicherten Bulletin-Ausgaben (null = nicht einschränken).</summary>
public sealed record BulletinListenFilter(Guid? SchuljahrID = null, Guid? PeriodeID = null, Guid? KlasseID = null, string? Suche = null);
