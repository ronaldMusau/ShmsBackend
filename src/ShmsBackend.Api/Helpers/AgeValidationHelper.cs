using System;

namespace ShmsBackend.Api.Helpers;

public static class AgeValidationHelper
{
    public static bool IsAtLeast(DateTime dob, int minAge) => dob.Date <= DateTime.UtcNow.Date.AddYears(-minAge);
}
