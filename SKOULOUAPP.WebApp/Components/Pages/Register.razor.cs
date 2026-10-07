using SkoulouApp.Core.DataModels;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.WebApp.Models.Auth;
using SkoulouApp.WebApp.Services;
using SkoulouApp.WebApp.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Globalization;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class Register
{
    [Inject]
    private AuthService AuthService { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private DatabaseActionGuard DatabaseActionGuard { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "type")]
    private string? RegistrationType { get; set; }

    private RegisterInputModel Model { get; set; } = new();
    private List<School> schools = [];
    private string? message;
    private string? errorMessage;
    private bool isSubmitting;
    private bool showTermsModal;
    private string? termsSectionToScroll;

    private ElementReference dateInput;
    private DotNetObjectReference<Register>? dotNetRef;

    private static readonly IReadOnlyList<ModernDropdownOption<RegistrierungsArt>> RegistrierungsArtOptions =
    [
        new(RegistrierungsArt.SchulLeiter, "Directeur (avec une nouvelle école)"),
        new(RegistrierungsArt.Schueler, "Élève (dans une école existante)")
    ];

    private static readonly IReadOnlyList<ModernDropdownOption<Geschlecht>> GeschlechtOptions =
    [
        new(Geschlecht.Divers, "Divers"),
        new(Geschlecht.Maennlich, "Masculin"),
        new(Geschlecht.Weiblich, "Féminin")
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> SchoolOptions =>
    [
        new((Guid?)null, "Veuillez choisir"),
        .. schools.Select(school => new ModernDropdownOption<Guid?>((Guid?)school.Id, school.Name, string.IsNullOrWhiteSpace(school.City) ? null : school.City))
    ];

    private IReadOnlyList<ModernDropdownOption<string?>> SchoolCountryOptions
        => CountryOptions.WithCurrentValue(Model.SchulLand);

    private Task SetGeschlechtAsync(Geschlecht geschlecht)
    {
        Model.Geschlecht = geschlecht;
        return Task.CompletedTask;
    }

    private Task SetRegistrierungsArtAsync(RegistrierungsArt art)
    {
        Model.Art = art;
        return Task.CompletedTask;
    }

    private Task SetSchoolAsync(Guid? schoolId)
    {
        Model.SelectedSchoolId = schoolId;
        return Task.CompletedTask;
    }

    private Task SetSchoolCountryAsync(string? country)
    {
        Model.SchulLand = country ?? string.Empty;
        return Task.CompletedTask;
    }

    protected override async Task OnInitializedAsync()
    {
        Model.Art = RegistrationType?.Trim().ToLowerInvariant() switch
        {
            "director" or "school" or "directeur" => RegistrierungsArt.SchulLeiter,
            "student" or "schueler" or "schüler" or "eleve" or "élève" => RegistrierungsArt.Schueler,
            _ => Model.Art
        };

        schools = await AuthService.GetActiveSchoolsAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            dotNetRef = DotNetObjectReference.Create(this);
            var initialValue = Model.Geburtsdatum?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            await JS.InvokeVoidAsync("SKOULOUAPPDatePicker.init", dateInput, dotNetRef, initialValue);
        }

        if (!string.IsNullOrWhiteSpace(termsSectionToScroll))
        {
            var sectionId = termsSectionToScroll;
            termsSectionToScroll = null;
            await JS.InvokeVoidAsync("SKOULOUAPP.scrollIntoView", sectionId);
        }
    }

    [JSInvokable]
    public void OnDateChanged(string value)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            Model.Geburtsdatum = parsed;
        }
        else
        {
            Model.Geburtsdatum = null;
        }

        StateHasChanged();
    }

    private async Task OpenTermsAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        isSubmitting = true;
        message = null;
        errorMessage = null;
        Model.AgbAccepted = false;
        try
        {
            await DatabaseActionGuard.RunAsync(async () =>
            {
                var validation = await AuthService.ValidateRegistrationAsync(Model);
                if (!validation.Succeeded)
                {
                    errorMessage = validation.ErrorMessage;
                    showTermsModal = false;
                    return;
                }

                termsSectionToScroll = Model.Art == RegistrierungsArt.Schueler ? "terms-student" : "terms-school";
                showTermsModal = true;
            });
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private async Task ScrollToTermsAsync(string sectionId)
    {
        await JS.InvokeVoidAsync("SKOULOUAPP.scrollIntoView", sectionId);
    }

    private void CancelTerms()
    {
        showTermsModal = false;
        Model.AgbAccepted = false;
    }

    private async Task ConfirmTermsAsync()
    {
        Model.AgbAccepted = true;
        await HandleRegisterAsync();
    }

    private async Task HandleRegisterAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        isSubmitting = true;
        message = null;
        errorMessage = null;
        try
        {
            await DatabaseActionGuard.RunAsync(async () =>
            {
                var result = Model.Art switch
                {
                    RegistrierungsArt.SchulLeiter => await AuthService.RegisterSchulLeiterAsync(Model),
                    _ => await AuthService.RegisterStudentAsync(Model)
                };

                if (!result.Succeeded)
                {
                    errorMessage = result.ErrorMessage;
                    showTermsModal = false;
                    Model.AgbAccepted = false;
                    return;
                }

                message = result.ErrorMessage;
                Model = new RegisterInputModel();
                showTermsModal = false;
            });
        }
        finally
        {
            isSubmitting = false;
        }
    }

    public void Dispose()
    {
        dotNetRef?.Dispose();
    }
}

