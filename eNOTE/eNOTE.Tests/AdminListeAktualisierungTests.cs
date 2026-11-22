using ENote.WebApp.Components.Pages;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Normaltest 21.11.2026 + Nutzerentscheidung 22.11.: Die Admin-Liste « Établissements » zeigte neue Registrierungen und
/// Zahlungsmeldungen erst nach F5. Die Seite lädt jetzt alle 30 s still neu und beendet den Takt beim Verlassen.
/// </summary>
public class AdminListeAktualisierungTests
{
    [Fact]
    public void Admin_Seite_Aktualisiert_Im_Takt_Und_Raeumt_Auf()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), Admin.AktualisierungsTakt);
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(Admin)));
    }
}
