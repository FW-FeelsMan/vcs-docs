using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using VCS_DOCs.Utilities;
using VCS_DOCs.Configuration;
using Microsoft.Extensions.FileProviders;
using StackExchange.Profiling;
using StackExchange.Profiling.Storage;
using Microsoft.Extensions.Caching.Memory;
using VCS_DOCs.Data.Hubs;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http.Features;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Data;
using Microsoft.AspNetCore.Identity;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Upload.Core;
using VCS_DOCs.Infrastructure.Services.Storage;
using VCS_DOCs.Core.Interfaces;
using VCS_DOCs.Services;
using VCS_DOCs.Upload.Core.Services;


var builder = WebApplication.CreateBuilder(args);

// === Services ===
builder.Services.AddRazorPages(options =>
{
	options.Conventions.ConfigureFilter(new AutoValidateAntiforgeryTokenAttribute());
})
.AddJsonOptions(jsonOptions =>
{
	jsonOptions.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
	jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = null;
});

var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

builder.Services.Configure<UserDataPathOptions>(options =>
{
	options.BasePath = Path.Combine(projectRoot, "Data", "userData");
});


builder.Services.Configure<FormOptions>(options =>
{
	options.MultipartBodyLengthLimit = long.MaxValue;
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
	options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
);
builder.Services.AddIdentity<User, IdentityRole>()
	.AddEntityFrameworkStores<ApplicationDbContext>()
	.AddDefaultTokenProviders();

builder.Services.AddSignalR(options =>
{
	options.EnableDetailedErrors = true;
});

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
	options.IdleTimeout = TimeSpan.FromMinutes(30);
	options.Cookie.HttpOnly = true;
	options.Cookie.IsEssential = true;
});

builder.Services.ConfigureApplicationCookie(options =>
{
	options.Cookie.HttpOnly = true;
	options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
	options.Cookie.SameSite = SameSiteMode.Lax;
	options.LoginPath = "/Login";
	options.AccessDeniedPath = "/Login";
	options.ExpireTimeSpan = TimeSpan.FromDays(7);
	options.SlidingExpiration = true;
});


builder.Services.AddAntiforgery(options =>
{
	options.HeaderName = "X-CSRF-TOKEN";
});

builder.Services.AddSingleton<IUserIdProvider, CustomUserIdProvider>();

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

builder.Services.AddMiniProfiler(options =>
{
	options.RouteBasePath = "/profiler";
	options.ShouldProfile = _ => builder.Environment.IsDevelopment();
	options.ResultsAuthorize = _ => true;
});
builder.Services.AddScoped<IUploadDbContext>(provider =>
	(IUploadDbContext)provider.GetRequiredService<ApplicationDbContext>());

builder.Services.AddScoped<IFileStorageService, PhysicalFileStorageService>();
builder.Services.AddScoped<UploadManager>();


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
builder.Services.AddScoped<IUserFileService, UserFileService>();

builder.Logging.ClearProviders(); 
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);
var app = builder.Build();
var userDataOptions = app.Services.GetRequiredService<IOptions<UserDataPathOptions>>().Value;
var userDataPath = userDataOptions.BasePath;

if (!Directory.Exists(userDataPath))
{
	Directory.CreateDirectory(userDataPath);
}

app.UseStaticFiles(new StaticFileOptions
{
	FileProvider = new PhysicalFileProvider(userDataPath),
	RequestPath = "/userdata"
});

// === MiniProfiler ===
var memoryCache = app.Services.GetRequiredService<IMemoryCache>();
MiniProfiler.DefaultOptions.Storage = new MemoryCacheStorage(memoryCache, TimeSpan.FromMinutes(30));

app.UseMiniProfiler();

// === Middleware ===
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseStaticFiles(new StaticFileOptions
{
	FileProvider = new PhysicalFileProvider(userDataPath),
	RequestPath = "/userdata"
});

app.UseSession();
app.UseRouting();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

// === Map ===
app.MapRazorPages();
app.MapControllers();
app.MapHub<UserStatusHub>("/Data/userStatusHub");

app.UseStatusCodePagesWithReExecute("/Errors/{0}");
app.Run();
