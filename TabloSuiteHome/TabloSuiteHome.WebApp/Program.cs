using TabloSuiteHome.WebApp.Components;
using TabloSuiteHome.WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Firmenangaben und App-Katalog (« TabloSuite » in appsettings.json); beim Start geprüft.
builder.Services.AddOptions<TabloSuiteOptionen>()
    .Bind(builder.Configuration.GetSection(TabloSuiteOptionen.Abschnitt))
    .Validate(o => o.Pruefe().Count == 0, "Catalogue TabloSuite invalide (voir TabloSuiteOptionen.Pruefe).")
    .ValidateOnStart();

// Aktueller Preis der Apps für die Karten (z. B. SKOULOU /tarifs/public), kurz zwischengespeichert.
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient(TarifAbfrage.Client, c => c.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddSingleton<TarifAbfrage>();

// Nur statisches Server-Rendering: keine Verbindung (SignalR) nötig, schnell auch bei schwachem Netz.
builder.Services.AddRazorComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/introuvable");
app.UseHttpsRedirection();

// www.tablosuite.com → tablosuite.com (eine Adresse für Suchmaschinen)
app.Use(async (context, next) =>
{
    var host = context.Request.Host;
    if (host.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
    {
        var ziel = $"https://{host.Host[4..]}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
        context.Response.Redirect(ziel, permanent: true);
        return;
    }
    await next();
});

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>();

app.Run();
