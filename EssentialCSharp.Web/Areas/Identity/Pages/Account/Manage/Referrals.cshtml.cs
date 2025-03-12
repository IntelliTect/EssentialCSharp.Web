using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage;

public class ReferralsDataModel : PageModel
{
    private readonly UserManager<EssentialCSharpWebUser> _UserManager;
    private readonly ILogger<ReferralsDataModel> _Logger;

    public int ReferralCount { get; private set; }

    public ReferralsDataModel(
        UserManager<EssentialCSharpWebUser> userManager,
        ILogger<ReferralsDataModel> logger)
    {
        _UserManager = userManager;
        _Logger = logger;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        EssentialCSharpWebUser? user = await _UserManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }
        ReferralCount = user.ReferralCount;

        return Page();
    }
}
