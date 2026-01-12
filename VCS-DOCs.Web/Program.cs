// D:\Unity\VCS-DOCs\VCS-DOCs.Web\Program.cs
using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using System.Net.Security;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using VCS_DOCs.Configuration;
using VCS_DOCs.Core.Interfaces;
using VCS_DOCs.Data.Hubs;
using VCS_DOCs.Infrastructure;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Infrastructure.Services;
using VCS_DOCs.Infrastructure.Services.Storage;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Services;
using VCS_DOCs.Upload.Core;
using VCS_DOCs.Upload.Core.Services;
using VCS_DOCs.Upload.Core.Services.Antivirus;
using VCS_DOCs.Upload.Core.Services.Tasks;
using VCS_DOCs.Utilities;

var builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------------------------------------------
// MVC / Razor Pages
// --------------------------------------------------------------------------------------
builder.Services
	.AddRazorPages(options =>
	{
		options.Conventions.ConfigureFilter(new AutoValidateAntiforgeryTokenAttribute());
	})
	.AddRazorPagesOptions(o =>
	{
		o.Conventions.AddPageRoute("/Errors/404", "/Errors/{code:int}");
	})
	.AddJsonOptions(jsonOptions =>
	{
		jsonOptions.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
		jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = null;
	});

builder.Services
	.AddControllersWithViews()
	.AddRazorRuntimeCompilation();

// --------------------------------------------------------------------------------------
// TLS / Kestrel limits
// --------------------------------------------------------------------------------------
builder.WebHost.ConfigureKestrel((ctx, opts) =>
{
	opts.Limits.MaxRequestBodySize = 10L * 1024 * 1024 * 1024;

	opts.ConfigureHttpsDefaults(https =>
	{
		var preferredThumb = ctx.Configuration["Tls:PreferredThumbprint"];
		if (string.IsNullOrWhiteSpace(preferredThumb))
			throw new InvalidOperationException("Tls:PreferredThumbprint is not configured.");

		using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
		store.Open(OpenFlags.ReadOnly);

		var cert = store.Certificates
			.OfType<X509Certificate2>()
			.FirstOrDefault(c => c.HasPrivateKey &&
								 c.Thumbprint.Equals(preferredThumb, StringComparison.OrdinalIgnoreCase))
			?? throw new InvalidOperationException($"Dev cert with thumbprint {preferredThumb} not found.");

		https.ServerCertificate = cert;
	});
});

builder.Services.Configure<FormOptions>(options =>
{
	options.MultipartBodyLengthLimit = long.MaxValue;
});

// --------------------------------------------------------------------------------------
// UserDataPath
// --------------------------------------------------------------------------------------
builder.Services.Configure<UserDataPathOptions>(builder.Configuration.GetSection("UserDataPathOptions"));

var configPath = builder.Configuration.GetSection("UserDataPathOptions")["BasePath"];
var absoluteUserDataPath = Path.GetFullPath(configPath ?? throw new InvalidOperationException("UserData path not found"));

builder.Services.Configure<UserDataPathOptions>(options =>
{
	options.BasePath = absoluteUserDataPath;
});

// --------------------------------------------------------------------------------------
// DB
// --------------------------------------------------------------------------------------
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
	var cs = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=VCSDocs.db";
	if (!cs.Contains("Cache=", StringComparison.OrdinalIgnoreCase)) cs += ";Cache=Shared";
	if (!cs.Contains("Pooling=", StringComparison.OrdinalIgnoreCase)) cs += ";Pooling=True";
	if (!cs.Contains("Default Timeout=", StringComparison.OrdinalIgnoreCase)) cs += ";Default Timeout=60";
	options.UseSqlite(cs);
});

// --------------------------------------------------------------------------------------
// Identity / Auth
// --------------------------------------------------------------------------------------
builder.Services
	.AddIdentity<User, IdentityRole>()
	.AddErrorDescriber<RussianIdentityErrorDescriber>()
	.AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.Configure<IdentityOptions>(opt =>
{
	opt.Password.RequireDigit = true;
	opt.Password.RequireLowercase = true;
	opt.Password.RequireUppercase = true;
	opt.Password.RequireNonAlphanumeric = false;
	opt.Password.RequiredLength = 6;
	opt.Password.RequiredUniqueChars = 1;
});

builder.Services.Configure<PasswordHasherOptions>(o =>
{
	o.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
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

builder.Services.AddAuthorization();

// --------------------------------------------------------------------------------------
// Session / Antiforgery
// --------------------------------------------------------------------------------------
builder.Services.AddSession(o =>
{
	o.IdleTimeout = TimeSpan.FromMinutes(30);
	o.Cookie.HttpOnly = true;
	o.Cookie.IsEssential = true;
});

builder.Services.AddAntiforgery(o =>
{
	o.HeaderName = "X-CSRF-TOKEN";
});

// --------------------------------------------------------------------------------------
// CORS
// --------------------------------------------------------------------------------------
builder.Services.AddCors(o =>
{
	o.AddPolicy("AllowAll", p =>
		p.AllowAnyOrigin()
		 .AllowAnyMethod()
		 .AllowAnyHeader());
});

// --------------------------------------------------------------------------------------
// SignalR
// --------------------------------------------------------------------------------------
builder.Services.AddSignalR(o => o.EnableDetailedErrors = true);
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, CustomUserIdProvider>();

// --------------------------------------------------------------------------------------
// HttpClient Bridge
// --------------------------------------------------------------------------------------
builder.Services.AddHttpClient("VDocsBridge", client =>
{
	var baseUrl = builder.Configuration["VDocs:BaseUrl"] ?? "https://vcs-docs.support.local:7120/";
	client.BaseAddress = new Uri(baseUrl);

	var apiKey = builder.Configuration["VDocs:SupportApiKey"];
	if (!string.IsNullOrEmpty(apiKey))
		client.DefaultRequestHeaders.Add("X-Support-ApiKey", apiKey);
})
.ConfigurePrimaryHttpMessageHandler(_ =>
{
	var h = new HttpClientHandler();
	h.ServerCertificateCustomValidationCallback = (_, cert, _, errors) =>
	{
		if (errors == SslPolicyErrors.None) return true;

		var tp = (cert as X509Certificate2)?.Thumbprint?.Replace(" ", "");
		if (tp is null) return false;

		return tp.Equals("1F1F09F62B5C4C450CA76CA1FDF2264276DFBF57", StringComparison.OrdinalIgnoreCase)
			|| tp.Equals("1179E6B4C27C5247ADB525DE245D65D7E3D73C8C", StringComparison.OrdinalIgnoreCase);
	};
	return h;
});

// --------------------------------------------------------------------------------------
// App services
// --------------------------------------------------------------------------------------
builder.Services.AddScoped<ISharedLinkService, VCS_DOCs.Infrastructure.Services.SharedLinkService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserFileService, UserFileService>();
builder.Services.AddScoped<IUploadDbContext>(provider => (IUploadDbContext)provider.GetRequiredService<ApplicationDbContext>());
builder.Services.AddScoped<IFileStorageService, PhysicalFileStorageService>();
builder.Services.AddScoped<UploadManager>();
builder.Services.AddScoped<FilePathValidator>();
builder.Services.AddScoped<IServerSettingsService, ServerSettingsService>();
builder.Services.AddScoped<IUserInfoProvider, UserInfoProvider>();

builder.Services.AddSingleton(sp =>
{
	var options = sp.GetRequiredService<IOptions<UserDataPathOptions>>();
	return new UserStoragePaths(options.Value.BasePath);
});

builder.Services.AddSingleton<ChunkHashService>(sp =>
{
	var userPaths = sp.GetRequiredService<UserStoragePaths>();
	return new ChunkHashService(userPaths);
});

builder.Services.AddSingleton<IAntivirusScanner>(sp =>
{
	var cfg = sp.GetRequiredService<IConfiguration>();
	var loggerFactory = sp.GetService<ILoggerFactory>();
	var userData = sp.GetRequiredService<IOptions<UserDataPathOptions>>().Value.BasePath;

	var avTemp = Path.Combine(userData, "_tmp", "av");
	Directory.CreateDirectory(avTemp);

	var amsi = new AmsiScanner("VCS-DOCs", loggerFactory?.CreateLogger<AmsiScanner>());
	var simple = new SimpleSignaturesScanner(cfg);

	return new CompositeScanner(avTemp, amsi, simple);
});

// --------------------------------------------------------------------------------------
// Rate limit
// --------------------------------------------------------------------------------------
builder.Services.Configure<IpRateLimitOptions>(options =>
{
	options.GeneralRules =
	[
		new RateLimitRule { Endpoint = "*", Limit = 50, Period = "10s" },
		new RateLimitRule { Endpoint = "/hub/*", Limit = 0, Period = "1s" }
	];
});

// --------------------------------------------------------------------------------------
// Logging / config
// --------------------------------------------------------------------------------------
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

builder.Configuration.AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: true);

// --------------------------------------------------------------------------------------
// Build
// --------------------------------------------------------------------------------------
var app = builder.Build();

// --------------------------------------------------------------------------------------
// Ensure folders / DB pragmas / roles seed
// --------------------------------------------------------------------------------------
Directory.CreateDirectory(absoluteUserDataPath);

using (var scope = app.Services.CreateScope())
{
	var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

	db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
	db.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
	db.Database.ExecuteSqlRaw("PRAGMA busy_timeout=8000;");

	var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
	foreach (var role in new[] { Roles.BaseUser, Roles.SupportAgent, Roles.SupportAdmin })
	{
		if (!await roleMgr.RoleExistsAsync(role))
			await roleMgr.CreateAsync(new IdentityRole(role));
	}
}

// --------------------------------------------------------------------------------------
// Pipeline
// --------------------------------------------------------------------------------------
// ВАЖНО для tuna/ngrok/CF Tunnel: корректная схема/host/ip из reverse proxy
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
	ForwardedHeaders =
		ForwardedHeaders.XForwardedFor |
		ForwardedHeaders.XForwardedProto |
		ForwardedHeaders.XForwardedHost,
	ForwardLimit = null,
	KnownNetworks = { },
	KnownProxies = { }
});

// HTTPS redirect только вне Development
if (!app.Environment.IsDevelopment())
{
	app.UseHttpsRedirection();
}

// StatusCodePages only for non-API
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"), branch =>
{
	branch.UseStatusCodePagesWithReExecute("/Errors/{0}");
});

// For public upload html requests
app.UseWhen(ctx =>
{
	if (!ctx.Request.Path.StartsWithSegments("/api/Upload/public", out _)) return false;
	if (!HttpMethods.IsGet(ctx.Request.Method)) return false;
	var accept = ctx.Request.Headers["Accept"].ToString();
	return string.IsNullOrEmpty(accept)
		   || accept.Contains("text/html", StringComparison.OrdinalIgnoreCase)
		   || accept.Contains("*/*", StringComparison.OrdinalIgnoreCase);
},
branch =>
{
	branch.UseStatusCodePagesWithReExecute("/Errors/{0}");
});

// Static files (userdata first)
app.UseStaticFiles(new StaticFileOptions
{
	FileProvider = new PhysicalFileProvider(absoluteUserDataPath),
	RequestPath = "/userdata"
});
app.UseStaticFiles();

app.UseSession();

app.UseRouting();
app.UseCors("AllowAll");

app.UseAuthentication();

// No-cache for Login and HTML responses
app.Use(async (ctx, next) =>
{
	bool isLogin = ctx.Request.Path.Equals("/Login", StringComparison.OrdinalIgnoreCase);

	if (isLogin)
	{
		if (ctx.User?.Identity?.IsAuthenticated == true)
		{
			await ctx.SignOutAsync();
			ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
		}

		ctx.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
		ctx.Response.Headers["Pragma"] = "no-cache";
		ctx.Response.Headers["Expires"] = "0";
	}

	ctx.Response.OnStarting(() =>
	{
		if (ctx.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true)
		{
			ctx.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
			ctx.Response.Headers["Pragma"] = "no-cache";
			ctx.Response.Headers["Expires"] = "0";
		}
		return Task.CompletedTask;
	});

	await next();
});

// Antiforgery header alias
app.Use(async (ctx, next) =>
{
	if (!ctx.Request.Headers.ContainsKey("X-CSRF-TOKEN") &&
		ctx.Request.Headers.TryGetValue("RequestVerificationToken", out var legacy) &&
		!string.IsNullOrWhiteSpace(legacy))
	{
		ctx.Request.Headers["X-CSRF-TOKEN"] = legacy.ToString();
	}

	await next();
});

app.UseMiddleware<IdempotencyMiddleware>();

// ЕДИНЫЙ guard для:
// 1) session termination (web_sid vs JwtId)
// 2) deleted accounts (IsDeleted)
app.Use(async (ctx, next) =>
{
	var path = ctx.Request.Path;

	bool isLogin = path.Equals("/Login", StringComparison.OrdinalIgnoreCase);
	bool isApi = path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

	bool isStatic =
		path.StartsWithSegments("/css", StringComparison.OrdinalIgnoreCase) ||
		path.StartsWithSegments("/js", StringComparison.OrdinalIgnoreCase) ||
		path.StartsWithSegments("/images", StringComparison.OrdinalIgnoreCase) ||
		path.StartsWithSegments("/lib", StringComparison.OrdinalIgnoreCase) ||
		path.StartsWithSegments("/fonts", StringComparison.OrdinalIgnoreCase) ||
		path.StartsWithSegments("/userdata", StringComparison.OrdinalIgnoreCase) ||
		path.StartsWithSegments("/_framework", StringComparison.OrdinalIgnoreCase);

	if (!isLogin && !isApi && !isStatic && ctx.User?.Identity?.IsAuthenticated == true)
	{
		var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
		var sid = ctx.User.FindFirst("web_sid")?.Value;

		if (!string.IsNullOrWhiteSpace(userId))
		{
			var db = ctx.RequestServices.GetRequiredService<ApplicationDbContext>();

			var u = await db.Users
				.AsNoTracking()
				.Where(x => x.Id == userId)
				.Select(x => new { x.IsDeleted, x.JwtId })
				.FirstOrDefaultAsync();

			if (u == null || u.IsDeleted)
			{
				await ctx.SignOutAsync();
				ctx.Response.Redirect("/Login?reason=deleted");
				return;
			}

			bool hasSidClaim = !string.IsNullOrEmpty(sid);
			bool sidMismatch = hasSidClaim && !string.IsNullOrEmpty(u.JwtId) && !string.Equals(u.JwtId, sid, StringComparison.Ordinal);

			if (sidMismatch)
			{
				await ctx.SignOutAsync();
				ctx.Response.Redirect("/Login?message=session_terminated");
				return;
			}
		}
	}

	await next();
});

app.UseAuthorization();

// Endpoints
app.MapRazorPages();
app.MapControllers();
app.MapHub<UserStatusHub>("/Data/userStatusHub");

app.Run();