using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage;

public class McpAccessModel(
    McpApiTokenService tokenService,
    UserManager<EssentialCSharpWebUser> userManager) : PageModel
{
    [TempData]
    public string? StatusMessage { get; set; }

    public string? GeneratedToken { get; private set; }

    public McpApiToken? GeneratedTokenEntity { get; private set; }

    public List<McpApiToken> UserTokens { get; private set; } = [];

    [BindProperty]
    [StringLength(256, ErrorMessage = "Token name must be 256 characters or fewer.")]
    public string TokenName { get; set; } = "My Token";

    [BindProperty]
    public DateOnly? ExpiresOn { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        DisableCaching();
        string? userId = userManager.GetUserId(User);
        if (userId is null) return Challenge();
        UserTokens = await tokenService.GetUserTokensAsync(userId);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        DisableCaching();
        string? userId = userManager.GetUserId(User);
        if (userId is null) return Challenge();

        if (string.IsNullOrWhiteSpace(TokenName))
            ModelState.AddModelError(nameof(TokenName), "Token name is required.");

        if (ExpiresOn.HasValue && ExpiresOn.Value < DateOnly.FromDateTime(DateTime.UtcNow))
            ModelState.AddModelError(nameof(ExpiresOn), "Expiry date must be today or in the future.");

        if (!ModelState.IsValid)
        {
            UserTokens = await tokenService.GetUserTokensAsync(userId);
            return Page();
        }

        // Convert date-only boundary to end-of-day UTC instant before persisting
        DateTime? expiresAt = ExpiresOn?.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var (rawToken, entity) = await tokenService.CreateTokenAsync(userId, TokenName.Trim(), expiresAt);
        GeneratedToken = rawToken;
        GeneratedTokenEntity = entity;
        UserTokens = await tokenService.GetUserTokensAsync(userId);
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid tokenId)
    {
        DisableCaching();
        string? userId = userManager.GetUserId(User);
        if (userId is null) return Challenge();

        bool revoked = await tokenService.RevokeTokenAsync(tokenId, userId);
        StatusMessage = revoked
            ? "Token revoked successfully."
            : "Error: Token not found or already revoked.";

        return RedirectToPage();
    }

    private void DisableCaching()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
    }
}
