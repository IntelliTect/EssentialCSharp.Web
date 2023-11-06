// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage;

public class PersonalDataModel : PageModel
{
    private readonly UserManager<EssentialCSharpWebUser> _UserManager;
    private readonly ILogger<PersonalDataModel> _Logger;

    public PersonalDataModel(
        UserManager<EssentialCSharpWebUser> userManager,
        ILogger<PersonalDataModel> logger)
    {
        _UserManager = userManager;
        _Logger = logger;
    }

    public async Task<IActionResult> OnGet()
    {
        EssentialCSharpWebUser? user = await _UserManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }

        return Page();
    }
}
