namespace Skoulou.Core.Rechte;

/// <summary>
/// Die Rechte-Matrix des Bauplans (§3.4) als Daten. Eine Zeile je Fähigkeit, eine Spalte je Rolle;
/// Koordinator-Einträge können zusätzlich ein Recht verlangen (<see cref="Regel.KoordinatorRecht"/>).
/// </summary>
public static class RechteMatrix
{
    /// <summary>Recht einer Rolle; <paramref name="KoordinatorRecht"/> nur für Koordinatoren relevant.</summary>
    public readonly record struct Regel(Zugriffsart Art, KoordinatorRechte KoordinatorRecht = KoordinatorRechte.Keine);

    private static readonly Regel Nein = new(Zugriffsart.Nein);
    private static readonly Regel Ja = new(Zugriffsart.Ja);
    private static readonly Regel Lesen = new(Zugriffsart.Lesen);
    private static readonly Regel Eigene = new(Zugriffsart.Eigene);
    private static Regel MitRecht(KoordinatorRechte recht) => new(Zugriffsart.Ja, recht);

    // Spalten: Direktor, Koordinator, Lehrer, Schüler, Eltern, Responsable, SuperAdmin
    private static readonly Dictionary<Faehigkeit, Regel[]> Tabelle = new()
    {
        [Faehigkeit.SchuleVerwalten] = [Ja, Nein, Nein, Nein, Nein, Lesen, Ja],
        [Faehigkeit.KontenVerwalten] = [Ja, Ja, Nein, Nein, Nein, Nein, Ja],
        [Faehigkeit.KoordinatorenVerwalten] = [Ja, Nein, Nein, Nein, Nein, Nein, Ja],
        [Faehigkeit.SchuljahrVerwalten] = [Ja, Ja, Nein, Nein, Nein, Nein, Nein],
        [Faehigkeit.KlassenVerwalten] = [Ja, Ja, Nein, Nein, Nein, Nein, Nein],
        [Faehigkeit.StundenplanVerwalten] = [Ja, Ja, Nein, Nein, Nein, Nein, Nein],
        [Faehigkeit.AnwesenheitErfassen] = [Ja, Ja, Eigene, Nein, Nein, Nein, Nein],
        [Faehigkeit.NotenErfassen] = [Ja, Ja, Eigene, Lesen, Lesen, Nein, Nein],
        [Faehigkeit.NotenAllerFaecher] = [Ja, Ja, Nein, Nein, Nein, Nein, Nein],
        [Faehigkeit.KlassenratErfassen] = [Ja, Ja, Eigene, Nein, Nein, Nein, Nein],
        [Faehigkeit.BulletinsVeroeffentlichen] = [Ja, Ja, Nein, Nein, Nein, Nein, Nein],
        [Faehigkeit.InhalteErstellen] = [Lesen, Lesen, Eigene, Nein, Nein, Nein, Nein],
        [Faehigkeit.InhalteFuerLehrerErfassen] = [Ja, Ja, Nein, Nein, Nein, Nein, Nein],
        [Faehigkeit.Korrigieren] = [Ja, Ja, Eigene, Nein, Nein, Nein, Nein],
        [Faehigkeit.FinanzenEinsehen] = [Ja, MitRecht(KoordinatorRechte.FinanzenEinsehen), Nein, Eigene, Eigene, Lesen, Lesen],
        [Faehigkeit.ZahlungenErfassen] = [Ja, MitRecht(KoordinatorRechte.ZahlungenErfassen), Nein, Nein, Nein, Nein, Nein],
        [Faehigkeit.BuchhaltungErfassen] = [Ja, MitRecht(KoordinatorRechte.BuchhaltungErfassen), Nein, Nein, Nein, Nein, Nein],
        [Faehigkeit.GebuehrenVerwalten] = [Ja, MitRecht(KoordinatorRechte.ZahlungenErfassen), Nein, Nein, Nein, Nein, Nein],
        [Faehigkeit.RechnungSkoulouBearbeiten] = [Ja, MitRecht(KoordinatorRechte.RechnungSkoulou), Nein, Nein, Nein, Lesen, Ja],
        [Faehigkeit.PlattformVerwalten] = [Nein, Nein, Nein, Nein, Nein, Nein, Ja],
        [Faehigkeit.SchulenBetreuen] = [Nein, Nein, Nein, Nein, Nein, Eigene, Ja],
        [Faehigkeit.Lernen] = [Nein, Nein, Nein, Eigene, Nein, Nein, Nein],
    };

    private static int Spalte(string? rolle) => rolle switch
    {
        Rollen.Direktor => 0,
        Rollen.Koordinator => 1,
        Rollen.Lehrer => 2,
        Rollen.Schueler => 3,
        Rollen.Eltern => 4,
        Rollen.Responsable => 5,
        Rollen.SuperAdmin => 6,
        _ => -1
    };

    /// <summary>Alle Fähigkeiten der Matrix (für Tests und Übersichten).</summary>
    public static IEnumerable<Faehigkeit> Faehigkeiten => Tabelle.Keys;

    /// <summary>Regel einer Rolle für eine Fähigkeit (unbekannte Rolle = Nein).</summary>
    public static Regel RegelFuer(string? rolle, Faehigkeit faehigkeit)
    {
        var spalte = Spalte(rolle);
        return spalte < 0 || !Tabelle.TryGetValue(faehigkeit, out var zeile) ? Nein : zeile[spalte];
    }

    /// <summary>Wirksamer Zugriff unter Berücksichtigung der Koordinator-Rechte.</summary>
    public static Zugriffsart Zugriff(string? rolle, Faehigkeit faehigkeit, KoordinatorRechte rechte = KoordinatorRechte.Keine)
    {
        var regel = RegelFuer(rolle, faehigkeit);
        if (regel.KoordinatorRecht != KoordinatorRechte.Keine && (rechte & regel.KoordinatorRecht) != regel.KoordinatorRecht)
            return Zugriffsart.Nein;
        return regel.Art;
    }

    /// <summary>Darf die Rolle schreiben (Ja oder Eigene)?</summary>
    public static bool DarfSchreiben(Zugriffsart art) => art is Zugriffsart.Ja or Zugriffsart.Eigene;
}
