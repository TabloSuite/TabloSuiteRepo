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
            // Klicktest 20.11.2026: nach 5 Fehlversuchen sperrt Identity 5 Minuten (Standard) – vorher kein Hinweis, dass man nur
            // warten muss. Gleicher Text für alle (verrät nicht, ob das Pseudonyme existiert oder gesperrt ist).
            "invalid-credentials" => "Pseudonyme ou mot de passe incorrect. Après 5 essais erronés, la connexion est bloquée 5 minutes : patientez puis réessayez.",
            "employe-sans-connexion" => Services.LadenZugriff.AnmeldungNurAmGeraetText,
            "limite" => Services.Sicherheit.Schutzgrenzen.MeldungLimite,
            _ => null
        };

        infoMessage = Info switch
        {
            "registered" => "Votre salon a été enregistré. Il sera activé après validation par l'administrateur.",
            "logged-out" => "Vous avez été déconnecté.",
            "password-updated" => "Mot de passe modifié. Connectez-vous avec le nouveau mot de passe.",
            "identifiant-modifie" => "Identifiant modifié. Connectez-vous avec le nouvel identifiant.",
            _ => null
        };
    }
}
