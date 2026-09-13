using System;
using System.ComponentModel.DataAnnotations;
using ShmsBackend.Api.Helpers;

namespace ShmsBackend.Api.Validation;

public class MinimumAgeAttribute : ValidationAttribute
{
    private readonly int _minAge;
    public MinimumAgeAttribute(int minAge) { _minAge = minAge; }

    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is not DateTime dob) return ValidationResult.Success; // nullable/optional field, skip if not provided
        return AgeValidationHelper.IsAtLeast(dob, _minAge)
            ? ValidationResult.Success
            : new ValidationResult($"Date of birth must indicate an age of at least {_minAge} years.");
    }
}
