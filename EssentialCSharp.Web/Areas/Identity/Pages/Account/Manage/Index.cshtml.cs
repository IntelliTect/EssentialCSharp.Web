// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage;

public class IndexModel : PageModel
{
    private readonly UserManager<EssentialCSharpWebUser> _UserManager;
    private readonly SignInManager<EssentialCSharpWebUser> _SignInManager;

    public IndexModel(
        UserManager<EssentialCSharpWebUser> userManager,
        SignInManager<EssentialCSharpWebUser> signInManager)
    {
        _UserManager = userManager;
        _SignInManager = signInManager;
    }

    public string Username { get; set; }

    public string FirstName { get; set; }
    public string LastName { get; set; }

    [TempData]
    public string StatusMessage { get; set; }

    [BindProperty]
    public InputModel Input { get; set; }

    public class InputModel
    {
        [Display(Name = "Username")]
        public string Username { get; set; }

        [Phone]
        [Display(Name = "Phone number")]
        public string PhoneNumber { get; set; }

        [Display(Name = "First Name")]
        public string FirstName { get; set; }

        [Display(Name = "Last Name")]
        public string LastName { get; set; }
    }

    private async Task LoadAsync(EssentialCSharpWebUser user)
    {
        string userName = await _UserManager.GetUserNameAsync(user);
        string phoneNumber = await _UserManager.GetPhoneNumberAsync(user);

        Input = new InputModel
        {
            Username = userName,
            PhoneNumber = phoneNumber,
            FirstName = user.FirstName,
            LastName = user.LastName
        };
    }

    public async Task<IActionResult> OnGetAsync()
    {
        EssentialCSharpWebUser user = await _UserManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }

        await LoadAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        EssentialCSharpWebUser user = await _UserManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(user);
            return Page();
        }

        string phoneNumber = await _UserManager.GetPhoneNumberAsync(user);
        if (Input.PhoneNumber != phoneNumber)
        {
            IdentityResult setPhoneResult = await _UserManager.SetPhoneNumberAsync(user, Input.PhoneNumber);
            if (!setPhoneResult.Succeeded)
            {
                StatusMessage = "Unexpected error when trying to set phone number.";
                return RedirectToPage();
            }
        }
        string username = await _UserManager.GetUserNameAsync(user);
        if (Input.Username != username)
        {
            IdentityResult setUsernameResult = await _UserManager.SetUserNameAsync(user, Input.Username);
            if (!setUsernameResult.Succeeded)
            {
                StatusMessage = "Unexpected error when trying to set username.";
                return RedirectToPage();
            }
        }
        if (Input.FirstName != user.FirstName)
        {
            user.FirstName = Input.FirstName;
            IdentityResult setFirstNameResult = await _UserManager.UpdateAsync(user);
            if (!setFirstNameResult.Succeeded)
            {
                StatusMessage = "Unexpected error when trying to set first name.";
                return RedirectToPage();
            }
        }
        if (Input.LastName != user.LastName)
        {
            user.LastName = Input.LastName;
            IdentityResult setLastNameResult = await _UserManager.UpdateAsync(user);
            if (!setLastNameResult.Succeeded)
            {
                StatusMessage = "Unexpected error when trying to set last name.";
                return RedirectToPage();
            }
        }

        await _SignInManager.RefreshSignInAsync(user);
        StatusMessage = "Your profile has been updated";
        return RedirectToPage();
    }
}
