using Skoulou.Core.Entitaeten;

namespace Skoulou.Tests.Simulation;

internal enum SchulTyp { Primaire, College, Lycee, Langues }

/// <summary>Steckbrief einer simulierten Schule (Klassen je Stufe, Klassengröße von–bis).</summary>
internal sealed record SchulProfil(string Name, string Ort, SchulTyp Typ, PeriodenArt Perioden, int KlassenJeStufe, int MinGroesse, int MaxGroesse,
    long? Preis = null, decimal? Prozent = null);

internal sealed record SimFach(Guid Id, string Name, decimal Koeffizient);

internal sealed class SimStufe(Guid id, string name, int nr)
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public int Nr { get; } = nr;
}

internal sealed class SimKlassenfach(Guid id, Guid fachId, Guid lehrer)
{
    public Guid Id { get; } = id;
    public Guid FachId { get; } = fachId;
    /// <summary>Wer die Noten erfasst (Lehrer des Lehrauftrags, sonst die Direktion).</summary>
    public Guid Lehrer { get; set; } = lehrer;
}

internal sealed class SimKlasse(Guid id, string name, SimStufe stufe, int session)
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public SimStufe Stufe { get; } = stufe;
    /// <summary>Sprachschule: Nr der Session (1…), sonst 0.</summary>
    public int Session { get; } = session;
    public List<SimKlassenfach> Faecher { get; } = [];
}

internal sealed class SimPeriode(Guid id, int nr, string titel, DateOnly beginn, DateOnly ende)
{
    public Guid Id { get; } = id;
    public int Nr { get; } = nr;
    public string Titel { get; } = titel;
    public DateOnly Beginn { get; } = beginn;
    public DateOnly Ende { get; } = ende;
    public bool Abgeschlossen { get; set; }
}

internal sealed record SimPosten(Guid Id, string Name, long Betrag, Guid? StufeId, IReadOnlyList<(long Betrag, DateOnly Faellig)> Raten);

internal sealed class SimJahr(Guid id, string bezeichnung, DateOnly beginn, DateOnly ende)
{
    public Guid Id { get; } = id;
    public string Bezeichnung { get; } = bezeichnung;
    public DateOnly Beginn { get; } = beginn;
    public DateOnly Ende { get; } = ende;
    public List<SimPeriode> Perioden { get; } = [];
    public List<SimKlasse> Klassen { get; } = [];
    public List<SimPosten> Posten { get; } = [];
    public long Preis { get; set; }

    public long SollFuer(Guid? stufeId) => Posten.Where(p => p.StufeId is null || p.StufeId == stufeId).Sum(p => p.Betrag);
    public long PostenBetrag(Guid? stufeId, string name) => Posten.Where(p => p.Name.StartsWith(name, StringComparison.Ordinal) && (p.StufeId is null || p.StufeId == stufeId)).Sum(p => p.Betrag);
}

/// <summary>Unabhängiges Gebührenbuch eines Schülers für ein Jahr (für die Invariante « Saldo = Soll − bezahlt + Vortrag »).</summary>
internal sealed class JahresKonto
{
    public Guid? StufeId { get; set; }
    public long Ermaessigung { get; set; }
    public long Bezahlt { get; set; }
}

internal sealed class SimSchueler(Guid id, string vorname, string nachname, double niveau, int zahler)
{
    public Guid Id { get; } = id;
    public string Vorname { get; } = vorname;
    public string Nachname { get; } = nachname;
    public double Niveau { get; } = niveau;
    /// <summary>0 = pünktlich, 1 = unregelmäßig, 2 = säumig, 3 = alles auf einmal.</summary>
    public int Zahler { get; } = zahler;
    public Guid? Eltern { get; set; }
    public SimKlasse? Klasse { get; set; }
    public bool Aktiv { get; set; }
    /// <summary>Sprachschule: nach einer Session weder Wechsel noch Austritt (die Einschreibung endet automatisch mit der Session).</summary>
    public bool SessionVergessen { get; set; }
    public Dictionary<Guid, JahresKonto> Konten { get; } = [];
}

internal sealed class SimSchule(int nr, SchulProfil profil)
{
    public int Nr { get; } = nr;
    public SchulProfil P { get; } = profil;
    public Guid Id { get; set; }
    public Guid Direktor { get; set; }
    public Guid KoordMit { get; set; }
    public Guid KoordOhne { get; set; }
    public Guid? Responsable { get; set; }
    public List<Guid> Lehrer { get; } = [];
    public List<SimStufe> Stufen { get; } = [];
    public List<SimFach> Faecher { get; } = [];
    public List<SimJahr> Jahre { get; } = [];
    public SimJahr Jahr => Jahre[^1];
    public Dictionary<Guid, SimSchueler> Schueler { get; } = [];
    public List<Guid> Elternkonten { get; } = [];
    public Dictionary<Guid, int> KinderJeEltern { get; } = [];
    public Dictionary<Guid, Guid> Kategorien { get; } = [];
    public bool Suspendiert { get; set; }

    /// <summary>Netto der manuellen Buchungen (Recette +, Dépense −, Gegenbuchungen abgezogen).</summary>
    public long ManuellNetto;

    /// <summary>Kumulierter Saldo bis einschließlich Jahr <paramref name="bisJahr"/> (positiv = offen, negativ = Guthaben).</summary>
    public long Saldo(SimSchueler s, Guid bisJahr)
    {
        long saldo = 0;
        foreach (var j in Jahre)
        {
            if (s.Konten.TryGetValue(j.Id, out var k))
                saldo += Math.Max(0, j.SollFuer(k.StufeId) - k.Ermaessigung) - k.Bezahlt;
            if (j.Id == bisJahr) break;
        }
        return saldo;
    }

    public IEnumerable<SimSchueler> Aktive => Schueler.Values.Where(s => s.Aktiv && s.Klasse is not null);
}

/// <summary>Geplanter Ablauf einer Plattformrechnung.</summary>
internal enum RechnungsWeg { Normal, Direkt, AnfechtenAufrecht, AnfechtenStorno, StornoNachZahlung, Offen }

internal sealed class SimRechnung(Guid id, SimSchule schule, RechnungsWeg weg, DateOnly erstellt)
{
    public Guid Id { get; } = id;
    public SimSchule Schule { get; } = schule;
    public RechnungsWeg Weg { get; } = weg;
    public DateOnly Erstellt { get; } = erstellt;
    public int Schritt { get; set; }
    public DateOnly Naechster { get; set; } = erstellt;
    public bool Fertig { get; set; }
}
