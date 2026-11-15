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
/// Simulation (Bauplan §9.4, S3): ≥ 30 Schulen, vier vollständige Schuljahre (2022-2023 … 2025-2026) und das laufende
/// Jahr 2026-2027 bis heute (14/11/2026) – alles über die echten Dienste. Die Uhr ist global; je Stichtag arbeiten die
/// Schulen parallel (Zufall je Schule und Stichtag mit festem Startwert), danach laufen Abrechnung und Rechnungswege
/// nacheinander. Gleichzeitige Kontoanlagen werden gezielt ausgelöst (die Simulation wiederholt nichts selbst).
/// </summary>
internal sealed partial class Simulation(SimDienste d, double faktor)
{
    public const int Startwert = 20_261_115;
    public const int ErstesJahr = 2022;
    public static readonly DateOnly Ende = new(2026, 11, 14);
    private const int Parallel = 8;

    private readonly SimApi api = new(d);
    private readonly Stopwatch uhr = Stopwatch.StartNew();

    public List<SimSchule> Schulen { get; } = [];
    /// <summary>Admin = Hauptadministrator (Start-Seed); Admin2/Admin3 legt er an; Admin4 wird gesperrt, entsperrt, archiviert; von zwei gleichzeitig angelegten bleibt AdminZwilling, AdminDoppelt wird archiviert.</summary>
    public Guid Admin, Admin2, Admin3, Admin4, Admin5, AdminZwilling, AdminDoppelt;
    public List<Guid> Responsables { get; } = [];
    public List<SimRechnung> Rechnungen { get; } = [];
    public ConcurrentQueue<string> Fehler { get; } = new();
    public ConcurrentDictionary<string, int> Zaehler { get; } = new();
    public List<string> Ablauf { get; } = [];
    public Guid AbgelehnteSchule, WartendeSchule;

    private Guid AdminFuer(int i) => (i % 3) switch { 0 => Admin, 1 => Admin2, _ => Admin3 };

    private readonly ConcurrentDictionary<Guid, byte> angemeldet = new();

    /// <summary>Erste Anmeldung eines Kontos: Pflichtschritt prüfen (seit E7b kein Bedingungsdialog mehr), einmal je Konto.</summary>
    private async Task ErsteAnmeldungAsync(Guid konto)
    {
        if (!angemeldet.TryAdd(konto, 0)) return;
        await d.Hole<KontoService>().KontoStandAsync(konto);
    }

    public void Zaehle(string was, int anzahl = 1) => Zaehler.AddOrUpdate(was, anzahl, (_, alt) => alt + anzahl);

    public void Notiere(string text)
    {
        var zeile = $"[{uhr.Elapsed:hh\\:mm\\:ss}] {d.HeuteDatum:dd/MM/yyyy} {text}";
        lock (Ablauf)
        {
            Ablauf.Add(zeile);
            var ordner = Path.Combine(Path.GetTempPath(), "sim", "skoulou");
            Directory.CreateDirectory(ordner);
            File.AppendAllText(Path.Combine(ordner, "simulation-fortschritt.txt"), zeile + Environment.NewLine);
        }
    }

    /// <summary>Erwartet Erfolg; ein fachlicher Fehler wird als Befund protokolliert (Test schlägt am Ende fehl).</summary>
    private async Task<T?> SollAsync<T>(string was, Func<Task<T>> aktion)
    {
        try
        {
            // Gleichzeitige Anlage desselben Identifiants in zwei Schulen wiederholen die Dienste selbst (E7b);
            // eine verbleibende Meldung « enregistrement simultané » ist ein Befund.
            return await aktion();
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
        d.Heute(new DateOnly(ErstesJahr, 6, 1));
        await PlattformAsync();
        await SchulenRegistrierenAsync();
        Notiere($"Plattform: {Schulen.Count} Schulen aktiv.");

        d.Heute(new DateOnly(ErstesJahr, 7, 4));
        await JeSchuleAsync("einrichten", EinrichtenAsync, 0);
        Notiere($"Eingerichtet: {Schulen.Sum(s => s.Schueler.Count)} Schüler.");

        for (var startJahr = ErstesJahr; startJahr <= Ende.Year; startJahr++)
        {
            d.Heute(new DateOnly(startJahr, 8, 25));
            await GleichzeitigeAnlagenAsync(startJahr);
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
        var laufend = startJahr == Ende.Year;
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
        var haupt = await d.MitDbAsync(db => db.Users.Where(k => k.IstHauptAdmin).Select(k => k.Id).ToListAsync());
        if (haupt.Count != 1 || haupt[0] != Admin) Fehler.Enqueue($"Start-Seed: {haupt.Count} Hauptadministrator(en) statt des Seed-Kontos.");

        // Der Hauptadministrator legt die weiteren Administratoren an (/admin/administrateurs); zwei gleichnamige gleichzeitig.
        Admin2 = await AdminAnlegenAsync("Akouvi", "Lawson");
        Admin3 = await AdminAnlegenAsync("Yao", "Mensah");
        Admin4 = await AdminAnlegenAsync("Sélom", "Ahadji");
        var doppelt = await Task.WhenAll(AdminAnlegenAsync("Kossi", "Amouzou"), AdminAnlegenAsync("Kossi", "Amouzou"));
        (AdminZwilling, AdminDoppelt) = (doppelt[0], doppelt[1]);
        await KontenVerschiedenAsync("Administrateurs gleichzeitig", doppelt);
        foreach (var a in new[] { Admin, Admin2, Admin3, Admin4 }) await ErsteAnmeldungAsync(a);
        await AdminVerwaltungAbgelehntAsync();

        var tarife = api.Tarife;
        await tarife.EinstellungSpeichernAsync(Admin, new TarifEinstellung(12_500, 30, 10));
        foreach (var (vor, nach) in new[] { ("Edem", "Kpodar"), ("Mawuena", "Akakpo"), ("Ibrahim", "Bouraïma") })
        {
            var zugang = await api.AdminKonten.ResponsableAnlegenAsync(Admin, new ResponsableEingabe(vor, nach, null,
                SimNamen.Mail(vor, nach, 0).Replace("@", ".resp@"), "+228 90 11 22 33"));
            Responsables.Add(zugang.KontoId);
            await ErsteAnmeldungAsync(zugang.KontoId);
        }

        Zaehle("Responsables", Responsables.Count);
    }

    /// <summary>« Ajouter un administrateur » durch den Hauptadministrator (Startpasswort wird nicht gespeichert).</summary>
    private async Task<Guid> AdminAnlegenAsync(string vorname, string nachname)
    {
        var mail = $"admin.{Guid.NewGuid():N}"[..18] + "@skoulou-sim.tg";
        var zugang = await SollAsync($"Administrateur {vorname} {nachname} anlegen", () => api.Administratoren.AnlegenAsync(Admin, new AdminEingabe(vorname, nachname, mail)))
                     ?? throw new InvalidOperationException($"Administrateur {vorname} {nachname} nicht angelegt.");
        Zaehle("Admins");
        return zugang.KontoId;
    }

    /// <summary>Gleichzeitig angelegte Konten haben verschiedene Identifiants (keine Dubletten, keine Meldung « simultané »).</summary>
    private async Task KontenVerschiedenAsync(string was, IReadOnlyCollection<Guid> ids)
    {
        var liste = ids.Where(i => i != Guid.Empty).ToList();
        var namen = await d.MitDbAsync(db => db.Users.Where(k => liste.Contains(k.Id)).Select(k => k.UserName!).ToListAsync());
        if (namen.Count != ids.Count || namen.Distinct(StringComparer.OrdinalIgnoreCase).Count() != namen.Count)
            Fehler.Enqueue($"{d.HeuteDatum:dd/MM/yyyy} {was}: {ids.Count} gleichzeitig, {namen.Count} angelegt, Identifiants {string.Join(", ", namen)}.");
        Zaehle("Gleichzeitige Anlagen", namen.Count);
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
        new("Institut de langues Le Pont", "Lomé", SchulTyp.Langues, PeriodenArt.Session, 1, 10, 18, Preis: 8_000, Gebuehr: SessionGebuehr.JeSession),
        new("Centre de langues Horizon", "Kara", SchulTyp.Langues, PeriodenArt.Session, 1, 8, 15, Preis: 8_000),
        // S3: acht weitere Schulen (zwei je Typ), darunter zwei Sprachschulen mit Gebühr je Session.
        new("EPP Hédzranawoé", "Lomé", SchulTyp.Primaire, PeriodenArt.Trimestre, 2, 28, 40),
        new("École primaire Saint-Michel", "Sokodé", SchulTyp.Primaire, PeriodenArt.Trimestre, 1, 20, 34),
        new("CEG Bassar", "Bassar", SchulTyp.College, PeriodenArt.Semestre, 2, 30, 45),
        new("Collège moderne de Tabligbo", "Tabligbo", SchulTyp.College, PeriodenArt.Trimestre, 2, 30, 45, Prozent: 12),
        new("Lycée d'Atakpamé", "Atakpamé", SchulTyp.Lycee, PeriodenArt.Trimestre, 2, 35, 50),
        new("Lycée technique de Lomé", "Lomé", SchulTyp.Lycee, PeriodenArt.Semestre, 2, 30, 45),
        new("École de langues Arc-en-ciel", "Lomé", SchulTyp.Langues, PeriodenArt.Session, 1, 10, 16, Preis: 8_000, Gebuehr: SessionGebuehr.GleicheBezeichnung),
        new("Centre linguistique du Sahel", "Dapaong", SchulTyp.Langues, PeriodenArt.Session, 1, 8, 14, Preis: 7_500, Gebuehr: SessionGebuehr.JeSession),
    ];

    /// <summary>
    /// Selbstregistrierung der Direktionen (je vier gleichzeitig, davon zwei mit gleichem Namen), Prüfung durch die Admins
    /// (validieren / ablehnen / offen lassen), Responsables, Tarife.
    /// </summary>
    private async Task SchulenRegistrierenAsync()
    {
        var z = new Zufall(Startwert);
        for (var start = 0; start < Profile.Length; start += 4)
        {
            var gruppe = Enumerable.Range(start, Math.Min(4, Profile.Length - start)).ToList();
            var namen = gruppe.Select(_ => (Vor: SimNamen.Vorname(z).Vorname, Nach: SimNamen.Nachname(z), Tel: SimNamen.Telefon(z))).ToList();
            if (namen.Count > 1) namen[1] = namen[0] with { Tel = SimNamen.Telefon(z) };
            var ergebnisse = await Task.WhenAll(gruppe.Select((i, n) => SollAsync($"Registrierung {Profile[i].Name}", async () =>
            {
                var p = Profile[i];
                var pw = "Direction" + Guid.NewGuid().ToString("N")[..8] + "4";
                var e = await api.Registrierung.RegistrierenAsync(new RegistrierungsEingabe(RegistrierungsArt.Direction, namen[n].Vor, namen[n].Nach, null,
                    SimNamen.Mail(namen[n].Vor, namen[n].Nach, i), namen[n].Tel, pw, pw, true, p.Name, p.Ort, "Togo"), $"10.0.{i}.1");
                return await d.MitDbAsync(db => db.Users.Where(k => k.UserName == e.Identifiant).Select(k => new { k.Id, SchuleId = k.SchuleId!.Value }).SingleAsync());
            })));
            for (var n = 0; n < gruppe.Count; n++)
            {
                var r = ergebnisse[n] ?? throw new InvalidOperationException($"Registrierung {Profile[gruppe[n]].Name} fehlgeschlagen.");
                Schulen.Add(new SimSchule(gruppe[n] + 1, Profile[gruppe[n]]) { Id = r.SchuleId, Direktor = r.Id });
            }
            await KontenVerschiedenAsync("Registrierungen gleichzeitig", ergebnisse.Select(r => r?.Id ?? Guid.Empty).ToList());
        }
        Zaehle("Schulen registriert", Profile.Length);

        // Zwei weitere Anmeldungen: eine wird abgelehnt, eine bleibt in Prüfung.
        foreach (var (name, ort, nr) in new[] { ("Académie Excellence Plus", "Vogan", 90), ("Cours du soir L'Espoir", "Vogan", 91) })
        {
            var e = await api.Registrierung.RegistrierenAsync(new RegistrierungsEingabe(RegistrierungsArt.Direction, "Komlan", "Folly", null,
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
        await AdminEreignisseAsync(tag);
    }
}
