using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VCS_DOCs.Support.Pages.Content.Operators
{
    [Authorize(Policy = "SupportOnly")]
    public class AccountsModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
