using System.Security.Claims;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Fehlersuche 21.11.2026: Der Hub « /hubs/umsatz » ist ohne Anmeldung erreichbar (Kunden-Display), und die SalonId steht im
/// Display-Link. Mit « AbonniereSalon(SalonId) » empfing jeder Tagesumsatz und Anzahl Verkäufe eines fremden Ladens.
/// Jetzt nur noch mit dem geheimen Salon-Schlüssel, den nur der Server kennt.
/// </summary>
public class UmsatzHubSchluesselTests
{
    private sealed class Gruppen(List<string> log) : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) { log.Add("+" + groupName); return Task.CompletedTask; }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) { log.Add("-" + groupName); return Task.CompletedTask; }
    }

    private sealed class Aufrufer : HubCallerContext
    {
        public override string ConnectionId => "verbindung-1";
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => new(new ClaimsIdentity());
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class Proxy(string gruppe, List<string> log) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) { log.Add(gruppe); return Task.CompletedTask; }
    }

    private sealed class Clients(List<string> log) : IHubClients
    {
        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        IClientProxy IHubClients<IClientProxy>.Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => new Proxy(groupName, log);
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class Kontext(List<string> log) : IHubContext<UmsatzHub>
    {
        public IHubClients Clients { get; } = new Clients(log);
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private static UmsatzHub Hub(List<string> log) => new() { Groups = new Gruppen(log), Context = new Aufrufer() };

    [Fact]
    public async Task Abonnieren_Mit_Der_SalonId_Wird_Ignoriert()
    {
        var log = new List<string>();
        var salonId = Guid.NewGuid();
        await Hub(log).AbonniereSalon(salonId.ToString());
        await Hub(log).AbonniereSalon(salonId.ToString("N"));
        await Hub(log).AbonniereSalon("");
        Assert.Empty(log);
    }

    [Fact]
    public async Task Verkauf_Geht_Nur_An_Die_Gruppe_Des_Geheimen_Schluessels()
    {
        var gesendet = new List<string>();
        var salonId = Guid.NewGuid();
        await new UmsatzBenachrichtigungService(new Kontext(gesendet)).VerkaufGemeldetAsync(salonId, 5000m, 2);

        var abonniert = new List<string>();
        await Hub(abonniert).AbonniereSalon(UmsatzHub.SalonSchluessel(salonId));

        Assert.Single(gesendet);
        Assert.Equal("+" + gesendet[0], Assert.Single(abonniert));
        Assert.DoesNotContain(salonId.ToString(), gesendet[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(salonId.ToString("N"), gesendet[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Schluessel_Ist_Stabil_Und_Je_Laden_Verschieden()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.Equal(UmsatzHub.SalonSchluessel(a), UmsatzHub.SalonSchluessel(a));
        Assert.NotEqual(UmsatzHub.SalonSchluessel(a), UmsatzHub.SalonSchluessel(b));
        Assert.True(UmsatzHub.IstSalonSchluessel(UmsatzHub.SalonSchluessel(a)));
        Assert.False(UmsatzHub.IstSalonSchluessel(a.ToString()));
    }
}
