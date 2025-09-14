using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VCS_DOCs.Support.Pages.Content.Operators
{
    [Authorize(Policy = "SupportOnly")]
    public sealed class TicketThreadModel : PageModel
    {
        [FromRoute] public string Id { get; set; } = "";
        [FromQuery]
        public string? Subject
        {
            get; set;
        }
        [FromQuery]
        public string? From
        {
            get; set;
        }

        public void OnGet()
        {
        }
    }
}
