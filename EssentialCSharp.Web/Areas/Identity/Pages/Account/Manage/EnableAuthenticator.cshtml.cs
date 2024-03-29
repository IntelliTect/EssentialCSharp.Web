﻿using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage;

public class EnableAuthenticatorModel(
    UserManager<EssentialCSharpWebUser> userManager,
    ILogger<EnableAuthenticatorModel> logger,
    UrlEncoder urlEncoder) : PageModel
{
    private static readonly CompositeFormat _AuthenticatorUriFormat = CompositeFormat.Parse("otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6");

    public string? SharedKey { get; set; }

    public string? AuthenticatorUri { get; set; }

    [TempData]
    public string[]? RecoveryCodes { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    private InputModel? _Input;
    [BindProperty]
    public InputModel Input
    {
        get => _Input!;
        set => _Input = value ?? throw new ArgumentNullException(nameof(value));
    }

    public class InputModel
    {

        [Required]
        [StringLength(ValidationMessages.VerificationCodeMaximumLength, ErrorMessage = ValidationMessages.StringLengthErrorMessage, MinimumLength = ValidationMessages.VerificationCodeMinimumLength)]
        [DataType(DataType.Text)]
        [Display(Name = "Verification Code")]
        public string? Code { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        EssentialCSharpWebUser? user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
        }

        await LoadSharedKeyAndQrCodeUriAsync(user);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        EssentialCSharpWebUser? user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
        }

        if (!ModelState.IsValid)
        {
            await LoadSharedKeyAndQrCodeUriAsync(user);
            return Page();
        }

        if (Input.Code is null)
        {
            return RedirectToPage("./TwoFactorAuthentication");
        }
        // Strip spaces and hyphens
        string verificationCode = Input.Code.Replace(" ", string.Empty).Replace("-", string.Empty);

        bool is2faTokenValid = await userManager.VerifyTwoFactorTokenAsync(
            user, userManager.Options.Tokens.AuthenticatorTokenProvider, verificationCode);

        if (!is2faTokenValid)
        {
            ModelState.AddModelError(nameof(Input.Code), "Verification code is invalid.");
            await LoadSharedKeyAndQrCodeUriAsync(user);
            return Page();
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);
        string userId = await userManager.GetUserIdAsync(user);
        logger.LogInformation("User with ID '{UserId}' has enabled 2FA with an authenticator app.", userId);

        StatusMessage = "Your authenticator app has been verified.";

        if (await userManager.CountRecoveryCodesAsync(user) == 0)
        {
            IEnumerable<string>? recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            if (recoveryCodes is null)
            {
                return RedirectToPage("./TwoFactorAuthentication");
            }
            RecoveryCodes = recoveryCodes.ToArray();
            return RedirectToPage("./ShowRecoveryCodes");
        }
        else
        {
            return RedirectToPage("./TwoFactorAuthentication");
        }
    }

    private async Task LoadSharedKeyAndQrCodeUriAsync(EssentialCSharpWebUser user)
    {
        // Load the authenticator key & QR code URI to display on the form
        string? unformattedKey = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(unformattedKey))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            unformattedKey = await userManager.GetAuthenticatorKeyAsync(user);
        }
        if (!string.IsNullOrEmpty(unformattedKey))
        {
            SharedKey = FormatKey(unformattedKey);
        }

        string? email = await userManager.GetEmailAsync(user);
        if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(unformattedKey))
        {
            AuthenticatorUri = GenerateQrCodeUri(email, unformattedKey);
        }
    }

#pragma warning disable CA1822 // Mark members as static
    private string FormatKey(string unformattedKey)
#pragma warning restore CA1822 // Mark members as static
    {
        var result = new StringBuilder();
        int currentPosition = 0;
        while (currentPosition + 4 < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }
        if (currentPosition < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition));
        }

        return result.ToString().ToLowerInvariant();
    }

    private string GenerateQrCodeUri(string email, string unformattedKey)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            _AuthenticatorUriFormat,
            urlEncoder.Encode("EssentialCSharp.com"),
            urlEncoder.Encode(email),
            unformattedKey);
    }
}
