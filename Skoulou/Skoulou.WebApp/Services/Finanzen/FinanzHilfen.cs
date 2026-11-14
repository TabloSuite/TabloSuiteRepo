using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Medien;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Finanzen;

/// <summary>Audit-Codes des Bereichs Finanzen (ergänzen <see cref="AuditAktionen"/>).</summary>
public static class FinanzAudit
{
    public const string BuchungStorniert = "finanzen.ecriture-annulee";
    public const string ErmaessigungGeloescht = "finanzen.reduction-retiree";
    public const string RechnungErstellt = "rechnung.erstellt";
    public const string Auszahlung = "responsable.versement";
    public const string Tarife = "plattform.tarife";
}

/// <summary>Systemkategorien und Standardkategorien des Kassenjournals.</summary>
public static class FinanzKategorien
{
    public const string Scolarite = "Scolarite";
    public const string FactureSkoulou = "FactureSkoulou";

    /// <summary>Allgemeine Kategorien (für alle Schulen, manuell bebuchbar).</summary>
    public static readonly (string Name, BuchungsRichtung Richtung)[] Standard =
    [
        ("Dons et subventions", BuchungsRichtung.Recette), ("Vente de fournitures et tenues", BuchungsRichtung.Recette),
        ("Autres recettes", BuchungsRichtung.Recette), ("Salaires et primes", BuchungsRichtung.Depense),
        ("Loyer", BuchungsRichtung.Depense), ("Eau, électricité, internet", BuchungsRichtung.Depense),
        ("Fournitures et matériel", BuchungsRichtung.Depense), ("Entretien et réparations", BuchungsRichtung.Depense),
        ("Transport et déplacements", BuchungsRichtung.Depense), ("Autres dépenses", BuchungsRichtung.Depense),
    ];

    private static (string Name, BuchungsRichtung Richtung) System(string code) => code switch
    {
        Scolarite => ("Scolarité", BuchungsRichtung.Recette),
        FactureSkoulou => ("Facture SKOULOU", BuchungsRichtung.Depense),
        _ => throw new ArgumentOutOfRangeException(nameof(code))
    };

    /// <summary>Legt fehlende System- und Standardkategorien an (beim Start, im Demo-Seed und in Tests).</summary>
    public static async Task SicherstellenAsync(SkoulouDbContext db, CancellationToken ct = default)
    {
        var vorhanden = await db.Kategorien.Where(k => k.SchuleId == null).Select(k => new { k.Name, k.Richtung, k.SystemCode }).ToListAsync(ct);
        foreach (var code in new[] { Scolarite, FactureSkoulou })
        {
            var (name, richtung) = System(code);
            if (vorhanden.All(k => k.SystemCode != code))
                db.Kategorien.Add(new Kategorie { Name = name, Richtung = richtung, SystemCode = code });
        }
        foreach (var (name, richtung) in Standard)
            if (!vorhanden.Any(k => k.Name == name && k.Richtung == richtung))
                db.Kategorien.Add(new Kategorie { Name = name, Richtung = richtung });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Systemkategorie (wird angelegt, falls sie fehlt – nur auf Schreibwegen aufgerufen).</summary>
    public static async Task<Kategorie> SystemAsync(SkoulouDbContext db, string code, CancellationToken ct)
    {
        var kategorie = await db.Kategorien.FirstOrDefaultAsync(k => k.SchuleId == null && k.SystemCode == code, ct);
        if (kategorie is not null) return kategorie;
        var (name, richtung) = System(code);
        kategorie = new Kategorie { Name = name, Richtung = richtung, SystemCode = code };
        db.Kategorien.Add(kategorie);
        await db.SaveChangesAsync(ct);
        return kategorie;
    }
}

/// <summary>Rechteprüfung des Bereichs Finanzen (Fähigkeiten aus der Matrix, Schule der Ressource).</summary>
internal static class FinanzRechte
{
    /// <summary>Hat der Akteur die Fähigkeit (ohne Ausnahme)?</summary>
    public static bool Hat(Akteur akteur, Faehigkeit faehigkeit, bool schreiben = false)
    {
        try
        {
            Zugriff.Pruefe(akteur, faehigkeit, schreiben);
            return true;
        }
        catch (KeinZugriffFehler)
        {
            return false;
        }
    }

    /// <summary>Direktion (Direktor/Koordinator) mit mindestens einer der Fähigkeiten; liefert die eigene Schule.</summary>
    public static async Task<SchulAkteur> DirektionAsync(this Zugriff zugriff, Guid akteurId, bool schreiben, CancellationToken ct, params Faehigkeit[] faehigkeiten)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        if (!akteur.IstDirektion) throw new KeinZugriffFehler();
        KeinZugriffFehler? letzter = null;
        foreach (var f in faehigkeiten)
        {
            try
            {
                var art = Zugriff.Pruefe(akteur, f, schreiben);
                if (akteur.SchuleId is not { } schuleId) throw new KeinZugriffFehler(OrgRechte.MeldungOhneSchule);
                return new SchulAkteur(akteur, art, schuleId);
            }
            catch (KeinZugriffFehler k)
            {
                letzter = k;
            }
        }
        throw letzter ?? new KeinZugriffFehler();
    }
}

/// <summary>Kleine Hilfen für Eingaben, Datumsangaben und Belege des Bereichs.</summary>
internal static class FinanzEingabe
{
    public const long MaxBetrag = 999_999_999;

    /// <summary>Betrag in ganzen FCFA, &gt; 0.</summary>
    public static long Betrag(long? wert, string feld = "betrag", string bezeichnung = "le montant")
    {
        if (wert is not { } b || b <= 0) throw new FachFehler($"Indiquez {bezeichnung} (en FCFA, supérieur à 0).", feld);
        if (b > MaxBetrag) throw new FachFehler($"Le montant ne peut pas dépasser {SkoulouKultur.Fcfa(MaxBetrag)}.", feld);
        return b;
    }

    public static DateOnly Datum(DateOnly? wert, DateOnly heute, string feld = "datum")
    {
        if (wert is not { } d) throw new FachFehler("Indiquez la date.", feld);
        if (d > heute) throw new FachFehler("La date ne peut pas être dans le futur.", feld);
        if (d < heute.AddYears(-3)) throw new FachFehler("La date est trop ancienne.", feld);
        return d;
    }

    public static string KanalText(ZahlungsKanal kanal) => kanal switch
    {
        ZahlungsKanal.Especes => "Espèces",
        ZahlungsKanal.MobileMoney => "Mobile Money",
        ZahlungsKanal.Virement => "Virement",
        ZahlungsKanal.Cheque => "Chèque",
        _ => kanal.ToString()
    };

    public static string StatusText(RechnungsStatus status) => status switch
    {
        RechnungsStatus.Ouverte => "À payer",
        RechnungsStatus.PaiementDeclare => "Paiement déclaré",
        RechnungsStatus.Payee => "Payée",
        RechnungsStatus.Contestee => "Contestée",
        RechnungsStatus.Annulee => "Annulée",
        _ => status.ToString()
    };

    /// <summary>Badge-Art für einen Rechnungsstatus.</summary>
    public static string StatusArt(RechnungsStatus status) => status switch
    {
        RechnungsStatus.Ouverte => "warn",
        RechnungsStatus.PaiementDeclare => "info",
        RechnungsStatus.Payee => "erfolg",
        RechnungsStatus.Contestee => "fehler",
        _ => "neutral"
    };
}

/// <summary>Hochgeladener Beleg (PDF/PNG/JPEG, höchstens 10 MB, Typ nur per Magic Bytes).</summary>
public sealed record BelegDatei(Stream Inhalt, string? Dateiname);

internal static class BelegSpeicher
{
    public const long MaxBytes = 10 * 1024 * 1024;

    public static string? Erkenne(ReadOnlySpan<byte> kopf)
    {
        if (kopf.Length >= 5 && kopf[..5].SequenceEqual("%PDF-"u8)) return "application/pdf";
        var bild = BildDatei.Erkenne(kopf);
        return bild is "image/png" or "image/jpeg" ? bild : null;
    }

    public static async Task<(byte[] Inhalt, string ContentType)> LeseAsync(Stream quelle, CancellationToken ct)
    {
        using var puffer = new MemoryStream();
        var block = new byte[81920];
        int n;
        while ((n = await quelle.ReadAsync(block, ct)) > 0)
        {
            puffer.Write(block, 0, n);
            if (puffer.Length > MaxBytes) throw new FachFehler("Le justificatif est trop lourd (10 Mo au maximum).", "beleg");
        }
        if (puffer.Length == 0) throw new FachFehler("Le fichier est vide.", "beleg");
        var inhalt = puffer.ToArray();
        var typ = Erkenne(inhalt) ?? throw new FachFehler("Seuls les fichiers PDF, PNG ou JPEG sont acceptés.", "beleg");
        return (inhalt, typ);
    }

    /// <summary>Speichert den Beleg als Medium (vor dem DB-Commit; bei Fehlern löscht der Aufrufer den Blob).</summary>
    public static async Task<Medium> SpeichereAsync(IMedienSpeicher speicher, Guid schuleId, Guid besitzerId, byte[] inhalt, string contentType,
        string? dateiname, CancellationToken ct)
    {
        var name = Path.GetFileName(dateiname ?? string.Empty);
        var medium = new Medium
        {
            SchuleId = schuleId, BesitzerId = besitzerId, Art = contentType == "application/pdf" ? MedienArt.Dokument : MedienArt.Bild,
            Zweck = MedienZweck.Beleg, ContentType = contentType,
            Dateiname = name is { Length: > 0 and <= 200 } ? name : "justificatif"
        };
        medium.BlobName = MedienPfad.Fuer(schuleId, MedienZweck.Beleg, medium.Id);
        using var strom = new MemoryStream(inhalt, writable: false);
        medium.Bytes = await speicher.SpeichernAsync(medium.BlobName, strom, contentType, ct);
        return medium;
    }
}
