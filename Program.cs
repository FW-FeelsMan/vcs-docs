using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs;
using Microsoft.AspNetCore.Mvc;
using VCS_DOCs.Data.Hubs;
using VCS_DOCs.Services.User;
using VCS_DOCs.Services.Upload;
using VCS_DOCs.Utilities;
using VCS_DOCs.Services;
using VCS_DOCs.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<UserServiceManager>();
builder.Services.AddTransient<UserFileUploadService>();

builder.Services.AddRazorPages(options => {
	options.Conventions.ConfigureFilter(new AutoValidateAntiforgeryTokenAttribute());
})
.AddJsonOptions(jsonOptions => {
	jsonOptions.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
	jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = null;
});
builder.Services.Configure<UserDataPathOptions>(options =>
{
	options.BasePath = Path.Combine(builder.Environment.ContentRootPath, "Data", "userData");
});
builder.Services.AddDbContext<ApplicationDbContext>(options =>
	options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
);

builder.Services.AddSignalR(options => {
	options.EnableDetailedErrors = true;
})
.AddHubOptions<UserStatusHub>(options => {
	options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<IUserService, UserService>();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
	options.IdleTimeout = TimeSpan.FromMinutes(30);
	options.Cookie.HttpOnly = true;
	options.Cookie.IsEssential = true;
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
	.AddCookie(options =>
	{
		options.Cookie.HttpOnly = true;
		options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
		options.Cookie.SameSite = SameSiteMode.Lax;
		options.LoginPath = "/Login";
		options.AccessDeniedPath = "/Login";
	});

builder.Services.AddAntiforgery(options =>
{
	options.HeaderName = "X-CSRF-TOKEN";
});

builder.Services.AddSingleton<IUserIdProvider, CustomUserIdProvider>();
builder.Services.AddSingleton<UserServiceManager>();

builder.Services.AddCors(options =>
{
	options.AddPolicy("AllowAll", policy =>
	{
		policy.AllowAnyOrigin()
			  .AllowAnyMethod()
			  .AllowAnyHeader();
	});
});

builder.Services.AddMemoryCache();
builder.Services.AddControllersWithViews()
	.AddRazorRuntimeCompilation();

builder.Services.AddSingleton<FileUploadTaskService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<FileUploadTaskService>());
builder.Services.AddScoped<IStorageQuotaService, EfStorageQuotaService>();
builder.WebHost.ConfigureKestrel(options =>
{
	options.Limits.MaxRequestBodySize = 10L * 1024 * 1024 * 1024;
});


builder.Services.Configure<IpRateLimitOptions>(options =>
{
	options.GeneralRules =
	[
		new RateLimitRule
		{
			Endpoint = "*",
			Limit = 50,
			Period = "10s"
		},
		new RateLimitRule
		{
			Endpoint = "/hub/*",
			Limit = 0,
			Period = "1s"
		}
	];
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseSession();
app.UseRouting();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

app.MapHub<UserStatusHub>("/Data/userStatusHub");
app.MapHub<UserStorageHub>("/userStorageHub");

app.MapRazorPages();

var userServiceManager = app.Services.GetRequiredService<UserServiceManager>();
app.Run();