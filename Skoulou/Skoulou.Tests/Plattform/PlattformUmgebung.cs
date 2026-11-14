using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.Tests.Organisation;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Medien;
using Skoulou.WebApp.Services.Organisation;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Plattform;

/// <summary>Dienste des Bereichs E2 auf der InMemory-Testumgebung (Identity, Uhr, Outbox wie in der App).</summary>
internal sealed class PlattformUmgebung : IAsyncDisposable
{
    public TestUmgebung Basis { get; } = new();
    public SpeicherAttrappe Speicher { get; } = new();
    public RegistrierungsBremse Bremse { get; }
    public RegistrierungService Registrierung { get; }
    public SchulVerwaltungService Schulen { get; }
    public KontenVerwaltungService Konten { get; }
    public AnkuendigungService Annonces { get; }
    public MessagerieService Messagerie { get; }
    public ProtokollService Journal { get; }
    public MedienVerwaltungService Medien { get; }
    public PlattformUebersicht Uebersicht { get; }

    public PlattformUmgebung()
    {
        var sp = Basis.Dienste;
        T Neu<T>(params object[] extra) => ActivatorUtilities.CreateInstance<T>(sp, extra);
        var mails = Neu<PlattformMails>();
        var fabrik = Neu<PlattformKontoFabrik>(Neu<KontoFabrik>());
        Bremse = new RegistrierungsBremse(Basis.Zeit);
        Registrierung = Neu<RegistrierungService>(fabrik, mails, Bremse);
        Schulen = Neu<SchulVerwaltungService>(mails);
        Konten = Neu<KontenVerwaltungService>(fabrik, mails);
        Annonces = new AnkuendigungService(Basis.Db, sp.GetRequiredService<Zugriff>(), sp.GetRequiredService<IMemoryCache>(), Basis.Zeit);
        Messagerie = Neu<MessagerieService>(mails);
        Journal = Neu<ProtokollService>();
        Medien = Neu<MedienVerwaltungService>((IMedienSpeicher)Speicher);
        Uebersicht = Neu<PlattformUebersicht>();
    }

    public IDbContextFactory<SkoulouDbContext> Db => Basis.Db;
    public TestZeit Zeit => Basis.Zeit;

    /// <summary>Seed (Rollen, Einstellungen) + Admin mit E-Mail.</summary>
    public async Task<Konto> StartAsync()
    {
        await Basis.SeedAsync();
        return await Basis.KontoAsync("admin", Rollen.SuperAdmin, email: "admin@exemple.tg");
    }

    public async Task<List<MailAusgang>> MailsAsync(string an)
    {
        await using var db = await Db.CreateDbContextAsync();
        return await db.MailAusgaenge.Where(m => m.An == an).ToListAsync();
    }

    public async Task<T> LiesAsync<T>(Func<SkoulouDbContext, Task<T>> lesen)
    {
        await using var db = await Db.CreateDbContextAsync();
        return await lesen(db);
    }

    public static RegistrierungsEingabe Direction(string schule = "Collège Horizon", string email = "dir@exemple.tg") => new(
        RegistrierungsArt.Direction, "Kossi", "Mensah", null, email, null, "motdepasse1", "motdepasse1", true, schule, "Lomé", null);

    public static RegistrierungsEingabe Eleve(Guid? schule, string? email = null) => new(
        RegistrierungsArt.Eleve, "Ama", "Dossou", null, email, null, "motdepasse1", "motdepasse1", true, SchuleId: schule,
        Geburtsdatum: new DateOnly(2013, 4, 2), ElternTelefon: "+228 90 00 00 00");

    public async ValueTask DisposeAsync() => await Basis.DisposeAsync();
}
