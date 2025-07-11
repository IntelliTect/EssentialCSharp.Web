using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace EssentialCSharp.Web.Services;

/// <summary>
/// Service for protecting and unprotecting personal data in the Identity user store
/// using ASP.NET Core Data Protection API.
/// </summary>
public class PersonalDataProtectionService : IPersonalDataProtector
{
    private readonly IDataProtector _protector;

    public PersonalDataProtectionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Microsoft.AspNetCore.Identity.PersonalData");
    }

    /// <summary>
    /// Protects (encrypts) the given personal data value.
    /// </summary>
    /// <param name="data">The data to protect</param>
    /// <returns>The protected (encrypted) data as a string</returns>
    public string Protect(string? data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return string.Empty;
        }

        return _protector.Protect(data);
    }

    /// <summary>
    /// Unprotects (decrypts) the given protected personal data value.
    /// </summary>
    /// <param name="data">The protected data to unprotect</param>
    /// <returns>The unprotected (decrypted) data as a string</returns>
    public string Unprotect(string? data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return string.Empty;
        }

        try
        {
            return _protector.Unprotect(data);
        }
        catch (Exception)
        {
            // If decryption fails, assume the data is not encrypted (for backward compatibility)
            // This handles cases where existing user data was stored before encryption was enabled
            return data;
        }
    }
}