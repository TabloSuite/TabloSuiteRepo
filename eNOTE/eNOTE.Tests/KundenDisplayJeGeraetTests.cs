using ENote.WebApp.Services;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// 17.11.2026: Kunden-Display je Kassengerät. Kassieren zwei Geräte gleichzeitig in derselben Kasse, darf das Display des einen
/// nie das Ticket des anderen zeigen.
/// </summary>
public class KundenDisplayJeGeraetTests
{
    private sealed class Proxy(string gruppe, List<(string Gruppe, string Methode, object?[] Args)> log) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            log.Add((gruppe, method, args));
            return Task.CompletedTask;
        }
    }

    private sealed class HubClients(List<(string Gruppe, string Methode, object?[] Args)> log) : IHubClients
    {
        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => new Proxy(groupName, log);
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class HubKontext(List<(string Gruppe, string Methode, object?[] Args)> log) : IHubContext<UmsatzHub>
    {
        public IHubClients Clients { get; } = new HubClients(log);
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private static DisplayTicket Ticket(string artikel, decimal betrag)
        => new([new DisplayPosition(artikel, 1, betrag)], betrag, 0m, betrag, Abgeschlossen: false, Rueckgeld: null);

    [Fact]
    public async Task Jedes_Geraet_Sendet_Nur_An_Sein_Display()
    {
        var log = new List<(string Gruppe, string Methode, object?[] Args)>();
        var dienst = new DisplayBenachrichtigungService(new HubKontext(log));
        var tablet = Guid.NewGuid().ToString("N");
        var handy = Guid.NewGuid().ToString("N");

        await dienst.TicketAnGeraetAsync(tablet, Ticket("Coupe homme", 2500));
        await dienst.TicketAnGeraetAsync(handy, Ticket("Box braids", 20000));

        Assert.Equal(2, log.Count);
        Assert.Equal(UmsatzHub.GeraetDisplayGruppenName(tablet), log[0].Gruppe);
        Assert.Equal(UmsatzHub.GeraetDisplayGruppenName(handy), log[1].Gruppe);
        Assert.NotEqual(log[0].Gruppe, log[1].Gruppe);
        Assert.Equal("DisplayAktualisiert", log[0].Methode);
        Assert.Equal("Coupe homme", ((DisplayTicket)log[0].Args[0]!).Positionen.Single().Name);
        Assert.Equal("Box braids", ((DisplayTicket)log[1].Args[0]!).Positionen.Single().Name);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("abc", false)]
    [InlineData("0123456789abcdef0123456789ABCDEF", true)]
    [InlineData("0123456789abcdef0123456789abcdeg", false)]   // g ist kein Hex
    [InlineData("0123456789abcdef0123456789abcdef0", false)]  // 33 Zeichen
    [InlineData("display-x/../../y", false)]
    public void Nur_Echte_Geraeteschluessel_Werden_Angenommen(string? schluessel, bool gueltig)
        => Assert.Equal(gueltig, UmsatzHub.IstGeraetSchluessel(schluessel));

    [Fact]
    public async Task Ungueltiger_Schluessel_Sendet_Nichts_Und_Gruppen_Sind_Unabhaengig_Von_Gross_Klein()
    {
        var log = new List<(string Gruppe, string Methode, object?[] Args)>();
        var dienst = new DisplayBenachrichtigungService(new HubKontext(log));
        await dienst.TicketAnGeraetAsync("kein-schluessel", Ticket("X", 1));
        Assert.Empty(log);

        var schluessel = Guid.NewGuid().ToString("N");
        Assert.Equal(UmsatzHub.GeraetDisplayGruppenName(schluessel), UmsatzHub.GeraetDisplayGruppenName(schluessel.ToUpperInvariant()));
        // Getrennt von den alten Gruppen je Kassensitzung.
        Assert.NotEqual(UmsatzHub.DisplayGruppenName(schluessel), UmsatzHub.GeraetDisplayGruppenName(schluessel));
    }
}
