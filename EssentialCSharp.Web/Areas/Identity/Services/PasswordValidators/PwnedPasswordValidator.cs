using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace EssentialCSharp.Web.Areas.Identity.Services.PasswordValidators;

/// <summary>
/// Validates that the password has not appeared in a known data breach using the
/// HaveIBeenPwned Pwned Passwords k-anonymity range API.
/// </summary>
public class PwnedPasswordValidator<TUser>(IHttpClientFactory httpClientFactory, ILogger<PwnedPasswordValidator<TUser>> logger)
    : IPasswordValidator<TUser>
    where TUser : class
{
    private const string ClientName = "HaveIBeenPwned";
    private const string PwnedPasswordsRangePath = "range/";

    public async Task<IdentityResult> ValidateAsync(UserManager<TUser> manager, TUser user, string? password)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(password);

        try
        {
            // Compute SHA-1 once; k-anonymity transmits only the 5-char prefix.
            string hash = ComputeSha1Hex(password);
            string prefix = hash[..5];
            string suffix = hash[5..];

            HttpClient client = httpClientFactory.CreateClient(ClientName);
            using HttpRequestMessage request = new(HttpMethod.Get, $"{PwnedPasswordsRangePath}{prefix}");
            request.Headers.Add("Add-Padding", "true");

            using HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string content = await response.Content.ReadAsStringAsync();

            // HIBP responses use CRLF per the API spec; split on both to be defensive.
            foreach (string line in content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
            {
                ReadOnlySpan<char> lineSpan = line.AsSpan();
                int separatorIndex = lineSpan.IndexOf(':');
                if (separatorIndex < 0) continue;

                ReadOnlySpan<char> hashSuffix = lineSpan[..separatorIndex];
                ReadOnlySpan<char> countSpan = lineSpan[(separatorIndex + 1)..];

                // Padded responses (Add-Padding: true) include decoy entries with count=0.
                // Only flag the password as breached when the count is genuinely > 0.
                if (hashSuffix.Equals(suffix, StringComparison.OrdinalIgnoreCase)
                    && long.TryParse(countSpan, out long count) && count > 0)
                {
                    return IdentityResult.Failed(new IdentityError
                    {
                        Code = "PwnedPassword",
                        Description = "This password has appeared in a known data breach. Please choose a different password."
                    });
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check password against HaveIBeenPwned. Failing open.");
        }

        return IdentityResult.Success;
    }

    private static string ComputeSha1Hex(string password)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(password);
        // SHA-1 is required by the HaveIBeenPwned k-anonymity range API protocol.
        // The hash is truncated to 5 chars before transmission so the full hash never leaves the process.
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
        byte[] hashBytes = SHA1.HashData(bytes);
#pragma warning restore CA5350
        return Convert.ToHexString(hashBytes);
    }
}
