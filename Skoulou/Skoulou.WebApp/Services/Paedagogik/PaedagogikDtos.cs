using Skoulou.Core.Entitaeten;
using Skoulou.Core.Paedagogik;

namespace Skoulou.WebApp.Services.Paedagogik;

// ---------- Inhalte (Lehrkraft, Direktion) ----------

/// <summary>Filter der Inhaltslisten (Lehrkraft: immer eigene; Direktion: alle der Schule).</summary>
public sealed record InhaltFilter(InhaltsArt Art, string? Suche = null, InhaltsStatus? Status = null,
    Guid? AutorId = null, Guid? FachId = null, Guid? StufeId = null, Guid? KlasseId = null);

public sealed record InhaltZeile(Guid Id, InhaltsArt Art, string Titel, string Stufe, string Fach, InhaltsStatus Status,
    Guid AutorId, string AutorName, bool SaisiParDirection, int AnzahlFragen, int AnzahlZuweisungen, DateTime GeaendertAmUtc);

/// <summary>Stufe + Fach, für die eine Lehrkraft Inhalte anlegen darf (aus ihren Lehraufträgen).</summary>
public sealed record StufeFach(Guid StufeId, string Stufe, Guid FachId, string Fach);

public sealed record LehrkraftOption(Guid Id, string Name, string Rolle);

public sealed record AuswahlEintrag(Guid Id, string Name);

/// <summary>Auswahllisten für Filter (Direktion) und Editor.</summary>
public sealed record InhaltOptionen(IReadOnlyList<StufeFach> Kombinationen, IReadOnlyList<LehrkraftOption> Lehrkraefte,
    IReadOnlyList<AuswahlEintrag> Faecher, IReadOnlyList<AuswahlEintrag> Klassen);

/// <summary>Kopf eines Inhalts für die Detailseite.</summary>
public sealed record InhaltKopf(Guid Id, InhaltsArt Art, string Titel, string Stufe, Guid StufeId, string Fach, Guid FachId,
    InhaltsStatus Status, Guid AutorId, string AutorName, string? ErfasstVonName, string? Zusammenfassung,
    int DauerMinuten, decimal Bestehensgrenze, int AnzahlFragen, decimal MaxPunkte, bool DarfBearbeiten, DateTime GeaendertAmUtc);

// ---------- Ansicht (Vorschau, Druck, Lehrkraft mit Lösungen) ----------

public sealed record AnsichtOption(Guid Id, string Text, bool Richtig);

public sealed record AnsichtFrage(Guid Id, int Nr, FragenTyp Typ, string TextHtml, decimal Punkte, string? AudioText, string Sprache,
    IReadOnlyList<AnsichtOption> Optionen, IReadOnlyList<string> Varianten, IReadOnlyList<IReadOnlyList<string>> Luecken,
    IReadOnlyList<string> Wortbank, int Revision);

public sealed record AnsichtBlock(Guid Id, int Nr, string? Titel, string? Consigne, IReadOnlyList<AnsichtFrage> Fragen);

public sealed record InhaltAnsicht(InhaltKopf Kopf, IReadOnlyList<string> Seiten, Guid? VideoMediumId, string? Beschreibung,
    IReadOnlyList<AnsichtBlock> Bloecke);

// ---------- Zuweisungen ----------

public sealed record KlassenOption(Guid Id, string Name, string? Stufe, bool GleicheStufe);

public sealed record ZuweisungZeile(Guid Id, Guid KlasseId, string Klasse, string Schuljahr, DateTime AbUtc, DateTime? FristUtc,
    int? MaxVersuche, VersuchsWertung Wertung, LoesungsSichtbarkeit Loesungen, ZuweisungsStatus Status,
    int Schueler, int Abgeschlossen, decimal? Durchschnitt, int OffeneKorrekturen);

// ---------- Schüler ----------

public sealed record SchuelerOption(Guid Id, string Text);

/// <summary>Frage aus Schülersicht: ohne Lösungen und ohne Audiotext (der wird nur beim Abspielen geholt).</summary>
public sealed record SchuelerFrage(Guid Id, int Nr, FragenTyp Typ, string TextHtml, decimal Punkte, bool HatAudio,
    IReadOnlyList<SchuelerOption> Optionen, int AnzahlLuecken, IReadOnlyList<string> Wortbank, bool AutomatischBewertet);

public sealed record SchuelerBlock(Guid Id, int Nr, string? Titel, string? Consigne, IReadOnlyList<SchuelerFrage> Fragen);

/// <summary>Lösung einer Frage (nur, wenn die Lösungsregel es erlaubt).</summary>
public sealed record Loesung(IReadOnlyList<Guid> RichtigeOptionen, IReadOnlyList<string> Antworten, IReadOnlyList<IReadOnlyList<string>> Luecken);

/// <summary>Stand einer beantworteten Frage im Versuch.</summary>
public sealed record AntwortStand(Guid FrageId, AntwortEingabe Eingabe, BewertungsStatus Status, decimal? Punkte, decimal MaxPunkte,
    int? RichtigeLuecken, string? Feedback, bool ManuellKorrigiert, Loesung? Loesung);

public sealed record VersuchInfo(Guid Id, int Nr, VersuchsStatus Status, decimal Punkte, decimal MaxPunkte, DateTime StartUtc,
    DateTime? AbgabeUtc, DateTime? DeadlineUtc);

public sealed record FachAnzahl(Guid FachId, string Fach, int Anzahl);

public sealed record LeconKarte(Guid ZuweisungId, Guid LeconId, string Titel, string? Zusammenfassung, string Fach, string Lehrer,
    DateTime AbUtc, DateTime? FristUtc, int AnzahlFragen, bool Abgeschlossen, decimal? Prozent, bool KorrekturOffen, bool HatVideo);

/// <summary>Alles, was der Leser einer Leçon braucht.</summary>
public sealed record SchuelerLecon(Guid ZuweisungId, Guid LeconId, string Titel, string Fach, string Lehrer, string? Zusammenfassung,
    IReadOnlyList<string> Seiten, Guid? VideoMediumId, IReadOnlyList<SchuelerBlock> Bloecke,
    DateTime? FristUtc, string? Sperrgrund, string Versuchsregel, LoesungsSichtbarkeit Loesungen,
    VersuchInfo? AktuellerVersuch, IReadOnlyList<AntwortStand> Antworten, bool DarfNeuenVersuch, int? VerbleibendeVersuche,
    int AnzahlVersuche, decimal? GewerteterProzent, bool Abgeschlossen, bool StimmeAus);

public sealed record FrageRueckmeldung(AntwortStand Stand, VersuchInfo Versuch, bool VersuchAbgeschlossen, bool HatNaechste);

public enum PruefungsZustand
{
    AVenir,
    Ouvert,
    EnCours,
    Envoye,
    Corrige,
    Manque
}

public sealed record PruefungsKarte(Guid ZuweisungId, string Titel, string Fach, string Lehrer, int DauerMinuten,
    DateTime AbUtc, DateTime? FristUtc, PruefungsZustand Zustand, DateTime? DeadlineUtc,
    decimal? Punkte, decimal MaxPunkte, decimal? Prozent, bool? Bestanden, bool KorrekturOffen);

/// <summary>Examen aus Schülersicht: Fragen nur während des eigenen Versuchs bzw. zur Durchsicht nach der Frist.</summary>
public sealed record PruefungsSitzung(PruefungsKarte Karte, string? Beschreibung, decimal Bestehensgrenze, VersuchInfo? Versuch,
    IReadOnlyList<SchuelerBlock> Bloecke, IReadOnlyList<AntwortStand> Antworten, int RestSekunden, bool LoesungenSichtbar);

public sealed record EleveUebersicht(string? Klasse, IReadOnlyList<AufgabeOffen> Offen, decimal? Gesamt, int Abgeschlossen, int Ausstehend,
    bool StimmeAus);

public sealed record AufgabeOffen(Guid ZuweisungId, InhaltsArt Art, string Titel, string Fach, DateTime? FristUtc, bool Begonnen);

public sealed record FachErgebnis(string Fach, decimal? Prozent, int Abgeschlossen, int Gesamt);

public sealed record ErgebnisZeile(Guid ZuweisungId, InhaltsArt Art, string Titel, string Fach, DateTime? FristUtc, decimal Punkte,
    decimal MaxPunkte, decimal? Prozent, bool Abgeschlossen, bool KorrekturOffen, bool Ueberfaellig, int AnzahlVersuche,
    Guid? GewerteterVersuchId, DateTime? LetzteAktivitaetUtc);

public sealed record SchuelerErgebnisse(decimal? Gesamt, IReadOnlyList<FachErgebnis> Faecher, IReadOnlyList<ErgebnisZeile> Zeilen);

// ---------- Lehrkraft: Ergebnisse, Korrektur, Start ----------

public sealed record ZuweisungKopf(Guid Id, InhaltsArt Art, Guid InhaltId, string Titel, string Klasse, string Fach, DateTime AbUtc,
    DateTime? FristUtc, int? MaxVersuche, VersuchsWertung Wertung, LoesungsSichtbarkeit Loesungen, ZuweisungsStatus Status,
    decimal MaxPunkte, decimal Bestehensgrenze, int DauerMinuten);

public enum SchuelerFortschritt
{
    PasCommence,
    EnCours,
    Termine,
    ACorriger
}

public sealed record SchuelerErgebnisZeile(Guid SchuelerId, string Name, SchuelerFortschritt Fortschritt, int AnzahlVersuche,
    decimal Punkte, decimal MaxPunkte, decimal? Prozent, DateTime? LetzteAktivitaetUtc, Guid? GewerteterVersuchId, bool InKlasse);

public sealed record ZuweisungErgebnisse(ZuweisungKopf Kopf, IReadOnlyList<SchuelerErgebnisZeile> Zeilen);

/// <summary>Eine Antwort in der Versuchsansicht für Lehrkräfte (mit Lösung und Korrekturmöglichkeit).</summary>
public sealed record VersuchsAntwortZeile(AnsichtFrage Frage, Guid? AntwortId, AntwortEingabe Eingabe, decimal? Punkte,
    BewertungsStatus Status, string? Feedback, bool ManuellKorrigiert, string? KorrigiertVon);

public sealed record VersuchsDetail(Guid VersuchId, string Schueler, string Titel, int Nr, VersuchsStatus Status, decimal Punkte,
    decimal MaxPunkte, DateTime StartUtc, DateTime? AbgabeUtc, IReadOnlyList<VersuchsAntwortZeile> Antworten);

public sealed record KorrekturZeile(Guid AntwortId, Guid VersuchId, Guid ZuweisungId, string Schueler, string Klasse, string Inhalt,
    InhaltsArt Art, int BlockNr, int FrageNr, string FrageText, DateTime? AbgegebenUtc, decimal? Punkte, decimal MaxPunkte, DateTime? KorrigiertAmUtc);

public sealed record KorrekturVerlauf(DateTime ZeitUtc, string Von, decimal? AltePunkte, decimal NeuePunkte, string? Feedback);

public sealed record KorrekturDetail(Guid AntwortId, string Schueler, string Klasse, string Inhalt, AnsichtFrage Frage,
    AntwortEingabe Eingabe, decimal? Punkte, string? Feedback, IReadOnlyList<KorrekturVerlauf> Verlauf);

public sealed record LehrerKlasse(Guid KlasseId, string Klasse, IReadOnlyList<string> Faecher, int Schueler);

public sealed record FristEintrag(Guid ZuweisungId, InhaltsArt Art, string Titel, string Klasse, DateTime FristUtc, int Abgeschlossen, int Schueler);

public sealed record LehrerStart(IReadOnlyList<LehrerKlasse> Klassen, int OffeneKorrekturen, IReadOnlyList<FristEintrag> Fristen,
    int Lecons, int Pruefungen);

// ---------- Medien, Formeln, Druck ----------

public sealed record MediumZeile(Guid Id, MedienArt Art, string Dateiname, long Bytes, int? DauerSek, string Besitzer, DateTime ErstelltAmUtc)
{
    public string Url => $"/medien/{Id}";
}

public sealed record FormelZeile(Guid Id, string LaTeX, string Bezeichnung);

public sealed record DruckDaten(string Schule, string? Klasse, string Lehrer, string Titel, string Fach, string Stufe, InhaltsArt Art,
    IReadOnlyList<string> Seiten, IReadOnlyList<AnsichtBlock> Bloecke, bool MitLoesungen, int DauerMinuten, decimal MaxPunkte, string? Beschreibung);
