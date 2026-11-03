using Microsoft.AspNetCore.Components;

namespace ENote.WebApp.Components.Pages;

public partial class Login
{
    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    [SupplyParameterFromQuery(Name = "error")]
    private string? Error { get; set; }

    [SupplyParameterFromQuery(Name = "info")]
    private string? Info { get; set; }

    private string? errorMessage;
    private string? infoMessage;

    protected override void OnParametersSet()
    {
        errorMessage = Error switch
        {
            "invalid-credentials" => "Adresse e-mail ou mot de passe incorrect, ou compte bloqué.",
            _ => null
        };

        infoMessage = Info switch
        {
            "registered" => "Votre salon a été enregistré. Il sera activé après validation par l'administrateur.",
            "logged-out" => "Vous avez été déconnecté.",
            _ => null
        };
    }
}
