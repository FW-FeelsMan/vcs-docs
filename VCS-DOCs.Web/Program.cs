//D:\Unity\VCS-DOCs\VCS-DOCs.Web\Program.cs
using AspNetCoreRateLimit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
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
using VCS_DOCs.Infrastructure.Services;
using VCS_DOCs.Upload.Core.Services.Antivirus;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// === Razor Pages + JSON ===
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

// === HTTP client (если нужен мост) ===
builder.Services.AddHttpClient("VDocsBridge", client =>
{
    var baseUrl = builder.Configuration["VDocs:BaseUrl"] ?? "https://vcs-docs.support.local:7120/";
    client.BaseAddress = new Uri(baseUrl);
    var apiKey = builder.Configuration["VDocs:SupportApiKey"];
    if (!string.IsNullOrEmpty(apiKey))
        client.DefaultRequestHeaders.Add("X-Support-ApiKey", apiKey);
})
.ConfigurePrimaryHttpMessageHandler(sp =>
{
    var h = new HttpClientHandler();
    h.ServerCertificateCustomValidationCallback = (req, cert, chain, errors) =>
    {
        if (errors == SslPolicyErrors.None) return true;
        var tp = (cert as X509Certificate2)?.Thumbprint?.Replace(" ", "");
        if (tp is null) return false;
        return tp.Equals("1F1F09F62B5C4C450CA76CA1FDF2264276DFBF57", StringComparison.OrdinalIgnoreCase)
            || tp.Equals("1179E6B4C27C5247ADB525DE245D65D7E3D73C8C", StringComparison.OrdinalIgnoreCase);
    };
    return h;
});

builder.WebHost.ConfigureKestrel((ctx, opts) =>
{
    opts.ConfigureHttpsDefaults(https =>
    {
        var preferredThumb = ctx.Configuration["Tls:PreferredThumbprint"];
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

builder.Services.Configure<UserDataPathOptions>(builder.Configuration.GetSection("UserDataPathOptions"));
var configPath = builder.Configuration.GetSection("UserDataPathOptions")["BasePath"];
var absoluteUserDataPath = Path.GetFullPath(configPath ?? throw new InvalidOperationException("UserData path not found"));
builder.Services.Configure<UserDataPathOptions>(options => { options.BasePath = absoluteUserDataPath; });

// === File Upload Limits ===
builder.Services.Configure<FormOptions>(options => { options.MultipartBodyLengthLimit = long.MaxValue; });

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10L * 1024 * 1024 * 1024; // 10 GB
});

// === DB + Identity ===
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<User, IdentityRole>()
    .AddErrorDescriber<RussianIdentityErrorDescriber>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddScoped<IPasswordHasher<User>>(_ =>
    new PasswordHasher<User>(
        Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3
        })));

// Парольная политика: без обязательного спецсимвола
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

builder.Services.AddAuthentication().AddCookie();
builder.Services.AddAuthorization();

// === SignalR + Hubs ===
builder.Services.AddSignalR(o => o.EnableDetailedErrors = true);
builder.Services.AddSingleton<IUserIdProvider, CustomUserIdProvider>();

builder.Services.AddSingleton<IAntivirusScanner>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var loggerFactory = sp.GetService<ILoggerFactory>();
    var amsi = new AmsiScanner("VCS-DOCs", loggerFactory?.CreateLogger<AmsiScanner>());
    var simple = new SimpleSignaturesScanner(cfg);
    return new CompositeScanner(amsi, simple);
});

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
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromMinutes(30);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
});
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");

builder.Services.AddCors(o =>
{
    o.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// === Custom Services ===
builder.Services.AddScoped<ISharedLinkService, VCS_DOCs.Infrastructure.Services.SharedLinkService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserFileService, UserFileService>();
builder.Services.AddScoped<IUploadDbContext>(provider =>
    (IUploadDbContext)provider.GetRequiredService<ApplicationDbContext>());
builder.Services.AddScoped<IFileStorageService, PhysicalFileStorageService>();
builder.Services.AddScoped<UploadManager>();
builder.Services.AddScoped<FilePathValidator>();
builder.Services.AddScoped<IServerSettingsService, ServerSettingsService>();
builder.Services.AddScoped<IUserInfoProvider, UserInfoProvider>();

// === MVC (без профайлера) ===
builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation();

// === Rate Limiting ===
builder.Services.Configure<IpRateLimitOptions>(options =>
{
    options.GeneralRules =
    [
        new RateLimitRule { Endpoint = "*",      Limit = 50, Period = "10s" },
        new RateLimitRule { Endpoint = "/hub/*", Limit = 0,  Period = "1s" }
    ];
});

// === Connected Task-Engine ===
builder.Services.AddSingleton<TaskRunner>(sp =>
{
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
builder.Configuration.AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: true);

// === App Build ===
var app = builder.Build();

// === Ensure Identity Roles exist (BaseUser / SupportAgent / SupportAdmin) ===
using (var scope = app.Services.CreateScope())
{
    var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    foreach (var role in new[] { Roles.BaseUser, Roles.SupportAgent, Roles.SupportAdmin })
    {
        if (!await roleMgr.RoleExistsAsync(role))
            await roleMgr.CreateAsync(new IdentityRole(role));
    }
}

// === Ensure User Data Directory ===
if (!Directory.Exists(absoluteUserDataPath))
    Directory.CreateDirectory(absoluteUserDataPath);

// === Status code pages (pretty) ===
app.UseHttpsRedirection();

app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"), branch =>
{
    branch.UseStatusCodePagesWithReExecute("/Errors/{0}");
});

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

// === Static Files ===
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(absoluteUserDataPath),
    RequestPath = "/userdata"
});
app.UseStaticFiles(); // wwwroot

// === (MiniProfiler удалён) ===

// === Rest of pipeline ===
app.UseSession();
app.UseRouting();
app.UseCors("AllowAll");

// ВАЖНО: один раз
app.UseAuthentication();

// 1) На /Login — чистим куки и запрещаем кэш
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.Equals("/Login", StringComparison.OrdinalIgnoreCase))
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

    // Любой HTML — no-cache
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

// 2) Проверка «живости» только на обычных страницах (не API, не статика, не /Login)
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
        var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var sid = ctx.User.FindFirst("web_sid")?.Value;

        var db = ctx.RequestServices.GetRequiredService<ApplicationDbContext>();
        var u = !string.IsNullOrEmpty(userId)
            ? await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId)
            : null;

        bool hasSidClaim = !string.IsNullOrEmpty(sid);
        bool sidMismatch = hasSidClaim && !string.IsNullOrEmpty(u?.JwtId) && !string.Equals(u!.JwtId, sid, StringComparison.Ordinal);

        bool softInvalid =
            u == null ||
            u.IsDeleted ||
            u.Access == 0 ||
            u.StatusOnline != 1 ||
            (!hasSidClaim && !string.IsNullOrEmpty(u?.JwtId));

        if (sidMismatch)
        {
            await ctx.SignOutAsync();
            ctx.Response.Redirect("/Login?message=session_terminated");
            return;
        }
        if (softInvalid)
        {
            await ctx.SignOutAsync();
            ctx.Response.Redirect("/Login");
            return;
        }
    }

    await next();
});

app.UseAuthorization();

// === Routes ===
app.MapRazorPages();
app.MapControllers();
app.MapHub<UserStatusHub>("/Data/userStatusHub");

app.Run();
