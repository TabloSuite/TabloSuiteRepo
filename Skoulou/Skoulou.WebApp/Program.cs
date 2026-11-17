using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Components;
using Skoulou.WebApp.Data.Seed;
using Skoulou.WebApp.Services.Finanzen;
using Skoulou.WebApp.Services.Formation;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Noten;
using Skoulou.WebApp.Services.Organisation;
using Skoulou.WebApp.Services.Paedagogik;
using Skoulou.WebApp.Services.Plattform;

// Einheitliche Anzeige auf jedem Server: « 150.000 FCFA », « 13,67 », « 12/11/2026 ».
var kultur = SkoulouKultur.Erstelle();
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = kultur;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = kultur;

var builder = WebApplication.CreateBuilder(args);
builder.AddSkoulouProtokollierung();
builder.Services.AddSkoulouInfrastruktur(builder.Configuration, builder.Environment);
builder.Services.AddPlattform().AddOrganisation().AddPaedagogik().AddNoten().AddFinanzen().AddFormation();

var app = builder.Build();
if (await StartAufgaben.AusfuehrenAsync(app, args)) return;

app.UseSkoulouPipeline();
app.MapInfrastrukturEndpunkte();
app.MapPlattformEndpunkte();
app.MapOrganisationEndpunkte();
app.MapPaedagogikEndpunkte();
app.MapNotenEndpunkte();
app.MapFinanzenEndpunkte();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.RunAsync();

/// <summary>Einstiegspunkt (für Tests sichtbar).</summary>
public partial class Program;
