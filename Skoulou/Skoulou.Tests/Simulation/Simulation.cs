using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data.Seed;
using Skoulou.WebApp.Services.Finanzen;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Simulation;

/// <summary>
/// Simulation (Bauplan §9.4): ≥ 20 Schulen, drei vollständige Schuljahre (2023-2024 … 2025-2026) und das laufende Jahr
/// 2026-2027 bis heute (14/11/2026) – alles über die echten Dienste. Die Uhr ist global; je Stichtag arbeiten die Schulen
/// parallel (Zufall je Schule und Stichtag mit festem Startwert), danach laufen Abrechnung und Rechnungswege nacheinander.
/// </summary>
internal sealed partial class Simulation(SimDienste d, double faktor)
{
    public const int Startwert = 20_261_114;
    public static readonly DateOnly Ende = new(2026, 11, 14);
    private const int Parallel = 8;

    private readonly SimApi api = new(d);
    private readonly Stopwatch uhr = Stopwatch.StartNew();

    public List<SimSchule> Schulen { get; } = [];
    public Guid Admin, Admin2, Admin3;
    public List<Guid> Responsables { get; } = [];
    public List<SimRechnung> Rechnungen { get; } = [];
    public ConcurrentQueue<string> Fehler { get; } = new();
    public ConcurrentDictionary<string, int> Zaehler { get; } = new();
    public List<string> Ablauf { get; } = [];
    public Guid AbgelehnteSchule, WartendeSchule;

    private Guid AdminFuer(int i) => (i % 3) switch { 0 => Admin, 1 => Admin2, _ => Admin3 };

    private readonly ConcurrentDictionary<Guid, byte> angemeldet = new();

    /// <summary>Erste Anmeldung eines Kontos: « J'accepte et je continue » (Nutzungsbedingungen), einmal je Konto.</summary>
    private async Task ErsteAnmeldungAsync(Guid konto)
    {
        if (!angemeldet.TryAdd(konto, 0)) return;
        await d.Hole<KontoService>().AgbAkzeptierenAsync(konto);
    }

    public void Zaehle(string was, int anzahl = 1) => Zaehler.AddOrUpdate(was, anzahl, (_, alt) => alt + anzahl);

    public void Notiere(string text)
    {
        var zeile = $"[{uhr.Elapsed:hh\\:mm\\:ss}] {d.HeuteDatum:dd/MM/yyyy} {text}";
        lock (Ablauf)
        {
            Ablauf.Add(zeile);
            var ordner = Path.Combine(Path.GetTempPath(), "skoulou-neu", "E7");
            Directory.CreateDirectory(ordner);
            File.AppendAllText(Path.Combine(ordner, "simulation-fortschritt.txt"), zeile + Environment.NewLine);
        }
    }

    /// <summary>Erwartet Erfolg; ein fachlicher Fehler wird als Befund protokolliert (Test schlägt am Ende fehl).</summary>
    private async Task<T?> SollAsync<T>(string was, Func<Task<T>> aktion)
    {
        try
        {
            try
            {
                return await aktion();
            }
            catch (FachFehler ex) when (ex.Message.Contains("enregistrement simultané", StringComparison.Ordinal))
            {
                // Gleichzeitige Anlage desselben Identifiants in zwei Schulen: wie die Meldung empfiehlt, erneut versuchen.
                Zaehle("Gleichzeitige Anlage wiederholt");
                return await aktion();
            }
        }
        catch (Exception ex) when (ex is FachFehler or KeinZugriffFehler or DbUpdateException or InvalidOperationException)
        {
            Fehler.Enqueue($"{d.HeuteDatum:dd/MM/yyyy} {was}: {ex.GetType().Name}: {ex.Message}");
            return default;
        }
    }

    private Task SollAsync(string was, Func<Task> aktion) => SollAsync(was, async () => { await aktion(); return true; });

    /// <summary>Erwartet eine Ablehnung (Rechte oder Regel); Erfolg ist ein Befund.</summary>
    private async Task AbgelehntAsync(string was, Func<Task> aktion, bool nurKeinZugriff = true)
    {
        try
        {
            await aktion();
            Fehler.Enqueue($"{d.HeuteDatum:dd/MM/yyyy} NICHT abgelehnt: {was}");
        }
        catch (KeinZugriffFehler)
        {
            Zaehle("Rechte: refusé (KeinZugriff)");
        }
        catch (FachFehler ex) when (!nurKeinZugriff)
        {
            Zaehle("Regel: abgelehnt (FachFehler)");
            _ = ex;
        }
        catch (FachFehler ex)
        {
            Fehler.Enqueue($"{d.HeuteDatum:dd/MM/yyyy} {was}: FachFehler statt « refusé » : {ex.Message}");
        }
    }

    private async Task JeSchuleAsync(string phase, Func<SimSchule, Zufall, Task> arbeit, int stichtag, bool auchSuspendiert = false)
    {
        var sw = Stopwatch.StartNew();
        await System.Threading.Tasks.Parallel.ForEachAsync(Schulen.Where(s => auchSuspendiert || !s.Suspendiert),
            new ParallelOptions { MaxDegreeOfParallelism = Parallel },
            async (s, _) => await arbeit(s, new Zufall(Zufall.Kombiniere(Startwert, s.Nr, stichtag, phase.Length))));
        if (sw.ElapsedMilliseconds > 20_000) Notiere($"{phase}: {sw.Elapsed.TotalSeconds:0} s");
    }

    // ---------------------------------------------------------------- Ablauf

    public async Task AusfuehrenAsync()
    {
        d.Heute(new DateOnly(2023, 6, 1));
        await PlattformAsync();
        await SchulenRegistrierenAsync();
        Notiere($"Plattform: {Schulen.Count} Schulen aktiv.");

        d.Heute(new DateOnly(2023, 7, 3));
        await JeSchuleAsync("einrichten", EinrichtenAsync, 0);
        Notiere($"Eingerichtet: {Schulen.Sum(s => s.Schueler.Count)} Schüler.");

        for (var startJahr = 2023; startJahr <= 2026; startJahr++)
        {
            await JahrAsync(startJahr);
            Notiere($"Jahr {startJahr}-{startJahr + 1} fertig: {Schulen.Sum(s => s.Schueler.Count)} Schüler, {Rechnungen.Count} Rechnungen.");
        }
    }

    /// <summary>Stichtage eines Jahres: 1. und 15. jedes Monats von September bis Juli (im laufenden Jahr bis heute).</summary>
    private static IEnumerable<DateOnly> Stichtage(int startJahr)
    {
        for (var m = 0; m < 11; m++)
        {
            var monat = new DateOnly(startJahr, 9, 1).AddMonths(m);
            foreach (var tag in new[] { monat, monat.AddDays(14) })
            {
                if (tag > Ende) yield break;
                yield return tag;
            }
        }
    }

    private async Task JahrAsync(int startJahr)
    {
        var laufend = startJahr == 2026;
        var vorher = new DateOnly(startJahr, 8, 31);
        foreach (var tag in Stichtage(startJahr))
        {
            if (tag.Month == 7 && tag.Day == 15) break;
            d.Heute(tag);
            var nr = tag.DayNumber;
            await SonderereignisseAsync(tag);
            await JeSchuleAsync("stichtag", (s, z) => StichtagAsync(s, z, vorher, tag), nr);
            await AbrechnenAsync(tag);
            await RechnungswegeAsync(tag);
            vorher = tag;
        }
        if (laufend)
        {
            d.Heute(Ende);
            await JeSchuleAsync("aktuell", (s, z) => AktuellePeriodeAsync(s, z), Ende.DayNumber);
            return;
        }

        d.Heute(new DateOnly(startJahr + 1, 7, 3));
        await AnteileAuszahlenAsync();
        await JeSchuleAsync("passage", PassageAsync, startJahr * 10 + 7);
    }

    /// <summary>Arbeit einer Schule an einem Stichtag (Zahlungen, Journal, Bewegungen, Noten, Inhalte).</summary>
    private async Task StichtagAsync(SimSchule s, Zufall z, DateOnly vorher, DateOnly heute)
    {
        var jahr = s.Jahr;
        if (heute < jahr.Beginn || heute > jahr.Ende.AddDays(20)) return;

        // Perioden, die seit dem letzten Stichtag geendet haben: Noten, Conseil, Veröffentlichung (Sprachschule: dann Sessionwechsel).
        foreach (var p in jahr.Perioden.Where(p => !p.Abgeschlossen && p.Ende < heute).ToList())
        {
            await PeriodeAbschliessenAsync(s, z, p);
            if (s.P.Typ == SchulTyp.Langues) await SessionWechselAsync(s, z, p);
        }
        if (heute <= jahr.Ende)
        {
            await ZahlungenAsync(s, z, heute);
            if (heute.Day == 1) await JournalAsync(s, z, heute);
            if (heute.Day == 15) await AppellAsync(s, z, heute);
            await BewegungenAsync(s, z, heute);
            if (heute.Month is 10 or 2 && heute.Day == 15) await InhalteAsync(s, z, heute);
        }
    }

    // ---------------------------------------------------------------- Plattform

    private async Task PlattformAsync()
    {
        await using (var scope = d.Dienste.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<StartSeed>().AusfuehrenAsync();
        await d.MitDbAsync(async db => { await FinanzKategorien.SicherstellenAsync(db); return true; });
        Admin = await d.MitDbAsync(db => db.Users.Where(k => k.Rolle == Rollen.SuperAdmin).Select(k => k.Id).SingleAsync());

        // Zwei weitere Administratoren (wie der Start-Seed: Identity, eigenes Passwort).
        Admin2 = await AdminAnlegenAsync("akouvi.lawson", "Akouvi", "Lawson");
        Admin3 = await AdminAnlegenAsync("yao.mensah", "Yao", "Mensah");
        foreach (var a in new[] { Admin, Admin2, Admin3 }) await ErsteAnmeldungAsync(a);

        var tarife = api.Tarife;
        await tarife.EinstellungSpeichernAsync(Admin, new TarifEinstellung(12_500, 30, 10));
        foreach (var (vor, nach) in new[] { ("Edem", "Kpodar"), ("Mawuena", "Akakpo"), ("Ibrahim", "Bouraïma") })
        {
            var zugang = await api.AdminKonten.ResponsableAnlegenAsync(Admin, new ResponsableEingabe(vor, nach, null,
                SimNamen.Mail(vor, nach, 0).Replace("@", ".resp@"), "+228 90 11 22 33"));
            Responsables.Add(zugang.KontoId);
            await ErsteAnmeldungAsync(zugang.KontoId);
        }
        Zaehle("Admins", 3);
        Zaehle("Responsables", Responsables.Count);
    }

    private async Task<Guid> AdminAnlegenAsync(string identifiant, string vorname, string nachname)
    {
        var konto = new Konto { UserName = identifiant, Email = $"{identifiant}@skoulou-sim.tg", Vorname = vorname, Nachname = nachname, Status = KontoStatus.Actif };
        var ergebnis = await KontoAnlage.AnlegenAsync(api.Benutzer, konto, Rollen.SuperAdmin, "Sim" + Guid.NewGuid().ToString("N")[..10] + "7");
        Assert.True(ergebnis.Succeeded, KontoAnlage.Fehlertext(ergebnis));
        return konto.Id;
    }

    private static readonly SchulProfil[] Profile =
    [
        new("EPP Bè-Kpota", "Lomé", SchulTyp.Primaire, PeriodenArt.Trimestre, 2, 28, 42),
        new("École primaire Les Colibris", "Lomé", SchulTyp.Primaire, PeriodenArt.Trimestre, 1, 22, 35, Prozent: 15),
        new("EPP Agoè-Nyivé", "Lomé", SchulTyp.Primaire, PeriodenArt.Trimestre, 2, 30, 45),
        new("École La Source", "Kpalimé", SchulTyp.Primaire, PeriodenArt.Trimestre, 1, 18, 32),
        new("École primaire Sainte-Thérèse", "Aného", SchulTyp.Primaire, PeriodenArt.Trimestre, 1, 20, 34),
        new("Groupe scolaire Les Bambins", "Tsévié", SchulTyp.Primaire, PeriodenArt.Trimestre, 1, 20, 30),
        new("EPP Kara-Tomdè", "Kara", SchulTyp.Primaire, PeriodenArt.Trimestre, 1, 25, 40),
        new("École primaire Le Flamboyant", "Atakpamé", SchulTyp.Primaire, PeriodenArt.Trimestre, 1, 18, 30),
        new("Collège Saint-Joseph", "Lomé", SchulTyp.College, PeriodenArt.Trimestre, 3, 35, 50, Prozent: 15),
        new("CEG Adidogomé", "Lomé", SchulTyp.College, PeriodenArt.Trimestre, 3, 40, 55),
        new("Collège Le Baobab", "Sokodé", SchulTyp.College, PeriodenArt.Trimestre, 2, 30, 45),
        new("Collège protestant d'Agou", "Agou", SchulTyp.College, PeriodenArt.Semestre, 1, 25, 40),
        new("CEG Dapaong Centre", "Dapaong", SchulTyp.College, PeriodenArt.Trimestre, 2, 35, 50),
        new("Collège Les Palmiers", "Tsévié", SchulTyp.College, PeriodenArt.Trimestre, 2, 25, 40),
        new("Collège Notre-Dame des Lacs", "Aného", SchulTyp.College, PeriodenArt.Trimestre, 1, 25, 40),
        new("Lycée de Tokoin", "Lomé", SchulTyp.Lycee, PeriodenArt.Semestre, 3, 40, 55),
        new("Lycée moderne de Kpalimé", "Kpalimé", SchulTyp.Lycee, PeriodenArt.Trimestre, 2, 35, 50),
        new("Lycée Les Cocotiers", "Lomé", SchulTyp.Lycee, PeriodenArt.Semestre, 2, 30, 45),
        new("Lycée Mono", "Notsé", SchulTyp.Lycee, PeriodenArt.Trimestre, 1, 30, 45),
        new("Lycée de la Kozah", "Kara", SchulTyp.Lycee, PeriodenArt.Semestre, 2, 35, 50),
        new("Institut de langues Le Pont", "Lomé", SchulTyp.Langues, PeriodenArt.Session, 1, 10, 18, Preis: 8_000),
        new("Centre de langues Horizon", "Kara", SchulTyp.Langues, PeriodenArt.Session, 1, 8, 15, Preis: 8_000),
    ];

    /// <summary>Selbstregistrierung der Direktionen, Prüfung durch die Admins (validieren / ablehnen / offen lassen), Responsables, Tarife.</summary>
    private async Task SchulenRegistrierenAsync()
    {
        var z = new Zufall(Startwert);
        var reg = api.Registrierung;
        for (var i = 0; i < Profile.Length; i++)
        {
            var p = Profile[i];
            var (vor, g) = SimNamen.Vorname(z);
            var nach = SimNamen.Nachname(z);
            var pw = "Direction" + Guid.NewGuid().ToString("N")[..8] + "4";
            var ergebnis = await reg.RegistrierenAsync(new RegistrierungsEingabe(RegistrierungsArt.Direction, vor, nach, null,
                SimNamen.Mail(vor, nach, i), SimNamen.Telefon(z), pw, pw, true,
                p.Name, p.Ort, "Togo"), $"10.0.{i}.1");
            var schule = new SimSchule(i + 1, p);
            (schule.Id, schule.Direktor) = await d.MitDbAsync(async db =>
            {
                var k = await db.Users.Where(k => k.UserName == ergebnis.Identifiant).Select(k => new { k.Id, k.SchuleId }).SingleAsync();
                return (k.SchuleId!.Value, k.Id);
            });
            _ = g;
            Schulen.Add(schule);
        }
        Zaehle("Schulen registriert", Profile.Length);

        // Zwei weitere Anmeldungen: eine wird abgelehnt, eine bleibt in Prüfung.
        foreach (var (name, ort, nr) in new[] { ("Académie Excellence Plus", "Vogan", 90), ("Cours du soir L'Espoir", "Vogan", 91) })
        {
            var e = await reg.RegistrierenAsync(new RegistrierungsEingabe(RegistrierungsArt.Direction, "Komlan", "Folly", null,
                SimNamen.Mail("Komlan", "Folly", nr), "+228 99 00 00 " + nr, "Direction" + nr + "abc", "Direction" + nr + "abc", true, name, ort, "Togo"), $"10.9.{nr}.1");
            var id = await d.MitDbAsync(db => db.Users.Where(k => k.UserName == e.Identifiant).Select(k => k.SchuleId!.Value).SingleAsync());
            if (nr == 90) AbgelehnteSchule = id; else WartendeSchule = id;
        }

        var verwaltung = api.Schulen;
        for (var i = 0; i < Schulen.Count; i++)
        {
            var s = Schulen[i];
            await verwaltung.ValidierenAsync(AdminFuer(i), s.Id);
            if (i % 8 != 7)
            {
                s.Responsable = Responsables[i % Responsables.Count];
                await verwaltung.ResponsableZuweisenAsync(AdminFuer(i + 1), s.Id, s.Responsable);
            }
            if (s.P.Preis is not null || s.P.Prozent is not null)
                await api.Tarife.SchulTarifSpeichernAsync(Admin, s.Id, s.P.Preis, s.P.Prozent);
        }
        await verwaltung.AblehnenAsync(Admin2, AbgelehnteSchule, "Dossier incomplet : autorisation d'ouverture manquante.");
        await AbgelehntAsync("Responsable validiert Schule", () => api.Schulen.ValidierenAsync(Responsables[0], WartendeSchule));
        await AbgelehntAsync("Abgelehnte Schule erneut ablehnen", () => api.Schulen.AblehnenAsync(Admin, AbgelehnteSchule, "Doublon du dossier."), nurKeinZugriff: false);
    }

    /// <summary>Datumsgebundene Ereignisse der Plattform (Suspendierung, Preisänderung).</summary>
    private async Task SonderereignisseAsync(DateOnly tag)
    {
        var palmiers = Schulen.First(s => s.P.Name == "Collège Les Palmiers");
        if (tag == new DateOnly(2025, 1, 15))
        {
            await SollAsync("Suspendieren", () => api.Schulen.SuspendierenAsync(Admin, palmiers.Id, "Factures SKOULOU impayées depuis plusieurs mois."));
            palmiers.Suspendiert = true;
            await AbgelehntAsync("Zahlung bei suspendierter Schule", () => api.Zahlungen.ErfassenAsync(palmiers.Direktor, palmiers.Jahr.Id,
                new ZahlungEingabe(Guid.NewGuid(), palmiers.Aktive.First().Id, 1_000, tag, ZahlungsKanal.Especes, null, null)), nurKeinZugriff: false);
            Notiere("Collège Les Palmiers suspendu.");
        }
        if (tag == new DateOnly(2025, 3, 1))
        {
            await SollAsync("Reaktivieren", () => api.Schulen.ReaktivierenAsync(Admin2, palmiers.Id));
            palmiers.Suspendiert = false;
            Notiere("Collège Les Palmiers réactivé.");
        }
        if (tag == new DateOnly(2025, 6, 15))
            await SollAsync("Tarif 13.000", () => api.Tarife.EinstellungSpeichernAsync(Admin3, new TarifEinstellung(13_000, 30, 10)));
    }
}
