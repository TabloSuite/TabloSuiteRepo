using Skoulou.Core.Rechte;
using Skoulou.WebApp.Components.Layout;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Plattform;

/// <summary>E7b: Guide utilisateur passt zur echten Navigation jeder Rolle (keine toten Links, keine Lücken).</summary>
public sealed class GuideTests
{
    private static readonly string[] Allgemein = ["/auth/connexion", "/profil", "/aide/videos", "/aide/guide", "/contact"];

    private static IEnumerable<(string Rolle, Func<KoordinatorRechte, bool> Recht, bool Haupt)> Faelle()
    {
        foreach (var rolle in Rollen.Alle)
        {
            yield return (rolle, _ => true, rolle == Rollen.SuperAdmin);
            if (rolle == Rollen.SuperAdmin) yield return (rolle, _ => true, false);
            if (rolle == Rollen.Koordinator)
            {
                yield return (rolle, _ => false, false);
                yield return (rolle, r => r == KoordinatorRechte.FinanzenEinsehen, false);
            }
        }
    }

    [Fact]
    public void Jeder_Schritt_fuehrt_auf_eine_erreichbare_Seite()
    {
        foreach (var (rolle, recht, haupt) in Faelle())
        {
            var nav = NavKarte.Fuer(rolle, recht, haupt).SelectMany(g => g.Eintraege).ToList();
            foreach (var s in GuideTexte.Fuer(rolle, recht, haupt).SelectMany(k => k.Schritte).Where(s => s.Href is not null))
                Assert.True(Allgemein.Contains(s.Href) || nav.Any(z => s.Href == z.Href
                        || (z.Match == Microsoft.AspNetCore.Components.Routing.NavLinkMatch.Prefix && s.Href!.StartsWith(z.Href + "/"))),
                    $"{rolle}: « {s.Titel} » verweist auf {s.Href}, das nicht in der Navigation steht");
        }
    }

    [Fact]
    public void Jede_Seite_der_Navigation_steht_im_Guide()
    {
        foreach (var (rolle, recht, haupt) in Faelle())
        {
            var schritte = GuideTexte.Fuer(rolle, recht, haupt).SelectMany(k => k.Schritte).Select(s => s.Href).ToHashSet();
            var direktion = rolle is Rollen.Direktor or Rollen.Koordinator;
            foreach (var e in NavKarte.Fuer(rolle, recht, haupt).SelectMany(g => g.Eintraege))
            {
                if (e.Href == Rollen.Startseite(rolle) || Allgemein.Contains(e.Href) || e.Href.StartsWith("/aide/")) continue;
                // Direktion: « Mon enseignement » wird mit einem Schritt (« Si vous enseignez ») erklärt.
                if (direktion && e.Href.StartsWith("/enseignant/")) continue;
                Assert.True(schritte.Contains(e.Href), $"{rolle}: Seite {e.Href} (« {e.Label} ») fehlt im Guide");
            }
        }
    }

    [Fact]
    public void Koordinator_ohne_Recht_und_Admin_sehen_nur_ihre_Schritte()
    {
        static List<string> Ziele(string rolle, Func<KoordinatorRechte, bool> recht, bool haupt = false) =>
            GuideTexte.Fuer(rolle, recht, haupt).SelectMany(k => k.Schritte).Select(s => s.Href).OfType<string>().ToList();

        var ohne = Ziele(Rollen.Koordinator, _ => false);
        Assert.DoesNotContain(ohne, h => h.StartsWith("/finances"));
        Assert.DoesNotContain("/direction/ecole", ohne);
        Assert.DoesNotContain("/direction/journal", ohne);
        Assert.Contains("/direction/annee/passage", ohne);
        Assert.DoesNotContain(GuideTexte.Fuer(Rollen.Koordinator, _ => false), k => k.Id == "finances");

        var mitBericht = Ziele(Rollen.Koordinator, r => r == KoordinatorRechte.FinanzenEinsehen);
        Assert.Contains("/finances/rapport", mitBericht);
        Assert.DoesNotContain("/finances/paiements", mitBericht);

        Assert.Contains("/admin/administrateurs", Ziele(Rollen.SuperAdmin, _ => true, haupt: true));
        Assert.DoesNotContain("/admin/administrateurs", Ziele(Rollen.SuperAdmin, _ => true, haupt: false));
        Assert.Equal("compte", GuideTexte.Fuer(Rollen.Eltern).Last().Id);
    }
}
