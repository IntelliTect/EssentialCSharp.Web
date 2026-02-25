using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage;

public class McpAccessModel : PageModel
{
    private readonly McpTokenService? _McpTokenService;
    private readonly UserManager<EssentialCSharpWebUser> _UserManager;

    [TempData]
    public string? StatusMessage { get; set; }

    public string? GeneratedToken { get; private set; }

    public DateTime? TokenExpiresAt { get; private set; }

    public bool McpEnabled => _McpTokenService is not null;

    public McpAccessModel(IServiceProvider serviceProvider, UserManager<EssentialCSharpWebUser> userManager)
    {
        _McpTokenService = serviceProvider.GetService<McpTokenService>();
        _UserManager = userManager;
    }

    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (_McpTokenService is null)
        {
            StatusMessage = "Error: MCP is not enabled on this server.";
            return Page();
        }

        EssentialCSharpWebUser? user = await _UserManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }

        string userId = await _UserManager.GetUserIdAsync(user);
        string? userName = user.UserName;
        string? email = await _UserManager.GetEmailAsync(user);

        // TODO: Implement per-user token tracking and limit to prevent unbounded token generation.
        // Store issued jti claims in the database and enforce a maximum active token count per user.
        var (token, expiresAt) = _McpTokenService.GenerateToken(userId, userName, email);
        GeneratedToken = token;
        TokenExpiresAt = expiresAt;

        return Page();
    }
}
