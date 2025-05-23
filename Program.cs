//Program.cs
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
using Microsoft.Extensions.FileProviders;
using StackExchange.Profiling;


var builder = WebApplication.CreateBuilder(args);

// ====== Services ======
builder.Services.AddSingleton<UserServiceManager>();
builder.Services.AddTransient<UserFileUploadService>();

builder.Services.AddRazorPages(options =>
{
	options.Conventions.ConfigureFilter(new AutoValidateAntiforgeryTokenAttribute());
})
.AddJsonOptions(jsonOptions =>
{
	jsonOptions.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
	jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = null;
});

builder.Services.Configure<UserDataPathOptions>(options =>
{
	options.BasePath = Path.Combine(builder.Environment.ContentRootPath, "Data", "userData");
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
	options.MultipartBodyLengthLimit = 15L * 1024 * 1024; // 15 MB
	options.MemoryBufferThreshold = 1024 * 1024; // 1 MB
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
	options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
);

builder.Services.AddSignalR(options =>
{
	options.EnableDetailedErrors = true;
})
.AddHubOptions<UserStatusHub>(options =>
{
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
	options.TrackConnectionOpenClose = true;
	options.ColorScheme = StackExchange.Profiling.ColorScheme.Dark;
}).AddEntityFramework();


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

// ====== Middlewares ======

app.UseHttpsRedirection();

// (1) Подключаем обычные статики (css, js, images)
app.UseStaticFiles();

// (2) Подключаем юзерские файлы отдельно
var userDataPath = Path.Combine(builder.Environment.ContentRootPath, "Data", "userData");

if (!Directory.Exists(userDataPath))
{
	Directory.CreateDirectory(userDataPath);
}

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

app.MapHub<UserStatusHub>("/Data/userStatusHub");
app.MapHub<UserStorageHub>("/userStorageHub");
app.MapRazorPages();

using (var scope = app.Services.CreateScope())
{
	var quotaService = scope.ServiceProvider.GetRequiredService<IStorageQuotaService>();
	await quotaService.CleanUpBrokenReservationsAsync();
}

var userServiceManager = app.Services.GetRequiredService<UserServiceManager>();

app.Run();