using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Data;

namespace Skoulou.WebApp.Services.Infrastruktur;

/// <summary>Bekannte Audit-Aktionen (stabile Codes für Filter und Anzeige).</summary>
public static class AuditAktionen
{
    public const string KontoAngelegt = "konto.angelegt";
    public const string KontoGesperrt = "konto.gesperrt";
    public const string KontoArchiviert = "konto.archiviert";
    public const string PasswortZurueckgesetzt = "konto.passwort-zurueckgesetzt";
    public const string PasswortGeaendert = "konto.passwort-geaendert";
    public const string AgbAkzeptiert = "konto.agb-akzeptiert";
    public const string RechteGeaendert = "konto.rechte-geaendert";
    public const string NoteGeaendert = "note.geaendert";
    public const string Veroeffentlicht = "bulletin.veroeffentlicht";
    public const string Berichtigt = "bulletin.berichtigt";
    public const string Zahlung = "finanzen.zahlung";
    public const string ZahlungStorniert = "finanzen.zahlung-storniert";
    public const string RechnungStatus = "rechnung.status";
    public const string SchulStatus = "schule.status";
    public const string Jahreswechsel = "schuljahr.wechsel";
}

/// <summary>
/// Schreibt Audit-Einträge in den übergebenen Kontext – damit landen sie in derselben Transaktion
/// wie die protokollierte Änderung (der Aufrufer speichert).
/// </summary>
public sealed class AuditService(TimeProvider zeit)
{
    public AuditEintrag Erfasse(SkoulouDbContext db, Guid? akteurId, Guid? schuleId, string aktion,
        string zielTyp, Guid? zielId, string text)
    {
        var eintrag = new AuditEintrag
        {
            ZeitUtc = zeit.GetUtcNow().UtcDateTime,
            AkteurId = akteurId,
            SchuleId = schuleId,
            Aktion = aktion,
            ZielTyp = zielTyp,
            ZielId = zielId,
            Text = text.Length > 2000 ? text[..2000] : text
        };
        db.AuditEintraege.Add(eintrag);
        return eintrag;
    }
}
