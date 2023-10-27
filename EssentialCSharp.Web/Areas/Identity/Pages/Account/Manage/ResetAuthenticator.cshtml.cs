// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage
{
    public class ResetAuthenticatorModel : PageModel
    {
        private readonly UserManager<EssentialCSharpWebUser> _UserManager;
        private readonly SignInManager<EssentialCSharpWebUser> _SignInManager;
        private readonly ILogger<ResetAuthenticatorModel> _Logger;

        public ResetAuthenticatorModel(
            UserManager<EssentialCSharpWebUser> userManager,
            SignInManager<EssentialCSharpWebUser> signInManager,
            ILogger<ResetAuthenticatorModel> logger)
        {
            _UserManager = userManager;
            _SignInManager = signInManager;
            _Logger = logger;
        }

        [TempData]
        public string StatusMessage { get; set; }

        public async Task<IActionResult> OnGet()
        {
            EssentialCSharpWebUser user = await _UserManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            EssentialCSharpWebUser user = await _UserManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
            }

            await _UserManager.SetTwoFactorEnabledAsync(user, false);
            await _UserManager.ResetAuthenticatorKeyAsync(user);
            _ = await _UserManager.GetUserIdAsync(user);
            _Logger.LogInformation("User with ID '{UserId}' has reset their authentication app key.", user.Id);

            await _SignInManager.RefreshSignInAsync(user);
            StatusMessage = "Your authenticator app key has been reset, you will need to configure your authenticator app using the new key.";

            return RedirectToPage("./EnableAuthenticator");
        }
    }
}
