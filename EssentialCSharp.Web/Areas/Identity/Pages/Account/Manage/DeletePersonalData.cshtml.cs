using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage;

public class DeletePersonalDataModel : PageModel
{
    private readonly UserManager<EssentialCSharpWebUser> _UserManager;
    private readonly SignInManager<EssentialCSharpWebUser> _SignInManager;
    private readonly ILogger<DeletePersonalDataModel> _Logger;

    public DeletePersonalDataModel(
        UserManager<EssentialCSharpWebUser> userManager,
        SignInManager<EssentialCSharpWebUser> signInManager,
        ILogger<DeletePersonalDataModel> logger)
    {
        _UserManager = userManager;
        _SignInManager = signInManager;
        _Logger = logger;
    }

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
        [DataType(DataType.Password)]
        public string? Password { get; set; }
    }

    public bool RequirePassword { get; set; }

    public async Task<IActionResult> OnGet()
    {
        EssentialCSharpWebUser? user = await _UserManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }

        RequirePassword = await _UserManager.HasPasswordAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        EssentialCSharpWebUser? user = await _UserManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }

        RequirePassword = await _UserManager.HasPasswordAsync(user);
        if (RequirePassword)
        {
            if (Input.Password is null)
            {
                ModelState.AddModelError(string.Empty, "Please enter a password.");
                return Page();
            }
            if (!await _UserManager.CheckPasswordAsync(user, Input.Password))
            {
                ModelState.AddModelError(string.Empty, "Incorrect password.");
                return Page();
            }
        }

        IdentityResult result = await _UserManager.DeleteAsync(user);
        string userId = await _UserManager.GetUserIdAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Unexpected error occurred deleting user.");
        }

        await _SignInManager.SignOutAsync();

        _Logger.LogInformation("User with ID '{UserId}' deleted themselves.", userId);

        return Redirect("~/");
    }
}
