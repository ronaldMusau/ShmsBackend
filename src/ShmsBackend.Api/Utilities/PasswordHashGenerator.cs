using BCrypt.Net;

namespace ShmsBackend.Api.Utilities;

/// <summary>
/// Utility class for generating password hashes
/// Run this in a console app or test to generate the super admin password hash
/// </summary>
public static class PasswordHashGenerator
{
    public static string GenerateHash(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);
    }

    public static bool VerifyHash(string password, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }

    // Example usage:
    // var hash = PasswordHashGenerator.GenerateHash("SuperAdmin123!");
    // Console.WriteLine(hash);
    // Use this hash in the SQL seed script
}