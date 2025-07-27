using AspNetCoreRateLimit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using StackExchange.Profiling;
using StackExchange.Profiling.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using VCS_DOCs.Utilities;
using VCS_DOCs.Configuration;
using VCS_DOCs.Data;
using VCS_DOCs.Data.Hubs;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Infrastructure.Services.Storage;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Upload.Core;
using VCS_DOCs.Core.Interfaces;
using VCS_DOCs.Services;
using VCS_DOCs.Upload.Core.Services;
using VCS_DOCs.TaskEngine;
using VCS_DOCs.Upload.Core.Services.Tasks;
using Microsoft.Extensions.Options;
using VCS_DOCs.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// === Razor Pages + JSON ===
builder.Services.AddRazorPages(options =>
{
	options.Conventions.ConfigureFilter(new AutoValidateAntiforgeryTokenAttribute());
})
.AddJsonOptions(jsonOptions =>
{
	jsonOptions.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
	jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = null;
});

// === User Data Path ===
builder.Services.Configure<UserDataPathOptions>(builder.Configuration.GetSection("UserDataPathOptions"));

var configPath = builder.Configuration.GetSection("UserDataPathOptions")["BasePath"];
var absoluteUserDataPath = Path.GetFullPath(configPath ?? throw new InvalidOperationException("UserData path not found"));

builder.Services.Configure<UserDataPathOptions>(options =>
{
	options.BasePath = absoluteUserDataPath;
});

// === File Upload Limits ===
builder.Services.Configure<FormOptions>(options =>
{
	options.MultipartBodyLengthLimit = long.MaxValue;
});

builder.WebHost.ConfigureKestrel(options =>
{
	options.Limits.MaxRequestBodySize = 10L * 1024 * 1024 * 1024;
});

// === DB + Identity ===
builder.Services.AddDbContext<ApplicationDbContext>(options =>
	options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddIdentity<User, IdentityRole>()
	.AddEntityFrameworkStores<ApplicationDbContext>()
	.AddDefaultTokenProviders();

// === SignalR + Hubs ===
builder.Services.AddSignalR(options =>
{
	options.EnableDetailedErrors = true;
});
builder.Services.AddSingleton<IUserIdProvider, CustomUserIdProvider>();

// === App Cookies ===
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

// === Session + Middleware ===
builder.Services.AddSession(options =>
{
	options.IdleTimeout = TimeSpan.FromMinutes(30);
	options.Cookie.HttpOnly = true;
	options.Cookie.IsEssential = true;
});
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

builder.Services.AddCors(options =>
{
	options.AddPolicy("AllowAll", policy =>
	{
		policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
	});
});

// === Custom Services ===
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserFileService, UserFileService>();
builder.Services.AddScoped<IUploadDbContext>(provider =>
	(IUploadDbContext)provider.GetRequiredService<ApplicationDbContext>());
builder.Services.AddScoped<IFileStorageService, PhysicalFileStorageService>();
builder.Services.AddScoped<UploadManager>();
builder.Services.AddScoped<FilePathValidator>();
builder.Services.AddScoped<IServerSettingsService, ServerSettingsService>();
builder.Services.AddScoped<IUserInfoProvider, UserInfoProvider>();

// === MVC + Profiling ===
builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation();
builder.Services.AddMiniProfiler(options =>
{
	options.RouteBasePath = "/profiler";
	options.ShouldProfile = _ => builder.Environment.IsDevelopment();
	options.ResultsAuthorize = _ => true;
});

// === Rate Limiting ===
builder.Services.Configure<IpRateLimitOptions>(options =>
{
	options.GeneralRules =
	[
		new RateLimitRule { Endpoint = "*", Limit = 50, Period = "10s" },
		new RateLimitRule { Endpoint = "/hub/*", Limit = 0, Period = "1s" }
	];
});
// === Connected Task-Engine ===
builder.Services.AddSingleton<TaskRunner>(sp => {
	var config = sp.GetRequiredService<IConfiguration>();
	var modulesPath = Path.Combine(builder.Environment.ContentRootPath, config["TaskEngineOptions:ModulesPath"]);
	return new TaskRunner(modulesPath);
});
builder.Services.AddSingleton<ChunkHashService>(sp =>
{
	var userPaths = sp.GetRequiredService<UserStoragePaths>();
	return new ChunkHashService(userPaths);
});
builder.Services.AddSingleton(sp =>
{
	var options = sp.GetRequiredService<IOptions<UserDataPathOptions>>();
	return new UserStoragePaths(options.Value.BasePath);
});

// === Logging ===
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// === App Build ===
var app = builder.Build();

// === Ensure User Data Directory ===
if (!Directory.Exists(absoluteUserDataPath))
	Directory.CreateDirectory(absoluteUserDataPath);

// === Static Files for User Data ===
app.UseStaticFiles(new StaticFileOptions
{
	FileProvider = new PhysicalFileProvider(absoluteUserDataPath),
	RequestPath = "/userdata"
});

// === MiniProfiler ===
var memoryCache = app.Services.GetRequiredService<IMemoryCache>();
MiniProfiler.DefaultOptions.Storage = new MemoryCacheStorage(memoryCache, TimeSpan.FromMinutes(30));
app.UseMiniProfiler();

// === Middleware ===
app.UseHttpsRedirection();
app.UseStaticFiles(); // wwwroot
app.UseSession();
app.UseRouting();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

// === Routes ===
app.MapRazorPages();
app.MapControllers();
app.MapHub<UserStatusHub>("/Data/userStatusHub");

// === Error Handling ===
app.UseStatusCodePagesWithReExecute("/Errors/{0}");
var cancellationTokenSource = new CancellationTokenSource();
var token = cancellationTokenSource.Token;

var inputTask = Task.Run(async () =>
{
	while (!token.IsCancellationRequested)
	{
		var command = Console.ReadLine();
		if (command?.ToLower() == "eject-ram")
		{
			await RamDiskManager.CleanupAsync();
			Console.WriteLine("RAM-диск был удалЄн вручную командой eject-ram");
		}
	}
}, token);

// —оздаЄм RAM-диск
var settingsService = app.Services.CreateScope()
	.ServiceProvider.GetRequiredService<IServerSettingsService>();

int ramSizeGb = await settingsService.GetRamDiskSizeGbAsync();

if (ramSizeGb > 0 && RamDiskManager.InitializeRamDisk(ramSizeGb))
{
	Console.WriteLine($"[RAM-DISK] создан на букве {RamDiskManager.RamDriveLetter}:");
}
else
{
	Console.WriteLine("[RAM-DISK] не создан Ч размер = 0 или всЄ плохо");
}

app.Lifetime.ApplicationStopping.Register(() =>
{
	Task.Run(async () =>
	{
		await RamDiskManager.CleanupAsync();
		Console.WriteLine("RAM-диск был удалЄн при остановке приложени€");
	}).GetAwaiter().GetResult();

	cancellationTokenSource.Cancel();
});

await app.RunAsync(); 
app.Run();
