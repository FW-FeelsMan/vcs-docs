using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VCS_DOCs.Support.Pages.Content.Users
{
    [Authorize(Policy = "SupportDeskAccess")]
    public sealed class UserTicketThreadModel : PageModel
    {
        [FromRoute] public string Id { get; set; } = "";
        public void OnGet()
        {
        }
    }
}
