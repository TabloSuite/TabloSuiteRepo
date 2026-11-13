namespace Skoulou.WebApp.Data.Seed;

/// <summary>Demo-Daten des Bereichs E6 Finanzen (nach der Grund-Demo-Schule; nur erfundene Daten).</summary>
public static class DemoSeedFinanzen
{
    /// <param name="dienste">Dienste der App (DbContextFactory, UserManager, Bereichsdienste …).</param>
    /// <param name="schuleId">Demo-Schule « Collège Démo Les Palmiers ».</param>
    /// <param name="neueKonten">Neu angelegte Demo-Konten (Passwörter landen nur in demo-konten.json).</param>
    public static Task ErgaenzenAsync(IServiceProvider dienste, Guid schuleId, List<DemoKonto> neueKonten, CancellationToken ct)
        => Task.CompletedTask;
}
