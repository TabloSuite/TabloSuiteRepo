using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Medien;

namespace Skoulou.WebApp.Services.Organisation;

/// <summary>Stammdaten, Dokumentdaten und Einstellungen der Schule (Seite « Établissement »).</summary>
public sealed record SchulDaten(
    Guid Id, string Name, string Ort, string Land, SchulStatus Status,
    string? OffizielleBezeichnung, string? Slogan, string? Adresse, string? Telefon, string? Email, string? Zulassungsnummer,
    string? Ministerium, string? RepublikZeile, string? StaatsMotto, string? DirectionRegionale, string? Statut,
    Guid? LogoMediumId, string? MatrikelPraefix, bool AbsenzOhneEntschuldigungZaehltNull, bool ElternSehenNotenLive)
{
    /// <summary>Beispiel der nächsten automatischen Matrikel (« CDP260001 »).</summary>
    public string MatrikelBeispiel(int jahr) => NummernFormat.Formatiere(NummernArt.Matrikel, jahr, 1, MatrikelPraefix ?? NummernFormat.PraefixAusName(Name));
}

public sealed record SchulStammEingabe(string? Name, string? Ort, string? Land);

public sealed record DokumentEingabe(
    string? OffizielleBezeichnung, string? Slogan, string? Adresse, string? Telefon, string? Email, string? Zulassungsnummer,
    string? Ministerium, string? RepublikZeile, string? StaatsMotto, string? DirectionRegionale, string? Statut);

public sealed record SchulEinstellungEingabe(string? MatrikelPraefix, bool AbsenzOhneEntschuldigungZaehltNull, bool ElternSehenNotenLive);

public sealed record KoordinatorZeile(Guid KontoId, string Name, string Identifiant, KontoStatus Status, KoordinatorRechte Rechte);

/// <summary>
/// Seite « Établissement »: nur der Direktor ändert Stammdaten, Dokumentdaten, Logo, Einstellungen
/// und die Rechte der Koordinatoren (Bauplan §3.4, §11.3).
/// </summary>
public sealed class SchulService(
    IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, IMedienSpeicher speicher)
{
    public async Task<SchulDaten> LadeAsync(Guid akteurId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuleVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Schulen.AsNoTracking().Where(s => s.Id == a.SchuleId)
            .Select(s => new SchulDaten(s.Id, s.Name, s.Ort, s.Land, s.Status, s.OffizielleBezeichnung, s.Slogan, s.Adresse, s.Telefon,
                s.Email, s.Zulassungsnummer, s.Ministerium, s.RepublikZeile, s.StaatsMotto, s.DirectionRegionale, s.Statut,
                s.LogoMediumId, s.MatrikelPraefix, s.AbsenzOhneEntschuldigungZaehltNull, s.ElternSehenNotenLive))
            .FirstAsync(ct);
    }

    public async Task StammdatenSpeichernAsync(Guid akteurId, SchulStammEingabe e, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuleVerwalten, ct: ct);
        var name = Eingabe.Pflicht(e.Name, 200, "le nom de l'établissement", "name");
        var ort = Eingabe.Pflicht(e.Ort, 120, "la ville", "ort");
        var land = Eingabe.Text(e.Land, 120, "le pays") ?? "Togo";
        await dbFactory.TransaktionAsync(async db =>
        {
            var schule = await db.Schulen.FirstAsync(s => s.Id == a.SchuleId, ct);
            var schluessel = TextNormalisierung.Schluessel(name);
            if (await db.Schulen.AnyAsync(s => s.NameNormalisiert == schluessel && s.Id != schule.Id, ct))
                throw new FachFehler("Un autre établissement porte déjà ce nom sur SKOULOU.", "name");
            if (schule.Name != name) audit.Erfasse(db, a.Id, schule.Id, OrgAudit.SchuleGeaendert, nameof(Schule), schule.Id, $"Nom : « {schule.Name} » → « {name} ».");
            schule.Name = name;
            schule.NameNormalisiert = schluessel;
            schule.Ort = ort;
            schule.Land = land;
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task DokumenteSpeichernAsync(Guid akteurId, DokumentEingabe e, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuleVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = await db.Schulen.FirstAsync(x => x.Id == a.SchuleId, ct);
        s.OffizielleBezeichnung = Eingabe.Text(e.OffizielleBezeichnung, 200, "la dénomination officielle");
        s.Slogan = Eingabe.Text(e.Slogan, 200, "la devise de l'établissement");
        s.Adresse = Eingabe.Text(e.Adresse, 300, "l'adresse");
        s.Telefon = Eingabe.Telefon(e.Telefon);
        s.Email = Eingabe.Email(e.Email);
        s.Zulassungsnummer = Eingabe.Text(e.Zulassungsnummer, 100, "le numéro d'autorisation");
        s.Ministerium = Eingabe.Text(e.Ministerium, 300, "le ministère");
        s.RepublikZeile = Eingabe.Text(e.RepublikZeile, 300, "la ligne « République »");
        s.StaatsMotto = Eingabe.Text(e.StaatsMotto, 300, "la devise nationale");
        s.DirectionRegionale = Eingabe.Text(e.DirectionRegionale, 200, "la direction régionale");
        s.Statut = Eingabe.Text(e.Statut, 60, "le statut");
        await db.SaveChangesAsync(ct);
    }

    public async Task EinstellungenSpeichernAsync(Guid akteurId, SchulEinstellungEingabe e, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuleVerwalten, ct: ct);
        string? praefix = null;
        if (!string.IsNullOrWhiteSpace(e.MatrikelPraefix))
        {
            praefix = e.MatrikelPraefix.Trim().ToUpperInvariant();
            if (praefix.Length > 6 || praefix.Any(c => c is not (>= 'A' and <= 'Z' or >= '0' and <= '9')))
                throw new FachFehler("Le préfixe du matricule comporte 1 à 6 lettres ou chiffres, sans espace ni accent.", "praefix");
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = await db.Schulen.FirstAsync(x => x.Id == a.SchuleId, ct);
        s.MatrikelPraefix = praefix;
        s.AbsenzOhneEntschuldigungZaehltNull = e.AbsenzOhneEntschuldigungZaehltNull;
        s.ElternSehenNotenLive = e.ElternSehenNotenLive;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Neues Logo (PNG/JPEG/WebP, höchstens 5 MB). Alte Logos bleiben gespeichert (Bulletins verweisen darauf).</summary>
    public async Task<Guid> LogoHochladenAsync(Guid akteurId, Stream datei, string? dateiname, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuleVerwalten, ct: ct);
        var (inhalt, typ) = await BildDatei.LeseAsync(datei, ct);
        return await dbFactory.TransaktionAsync(async db =>
        {
            var schule = await db.Schulen.FirstAsync(s => s.Id == a.SchuleId, ct);
            var medium = await BildDatei.SpeichereAsync(db, speicher, schule.Id, a.Id, MedienZweck.Logo, inhalt, typ, dateiname, ct);
            await NutzungEntfernenAsync(db, schule, ct);
            db.MedienNutzungen.Add(new MedienNutzung { MediumId = medium.Id, Typ = "Schule.Logo", ZielId = schule.Id });
            schule.LogoMediumId = medium.Id;
            await db.SaveChangesAsync(ct);
            return medium.Id;
        }, ct);
    }

    public async Task LogoEntfernenAsync(Guid akteurId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuleVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var schule = await db.Schulen.FirstAsync(s => s.Id == a.SchuleId, ct);
        await NutzungEntfernenAsync(db, schule, ct);
        schule.LogoMediumId = null;
        await db.SaveChangesAsync(ct);
    }

    private static async Task NutzungEntfernenAsync(SkoulouDbContext db, Schule schule, CancellationToken ct)
    {
        if (schule.LogoMediumId is not { } alt) return;
        db.MedienNutzungen.RemoveRange(await db.MedienNutzungen.Where(n => n.MediumId == alt && n.Typ == "Schule.Logo").ToListAsync(ct));
    }

    public async Task<IReadOnlyList<KoordinatorZeile>> KoordinatorenAsync(Guid akteurId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KoordinatorenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking()
            .Where(k => k.SchuleId == a.SchuleId && k.Rolle == Rollen.Koordinator && k.Status != KontoStatus.Archive)
            .OrderBy(k => k.Nachname).ThenBy(k => k.Vorname)
            .Select(k => new KoordinatorZeile(k.Id, (k.Vorname + " " + k.Nachname).Trim(), k.UserName!, k.Status, k.KoordinatorRechte))
            .ToListAsync(ct);
    }

    /// <summary>Zusatzrechte eines Koordinators setzen (nur der Direktor; mit Audit).</summary>
    public async Task KoordinatorRechteSetzenAsync(Guid akteurId, Guid koordinatorId, KoordinatorRechte rechte, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KoordinatorenVerwalten, ct: ct);
        rechte &= KoordinatorRechte.Alle;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var konto = await OrgAbfragen.KontoAsync(db, a, koordinatorId, ct, Rollen.Koordinator);
        if (konto.KoordinatorRechte == rechte) return;
        audit.Erfasse(db, a.Id, a.SchuleId, AuditAktionen.RechteGeaendert, nameof(Konto), konto.Id,
            $"Droits de {konto.VollerName} : {RechteText(konto.KoordinatorRechte)} → {RechteText(rechte)}.");
        konto.KoordinatorRechte = rechte;
        await db.SaveChangesAsync(ct);
    }

    public static string RechteText(KoordinatorRechte r)
    {
        if (r == KoordinatorRechte.Keine) return "aucun droit financier";
        var teile = new List<string>();
        if (r.HasFlag(KoordinatorRechte.FinanzenEinsehen)) teile.Add("voir les finances");
        if (r.HasFlag(KoordinatorRechte.ZahlungenErfassen)) teile.Add("encaisser");
        if (r.HasFlag(KoordinatorRechte.BuchhaltungErfassen)) teile.Add("journal de caisse");
        if (r.HasFlag(KoordinatorRechte.RechnungSkoulou)) teile.Add("facturation SKOULOU");
        return string.Join(", ", teile);
    }
}
