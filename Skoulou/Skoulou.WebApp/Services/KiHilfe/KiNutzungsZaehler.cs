using Microsoft.Extensions.Options;

namespace Skoulou.WebApp.Services.KiHilfe;

/// <summary>Ergebnis einer Reservierung.</summary>
public enum KiKontingent { Frei, KontoGrenze, AppGrenze }

/// <summary>
/// Kostengrenzen der KI-Hilfe im Speicher (Azure läuft mit einer Instanz; nach einem Neustart beginnt die Zählung neu):
/// höchstens <see cref="KiHilfeOptionen.FragenJeKontoUndTag"/> Fragen je Konto und Tag (UTC) und
/// <see cref="KiHilfeOptionen.FragenJeTag"/> für die ganze App. Gespeichert werden nur Zähler – keine Fragen.
/// </summary>
public sealed class KiNutzungsZaehler(IOptions<KiHilfeOptionen> optionen, TimeProvider zeit)
{
    private readonly object sperre = new();
    private readonly Dictionary<string, int> jeKonto = new(StringComparer.Ordinal);
    private DateOnly tag;
    private int gesamt;

    /// <summary>Reserviert eine Frage (vor dem Aufruf, damit parallele Klicks die Grenze nicht überholen).</summary>
    public KiKontingent Reservieren(string konto)
    {
        lock (sperre)
        {
            Tageswechsel();
            if (gesamt >= Math.Max(0, optionen.Value.FragenJeTag)) return KiKontingent.AppGrenze;
            var bisher = jeKonto.GetValueOrDefault(konto);
            if (bisher >= KiHilfeOptionen.FragenJeKontoUndTag) return KiKontingent.KontoGrenze;
            jeKonto[konto] = bisher + 1;
            gesamt++;
            return KiKontingent.Frei;
        }
    }

    /// <summary>Gibt eine Reservierung zurück (technischer Fehler: zählt nicht gegen den Nutzer).</summary>
    public void Freigeben(string konto)
    {
        lock (sperre)
        {
            Tageswechsel();
            if (jeKonto.TryGetValue(konto, out var n) && n > 0)
            {
                jeKonto[konto] = n - 1;
                gesamt = Math.Max(0, gesamt - 1);
            }
        }
    }

    /// <summary>Verbleibende Fragen dieses Kontos heute.</summary>
    public int Verbleibend(string konto)
    {
        lock (sperre)
        {
            Tageswechsel();
            return Math.Max(0, KiHilfeOptionen.FragenJeKontoUndTag - jeKonto.GetValueOrDefault(konto));
        }
    }

    /// <summary>Fragen der ganzen App heute (für das Log).</summary>
    public int HeuteGesamt
    {
        get { lock (sperre) { Tageswechsel(); return gesamt; } }
    }

    private void Tageswechsel()
    {
        var heute = DateOnly.FromDateTime(zeit.GetUtcNow().UtcDateTime);
        if (heute == tag) return;
        tag = heute;
        gesamt = 0;
        jeKonto.Clear();
    }
}
