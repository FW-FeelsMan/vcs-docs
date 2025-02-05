using Microsoft.EntityFrameworkCore;
using VCS_DOCs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
	options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
);

builder.Services.AddDistributedMemoryCache();  
builder.Services.AddSession(options =>
{
	options.IdleTimeout = TimeSpan.FromMinutes(30);  // Время жизни сессии
	options.Cookie.HttpOnly = true;  
	options.Cookie.IsEssential = true;  
});

builder.Services.AddAuthentication("Identity.Application")
	.AddCookie("Identity.Application", options =>
	{
		options.LoginPath = "/Login"; 
		options.LogoutPath = "/Logout"; 
	});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error");
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseSession();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();
