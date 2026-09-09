using HalloApp.Core.DataModels.Identity;

namespace HalloApp.Core.Models.Handbuch;

public static class HandbuchTabSichtbarkeit
{
    public static IReadOnlyList<string> GetTabIds(string role)
    {
        var tabs = new List<string> { "start" };

        if (role is Rollen.Schueler or Rollen.Lehrer or Rollen.SchulLeiter or Rollen.Koordinator or Rollen.SuperAdmin) tabs.Add("learning");
        if (role is Rollen.Lehrer or Rollen.SchulLeiter or Rollen.Koordinator or Rollen.SuperAdmin) tabs.Add("teaching");
        if (role is Rollen.SchulLeiter or Rollen.Koordinator or Rollen.ProduktOwner or Rollen.SuperAdmin) tabs.Add("school");
        if (role is Rollen.Lehrer or Rollen.SchulLeiter or Rollen.Koordinator or Rollen.ProduktOwner or Rollen.SuperAdmin) tabs.Add("commissions");
        if (role is Rollen.SchulLeiter or Rollen.SuperAdmin) tabs.Add("finance");
        if (role == Rollen.SuperAdmin) tabs.Add("admin");

        return tabs;
    }
}
