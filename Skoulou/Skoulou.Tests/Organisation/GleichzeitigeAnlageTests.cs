using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.Tests.Sql;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.Tests.Organisation;

/// <summary>
/// E7b-Zusatz 4 (nur mit SKOULOU_SQL_TESTS=1): legt eine andere Schule im selben Moment eine Person mit demselben Namen an,
/// wiederholt die Kontoanlage automatisch mit dem nächsten Identifiant-Vorschlag (höchstens drei Versuche).
/// </summary>
public class GleichzeitigeAnlageTests(SqlDatenbank datenbank) : IClassFixture<SqlDatenbank>
{
    private static Konto Konto(string identifiant) => new()
    {
        Id = NeueId.Erzeuge(), UserName = identifiant, NormalizedUserName = identifiant.ToUpperInvariant(), Vorname = "Ama", Nachname = "Mensah",
        Rolle = Rollen.Lehrer, SecurityStamp = "x"
    };

    private static Task<string> VorschlagAsync(SkoulouDbContext db, string nachname)
        => IdentifiantRegel.VorschlagAsync("Ama", nachname, async k => await db.Users.AnyAsync(u => u.NormalizedUserName == k.ToUpperInvariant()));

    /// <summary>Arbeit wie in den Diensten: Vorschlag rechnen, anlegen; vor den ersten <paramref name="stoerungen"/> Speicherungen kommt « eine andere Schule » zuvor.</summary>
    private async Task<(string Identifiant, int Laeufe)> AnlegenAsync(string nachname, int stoerungen, int versuche)
    {
        var laeufe = 0;
        var identifiant = await new SqlFabrik(datenbank).TransaktionAsync(async db =>
        {
            laeufe++;
            var vorschlag = await VorschlagAsync(db, nachname);
            if (laeufe <= stoerungen)
            {
                await using var andere = datenbank.Kontext();
                andere.Users.Add(Konto(vorschlag));
                await andere.SaveChangesAsync();
            }
            db.Users.Add(Konto(vorschlag));
            await db.SaveChangesAsync();
            return vorschlag;
        }, versuche: versuche);
        return (identifiant, laeufe);
    }

    [SqlFact]
    public async Task Gleichzeitige_Anlage_nimmt_den_naechsten_Vorschlag()
    {
        var nachname = "Gleich" + Guid.NewGuid().ToString("N")[..6];
        var (identifiant, laeufe) = await AnlegenAsync(nachname, stoerungen: 1, versuche: DbArbeit.KontoVersuche);
        Assert.Equal(2, laeufe);
        Assert.Equal($"ama.{nachname.ToLowerInvariant()}2", identifiant);
    }

    [SqlFact]
    public async Task Nach_drei_Versuchen_kommt_die_Meldung()
    {
        var nachname = "Gleich" + Guid.NewGuid().ToString("N")[..6];
        var fehler = await Assert.ThrowsAsync<FachFehler>(() => AnlegenAsync(nachname, stoerungen: 5, versuche: DbArbeit.KontoVersuche));
        Assert.Equal(DbArbeit.MeldungSimultan, fehler.Message);
        // Ohne Wiederholung (alle anderen Schreibvorgänge) bleibt es bei der Meldung nach dem ersten Versuch.
        await Assert.ThrowsAsync<FachFehler>(() => AnlegenAsync("Gleich" + Guid.NewGuid().ToString("N")[..6], stoerungen: 1, versuche: 1));
    }
}
