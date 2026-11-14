using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Art der Selbstregistrierung.</summary>
public enum RegistrierungsArt
{
    Direction,
    Eleve
}

/// <summary>Formular « Inscription » (Direction: neue Schule + Direktorkonto; Élève: Konto bei einer aktiven Schule).</summary>
public sealed record RegistrierungsEingabe(
    RegistrierungsArt Art, string? Vorname, string? Nachname, string? Identifiant, string? Email, string? Telefon,
    string? Passwort, string? Bestaetigung, bool AgbAkzeptiert,
    string? SchulName = null, string? Ort = null, string? Land = null,
    Guid? SchuleId = null, DateOnly? Geburtsdatum = null, string? ElternTelefon = null, string? ElternEmail = null);

/// <summary>Treffer der Schulsuche (nur aktive Schulen).</summary>
public sealed record SchulTreffer(Guid Id, string Name, string Ort);

/// <summary>Ergebnis einer Registrierung (für die Erfolgsseite).</summary>
public sealed record RegistrierungsErgebnis(RegistrierungsArt Art, string Identifiant, string SchulName);

/// <summary>
/// Bremse gegen Massenregistrierungen (zusätzlich zum Rate-Limiter des Endpunkts): je Verbindung und je
/// E-Mail-Adresse nur wenige erfolgreiche Registrierungen in 24 Stunden.
/// </summary>
public sealed class RegistrierungsBremse(TimeProvider zeit)
{
    public const int ProVerbindung = 10;
    public const int ProAdresse = 3;
    public static readonly TimeSpan Fenster = TimeSpan.FromHours(24);
    public const string Meldung = "Trop d'inscriptions depuis cette connexion ou avec cette adresse. Réessayez demain ou contactez-nous.";

    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> zaehler = new();

    private static IEnumerable<(string Schluessel, int Grenze)> Schluessel(string? ip, string? email)
    {
        if (!string.IsNullOrWhiteSpace(ip)) yield return ("ip:" + ip.Trim(), ProVerbindung);
        if (!string.IsNullOrWhiteSpace(email)) yield return ("mail:" + email.Trim().ToLowerInvariant(), ProAdresse);
    }

    /// <summary>Ist eine weitere Registrierung erlaubt?</summary>
    public bool IstErlaubt(string? ip, string? email)
    {
        var grenze = zeit.GetUtcNow() - Fenster;
        foreach (var (s, max) in Schluessel(ip, email))
        {
            if (!zaehler.TryGetValue(s, out var liste)) continue;
            lock (liste)
            {
                liste.RemoveAll(t => t < grenze);
                if (liste.Count >= max) return false;
            }
        }
        return true;
    }

    /// <summary>Merkt eine erfolgreiche Registrierung.</summary>
    public void Merken(string? ip, string? email)
    {
        foreach (var (s, _) in Schluessel(ip, email))
        {
            var liste = zaehler.GetOrAdd(s, _ => []);
            lock (liste) liste.Add(zeit.GetUtcNow());
        }
    }
}

/// <summary>
/// Selbstregistrierung (Fachkonzept 01 §3.6, verbessert): eine Transaktion, Schulname normalisiert eindeutig,
/// Schule und Direktion warten auf die Validierung durch SKOULOU, Schüler nur bei aktiven Schulen und
/// warten auf die Validierung durch die Direktion (E3, /direction/eleves).
/// </summary>
public sealed class RegistrierungService(
    IDbContextFactory<SkoulouDbContext> dbFactory,
    PlattformKontoFabrik konten,
    AuditService audit,
    PlattformMails mails,
    RegistrierungsBremse bremse,
    TimeProvider zeit)
{
    public const string MeldungSchuleVorhanden = "Un établissement portant ce nom est déjà inscrit sur SKOULOU. S'il s'agit du vôtre, contactez-nous.";
    public const string MeldungAgb = "Veuillez accepter les conditions d'utilisation pour finaliser l'inscription.";
    public const string MeldungSchuleWaehlen = "Veuillez choisir votre établissement.";
    public const string MeldungSchuleGeschlossen = "Cet établissement n'accepte pas d'inscriptions en ligne pour le moment.";

    /// <summary>Aktive Schulen für die Schülerregistrierung (ab 2 Zeichen, höchstens 15).</summary>
    public async Task<IReadOnlyList<SchulTreffer>> SchulenSuchenAsync(string? suche, CancellationToken ct = default)
    {
        var text = PlattformListen.Suchtext(suche);
        if (text is null || text.Length < 2) return [];
        var schluessel = TextNormalisierung.Schluessel(text);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Schulen.AsNoTracking()
            .Where(s => s.Status == SchulStatus.Active && (s.NameNormalisiert.Contains(schluessel) || s.Ort.Contains(text)))
            .OrderBy(s => s.Name).Take(15)
            .Select(s => new SchulTreffer(s.Id, s.Name, s.Ort)).ToListAsync(ct);
    }

    /// <summary>Freies Identifiant « prenom.nom » vorschlagen.</summary>
    public async Task<string> IdentifiantVorschlagAsync(string? vorname, string? nachname, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await konten.Basis.VorschlagAsync(db, vorname, nachname, ct: ct);
    }

    /// <summary>Vorprüfung ohne Anlage (vor dem Dialog der Nutzungsbedingungen); wirft <see cref="FachFehler"/> mit Feld.</summary>
    public async Task PruefeAsync(RegistrierungsEingabe e, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await PruefeAsync(db, e, ct);
    }

    public async Task<RegistrierungsErgebnis> RegistrierenAsync(RegistrierungsEingabe e, string? verbindung, CancellationToken ct = default)
    {
        if (!e.AgbAkzeptiert) throw new FachFehler(MeldungAgb, "agb");
        if (!bremse.IstErlaubt(verbindung, e.Email)) throw new FachFehler(RegistrierungsBremse.Meldung);

        var (ergebnis, konto, schulName, ort) = await dbFactory.TransaktionAsync(async db =>
        {
            var p = await PruefeAsync(db, e, ct);
            var agb = await db.PlattformEinstellungen.Where(x => x.Id == PlattformEinstellung.EinzigeId).Select(x => x.AgbVersion).FirstOrDefaultAsync(ct);
            var daten = new KontoDaten(p.Vorname, p.Nachname, p.Identifiant, p.Email, p.Telefon, Geschlecht.Divers, e.Geburtsdatum);
            Konto neu;
            string name, ortText;
            if (e.Art == RegistrierungsArt.Direction)
            {
                var schule = new Schule
                {
                    Name = p.SchulName!, NameNormalisiert = TextNormalisierung.Schluessel(p.SchulName), Ort = p.Ort!, Land = p.Land!,
                    Status = SchulStatus.EnAttente, MatrikelPraefix = NummernFormat.PraefixAusName(p.SchulName!)
                };
                db.Schulen.Add(schule);
                neu = await konten.AnlegenAsync(db, daten, p.Identifiant, Rollen.Direktor, schule.Id, KontoStatus.EnAttente, e.Passwort!,
                    mussPasswortAendern: false, erstelltVonId: null, selbstRegistriert: true, ct);
                audit.Erfasse(db, neu.Id, schule.Id, PlattformAudit.SchuleRegistriert, nameof(Schule), schule.Id,
                    $"Inscription en ligne de « {schule.Name} » ({schule.Ort}) par {neu.VollerName}.");
                (name, ortText) = (schule.Name, schule.Ort);
            }
            else
            {
                var schule = await db.Schulen.AsNoTracking().FirstAsync(s => s.Id == e.SchuleId, ct);
                neu = await konten.AnlegenAsync(db, daten, p.Identifiant, Rollen.Schueler, schule.Id, KontoStatus.EnAttente, e.Passwort!,
                    mussPasswortAendern: false, erstelltVonId: null, selbstRegistriert: true, ct);
                db.SchuelerProfile.Add(new SchuelerProfil
                {
                    KontoId = neu.Id, SchuleId = schule.Id, ElternTelefon = p.ElternTelefon, ElternEmail = p.ElternEmail
                });
                (name, ortText) = (schule.Name, schule.Ort);
            }
            neu.AgbVersion = agb;
            neu.AgbAkzeptiertAmUtc = zeit.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            return (new RegistrierungsErgebnis(e.Art, p.Identifiant, name), neu, name, ortText);
        }, ct, DbArbeit.KontoVersuche);

        bremse.Merken(verbindung, e.Email);
        if (e.Art == RegistrierungsArt.Direction)
        {
            await mails.SchuleEingegangenAsync(konto.Email, konto.Vorname, ergebnis.Identifiant, schulName, ct);
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var admins = await db.Users.AsNoTracking()
                .Where(k => k.Rolle == Rollen.SuperAdmin && k.Status == KontoStatus.Actif && k.Email != null)
                .Select(k => k.Email!).Distinct().ToListAsync(ct);
            foreach (var admin in admins) await mails.NeueSchuleAnAdminAsync(admin, schulName, ort, konto.VollerName, ct);
        }
        else
        {
            await mails.SchuelerEingegangenAsync(konto.Email, konto.Vorname, ergebnis.Identifiant, schulName, ct);
        }
        return ergebnis;
    }

    private sealed record Geprueft(string Vorname, string Nachname, string Identifiant, string? Email, string? Telefon,
        string? SchulName, string? Ort, string? Land, string? ElternTelefon, string? ElternEmail);

    private async Task<Geprueft> PruefeAsync(SkoulouDbContext db, RegistrierungsEingabe e, CancellationToken ct)
    {
        var vorname = Eingabe.Pflicht(e.Vorname, 100, "votre prénom", "prenom");
        var nachname = Eingabe.Pflicht(e.Nachname, 100, "votre nom", "nom");
        var direction = e.Art == RegistrierungsArt.Direction;
        var email = e.Email?.Trim();
        if (string.IsNullOrEmpty(email))
        {
            if (direction) throw new FachFehler("Une adresse e-mail est nécessaire pour la direction (validation, factures, mot de passe oublié).", "email");
            email = null;
        }
        else if (email.Length > 256 || !MailAusgangService.IstAdresse(email))
        {
            throw new FachFehler("L'adresse e-mail n'est pas valide.", "email");
        }
        var telefon = Telefon(e.Telefon, "telephone");
        if (PasswortService.PruefeNeues(e.Passwort, e.Bestaetigung) is { } pw)
            throw new FachFehler(pw, pw == PasswortService.MeldungBestaetigung ? "confirmation" : "motdepasse");

        string? schulName = null, ort = null, land = null, elternTel = null, elternMail = null;
        if (direction)
        {
            schulName = Eingabe.Pflicht(e.SchulName, 200, "le nom de votre établissement", "ecole-nom");
            ort = Eingabe.Pflicht(e.Ort, 120, "la ville", "ville");
            land = Eingabe.Text(e.Land, 120, "le pays", feld: "pays") ?? "Togo";
            var schluessel = TextNormalisierung.Schluessel(schulName);
            if (schluessel.Length < 3) throw new FachFehler("Le nom de l'établissement est trop court.", "ecole-nom");
            if (await db.Schulen.AnyAsync(s => s.NameNormalisiert == schluessel, ct)) throw new FachFehler(MeldungSchuleVorhanden, "ecole-nom");
        }
        else
        {
            if (e.SchuleId is not { } schuleId) throw new FachFehler(MeldungSchuleWaehlen, "ecole");
            if (!await db.Schulen.AnyAsync(s => s.Id == schuleId && s.Status == SchulStatus.Active, ct)) throw new FachFehler(MeldungSchuleGeschlossen, "ecole");
            var heute = DateOnly.FromDateTime(zeit.GetUtcNow().UtcDateTime);
            if (e.Geburtsdatum is { } g && (g > heute || g.Year < 1920)) throw new FachFehler("La date de naissance n'est pas plausible.", "naissance");
            elternTel = Telefon(e.ElternTelefon, "parent-tel");
            elternMail = string.IsNullOrWhiteSpace(e.ElternEmail) ? null : e.ElternEmail.Trim();
            if (elternMail is not null && (elternMail.Length > 256 || !MailAusgangService.IstAdresse(elternMail)))
                throw new FachFehler("L'adresse e-mail du parent n'est pas valide.", "parent-email");
        }
        var identifiant = await konten.Basis.IdentifiantAsync(db, e.Identifiant, vorname, nachname, ct: ct);
        return new Geprueft(vorname, nachname, identifiant, email, telefon, schulName, ort, land, elternTel, elternMail);
    }

    private static string? Telefon(string? wert, string feld)
    {
        var t = wert?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        if (t.Length > 50 || t.Any(c => !(char.IsDigit(c) || c is '+' or ' ' or '-' or '.' or '(' or ')')))
            throw new FachFehler("Le numéro de téléphone ne peut contenir que des chiffres, des espaces et le signe +.", feld);
        return t;
    }
}
