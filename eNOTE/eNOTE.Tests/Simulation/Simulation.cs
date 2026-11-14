using System.Collections.Concurrent;
using System.Diagnostics;
using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.WebApp.Data.Seed;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ENote.Tests.Simulation;

internal sealed class SimMitarbeiter
{
    public Guid Id;
    public Guid UserSalonId;
    public string Pin = "";
    public string Name = "";
    public bool Erstatten;
}

internal sealed class SimArtikel
{
    public Guid Id;
    public string Name = "";
    public decimal Preis;
    public bool Produkt;
    public Guid? BasisId;
    public decimal Abzug = 1;
    public bool Favorit;
    public bool Verkaufbar = true;
}

internal sealed record SimRabatt(Guid Id, ENote.Core.DataModels.Katalog.RabattArt Art, decimal Wert, Guid? LeistungId, bool Bedingung);

internal sealed record SimVerkauf(Guid Id, DateTime ZeitUtc, Guid? KundeId);

internal sealed class SimLaden
{
    public int Nr;
    public required LadenVorlage V;
    public string Name = "";
    public string Ort = "";
    public string? Land;
    public int Kanal; // 0 Selbstregistrierung, 1 Admin, 2 Responsable, 3 zweiter Laden
    public DateOnly Start;
    public Guid Id;
    public Guid Besitzer;
    public string BesitzerPseudonym = "";
    public string BesitzerPasswort = "";
    public string BesitzerPin = "";
    public Guid BesitzerUserSalon;
    public Guid? Responsable;
    public int Zahler; // 0 pünktlich, 1 spät (Kassensperre), 2 einmal abgelehnt
    public bool Freigegeben, Abgelehnt, Wartend, Deaktiviert, Geloescht, Eingerichtet;
    public DateTime? GekuendigtZum;
    public List<SimMitarbeiter> Team { get; } = [];
    public List<SimArtikel> Katalog { get; } = [];
    public Guid DiversId;
    public List<SimRabatt> Rabatte { get; } = [];
    public List<Guid> Kunden { get; } = [];
    public List<Guid> AusgabenKategorien { get; } = [];
    public List<SimVerkauf> LetzteVerkaeufe { get; } = [];
    public Dictionary<Guid, decimal> Lager { get; } = [];
    public Dictionary<Guid, (Guid BesuchId, int Tage)> Patienten { get; } = [];
    public Dictionary<Guid, decimal> KundenUmsatz { get; } = [];
    public Dictionary<(int, int), decimal> MonatsUmsatz { get; } = [];
    public decimal Umsatz, Erstattet;
    public int Verkaeufe, Erstattungen, KasseGesperrt, Sitzungen, Differenzen;
    public int Telefon;

    public bool Betrieb => Eingerichtet && Freigegeben && !Deaktiviert && !Geloescht && !Abgelehnt;
    public TimeSpan Versatz => Land is "Bénin" or "Cameroun" ? TimeSpan.FromHours(1) : TimeSpan.Zero;
}

/// <summary>
/// eNOTE-Daten-Simulation: Plattform (Hauptadmin, Admins, Responsables), ≥ 30 Läden verschiedener Art in sieben Ländern,
/// ≥ 18 Monate täglicher Betrieb über die echten Dienste, Abrechnung, Provisionen, Kündigungen. Die Uhr ist global; je
/// Tag arbeiten die Läden parallel mit eigener Ortszeit (AsyncLocal), danach laufen Plattform und Abrechnung nacheinander.
/// </summary>
internal sealed partial class Simulation(SimDienste d, double faktor)
{
    public const int Startwert = 20_261_114;
    public static readonly DateOnly Beginn = new(2025, 3, 3);
    public static readonly DateOnly Ende = new(2026, 11, 13);
    private const int Parallel = 8;

    private readonly Stopwatch stoppuhr = Stopwatch.StartNew();
    public List<SimLaden> Laeden { get; } = [];
    public Guid Admin, Admin2, Admin3;
    public List<Guid> Responsables { get; } = [];
    public ConcurrentQueue<string> Fehler { get; } = new();
    public ConcurrentDictionary<string, int> Zaehler { get; } = new();
    public List<string> Ablauf { get; } = [];
    public List<string> Geprueft { get; } = [];
    public List<(string Liste, double Ms)> Zeiten { get; } = [];

    /// <summary>Preisgeschichte aus Sicht der Simulation: (ab, globaler Monatspreis) und je Laden individuelle Stände.</summary>
    public List<(DateTime Ab, decimal Preis)> GlobalPreise { get; } = [(DateTime.MinValue, 3600m)];
    public Dictionary<Guid, List<(DateTime Ab, decimal? Preis)>> IndividuellePreise { get; } = [];

    private SimUhr Uhr => d.Uhr;
    private DateOnly Heute => DateOnly.FromDateTime(Uhr.Jetzt);
    private static string Passwort(int nr) => $"sim{nr:000}pass";

    public void Zaehle(string was, int anzahl = 1) => Zaehler.AddOrUpdate(was, anzahl, (_, alt) => alt + anzahl);

    public void Notiere(string text)
    {
        var zeile = $"[{stoppuhr.Elapsed:hh\\:mm\\:ss}] {Uhr.Global:dd/MM/yyyy} {text}";
        lock (Ablauf)
        {
            Ablauf.Add(zeile);
            var ordner = Path.Combine(Path.GetTempPath(), "sim", "enote");
            Directory.CreateDirectory(ordner);
            File.AppendAllText(Path.Combine(ordner, "simulation-fortschritt.txt"), zeile + Environment.NewLine);
        }
    }

    public void Befund(string text) => Fehler.Enqueue($"{Uhr.Jetzt:dd/MM/yyyy HH:mm} {text}");

    /// <summary>Erwartet Erfolg; eine Ausnahme ist ein Befund.</summary>
    private async Task<T?> SollAsync<T>(string was, Func<Task<T>> aktion)
    {
        try { return await aktion(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Befund($"{was}: {ex.GetType().Name}: {ex.Message}");
            return default;
        }
    }

    private Task SollAsync(string was, Func<Task> aktion) => SollAsync(was, async () => { await aktion(); return true; });

    /// <summary>Erwartet « refusé » (UnauthorizedAccessException); Erfolg ist ein Befund.</summary>
    private async Task VerweigertAsync(string was, Func<Task> aktion)
    {
        try
        {
            await aktion();
            Befund($"NICHT verweigert: {was}");
        }
        catch (UnauthorizedAccessException) { Zaehle("Rechte: refusé"); }
        catch (Exception ex) { Befund($"{was}: {ex.GetType().Name} statt « refusé » : {ex.Message}"); }
    }

    private async Task JeLadenAsync(string phase, Func<SimLaden, Zufall, Task> arbeit, IEnumerable<SimLaden> laeden)
    {
        var tag = Heute.DayNumber;
        await System.Threading.Tasks.Parallel.ForEachAsync(laeden, new ParallelOptions { MaxDegreeOfParallelism = Parallel },
            async (l, _) =>
            {
                try { await arbeit(l, new Zufall(Zufall.Kombiniere(Startwert, l.Nr, tag, phase.Length))); }
                catch (Exception ex) { Befund($"{phase} {l.Name}: {ex.GetType().Name}: {ex.Message}"); }
            });
    }

    private void Global(DateOnly tag, int stunde, int minute = 0) =>
        Uhr.Global = new DateTime(tag.Year, tag.Month, tag.Day, stunde, minute, 0, DateTimeKind.Utc);

    // ------------------------------------------------------------------ Ablauf

    public async Task AusfuehrenAsync()
    {
        Global(Beginn.AddDays(-2), 9);
        await PlattformAsync();
        PlaneLaeden();
        Notiere($"Plattform bereit: {Laeden.Count} Läden geplant, {Responsables.Count} Responsables.");

        var letzterMonat = 0;
        for (var tag = Beginn; tag <= Ende; tag = tag.AddDays(1))
        {
            // Nachts: Dienste wie VertragsSynchronisierungsDienst / MailVersandDienst.
            Global(tag, 5);
            await NachtAsync(tag);

            // Vormittags: Registrierungen, Freigaben, Einrichtung neuer Läden.
            Global(tag, 9);
            await RegistrierungenAsync(tag);

            // Tagesbetrieb aller Läden parallel (je Laden eigene Ortszeit).
            var offen = Laeden.Where(l => l.Betrieb && (l.V.Sonntag || tag.DayOfWeek != DayOfWeek.Sunday)).ToList();
            await JeLadenAsync("tag", (l, z) => LadenTagAsync(l, tag, z), offen);

            // Abends: Abrechnung, Plattform-Ereignisse, Rechte-Stichproben.
            Global(tag, 20, 30);
            await AbrechnungAsync(tag);
            await EreignisseAsync(tag);
            if (tag.Day == 5) await ProvisionenAsync(tag);
            if (tag.Day == 20) await RechteAsync(tag);

            if (tag.Month != letzterMonat)
            {
                letzterMonat = tag.Month;
                Notiere($"Monat {tag:MM/yyyy}: {Laeden.Count(l => l.Betrieb)} Läden im Betrieb, Verkäufe {Laeden.Sum(l => l.Verkaeufe):N0}, Befunde {Fehler.Count}.");
            }
        }
        Global(Ende, 21);
        Notiere($"Ablauf fertig: Verkäufe {Laeden.Sum(l => l.Verkaeufe):N0}, Erstattungen {Laeden.Sum(l => l.Erstattungen):N0}.");
    }

    private async Task NachtAsync(DateOnly tag)
    {
        // Wie der stündliche Hintergrunddienst (hier einmal je Nacht): fällige Tarife, Perioden, Löschungen, Abo-Mails.
        await SollAsync("Tarife anwenden", () => d.Hole<PreisAnpassungService>().WendeFaelligeAnAsync());
        var fehler = await SollAsync("Perioden synchronisieren", () => d.Hole<VertragsService>().SynchronisiereAlleAsync());
        foreach (var (salon, ex) in fehler ?? []) Befund($"Synchronisierung {salon}: {ex.Message}");
        var geloescht = await SollAsync("Löschungen", () => d.Hole<KuendigungService>().FuehreFaelligeLoeschungenAusAsync());
        if (geloescht > 0)
        {
            Zaehle("Läden gelöscht (nach 30 Tagen)", geloescht);
            foreach (var l in Laeden.Where(l => l.Deaktiviert && !l.Geloescht))
                if (await d.MitDbAsync(db => db.Salons.AnyAsync(s => s.Id == l.Id && s.GeloeschtUtc != null)))
                {
                    l.Geloescht = true;
                    Notiere($"{l.Name}: Daten gelöscht (Löschung nach Bedenkzeit).");
                }
        }
        var mails = await SollAsync("Abo-Mails", () => d.Hole<ENote.WebApp.Services.Mails.MailBenachrichtigungen>().PruefeAboMailsAsync());
        Zaehle("Abo-Mails (Outbox)", mails);
    }

    // ------------------------------------------------------------------ Plattform

    private async Task PlattformAsync()
    {
        await using (var scope = d.Dienste.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            await using var db = await d.Db.CreateDbContextAsync();
            await ApplicationDbInitializer.SeedAsync(db, sp.GetRequiredService<RoleManager<Role>>(), sp.GetRequiredService<UserManager<AppUser>>(), d.Konfiguration);
        }
        Admin = await d.MitDbAsync(db => db.Users.Where(u => u.UserName == SimDienste.AdminPseudonym).Select(u => u.Id).SingleAsync());
        Pruefe(await d.Hole<AdminKontenService>().IstErsterAdminAsync(Admin), "Seed-Admin ist Hauptadministrateur");

        // Versandweg prüfen: kein Postmark/SMTP → alles bleibt in der Warteschlange.
        var anbieter = d.Hole<ENote.WebApp.Services.Mails.EmailService>().Anbieter;
        if (anbieter != "Aucun") throw new InvalidOperationException($"Mailversand wäre aktiv ({anbieter}) – Abbruch.");
        Geprueft.Add("Mailversand inaktiv (Anbieter « Aucun »): alle Mails nur in der Warteschlange.");

        var konten = d.Hole<AdminKontenService>();
        await konten.LegeAnAsync(Admin, "Kafui", "Lawson", "kafui.lawson@enote-sim.tg", "Admin2sim1", benutzername: "admin2.sim");
        await konten.LegeAnAsync(Admin, "Délali", "Ekué", "delali.ekue@enote-sim.tg", "Admin3sim1", benutzername: "admin3.sim");
        Admin2 = await KontoAsync("admin2.sim");
        Admin3 = await KontoAsync("admin3.sim");
        await VerweigertAsync("Admin ohne Hauptrecht legt Admin an", () => d.Hole<AdminKontenService>().LegeAnAsync(Admin2, "X", "Y", "x@enote-sim.tg", "Abcdef12", benutzername: "admin4.sim"));

        var po = new[] { ("Komla", "Amouzou"), ("Abla", "Kpodar"), ("Yao", "Kouassi"), ("Fatou", "Ndiaye") };
        for (var i = 0; i < po.Length; i++)
        {
            var (vn, nn) = po[i];
            var r = await d.Hole<AdminService>().LegeProduktOwnerAnAsync(vn, nn, $"resp{i + 1}.sim", $"resp{i + 1}@enote-sim.tg", Passwort(900 + i));
            if (!r.Erfolg) throw new InvalidOperationException("Responsable: " + r.Fehler);
            Responsables.Add(r.ProduktOwner!.Id);
        }
        Zaehle("Admins", 3);
        Zaehle("Responsables", Responsables.Count);
    }

    private Task<Guid> KontoAsync(string pseudonym) =>
        d.MitDbAsync(db => db.Users.Where(u => u.UserName == pseudonym).Select(u => u.Id).SingleAsync());

    public void Pruefe(bool ok, string was)
    {
        if (ok) Zaehle("Prüfungen bestanden");
        else Befund("Prüfung fehlgeschlagen: " + was);
    }

    /// <summary>34 Läden über die ersten zehn Monate verteilt, drei Kanäle, sieben Länder; dazu 2 zweite Läden, 2 Ablehnungen, 1 wartend.</summary>
    private void PlaneLaeden()
    {
        for (var i = 0; i < 34; i++)
        {
            var v = SimNamen.Vorlagen[i % SimNamen.Vorlagen.Length];
            var (ort, land) = SimNamen.Orte[i];
            var kanal = land == "Togo" ? i % 3 : 1 + i % 2; // Selbstregistrierung nur ohne Land (= Togo)
            Laeden.Add(new SimLaden
            {
                Nr = i + 1, V = v, Name = $"{v.Praefix} {ort}", Ort = ort, Land = kanal == 0 ? null : land, Kanal = kanal,
                Start = Beginn.AddDays(i * 9 + i % 4), Zahler = i % 7 == 3 ? 1 : i % 7 == 5 ? 2 : 0,
                BesitzerPin = $"{4000 + i * 37 % 5000:0000}",
            });
        }
    }
}
