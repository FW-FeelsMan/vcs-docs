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
using VCS_DOCs.Infrastructure.Services;
using VCS_DOCs.Upload.Core.Services.Antivirus;
using ClamAV.Net.Client;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

// === Razor Pages + JSON ===
builder.Services
    .AddRazorPages(options =>
    {
        options.Conventions.ConfigureFilter(new AutoValidateAntiforgeryTokenAttribute());
    })
    // Один файл Pages/Errors/404.cshtml обслуживает все коды: /Errors/{code}
    .AddRazorPagesOptions(o =>
    {
        o.Conventions.AddPageRoute("/Errors/404", "/Errors/{code:int}");
    })
    .AddJsonOptions(jsonOptions =>
    {
        jsonOptions.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

// === User Data Path ===
//builder.Services.AddHttpClient();
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

    // Разрешаем только НАШИ dev-серты по отпечатку (RemoteCertificateNameMismatch нам тогда не страшен).
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
    options.Limits.MaxRequestBodySize = 10L * 1024 * 1024 * 1024; // 10 GB
});

// === DB + Identity ===
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddIdentity<User, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddAuthentication().AddCookie(); // ensure cookie auth present
builder.Services.AddAuthorization();

// === SignalR + Hubs ===
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
});
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

// === Ensure User Data Directory ===
if (!Directory.Exists(absoluteUserDataPath))
    Directory.CreateDirectory(absoluteUserDataPath);

// === Status code pages (pretty) ===
app.UseHttpsRedirection();

// Для всех НЕ-API отдаём красивую Razor-страницу /Errors/{code}
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"), branch =>
{
    branch.UseStatusCodePagesWithReExecute("/Errors/{0}");
});
// Для браузерной навигации на публичные API-ссылки тоже хотим Razor-страницу
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

// === MiniProfiler ===
var memoryCache = app.Services.GetRequiredService<IMemoryCache>();
MiniProfiler.DefaultOptions.Storage = new MemoryCacheStorage(memoryCache, TimeSpan.FromMinutes(30));
app.UseMiniProfiler();

// === Rest of pipeline ===
app.UseSession();
app.UseRouting();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

// === Routes ===
app.MapRazorPages();
app.MapControllers();
app.MapHub<UserStatusHub>("/Data/userStatusHub");

app.Run();
