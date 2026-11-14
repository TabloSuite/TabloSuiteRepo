using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;

namespace Skoulou.WebApp.Services.Organisation;

/// <summary>Audit-Codes des Bereichs Organisation (ergänzen die gemeinsamen <see cref="AuditAktionen"/>).</summary>
public static class OrgAudit
{
    public const string KontoGeaendert = "konto.geaendert";
    public const string KontoEntsperrt = "konto.entsperrt";
    public const string KontoReintegriert = "konto.reintegriert";
    public const string ImportAusgefuehrt = "konto.import";
    public const string InscriptionValidiert = "eleve.inscription-validee";
    public const string InscriptionAbgelehnt = "eleve.inscription-refusee";
    public const string Eingeschrieben = "einschreibung.angelegt";
    public const string KlasseGewechselt = "einschreibung.klassenwechsel";
    public const string Austritt = "einschreibung.depart";
    public const string EinschreibungStorniert = "einschreibung.annulee";
    public const string ElternVerknuepft = "eltern.lien";
    public const string ElternGetrennt = "eltern.lien-retire";
    public const string SchuljahrAngelegt = "schuljahr.angelegt";
    public const string SchuljahrGestartet = "schuljahr.gestartet";
    public const string SchuljahrAbgeschlossen = "schuljahr.abgeschlossen";
    public const string SchuljahrWiedereroeffnet = "schuljahr.wiedereroeffnet";
    public const string PeriodeStatus = "periode.status";
    public const string SchuleGeaendert = "schule.geaendert";
    public const string KartenCodeErneuert = "eleve.carte-renouvelee";

    /// <summary>Lesbare Bezeichnung eines Audit-Codes (Journal der Direktion).</summary>
    public static string Bezeichnung(string aktion) => aktion switch
    {
        AuditAktionen.KontoAngelegt => "Compte créé",
        AuditAktionen.KontoGesperrt => "Compte bloqué",
        KontoEntsperrt => "Compte débloqué",
        AuditAktionen.KontoArchiviert => "Compte archivé",
        KontoReintegriert => "Compte réintégré",
        KontoGeaendert => "Compte modifié",
        AuditAktionen.PasswortZurueckgesetzt => "Mot de passe réinitialisé",
        AuditAktionen.PasswortGeaendert => "Mot de passe modifié",
        AuditAktionen.AgbAkzeptiert => "Conditions acceptées",
        AuditAktionen.RechteGeaendert => "Droits modifiés",
        ImportAusgefuehrt => "Import de comptes",
        InscriptionValidiert => "Inscription validée",
        InscriptionAbgelehnt => "Inscription refusée",
        Eingeschrieben => "Inscription en classe",
        KlasseGewechselt => "Changement de classe",
        Austritt => "Départ d'un élève",
        EinschreibungStorniert => "Inscription annulée",
        ElternVerknuepft => "Parent lié",
        ElternGetrennt => "Lien parent retiré",
        SchuljahrAngelegt => "Année scolaire créée",
        SchuljahrGestartet => "Année scolaire démarrée",
        SchuljahrAbgeschlossen => "Année scolaire clôturée",
        SchuljahrWiedereroeffnet => "Année scolaire rouverte",
        PeriodeStatus => "Période ouverte ou fermée",
        SchuleGeaendert => "Établissement modifié",
        KartenCodeErneuert => "Carte d'élève renouvelée",
        AuditAktionen.NoteGeaendert => "Note modifiée",
        AuditAktionen.Veroeffentlicht => "Bulletins publiés",
        AuditAktionen.Berichtigt => "Bulletin rectifié",
        AuditAktionen.Zahlung => "Paiement",
        AuditAktionen.ZahlungStorniert => "Paiement annulé",
        AuditAktionen.RechnungStatus => "Facture SKOULOU",
        AuditAktionen.SchulStatus => "Statut de l'établissement",
        AuditAktionen.Jahreswechsel => "Passage d'année",
        // Codes aus Noten (E5) und Finanzen (E6)
        "bulletin.retire" => "Bulletin retiré",
        "bulletin.rectification-ouverte" => "Rectification ouverte",
        "bulletin.rectification-abandonnee" => "Rectification abandonnée",
        "finanzen.ecriture-annulee" => "Écriture annulée",
        "finanzen.reduction-retiree" => "Réduction retirée",
        "rechnung.erstellt" => "Facture SKOULOU émise",
        "responsable.versement" => "Versement au responsable",
        _ => aktion
    };
}

/// <summary>Rollengruppen als Arrays (in EF-Abfragen sicher übersetzbar).</summary>
public static class OrgRollen
{
    /// <summary>Wer unterrichten darf (Lehraufträge, Professeur principal).</summary>
    public static readonly string[] Lehrkraefte = [Rollen.Lehrer, Rollen.Direktor, Rollen.Koordinator];

    /// <summary>Personal der Schule (Seite « Équipe »).</summary>
    public static readonly string[] Personal = [Rollen.Direktor, Rollen.Koordinator, Rollen.Lehrer];
}

/// <summary>Akteur mit seiner Schule (alle Schulrollen haben genau eine).</summary>
public sealed record SchulAkteur(Akteur Akteur, Zugriffsart Art, Guid SchuleId)
{
    public Guid Id => Akteur.Id;
    public string Rolle => Akteur.Rolle;
    public bool IstDirektion => Akteur.IstDirektion;
}

/// <summary>Rechteprüfung des Bereichs: Fähigkeit + eigene Schule; Ressourcen anderer Schulen sind tabu.</summary>
internal static class OrgRechte
{
    public const string MeldungOhneSchule = "Aucun établissement n'est associé à votre compte.";

    /// <summary>Lädt den Akteur, prüft die Fähigkeit und liefert seine Schule (ohne Schule → kein Zugriff).</summary>
    public static async Task<SchulAkteur> SchuleAsync(this Zugriff zugriff, Guid akteurId, Faehigkeit faehigkeit,
        bool schreiben = true, CancellationToken ct = default)
    {
        var (akteur, art) = await zugriff.PruefeAsync(akteurId, faehigkeit, null, schreiben, ct);
        if (akteur.SchuleId is not { } schuleId) throw new KeinZugriffFehler(MeldungOhneSchule);
        return new SchulAkteur(akteur, art, schuleId);
    }

    /// <summary>Wirft, wenn die Ressource einer anderen Schule gehört.</summary>
    public static void PruefeSchule(this SchulAkteur akteur, Guid ressourcenSchuleId)
    {
        if (akteur.SchuleId != ressourcenSchuleId) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
    }
}

/// <summary>Schreibvorgänge in einer Transaktion (mit der Wiederholungsstrategie von SQL Server verträglich).</summary>
internal static class DbArbeit
{
    /// <summary>
    /// Führt die Arbeit mit einem frischen Kontext in einer Transaktion aus; bei einer Wiederholung
    /// (Verbindungsabbruch) läuft sie erneut von vorn. InMemory (Tests) kennt keine Transaktionen.
    /// </summary>
    public static async Task<T> TransaktionAsync<T>(this IDbContextFactory<SkoulouDbContext> fabrik,
        Func<SkoulouDbContext, Task<T>> arbeit, CancellationToken ct = default)
    {
        await using var probe = await fabrik.CreateDbContextAsync(ct);
        var strategie = probe.Database.CreateExecutionStrategy();
        return await strategie.ExecuteAsync(async () =>
        {
            await using var db = await fabrik.CreateDbContextAsync(ct);
            if (!db.Database.IsRelational()) return await arbeit(db);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var ergebnis = await arbeit(db);
                await tx.CommitAsync(ct);
                return ergebnis;
            }
            catch (DbUpdateException ex) when (IstEindeutigkeitsFehler(ex))
            {
                throw new FachFehler("Cette donnée existe déjà (enregistrement simultané). Rechargez la page et réessayez.");
            }
        });
    }

    public static Task TransaktionAsync(this IDbContextFactory<SkoulouDbContext> fabrik,
        Func<SkoulouDbContext, Task> arbeit, CancellationToken ct = default)
        => fabrik.TransaktionAsync<bool>(async db => { await arbeit(db); return true; }, ct);

    /// <summary>Verletzung eines Unique-Index (SQL Server 2601/2627)?</summary>
    public static bool IstEindeutigkeitsFehler(DbUpdateException ex)
        => ex.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627;
}

/// <summary>Prüft und bereinigt Eingaben (Meldungen französisch, am Feld anzeigbar).</summary>
internal static class Eingabe
{
    /// <summary>Getrimmt; leer → null (bzw. Fehler bei Pflicht); zu lang → Fehler.</summary>
    public static string? Text(string? wert, int max, string bezeichnung, bool pflicht = false, string? feld = null)
    {
        var t = string.IsNullOrWhiteSpace(wert) ? null : string.Join(' ', wert.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (t is null)
        {
            if (pflicht) throw new FachFehler($"Indiquez {bezeichnung}.", feld);
            return null;
        }
        if (t.Length > max) throw new FachFehler($"{Gross(bezeichnung)} est trop long ({max} caractères au maximum).", feld);
        return t;
    }

    public static string Pflicht(string? wert, int max, string bezeichnung, string? feld = null)
        => Text(wert, max, bezeichnung, true, feld)!;

    public static string? Email(string? wert, string feld = "email")
    {
        var t = Text(wert, 256, "l'adresse e-mail", feld: feld);
        if (t is null) return null;
        if (!MailAusgangService.IstAdresse(t)) throw new FachFehler("L'adresse e-mail n'est pas valide.", feld);
        return t.ToLowerInvariant();
    }

    public static string? Telefon(string? wert, string feld = "telefon")
    {
        var t = Text(wert, 50, "le numéro de téléphone", feld: feld);
        if (t is null) return null;
        if (t.Any(c => !(char.IsDigit(c) || c is '+' or ' ' or '-' or '.' or '(' or ')')))
            throw new FachFehler("Le numéro de téléphone ne peut contenir que des chiffres, des espaces et le signe +.", feld);
        return t;
    }

    private static string Gross(string text)
    {
        // « le nom » → « Le nom »
        return text.Length == 0 ? text : char.ToUpper(text[0], SkoulouKultur.Kultur) + text[1..];
    }

    /// <summary>Suchbegriff für LIKE (getrimmt, höchstens 100 Zeichen, Platzhalter maskiert); leer → null.</summary>
    public static string? Suche(string? suche)
    {
        if (string.IsNullOrWhiteSpace(suche)) return null;
        var t = suche.Trim();
        if (t.Length > 100) t = t[..100];
        return t.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
    }

    /// <summary>Seitengröße begrenzen (1…200).</summary>
    public static int Anzahl(int anzahl) => Math.Clamp(anzahl, 1, 200);
}

/// <summary>Seite einer serverseitig geblätterten Liste.</summary>
public sealed record Seite<T>(IReadOnlyList<T> Eintraege, bool Mehr)
{
    public static async Task<Seite<T>> LadeAsync(IQueryable<T> abfrage, int ueberspringen, int anzahl, CancellationToken ct)
    {
        anzahl = Eingabe.Anzahl(anzahl);
        var liste = await abfrage.Skip(Math.Max(0, ueberspringen)).Take(anzahl + 1).ToListAsync(ct);
        var mehr = liste.Count > anzahl;
        if (mehr) liste.RemoveAt(anzahl);
        return new Seite<T>(liste, mehr);
    }

    public (IReadOnlyList<T>, bool) AlsTupel() => (Eintraege, Mehr);
}

/// <summary>Kleine Abfragen des Bereichs, die mehrere Dienste brauchen.</summary>
internal static class OrgAbfragen
{
    /// <summary>LIKE-Suche über Vor-/Nachname und Identifiant.</summary>
    public static IQueryable<Konto> SucheKonten(IQueryable<Konto> konten, string? suche)
    {
        var s = Eingabe.Suche(suche);
        if (s is null) return konten;
        var muster = $"%{s}%";
        return konten.Where(k => EF.Functions.Like(k.Vorname + " " + k.Nachname, muster)
                                 || EF.Functions.Like(k.Nachname + " " + k.Vorname, muster)
                                 || EF.Functions.Like(k.UserName!, muster)
                                 || (k.Email != null && EF.Functions.Like(k.Email, muster))
                                 || (k.Telefon != null && EF.Functions.Like(k.Telefon, muster)));
    }

    /// <summary>Schuljahr der Schule laden (oder Fehler).</summary>
    public static async Task<Schuljahr> SchuljahrAsync(SkoulouDbContext db, SchulAkteur akteur, Guid jahrId, CancellationToken ct)
    {
        var jahr = await db.Schuljahre.FirstOrDefaultAsync(j => j.Id == jahrId, ct)
                   ?? throw new FachFehler("Cette année scolaire est introuvable.");
        akteur.PruefeSchule(jahr.SchuleId);
        return jahr;
    }

    /// <summary>Klasse der Schule laden (oder Fehler).</summary>
    public static async Task<Klasse> KlasseAsync(SkoulouDbContext db, SchulAkteur akteur, Guid klasseId, CancellationToken ct)
    {
        var klasse = await db.Klassen.FirstOrDefaultAsync(k => k.Id == klasseId, ct)
                     ?? throw new FachFehler("Cette classe est introuvable.");
        akteur.PruefeSchule(klasse.SchuleId);
        return klasse;
    }

    /// <summary>Konto der Schule mit erwarteter Rolle laden (oder Fehler).</summary>
    public static async Task<Konto> KontoAsync(SkoulouDbContext db, SchulAkteur akteur, Guid kontoId, CancellationToken ct, params string[] rollen)
    {
        var konto = await db.Users.FirstOrDefaultAsync(k => k.Id == kontoId, ct)
                    ?? throw new FachFehler("Ce compte est introuvable.");
        if (konto.SchuleId is not { } s) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
        akteur.PruefeSchule(s);
        if (rollen.Length > 0 && !rollen.Contains(konto.Rolle)) throw new FachFehler("Ce compte n'a pas le rôle attendu.");
        return konto;
    }

    /// <summary>Wirft, wenn das Jahr abgeschlossen ist (Änderungen gesperrt).</summary>
    public static void PruefeOffen(Schuljahr jahr)
    {
        if (jahr.Status == SchuljahrStatus.Abgeschlossen)
            throw new FachFehler($"L'année scolaire {jahr.Bezeichnung} est clôturée : rouvrez-la d'abord pour la modifier.");
    }
}
