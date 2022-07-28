using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Pages
{
    public class TermsOfServiceModel : PageModel
    {
        private readonly ILogger<TermsOfServiceModel> _logger;

        public TermsOfServiceModel(ILogger<TermsOfServiceModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
        }
    }
}
