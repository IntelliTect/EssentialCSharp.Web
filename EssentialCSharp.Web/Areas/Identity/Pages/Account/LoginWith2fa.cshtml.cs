﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account
{
    public class LoginWith2faModel : PageModel
    {

        private readonly SignInManager<EssentialCSharpWebUser> _signInManager;
        private readonly UserManager<EssentialCSharpWebUser> _userManager;
        private readonly ILogger<LoginWith2faModel> _logger;

        public LoginWith2faModel(
            SignInManager<EssentialCSharpWebUser> signInManager,
            UserManager<EssentialCSharpWebUser> userManager,
            ILogger<LoginWith2faModel> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public bool RememberMe { get; set; }

        public string ReturnUrl { get; set; }

        public class InputModel
        {

            [Required]
            [StringLength(ValidationMessages.VerificationCodeMaximumLength, ErrorMessage = ValidationMessages.StringLengthErrorMessage, MinimumLength = ValidationMessages.VerificationCodeMaximumLength)]
            [DataType(DataType.Text)]
            [Display(Name = "Authenticator code")]
            public string TwoFactorCode { get; set; }

            [Display(Name = "Remember this machine")]
            public bool RememberMachine { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(bool rememberMe, string returnUrl = null)
        {
            // Ensure the user has gone through the username & password screen first
            _ = await _signInManager.GetTwoFactorAuthenticationUserAsync() ?? throw new InvalidOperationException($"Unable to load two-factor authentication user.");
            ReturnUrl = returnUrl;
            RememberMe = rememberMe;

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(bool rememberMe, string returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            returnUrl ??= Url.Content("~/");

            EssentialCSharpWebUser user = await _signInManager.GetTwoFactorAuthenticationUserAsync() ?? throw new InvalidOperationException($"Unable to load two-factor authentication user.");
            string authenticatorCode = Input.TwoFactorCode.Replace(" ", string.Empty).Replace("-", string.Empty);

            Microsoft.AspNetCore.Identity.SignInResult result = await _signInManager.TwoFactorAuthenticatorSignInAsync(authenticatorCode, rememberMe, Input.RememberMachine);

            // Not sure what this is used for but was in identity scaffloding so hesitant to remove without understanding
            _ = await _userManager.GetUserIdAsync(user);

            if (result.Succeeded)
            {
                _logger.LogInformation("User with ID '{UserId}' logged in with 2fa.", user.Id);
                return LocalRedirect(returnUrl);
            }
            else if (result.IsLockedOut)
            {
                _logger.LogWarning("User with ID '{UserId}' account locked out.", user.Id);
                return RedirectToPage("./Lockout");
            }
            else
            {
                _logger.LogWarning("Invalid authenticator code entered for user with ID '{UserId}'.", user.Id);
                ModelState.AddModelError(string.Empty, "Invalid authenticator code.");
                return Page();
            }
        }
    }
}
