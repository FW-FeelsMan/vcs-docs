using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VCS_DOCs.Support.Pages.Content.Users
{
    [Authorize(Policy = "SupportDeskAccess")]
    public class UserTicketModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
