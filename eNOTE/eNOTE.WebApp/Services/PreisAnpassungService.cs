using System.Globalization;
using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Infrastruktur;
using ENote.WebApp.Data;
using ENote.WebApp.Services.Mails;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Änderung des Standard-Monatspreises für alle Läden – sofort (nur Senkung) oder ab dem 1. eines
/// kommenden Monats. Regeln:
/// <list type="bullet">
/// <item>Der Standard ist der Mindestpreis: kein individueller Preis liegt darunter.</item>
/// <item>Erhöhung um X F → jeder individuelle Preis steigt ebenfalls um X F.</item>
/// <item>Senkung → individuelle Preise bleiben, fallen aber nie unter den neuen Standard.</item>
/// <item>Bereits erzeugte Rechnungen ändern sich nicht; neue Perioden ab dem Datum nutzen den neuen Preis
///   (die Preisstände gelten datumsgenau, siehe <see cref="VertragsService"/>).</item>
/// </list>
/// Optional werden die Besitzer per E-Mail informiert; in der App erscheint immer ein Tarif-Banner.
/// </summary>
public class PreisAnpassungService(IDbContextFactory<ApplicationDbContext> factory, TimeProvider zeit,
    EmailService? email = null, MailBenachrichtigungen? mails = null)
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public sealed record LadenPreis(Guid SalonId, string Laden, decimal Alt, decimal Neu, bool Individuell);

    public sealed record Vorschau(decimal AlterStandard, decimal NeuerStandard, DateTime AbUtc, IReadOnlyList<LadenPreis> Laeden)
    {
        public decimal Differenz => NeuerStandard - AlterStandard;
        public int Betroffen => Laeden.Count(l => l.Neu != l.Alt);
    }

    public sealed record Stand(decimal AktuellerStandard, PreisAnpassung? Geplant, IReadOnlyList<PreisAnpassung> Verlauf);

    public sealed record Ergebnis(Guid Id, DateTime AbUtc, int AngepassteLaeden, int Mails);

    /// <summary>Text der Entwarnung (Titel/Text dürfen Platzhalter enthalten) und ob sie auch per E-Mail geht.</summary>
    public sealed record EntwarnungsText(string Titel, string Text, bool PerMail);

    /// <summary>
    /// Für den Dialog « Annuler le changement de tarif »: wie viele Ankündigungsmails es gab, wie viele Mails eine
    /// Entwarnung erzeugen würde, und die ausgefüllte Vorlage.
    /// </summary>
    public sealed record AufhebungsVorschau(PreisAnpassung Anpassung, int AnkuendigungsMails, int MailEmpfaenger, TextVorlage Vorlage)
    {
        public bool AnkuendigungPerMail => AnkuendigungsMails > 0;
    }

    public sealed record AufhebungsErgebnis(Guid? EntwarnungId, int Mails);

    public const string KategorieAnkuendigung = "changement-tarif", KategorieEntwarnung = "tarif-annule";

    /// <summary>Fertige Textvorlage für Banner und E-Mail; Platzhalter werden beim Speichern ersetzt.</summary>
    public sealed record TextVorlage(string Name, string Titel, string Text);

    /// <summary>Platzhalter, die in Titel und Text automatisch ersetzt werden.</summary>
    public static readonly IReadOnlyList<(string Platzhalter, string Bedeutung)> Platzhalter =
    [
        ("{ancien}", "tarif actuel"), ("{nouveau}", "nouveau tarif"), ("{ecart}", "différence"),
        ("{date}", "date de début"), ("{quand}", "« à partir du … » ou « dès maintenant »")
    ];

    public static readonly IReadOnlyList<TextVorlage> VorlagenErhoehung =
    [
        new("Sobre", "Nouveau tarif {quand}",
            "Le tarif mensuel de base d'eNOTE passe de {ancien} à {nouveau} {quand}.\n\nLes remises sur 3, 6 et 12 mois restent appliquées et les périodes déjà facturées ne changent pas. Votre tarif exact figure dans « Abonnement »."),
        new("Chaleureux", "eNOTE évolue, son tarif aussi",
            "Merci de votre confiance ! Pour continuer à améliorer eNOTE – nouvelles fonctions, assistance et hébergement sécurisé – le tarif mensuel de base passe de {ancien} à {nouveau} {quand}, soit {ecart} de plus par mois.\n\nVos remises sur 3, 6 et 12 mois restent appliquées et les périodes déjà facturées ne changent pas."),
        new("Avec conseil", "Changement de tarif : {nouveau} par mois",
            "{quand}, l'abonnement mensuel d'eNOTE passe de {ancien} à {nouveau}. Les périodes déjà facturées ne changent pas.\n\nAstuce : les formules de 6 et 12 mois vous font économiser 7,5 % et 10 %. Vous pouvez changer de formule à tout moment dans « Abonnement »."),
    ];

    public static readonly IReadOnlyList<TextVorlage> VorlagenSenkung =
    [
        new("Bonne nouvelle", "Bonne nouvelle : le tarif baisse",
            "Le tarif mensuel de base d'eNOTE passe de {ancien} à {nouveau} {quand}, soit {ecart} de moins par mois.\n\nLes remises sur 3, 6 et 12 mois restent appliquées. Votre tarif exact figure dans « Abonnement »."),
        new("Merci", "Merci : eNOTE devient moins cher",
            "Merci d'utiliser eNOTE ! Grâce à votre fidélité, le tarif mensuel de base baisse de {ecart} : {nouveau} au lieu de {ancien}, {quand}.\n\nVos remises sur 3, 6 et 12 mois s'appliquent toujours."),
        new("Sobre", "Nouveau tarif : {nouveau} par mois",
            "Le tarif mensuel de base d'eNOTE passe de {ancien} à {nouveau} {quand}. Les périodes déjà facturées ne changent pas."),
    ];

    public static IReadOnlyList<TextVorlage> Vorlagen(bool erhoehung) => erhoehung ? VorlagenErhoehung : VorlagenSenkung;

    /// <summary>Entwarnung, wenn eine angekündigte Änderung aufgehoben wird (gleiche Platzhalter, Werte der Ankündigung).</summary>
    public static readonly TextVorlage EntwarnungErhoehung = new("Entwarnung",
        "Bonne nouvelle : la hausse de tarif annoncée n'aura pas lieu",
        "Nous vous avions annoncé que le tarif mensuel de base d'eNOTE passerait de {ancien} à {nouveau} {quand}. Cette hausse est annulée : le tarif reste à {ancien}.\n\nVous n'avez rien à faire. Votre tarif exact figure dans « Abonnement ».");

    public static readonly TextVorlage EntwarnungSenkung = new("Annulation",
        "Le changement de tarif annoncé est annulé",
        "Le changement de tarif annoncé ({ancien} → {nouveau} {quand}) n'aura pas lieu : le tarif mensuel de base reste à {ancien}.\n\nVotre tarif exact figure dans « Abonnement ».");

    public static TextVorlage Entwarnung(PreisAnpassung p) => p.NeuerStandard > p.AlterStandard ? EntwarnungErhoehung : EntwarnungSenkung;

    /// <summary>Platzhalter mit den Werten der aufgehobenen Ankündigung.</summary>
    public static string FuelleFuer(string vorlage, PreisAnpassung p) => Fuelle(vorlage, p.AlterStandard, p.NeuerStandard, p.GueltigAbUtc);

    /// <summary>Ersetzt die Platzhalter; der erste Buchstabe wird großgeschrieben (z. B. „{quand}, …“ am Satzanfang).</summary>
    public static string Fuelle(string vorlage, decimal alt, decimal neu, DateTime? abUtc)
    {
        var mitPlatzhalterAmAnfang = vorlage.TrimStart().StartsWith('{');
        static string F(decimal betrag) => betrag.ToString("N0", Fr) + " F";
        var quand = abUtc is DateTime ab ? "à partir du " + DatumText(ab) : "dès maintenant";
        var text = vorlage
            .Replace("{ancien}", F(alt)).Replace("{nouveau}", F(neu)).Replace("{ecart}", F(Math.Abs(neu - alt)))
            .Replace("{date}", DatumText(abUtc ?? DateTime.UtcNow)).Replace("{quand}", quand)
            .Trim();
        return mitPlatzhalterAmAnfang && text.Length > 0 ? char.ToUpper(text[0], Fr) + text[1..] : text;
    }

    /// <summary>Datum im Fließtext, z. B. „1er janvier 2027“.</summary>
    public static string DatumText(DateTime utc)
        => utc.Day == 1 ? "1er " + utc.ToString("MMMM yyyy", Fr) : utc.ToString("d MMMM yyyy", Fr);

    /// <summary>Wählbare Startdaten: der 1. der nächsten 12 Monate.</summary>
    public IReadOnlyList<DateTime> Monatsanfaenge()
    {
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        var erster = new DateTime(jetzt.Year, jetzt.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        return Enumerable.Range(0, 12).Select(i => erster.AddMonths(i)).ToList();
    }

    private static async Task<decimal> StandardAmAsync(ApplicationDbContext db, DateTime datum, CancellationToken ct)
    {
        var stand = await db.MonatsPreisStaende.AsNoTracking()
            .Where(p => p.SalonId == null && p.GueltigAbUtc <= datum && p.MonatsPreis != null)
            .OrderByDescending(p => p.GueltigAbUtc).Select(p => p.MonatsPreis).FirstOrDefaultAsync(ct);
        return stand ?? await db.PlattformPreise.AsNoTracking()
            .Where(p => p.Intervall == AboIntervall.Monatlich && p.IstAktuell)
            .OrderByDescending(p => p.GueltigAbUtc).Select(p => (decimal?)p.Preis).FirstOrDefaultAsync(ct) ?? 0m;
    }

    /// <summary>Individueller Preis eines Ladens zu einem Datum (null = Standard).</summary>
    private static decimal? IndividuellAm(IEnumerable<MonatsPreisStand> staende, Guid salonId, decimal? feld, DateTime datum)
    {
        var stand = staende.Where(p => p.SalonId == salonId && p.GueltigAbUtc <= datum)
            .OrderByDescending(p => p.GueltigAbUtc).FirstOrDefault();
        var preis = stand is not null ? stand.MonatsPreis : feld;
        return preis is > 0 ? preis : null;
    }

    /// <summary>Neuer individueller Preis nach der Regel (null = Standard gilt).</summary>
    public static decimal? NeuerIndividuellerPreis(decimal? individuell, decimal alterStandard, decimal neuerStandard)
    {
        if (individuell is not decimal p) return null;
        var differenz = neuerStandard - alterStandard;
        var neu = differenz > 0 ? p + differenz : p;
        return neu > neuerStandard ? neu : null;
    }

    private static DateTime? PruefeAb(DateTime? abUtc, decimal alt, decimal neu, DateTime jetzt)
    {
        if (abUtc is null)
        {
            if (neu > alt) throw new ArgumentException("Une hausse doit être annoncée : choisissez un mois à venir.");
            return null;
        }
        var ab = DateTime.SpecifyKind(abUtc.Value, DateTimeKind.Utc);
        if (ab.Day != 1 || ab.TimeOfDay != TimeSpan.Zero) throw new ArgumentException("Le nouveau tarif commence toujours le 1er du mois.");
        if (ab <= jetzt) throw new ArgumentException("Choisissez un mois à venir.");
        return ab;
    }

    public async Task<Stand> GetStandAsync(Guid bearbeiter, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AdminKontenService.PruefeAsync(db, bearbeiter, false, ct);
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        // Verlauf mit aufgehobenen Änderungen (Journal: wann aufgehoben, Entwarnung per App/Mail), neueste zuerst.
        var verlauf = await db.PreisAnpassungen.AsNoTracking()
            .OrderByDescending(p => p.ErstelltUtc).ThenByDescending(p => p.GueltigAbUtc).Take(8).ToListAsync(ct);
        var geplant = await db.PreisAnpassungen.AsNoTracking().Where(p => !p.IstAufgehoben && p.GueltigAbUtc > jetzt)
            .OrderBy(p => p.GueltigAbUtc).FirstOrDefaultAsync(ct);
        return new Stand(await StandardAmAsync(db, jetzt, ct), geplant, verlauf);
    }

    /// <summary>Was würde sich für jeden aktiven Laden ändern?</summary>
    public async Task<Vorschau> GetVorschauAsync(Guid bearbeiter, decimal neuerStandard, DateTime? abUtc, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AdminKontenService.PruefeAsync(db, bearbeiter, false, ct);
        return await BerechneAsync(db, neuerStandard, abUtc, ct);
    }

    private async Task<Vorschau> BerechneAsync(ApplicationDbContext db, decimal neuerStandard, DateTime? abUtc, CancellationToken ct)
    {
        if (neuerStandard <= 0) throw new ArgumentException("Veuillez saisir un montant positif.");
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        var stichtag = abUtc ?? jetzt;
        var alt = await StandardAmAsync(db, stichtag, ct);
        var laeden = await db.Salons.AsNoTracking().Where(s => s.IstAktiv)
            .OrderBy(s => s.Name).Select(s => new { s.Id, s.Name, s.IndividuellerMonatsPreis }).ToListAsync(ct);
        var ids = laeden.Select(l => l.Id).ToList();
        var staende = await db.MonatsPreisStaende.AsNoTracking()
            .Where(p => p.SalonId != null && ids.Contains(p.SalonId.Value)).ToListAsync(ct);
        var zeilen = laeden.Select(l =>
        {
            var individuell = IndividuellAm(staende, l.Id, l.IndividuellerMonatsPreis, stichtag);
            var neu = NeuerIndividuellerPreis(individuell, alt, neuerStandard);
            return new LadenPreis(l.Id, l.Name, individuell ?? alt, neu ?? neuerStandard, individuell is not null);
        }).ToList();
        return new Vorschau(alt, neuerStandard, stichtag, zeilen);
    }

    /// <summary>
    /// Legt die Preisänderung fest: Standard-Stand + angepasste individuelle Stände ab dem Datum,
    /// Tarif-Banner für die Besitzer und – falls gewünscht – eine E-Mail an jeden Besitzer.
    /// </summary>
    public async Task<Ergebnis> PlaneAsync(Guid bearbeiter, decimal neuerStandard, DateTime? abUtc, bool perMail,
        string? titel = null, string? text = null, CancellationToken ct = default)
    {
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        PreisAnpassung anpassung;
        List<LadenPreis> geaendert;
        string bannerTitel, bannerText;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            await AdminKontenService.PruefeAsync(db, bearbeiter, false, ct);
            if (await db.PreisAnpassungen.AnyAsync(p => !p.IstAufgehoben && p.GueltigAbUtc > jetzt, ct))
                throw new InvalidOperationException("Un changement de tarif est déjà planifié. Annulez-le d'abord.");

            var aktuell = await StandardAmAsync(db, jetzt, ct);
            var ab = PruefeAb(abUtc, aktuell, neuerStandard, jetzt);
            var vorschau = await BerechneAsync(db, neuerStandard, ab, ct);
            if (vorschau.NeuerStandard == vorschau.AlterStandard) throw new ArgumentException("Le montant est identique au tarif actuel.");
            var gueltigAb = ab ?? jetzt;

            anpassung = new PreisAnpassung
            {
                AlterStandard = vorschau.AlterStandard, NeuerStandard = neuerStandard, GueltigAbUtc = gueltigAb,
                ErstelltUtc = jetzt, ErstelltVonId = bearbeiter, IstAngewendet = ab is null
            };
            db.PreisAnpassungen.Add(anpassung);
            db.MonatsPreisStaende.Add(new MonatsPreisStand { SalonId = null, MonatsPreis = neuerStandard, GueltigAbUtc = gueltigAb, PreisAnpassungId = anpassung.Id });

            var salons = await db.Salons.Where(s => s.IstAktiv).ToDictionaryAsync(s => s.Id, ct);
            foreach (var zeile in vorschau.Laeden.Where(l => l.Individuell))
            {
                var neu = NeuerIndividuellerPreis(zeile.Alt, vorschau.AlterStandard, neuerStandard);
                if (neu == zeile.Alt) continue;
                db.MonatsPreisStaende.Add(new MonatsPreisStand { SalonId = zeile.SalonId, MonatsPreis = neu, GueltigAbUtc = gueltigAb, PreisAnpassungId = anpassung.Id });
                if (ab is null && salons.TryGetValue(zeile.SalonId, out var salon))
                {
                    salon.IndividuellerMonatsPreis = neu;
                    salon.UpdatedAtUtc = jetzt;
                }
                anpassung.AngepassteLaeden++;
            }

            if (ab is null) SetzePlattformPreis(db, neuerStandard, jetzt);

            // Text: gewählte/bearbeitete Vorlage, sonst die erste passende Vorlage.
            var vorlage = Vorlagen(neuerStandard > vorschau.AlterStandard)[0];
            var fertigerTitel = Fuelle(string.IsNullOrWhiteSpace(titel) ? vorlage.Titel : titel, vorschau.AlterStandard, neuerStandard, ab);
            var fertigerText = Fuelle(string.IsNullOrWhiteSpace(text) ? vorlage.Text : text, vorschau.AlterStandard, neuerStandard, ab);
            if (fertigerTitel.Length > AnkuendigungService.TitelMax) throw new ArgumentException($"Titre : {AnkuendigungService.TitelMax} caractères au maximum.");
            if (fertigerText.Length > AnkuendigungService.TextMax) throw new ArgumentException($"Message : {AnkuendigungService.TextMax} caractères au maximum.");
            var banner = AnkuendigungService.Erzeuge(db, AnkuendigungArt.Tarif, AnkuendigungZiel.Proprietaires,
                fertigerTitel, fertigerText, "/abonnement", "Voir mon abonnement", jetzt, gueltigAb.AddDays(31), bearbeiter);
            bannerTitel = fertigerTitel;
            bannerText = fertigerText;
            anpassung.AnkuendigungId = banner.Id;

            await db.SaveChangesAsync(ct);
            geaendert = vorschau.Laeden.Where(l => l.Neu != l.Alt).ToList();
        }

        var anzahl = perMail ? await BenachrichtigeAsync(anpassung, geaendert, bannerTitel, bannerText, ct) : 0;
        if (anzahl > 0)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var gespeichert = await db.PreisAnpassungen.FirstAsync(p => p.Id == anpassung.Id, ct);
            gespeichert.MailsGesendet = anzahl;
            await db.SaveChangesAsync(ct);
        }
        return new Ergebnis(anpassung.Id, anpassung.GueltigAbUtc, anpassung.AngepassteLaeden, anzahl);
    }

    private static void SetzePlattformPreis(ApplicationDbContext db, decimal preis, DateTime jetzt)
    {
        foreach (var alt in db.PlattformPreise.Where(p => p.Intervall == AboIntervall.Monatlich && p.IstAktuell))
            alt.IstAktuell = false;
        db.PlattformPreise.Add(new PlattformPreis { Intervall = AboIntervall.Monatlich, Preis = preis, IstAktuell = true, GueltigAbUtc = jetzt });
    }

    /// <summary>Eine E-Mail je Besitzer und Laden mit dem persönlichen neuen Preis.</summary>
    private async Task<int> BenachrichtigeAsync(PreisAnpassung anpassung, IReadOnlyList<LadenPreis> laeden, string titel, string text, CancellationToken ct)
    {
        if (email is null || laeden.Count == 0) return 0;
        await using var db = await factory.CreateDbContextAsync(ct);
        var ids = laeden.Select(l => l.SalonId).ToList();
        var besitzerRolle = db.Roles.Where(r => r.Name == AppRollen.Besitzer).Select(r => r.Id);
        var empfaenger = await db.UserSalons.AsNoTracking()
            .Where(us => ids.Contains(us.SalonId) && !us.AppUser.IsBlocked && us.AppUser.Email != null && us.AppUser.Email != ""
                && db.UserRoles.Any(ur => ur.UserId == us.AppUserId && besitzerRolle.Contains(ur.RoleId)))
            .Select(us => new { us.SalonId, us.AppUser.Vorname, Email = us.AppUser.Email! })
            .ToListAsync(ct);
        var aboUrl = (mails?.AppUrl ?? BonQr.AppSeite) + "/abonnement";
        var anzahl = 0;
        foreach (var e in empfaenger)
        {
            var laden = laeden.First(l => l.SalonId == e.SalonId);
            var html = TabloSuiteMails.ChangementTarif(e.Vorname, laden.Laden, titel, text, laden.Alt, laden.Neu, anpassung.GueltigAbUtc, aboUrl);
            if (await email.SendeAsync(e.Email, titel, html, KategorieAnkuendigung, ct))
                anzahl++;
        }
        return anzahl;
    }

    /// <summary>Vorschau für den Dialog « Annuler le changement de tarif ».</summary>
    public async Task<AufhebungsVorschau> GetAufhebungsVorschauAsync(Guid bearbeiter, Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AdminKontenService.PruefeAsync(db, bearbeiter, false, ct);
        var anpassung = await db.PreisAnpassungen.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new ArgumentException("Changement introuvable.");
        var angekuendigt = await AnkuendigungsMailsAsync(db, anpassung, ct);
        var empfaenger = angekuendigt.Count > 0 ? angekuendigt.Count : (await BesitzerBeiAnkuendigungAsync(db, anpassung, ct)).Count;
        return new AufhebungsVorschau(anpassung, angekuendigt.Count, empfaenger, Entwarnung(anpassung));
    }

    /// <summary>
    /// Hebt eine noch nicht wirksame Änderung auf (Preisstände und Tarif-Banner werden entfernt) und gibt Entwarnung:
    /// Tarif-Banner für die Besitzer wie bei der Ankündigung und – falls gewünscht – je eine E-Mail an genau die
    /// Adressen, die die Ankündigung per E-Mail bekamen (ging sie nur in die App: an die Besitzer der Läden, die es
    /// damals gab). Ohne <paramref name="entwarnung"/>: Vorlage, E-Mail nur, wenn auch die Ankündigung per E-Mail ging.
    /// Aufhebung und Entwarnung stehen im Verlauf (Administration › Tarifs, Annonces).
    /// </summary>
    public async Task<AufhebungsErgebnis> AufhebenAsync(Guid bearbeiter, Guid id, EntwarnungsText? entwarnung = null, CancellationToken ct = default)
    {
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        Ankuendigung banner;
        List<Empfaenger> empfaenger;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            await AdminKontenService.PruefeAsync(db, bearbeiter, false, ct);
            var anpassung = await db.PreisAnpassungen.FirstOrDefaultAsync(p => p.Id == id, ct)
                ?? throw new ArgumentException("Changement introuvable.");
            if (anpassung.IstAufgehoben) return new AufhebungsErgebnis(anpassung.EntwarnungAnkuendigungId, anpassung.EntwarnungMails);
            if (anpassung.GueltigAbUtc <= jetzt)
                throw new InvalidOperationException("Ce tarif est déjà en vigueur : planifiez un nouveau changement.");

            var vorlage = Entwarnung(anpassung);
            var titel = FuelleFuer(string.IsNullOrWhiteSpace(entwarnung?.Titel) ? vorlage.Titel : entwarnung.Titel, anpassung);
            var text = FuelleFuer(string.IsNullOrWhiteSpace(entwarnung?.Text) ? vorlage.Text : entwarnung.Text, anpassung);
            if (titel.Length > AnkuendigungService.TitelMax) throw new ArgumentException($"Titre : {AnkuendigungService.TitelMax} caractères au maximum.");
            if (text.Length > AnkuendigungService.TextMax) throw new ArgumentException($"Message : {AnkuendigungService.TextMax} caractères au maximum.");

            var angekuendigt = await AnkuendigungsMailsAsync(db, anpassung, ct);
            var perMail = entwarnung?.PerMail ?? angekuendigt.Count > 0;
            empfaenger = !perMail ? [] : angekuendigt.Count > 0 ? angekuendigt : await BesitzerBeiAnkuendigungAsync(db, anpassung, ct);

            db.MonatsPreisStaende.RemoveRange(await db.MonatsPreisStaende.Where(p => p.PreisAnpassungId == id).ToListAsync(ct));
            if (anpassung.AnkuendigungId is Guid bannerId
                && await db.Ankuendigungen.FirstOrDefaultAsync(a => a.Id == bannerId, ct) is { } alt)
                alt.IstAktiv = false;
            // Wie die Ankündigung: Tarif-Banner für die Besitzer, sichtbar bis zum angekündigten Datum (mindestens 7 Tage).
            var bis = anpassung.GueltigAbUtc > jetzt.AddDays(7) ? anpassung.GueltigAbUtc : jetzt.AddDays(7);
            banner = AnkuendigungService.Erzeuge(db, AnkuendigungArt.Tarif, AnkuendigungZiel.Proprietaires,
                titel, text, "/abonnement", "Voir mon abonnement", jetzt, bis, bearbeiter);
            anpassung.IstAufgehoben = true;
            anpassung.AufgehobenUtc = jetzt;
            anpassung.AufgehobenVonId = bearbeiter;
            anpassung.EntwarnungAnkuendigungId = banner.Id;
            await db.SaveChangesAsync(ct);
        }

        var anzahl = 0;
        if (email is not null && empfaenger.Count > 0)
        {
            var aboUrl = (mails?.AppUrl ?? BonQr.AppSeite) + "/abonnement";
            foreach (var e in empfaenger)
            {
                var html = TabloSuiteMails.TarifAnnule(e.Vorname, banner.Titel, banner.Text, aboUrl);
                if (await email.SendeAsync(e.Email, banner.Titel, html, KategorieEntwarnung, ct)) anzahl++;
            }
            if (anzahl > 0)
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                (await db.PreisAnpassungen.FirstAsync(p => p.Id == id, ct)).EntwarnungMails = anzahl;
                (await db.Ankuendigungen.FirstAsync(a => a.Id == banner.Id, ct)).MailsGesendet = anzahl;
                await db.SaveChangesAsync(ct);
            }
        }
        return new AufhebungsErgebnis(banner.Id, anzahl);
    }

    private sealed record Empfaenger(string Email, string? Vorname);

    /// <summary>
    /// Ankündigungsmails dieser Änderung aus der Mail-Warteschlange (eine Zeile je Mail, auch ohne konfigurierten
    /// Mailweg). Der Vorname kommt vom Besitzerkonto mit dieser Adresse, wenn er eindeutig ist.
    /// </summary>
    private static async Task<List<Empfaenger>> AnkuendigungsMailsAsync(ApplicationDbContext db, PreisAnpassung anpassung, CancellationToken ct)
    {
        var titel = await db.Ankuendigungen.AsNoTracking().Where(a => a.Id == anpassung.AnkuendigungId).Select(a => a.Titel).FirstOrDefaultAsync(ct);
        if (titel is null) return [];
        var betreff = EmailService.MitAppName(titel);
        // Genau ab der Ankündigung: eine früher aufgehobene Ankündigung mit demselben Titel (gleicher Monat, gleiche
        // Vorlage) zählt nicht mit – in der Klickprüfung W1 bekam sonst jeder Besitzer drei Entwarnungen.
        var ab = anpassung.ErstelltUtc;
        var adressen = await db.MailNachrichten.AsNoTracking()
            .Where(m => m.Kategorie == KategorieAnkuendigung && m.Betreff == betreff && m.CreatedAtUtc >= ab)
            .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id).Select(m => m.Empfaenger).ToListAsync(ct);
        if (adressen.Count == 0) return [];
        var besitzerRolle = db.Roles.Where(r => r.Name == AppRollen.Besitzer).Select(r => r.Id);
        var namen = await db.Users.AsNoTracking()
            .Where(u => u.Email != null && adressen.Contains(u.Email) && db.UserRoles.Any(ur => ur.UserId == u.Id && besitzerRolle.Contains(ur.RoleId)))
            .Select(u => new { u.Email, u.Vorname }).ToListAsync(ct);
        string? Vorname(string adresse)
        {
            var passend = namen.Where(n => string.Equals(n.Email, adresse, StringComparison.OrdinalIgnoreCase)).Select(n => n.Vorname).Distinct().ToList();
            return passend.Count == 1 ? passend[0] : null;
        }
        return adressen.Select(a => new Empfaenger(a, Vorname(a))).ToList();
    }

    /// <summary>Ankündigung ohne Mail: Besitzer (nicht gesperrt, mit Adresse) der aktiven Läden, die es bei der Ankündigung schon gab.</summary>
    private static async Task<List<Empfaenger>> BesitzerBeiAnkuendigungAsync(ApplicationDbContext db, PreisAnpassung anpassung, CancellationToken ct)
    {
        var besitzerRolle = db.Roles.Where(r => r.Name == AppRollen.Besitzer).Select(r => r.Id);
        return await db.UserSalons.AsNoTracking()
            .Where(us => us.Salon.IstAktiv && us.Salon.CreatedAtUtc <= anpassung.ErstelltUtc
                && !us.AppUser.IsBlocked && us.AppUser.Email != null && us.AppUser.Email != ""
                && db.UserRoles.Any(ur => ur.UserId == us.AppUserId && besitzerRolle.Contains(ur.RoleId)))
            .OrderBy(us => us.Salon.Name).ThenBy(us => us.AppUserId)
            .Select(us => new Empfaenger(us.AppUser.Email!, us.AppUser.Vorname))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Bringt fällig gewordene Änderungen in die Salon-Felder (Anzeige) und in den aktuellen Plattformpreis.
    /// Die Abrechnung liest die Preisstände ohnehin datumsgenau. Läuft im Hintergrunddienst.
    /// </summary>
    public async Task<int> WendeFaelligeAnAsync(CancellationToken ct = default)
    {
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        await using var db = await factory.CreateDbContextAsync(ct);
        var faellig = await db.PreisAnpassungen
            .Where(p => !p.IstAufgehoben && !p.IstAngewendet && p.GueltigAbUtc <= jetzt)
            .OrderBy(p => p.GueltigAbUtc).ToListAsync(ct);
        foreach (var anpassung in faellig)
        {
            var staende = await db.MonatsPreisStaende.AsNoTracking()
                .Where(p => p.PreisAnpassungId == anpassung.Id && p.SalonId != null).ToListAsync(ct);
            foreach (var stand in staende)
            {
                // Nur übernehmen, wenn danach kein neuerer (manueller) Preis gesetzt wurde.
                var neuer = await db.MonatsPreisStaende.AnyAsync(p => p.SalonId == stand.SalonId && p.GueltigAbUtc > stand.GueltigAbUtc && p.GueltigAbUtc <= jetzt, ct);
                if (neuer) continue;
                if (await db.Salons.FirstOrDefaultAsync(s => s.Id == stand.SalonId, ct) is { } salon)
                {
                    salon.IndividuellerMonatsPreis = stand.MonatsPreis;
                    salon.UpdatedAtUtc = jetzt;
                }
            }
            SetzePlattformPreis(db, anpassung.NeuerStandard, anpassung.GueltigAbUtc);
            anpassung.IstAngewendet = true;
        }
        if (faellig.Count > 0) await db.SaveChangesAsync(ct);
        return faellig.Count;
    }

    /// <summary>
    /// Admin setzt einen individuellen Preis, während eine Änderung geplant ist: der Laden-Stand der
    /// geplanten Änderung wird nach derselben Regel neu berechnet.
    /// </summary>
    internal static async Task PasseGeplanteAnAsync(ApplicationDbContext db, Guid salonId, decimal? preis, DateTime jetzt, CancellationToken ct)
    {
        var geplant = await db.PreisAnpassungen.Where(p => !p.IstAufgehoben && p.GueltigAbUtc > jetzt)
            .OrderBy(p => p.GueltigAbUtc).FirstOrDefaultAsync(ct);
        if (geplant is null) return;
        db.MonatsPreisStaende.RemoveRange(await db.MonatsPreisStaende
            .Where(p => p.PreisAnpassungId == geplant.Id && p.SalonId == salonId).ToListAsync(ct));
        if (preis is null) return;
        var neu = NeuerIndividuellerPreis(preis, geplant.AlterStandard, geplant.NeuerStandard);
        db.MonatsPreisStaende.Add(new MonatsPreisStand { SalonId = salonId, MonatsPreis = neu, GueltigAbUtc = geplant.GueltigAbUtc, PreisAnpassungId = geplant.Id });
    }

    /// <summary>Kommender Monatspreis eines Ladens (für die Besitzer-Seite), falls eine Änderung geplant ist.</summary>
    public static async Task<(DateTime AbUtc, decimal Preis)?> KommenderPreisAsync(ApplicationDbContext db, Guid salonId, DateTime jetzt, CancellationToken ct)
    {
        var geplant = await db.PreisAnpassungen.AsNoTracking().Where(p => !p.IstAufgehoben && p.GueltigAbUtc > jetzt)
            .OrderBy(p => p.GueltigAbUtc).FirstOrDefaultAsync(ct);
        if (geplant is null) return null;
        var eigener = await db.MonatsPreisStaende.AsNoTracking()
            .Where(p => p.SalonId == salonId && p.GueltigAbUtc <= geplant.GueltigAbUtc)
            .OrderByDescending(p => p.GueltigAbUtc).FirstOrDefaultAsync(ct);
        decimal? individuell = eigener is not null
            ? eigener.MonatsPreis
            : await db.Salons.AsNoTracking().Where(s => s.Id == salonId).Select(s => s.IndividuellerMonatsPreis).FirstOrDefaultAsync(ct);
        return (geplant.GueltigAbUtc, individuell is > 0 ? individuell.Value : geplant.NeuerStandard);
    }
}
