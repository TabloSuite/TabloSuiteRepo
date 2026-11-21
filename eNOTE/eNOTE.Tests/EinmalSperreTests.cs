using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Fehlersuche 21.11.2026: zweimal Enter im Feld « Montant reçu » buchte denselben Verkauf zweimal (Lager −2), ebenso
/// Retrait und Erstattung über Enter im PIN-Feld. Die Kasse führt Buchungen jetzt über <see cref="EinmalSperre"/> aus.
/// </summary>
public class EinmalSperreTests
{
    [Fact]
    public async Task Zweiter_Aufruf_Waehrend_Die_Aktion_Laeuft_Wird_Verworfen()
    {
        var sperre = new EinmalSperre();
        var freigabe = new TaskCompletionSource();
        var buchungen = 0;
        async Task Buche() { buchungen++; await freigabe.Task; }

        var erster = sperre.FuehreAusAsync(Buche);
        var zweiter = sperre.FuehreAusAsync(Buche);   // Doppel-Enter
        var dritter = sperre.FuehreAusAsync(Buche);   // Dreifach-Enter
        Assert.True(sperre.Laeuft);
        freigabe.SetResult();

        Assert.True(await erster);
        Assert.False(await zweiter);
        Assert.False(await dritter);
        Assert.Equal(1, buchungen);
        Assert.False(sperre.Laeuft);
    }

    [Fact]
    public async Task Nach_Ende_Ist_Die_Naechste_Buchung_Wieder_Moeglich()
    {
        var sperre = new EinmalSperre();
        var buchungen = 0;
        Assert.True(await sperre.FuehreAusAsync(() => { buchungen++; return Task.CompletedTask; }));
        Assert.True(await sperre.FuehreAusAsync(() => { buchungen++; return Task.CompletedTask; }));
        Assert.Equal(2, buchungen);
    }

    [Fact]
    public async Task Fehler_In_Der_Aktion_Gibt_Die_Sperre_Wieder_Frei()
    {
        var sperre = new EinmalSperre();
        await Assert.ThrowsAsync<InvalidOperationException>(() => sperre.FuehreAusAsync(() => throw new InvalidOperationException("DB weg")));
        Assert.False(sperre.Laeuft);
        Assert.True(await sperre.FuehreAusAsync(() => Task.CompletedTask));
    }

    [Fact]
    public async Task Gleichzeitige_Aufrufe_Aus_Mehreren_Threads_Buchen_Nur_Einmal()
    {
        var sperre = new EinmalSperre();
        var freigabe = new TaskCompletionSource();
        var buchungen = 0;
        Task Buche() { Interlocked.Increment(ref buchungen); return freigabe.Task; }
        var erster = sperre.FuehreAusAsync(Buche);
        // 19 weitere Aufrufe aus anderen Threads, während die erste Buchung noch läuft
        var weitere = await Task.WhenAll(Enumerable.Range(0, 19).Select(_ => Task.Run(() => sperre.FuehreAusAsync(Buche))));
        freigabe.SetResult();
        Assert.True(await erster);
        Assert.All(weitere, Assert.False);
        Assert.Equal(1, buchungen);
    }
}
