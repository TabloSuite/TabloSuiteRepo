using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Skoulou.Core.Entitaeten;

namespace Skoulou.WebApp.Services.Noten;

public sealed record SnapshotSchule(string Name, string? Slogan, string? Adresse, string? Kontakt, string? Zulassung, string? Ministerium,
    string? Republik, string? Motto, string? DirectionRegionale, string? Statut, string Ort);

public sealed record SnapshotSchueler(string Nachname, string Vorname, string? Matrikel, string? Geburtsdatum, string? Geburtsort, string? Geschlecht,
    string? Nationalitaet, bool Interne, bool Affecte, bool Redoublant)
{
    public string Name => $"{Nachname.ToUpperInvariant()} {Vorname}".Trim();
}

public sealed record SnapshotFach(string Name, string? Gruppe, string? Lehrer, decimal Koeffizient, decimal? Moyenne, string MoyenneText, decimal? Punkte,
    string? Rang, string? Teilnoten, string? Appreciation, string? JahresMoyenne);

public sealed record SnapshotGruppe(string Name, string Moyenne, decimal Koeffizienten);

public sealed record SnapshotPeriode(string Titel, string Moyenne, string Rang);

public sealed record SnapshotBilan(string Moyenne, string Rang, string Entscheidung, IReadOnlyList<SnapshotPeriode> Perioden);

public sealed record SnapshotStatistik(string Mittel, string Min, string Max, string ProzentUeber10, int Klassiert);

public sealed record SnapshotAnwesenheit(bool Erfasst, int Absences, int Justifiees, int Retards, int Minuten);

/// <summary>Layout der Vorlage zum Zeitpunkt der Erstellung (Vorlagenänderungen wirken nur auf künftige Bulletins).</summary>
public sealed record SnapshotLayout(string Farbe, bool ZeigeKoeffizienten, bool ZeigeRang, bool ZeigeVorperioden, bool ZeigeUnterschriften,
    bool ZeigeLehrkraefte, bool ZeigeAnwesenheit, bool ZeigeTeilnoten, bool ZeigePunkte, bool ZeigeFachRang, bool ZeigeAppreciation,
    bool ZeigeSignaturSpalte, bool ZeigeFachGruppen, bool ZeigeKlassenStatistik, bool ZeigeMentionen, bool ZeigeJahresBilanz, bool ZeigeFoto,
    bool ZeigeNoteInWorten, int SchriftGroesse, int SeitenRand, string? Fusstext)
{
    public static SnapshotLayout Aus(BulletinVorlage v) => new(v.Farbe, v.ZeigeKoeffizienten, v.ZeigeRang, v.ZeigeVorperioden, v.ZeigeUnterschriften,
        v.ZeigeLehrkraefte, v.ZeigeAnwesenheit, v.ZeigeTeilnoten, v.ZeigePunkte, v.ZeigeFachRang, v.ZeigeAppreciation, v.ZeigeSignaturSpalte,
        v.ZeigeFachGruppen, v.ZeigeKlassenStatistik, v.ZeigeMentionen, v.ZeigeJahresBilanz, v.ZeigeFoto, v.ZeigeNoteInWorten,
        v.SchriftGroesse, v.SeitenRand, v.Fusstext);
}

/// <summary>Inhalt eines Bulletins (Grundlage des Hashs): alles, was gedruckt wird, ohne Nummer/Datum/QR.</summary>
public sealed record BulletinInhalt(
    SnapshotSchule Schule, string Jahr, string Periode, string Titel, string Klasse, int Effectif, SnapshotSchueler Schueler,
    IReadOnlyList<SnapshotFach> Faecher, IReadOnlyList<SnapshotGruppe> Gruppen, decimal? Moyenne, string MoyenneText, string? MoyenneInWorten,
    string Rang, decimal SummeKoeffizienten, decimal? SummePunkte, string? Appreciation, SnapshotStatistik? Statistik,
    IReadOnlyList<SnapshotPeriode> Vorperioden, SnapshotBilan? Bilan, Distinktion Distinktion, string DistinktionText,
    Sanktionen Sanktionen, IReadOnlyList<string> SanktionenTexte, string? ConseilBemerkung, SnapshotAnwesenheit Anwesenheit,
    string? Klassenlehrer, SnapshotLayout Layout);

/// <summary>Dokumentdaten (nicht im Hash): Nummer, Datum, Prüf-Token, ersetzte Nummer.</summary>
public sealed record SnapshotDokument(string Nummer, string Datum, string? Token, string? ErsetztNummer, bool IstRectificatif, string? Grund, bool Entwurf);

/// <summary>Eingefrorenes Bulletin (JSON in <see cref="Bulletin.SnapshotJson"/>, ohne Bildbytes).</summary>
public sealed record BulletinSnapshot(int Version, BulletinInhalt Inhalt, SnapshotDokument? Dokument)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public string AlsJson() => JsonSerializer.Serialize(this, Json);

    public static BulletinSnapshot AusJson(string json)
        => JsonSerializer.Deserialize<BulletinSnapshot>(json, Json) ?? throw new InvalidOperationException("Snapshot illisible.");

    /// <summary>SHA-256 (hex) über den Inhalt – gleicher Inhalt = gleicher Hash, unabhängig von Nummer und Datum.</summary>
    public static string HashVon(BulletinInhalt inhalt)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(inhalt, Json))));

    /// <summary>Hash einer ganzen Klasse (Reihenfolge der Schüler fest).</summary>
    public static string HashVon(IEnumerable<string> einzelHashes)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', einzelHashes))));
}
