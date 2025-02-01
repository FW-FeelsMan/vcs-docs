using System.IO;
using Microsoft.AspNetCore.Mvc;

namespace VCS_DOCs.Utilities
{
	public class FileController : Controller
	{
		public IActionResult Index()
		{
			string[] files = Directory.GetFiles("C:/xampp/htdocs/PTK-DOCs-Shared");
			return View(files);
		}

	}
}
