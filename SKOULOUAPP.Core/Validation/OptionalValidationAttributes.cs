using System.ComponentModel.DataAnnotations;

namespace HalloApp.Core.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class OptionalPhoneAttribute : ValidationAttribute
{
    private static readonly PhoneAttribute PhoneValidator = new();

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is string text && string.IsNullOrWhiteSpace(text))
        {
            return ValidationResult.Success;
        }

        if (value is null)
        {
            return ValidationResult.Success;
        }

        return PhoneValidator.IsValid(value)
            ? ValidationResult.Success
            : new ValidationResult(FormatErrorMessage(validationContext.DisplayName));
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class OptionalEmailAddressAttribute : ValidationAttribute
{
    private static readonly EmailAddressAttribute EmailValidator = new();

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is string text && string.IsNullOrWhiteSpace(text))
        {
            return ValidationResult.Success;
        }

        if (value is null)
        {
            return ValidationResult.Success;
        }

        return EmailValidator.IsValid(value)
            ? ValidationResult.Success
            : new ValidationResult(FormatErrorMessage(validationContext.DisplayName));
    }
}