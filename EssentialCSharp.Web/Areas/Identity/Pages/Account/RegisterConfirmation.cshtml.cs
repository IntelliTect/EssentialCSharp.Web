using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class RegisterConfirmationModel : PageModel
{
    public string? Email { get; set; }

    // The page is the same whether or not an account exists for the email, so it can't be used to find out
    // which addresses are registered.
    public IActionResult OnGet(string? email)
    {
        if (email is null)
        {
            return RedirectToPage("/Index");
        }

        Email = email;
        return Page();
    }
}
