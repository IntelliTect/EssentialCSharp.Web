﻿using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

public class LoginWith2faModel(
    SignInManager<EssentialCSharpWebUser> signInManager,
    UserManager<EssentialCSharpWebUser> userManager,
    ILogger<LoginWith2faModel> logger) : PageModel
{
    private InputModel? _Input;
    [BindProperty]
    public InputModel Input
    {
        get => _Input!;
        set => _Input = value ?? throw new ArgumentNullException(nameof(value));
    }

    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required]
        [StringLength(ValidationMessages.VerificationCodeMaximumLength, ErrorMessage = ValidationMessages.StringLengthErrorMessage, MinimumLength = ValidationMessages.VerificationCodeMaximumLength)]
        [DataType(DataType.Text)]
        [Display(Name = "Authenticator code")]
        public string? TwoFactorCode { get; set; }

        [Display(Name = "Remember this machine")]
        public bool RememberMachine { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(bool rememberMe, string? returnUrl = null)
    {
        // Ensure the user has gone through the username & password screen first
        _ = await signInManager.GetTwoFactorAuthenticationUserAsync() ?? throw new InvalidOperationException($"Unable to load two-factor authentication user.");
        if (returnUrl is not null) ReturnUrl = returnUrl;
        RememberMe = rememberMe;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(bool rememberMe, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        returnUrl ??= Url.Content("~/");

        EssentialCSharpWebUser user = await signInManager.GetTwoFactorAuthenticationUserAsync() ?? throw new InvalidOperationException($"Unable to load two-factor authentication user.");
        if (Input.TwoFactorCode is null)
        {
            return RedirectToPage("./Lockout", new { ReturnUrl = returnUrl });
        }
        string authenticatorCode = Input.TwoFactorCode.Replace(" ", string.Empty).Replace("-", string.Empty);

        Microsoft.AspNetCore.Identity.SignInResult result = await signInManager.TwoFactorAuthenticatorSignInAsync(authenticatorCode, rememberMe, Input.RememberMachine);

        // Not sure what this is used for but was in identity scaffolding so hesitant to remove without understanding
        _ = await userManager.GetUserIdAsync(user);

        if (result.Succeeded)
        {
            logger.LogInformation("User with ID '{UserId}' logged in with 2fa.", user.Id);
            return LocalRedirect(returnUrl);
        }
        else if (result.IsLockedOut)
        {
            logger.LogWarning("User with ID '{UserId}' account locked out.", user.Id);
            return RedirectToPage("./Lockout");
        }
        else
        {
            logger.LogWarning("Invalid authenticator code entered for user with ID '{UserId}'.", user.Id);
            ModelState.AddModelError(string.Empty, "Invalid authenticator code.");
            return Page();
        }
    }
}
