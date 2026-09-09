using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.Models.Handbuch;
using Xunit;

namespace HalloApp.Tests.Services;

public class HandbuchRoleTests
{
    [Theory]
    [InlineData(Rollen.SuperAdmin, true, true, true, true, true, true)]
    [InlineData(Rollen.SchulLeiter, true, true, true, true, true, false)]
    [InlineData(Rollen.Koordinator, true, true, true, true, false, false)]
    [InlineData(Rollen.ProduktOwner, false, false, true, true, false, false)]
    [InlineData(Rollen.Lehrer, true, true, false, true, false, false)]
    [InlineData(Rollen.Schueler, true, false, false, false, false, false)]
    public void GuideTabs_MatchRolePermissions(
        string role,
        bool learning,
        bool teaching,
        bool school,
        bool commissions,
        bool finance,
        bool administration)
    {
        var tabs = HandbuchTabSichtbarkeit.GetTabIds(role);

        Assert.Equal(learning, tabs.Contains("learning"));
        Assert.Equal(teaching, tabs.Contains("teaching"));
        Assert.Equal(school, tabs.Contains("school"));
        Assert.Equal(commissions, tabs.Contains("commissions"));
        Assert.Equal(finance, tabs.Contains("finance"));
        Assert.Equal(administration, tabs.Contains("admin"));
    }

    [Theory]
    [InlineData(Rollen.SuperAdmin)]
    [InlineData(Rollen.SchulLeiter)]
    [InlineData(Rollen.Koordinator)]
    [InlineData(Rollen.ProduktOwner)]
    [InlineData(Rollen.Lehrer)]
    [InlineData(Rollen.Schueler)]
    public void VisibleGuideTabs_HaveDefinitionsAndContent(string role)
    {
        var tabs = HandbuchTabSichtbarkeit.GetTabIds(role);
        var panels = HandbuchContent.GetPanels(role);

        foreach (var tab in tabs)
        {
            Assert.True(HandbuchContent.TabDefinitions.ContainsKey(tab), $"Missing tab definition for {tab}.");
            if (tab != "start")
            {
                Assert.True(panels.ContainsKey(tab), $"Missing panel content for {tab}.");
            }
        }
    }
}
