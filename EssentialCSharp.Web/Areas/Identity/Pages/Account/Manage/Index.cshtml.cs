using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage;

public class IndexModel(
    UserManager<EssentialCSharpWebUser> userManager,
    SignInManager<EssentialCSharpWebUser> signInManager) : PageModel
{
    public string? Username { get; set; }

    public string? FirstName { get; set; }
    public string? LastName { get; set; }

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
        [Display(Name = "Username")]
        public string? Username { get; set; }

        [Phone]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }

        [Display(Name = "First Name")]
        public string? FirstName { get; set; }

        [Display(Name = "Last Name")]
        public string? LastName { get; set; }
    }

    private async Task LoadAsync(EssentialCSharpWebUser user)
    {
        string? userName = await userManager.GetUserNameAsync(user);
        string? phoneNumber = await userManager.GetPhoneNumberAsync(user);

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
        EssentialCSharpWebUser? user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
        }

        await LoadAsync(user);
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
            await LoadAsync(user);
            return Page();
        }

        string? phoneNumber = await userManager.GetPhoneNumberAsync(user);
        if (Input.PhoneNumber != phoneNumber)
        {
            IdentityResult setPhoneResult = await userManager.SetPhoneNumberAsync(user, Input.PhoneNumber);
            if (!setPhoneResult.Succeeded)
            {
                StatusMessage = "Unexpected error when trying to set phone number.";
                return RedirectToPage();
            }
        }
        string? username = await userManager.GetUserNameAsync(user);
        if (Input.Username != username)
        {
            IdentityResult setUsernameResult = await userManager.SetUserNameAsync(user, Input.Username);
            if (!setUsernameResult.Succeeded)
            {
                StatusMessage = "Unexpected error when trying to set username.";
                return RedirectToPage();
            }
        }
        if (Input.FirstName != user.FirstName)
        {
            user.FirstName = Input.FirstName;
            IdentityResult setFirstNameResult = await userManager.UpdateAsync(user);
            if (!setFirstNameResult.Succeeded)
            {
                StatusMessage = "Unexpected error when trying to set first name.";
                return RedirectToPage();
            }
        }
        if (Input.LastName != user.LastName)
        {
            user.LastName = Input.LastName;
            IdentityResult setLastNameResult = await userManager.UpdateAsync(user);
            if (!setLastNameResult.Succeeded)
            {
                StatusMessage = "Unexpected error when trying to set last name.";
                return RedirectToPage();
            }
        }

        await signInManager.RefreshSignInAsync(user);
        StatusMessage = "Your profile has been updated";
        return RedirectToPage();
    }
}
