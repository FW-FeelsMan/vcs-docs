using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;

namespace VCS_DOCs.Pages.Content
{
	public class profile_pageModel : PageModel
	{
		private readonly ApplicationDbContext _context;

		public User CurrentUser { get; set; }

		public profile_pageModel(ApplicationDbContext context)
		{
			_context = context;
		}

		public async Task OnGetAsync()
		{
			string username = User.Identity?.Name;
			if (!string.IsNullOrEmpty(username))
			{
				CurrentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
			}
		}
	}
}
