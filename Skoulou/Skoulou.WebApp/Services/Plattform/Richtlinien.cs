using Microsoft.AspNetCore.Authorization;
using Skoulou.Core.Rechte;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Seitenrichtlinien je Bereich (grob nach Rolle; feine Rechte prüfen die Services).</summary>
public static class Richtlinien
{
    public const string Admin = "Admin";
    public const string Responsable = "Responsable";
    public const string Direction = "Direction";
    public const string Direktor = "Direktor";
    public const string Unterricht = "Unterricht";
    public const string Finanzen = "Finanzen";
    public const string Eleve = "Eleve";
    public const string Parents = "Parents";

    public static void Konfigurieren(AuthorizationOptions o)
    {
        o.AddPolicy(Admin, p => p.RequireRole(Rollen.SuperAdmin));
        o.AddPolicy(Responsable, p => p.RequireRole(Rollen.Responsable));
        o.AddPolicy(Direction, p => p.RequireRole(Rollen.Direktor, Rollen.Koordinator));
        o.AddPolicy(Direktor, p => p.RequireRole(Rollen.Direktor));
        o.AddPolicy(Unterricht, p => p.RequireRole(Rollen.Lehrer, Rollen.Direktor, Rollen.Koordinator));
        o.AddPolicy(Finanzen, p => p.RequireRole(Rollen.Direktor, Rollen.Koordinator));
        o.AddPolicy(Eleve, p => p.RequireRole(Rollen.Schueler));
        o.AddPolicy(Parents, p => p.RequireRole(Rollen.Eltern));
    }
}
