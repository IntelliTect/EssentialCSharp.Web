using System.Security.Cryptography;

namespace EssentialCSharp.Web.Middleware;

/// <summary>
/// Per-request Content-Security-Policy nonce support.
/// </summary>
public static class CspNonce
{
    /// <summary>
    /// Placeholder in header values that is replaced with the per-request nonce.
    /// </summary>
    public const string Placeholder = "{nonce}";

    private const string ItemsKey = "EssentialCSharp.CspNonce";

    /// <summary>
    /// Gets the nonce for the current request, creating one if needed.
    /// </summary>
    public static string GetOrCreate(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Items.TryGetValue(ItemsKey, out object? value) && value is string nonce)
        {
            return nonce;
        }

        nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Items[ItemsKey] = nonce;
        return nonce;
    }

    /// <summary>
    /// Gets the nonce for the current request if a CSP using a nonce was emitted.
    /// </summary>
    public static string? Get(HttpContext? context) =>
        context is not null && context.Items.TryGetValue(ItemsKey, out object? value) ? value as string : null;
}
