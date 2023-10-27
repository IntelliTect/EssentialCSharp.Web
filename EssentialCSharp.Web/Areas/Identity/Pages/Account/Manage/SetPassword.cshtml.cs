﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage
{
    public class SetPasswordModel : PageModel
    {
        private readonly UserManager<EssentialCSharpWebUser> _UserManager;
        private readonly SignInManager<EssentialCSharpWebUser> _SignInManager;

        public SetPasswordModel(
            UserManager<EssentialCSharpWebUser> userManager,
            SignInManager<EssentialCSharpWebUser> signInManager)
        {
            _UserManager = userManager;
            _SignInManager = signInManager;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        [TempData]
        public string StatusMessage { get; set; }

        public class InputModel
        {

            [Required]
            [StringLength(ValidationMessages.PasswordMaximumLength, ErrorMessage = ValidationMessages.StringLengthErrorMessage, MinimumLength = ValidationMessages.PasswordMinimumLength)]
            [DataType(DataType.Password)]
            [Display(Name = "New password")]
            public string NewPassword { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Confirm new password")]
            [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            EssentialCSharpWebUser user = await _UserManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
            }

            bool hasPassword = await _UserManager.HasPasswordAsync(user);

            if (hasPassword)
            {
                return RedirectToPage("./ChangePassword");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            EssentialCSharpWebUser user = await _UserManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
            }

            IdentityResult addPasswordResult = await _UserManager.AddPasswordAsync(user, Input.NewPassword);
            if (!addPasswordResult.Succeeded)
            {
                foreach (IdentityError error in addPasswordResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return Page();
            }

            await _SignInManager.RefreshSignInAsync(user);
            StatusMessage = "Your password has been set.";

            return RedirectToPage();
        }
    }
}
