//D:\Unity\VCS-DOCs\VCS-DOCs.Support\Program.cs
using System.Net.Security;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Configuration;
using VCS_DOCs.Data;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Support.Hubs;
using VCS_DOCs.Support.Infrastructure.Auth;
using VCS_DOCs.Support.Infrastructure.Provision;
using VCS_DOCs.Support.Integration;
using VCS_DOCs.TaskEngine;
using VCS_DOCs.Core.Notifications;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddDbContext<ApplicationDbContext>(o =>
        {
            var cs = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=VCSDocs.db";
            if (!cs.Contains("Cache=", StringComparison.OrdinalIgnoreCase)) cs += ";Cache=Shared";
            if (!cs.Contains("Pooling=", StringComparison.OrdinalIgnoreCase)) cs += ";Pooling=True";
            if (!cs.Contains("Default Timeout=", StringComparison.OrdinalIgnoreCase)) cs += ";Default Timeout=60";
            o.UseSqlite(cs, x => x.MigrationsAssembly("VCS-DOCs.Web"));
        });

        builder.Services.AddControllers().ConfigureApplicationPartManager(apm =>
        {
            var dead = apm.ApplicationParts.Where(p => p.Name == "VCS-DOCs.Web").ToList();
            foreach (var part in dead) apm.ApplicationParts.Remove(part);
        });

        builder.Services
            .AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<RussianIdentityErrorDescriber>();

        builder.Services.AddScoped<IPasswordHasher<User>>(_ =>
            new PasswordHasher<User>(Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3
            })));

        builder.Services.AddSignalR(o => { o.EnableDetailedErrors = true; });

        builder.Services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = ".VcsDocs.Support.Auth";
            o.LoginPath = "/Account/LoginSupport";
            o.AccessDeniedPath = "/Errors/403";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.None;
            o.Events = new CookieAuthenticationEvents
            {
                OnValidatePrincipal = async ctx =>
                {
                    var userId = ctx.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    var sid = ctx.Principal.FindFirst("support_sid")?.Value;
                    if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(sid))
                    {
                        ctx.RejectPrincipal();
                        await ctx.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                        return;
                    }
                }
            };
        });

        builder.Services.Configure<UserDataPathOptions>(builder.Configuration.GetSection("UserDataPath"));

        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("SupportOnly", p => p.RequireRole(Roles.SupportAdmin, Roles.SupportAgent));
            o.AddPolicy("SupportDeskAccess", p => p.RequireRole(Roles.SupportAdmin, Roles.SupportAgent, Roles.BaseUser));
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy("api-burst", http =>
            {
                string partitionKey =
                    http.User?.Identity?.IsAuthenticated == true
                        ? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anon"
                        : http.Connection.RemoteIpAddress?.ToString() ?? "anon";

                return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 10,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 100,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(2),
                    TokensPerPeriod = 10,
                    AutoReplenishment = true
                });
            });
        });

        builder.Services.AddRazorPages(o =>
        {
            o.Conventions.AuthorizeFolder("/", "SupportDeskAccess");
            o.Conventions.AuthorizeFolder("/Content/Users", "SupportDeskAccess");
            o.Conventions.AuthorizeFolder("/Content/Operators", "SupportOnly");
            o.Conventions.AllowAnonymousToPage("/Account/LoginSupport");
            o.Conventions.AllowAnonymousToPage("/Errors/404");
            o.Conventions.AllowAnonymousToPage("/Errors/500");
        });

        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession(o =>
        {
            o.IdleTimeout = TimeSpan.FromMinutes(30);
            o.Cookie.HttpOnly = true;
            o.Cookie.IsEssential = true;
            o.Cookie.SameSite = SameSiteMode.None;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });
        builder.Services.AddMemoryCache();

        builder.Services.AddHttpClient("VDocsBridge", (sp, c) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var baseUrl = cfg["VDocs:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("VDocs:BaseUrl is missing in Support/appsettings*.json");

            c.BaseAddress = new Uri(baseUrl, UriKind.Absolute);

            var apiKey = cfg["VDocs:SupportApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
                c.DefaultRequestHeaders.Add("X-Support-ApiKey", apiKey);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (req, cert, chain, errors) =>
            {
                if (errors == SslPolicyErrors.None) return true;
                var tp = (cert as X509Certificate2)?.Thumbprint?.Replace(" ", "");
                if (tp is null) return false;
                return tp.Equals("1F1F09F62B5C4C450CA76CA1FDF2264276DFBF57", StringComparison.OrdinalIgnoreCase)
                    || tp.Equals("1179E6B4C27C5247ADB525DE245D65D7E3D73C8C", StringComparison.OrdinalIgnoreCase);
            }
        });

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

        builder.Services.AddScoped<PresenceOrchestrator>();
        builder.Services.AddScoped<IUserService, SupportUserService>();
        builder.Services.AddScoped<ISupportUserProvisioning, SupportUserProvisioning>();

        builder.Services.AddSingleton<IExternalProjectAdapter>(sp =>
            new SqliteVDocsAdapter(
                builder.Configuration.GetConnectionString("VDocsDb")
                ?? builder.Configuration.GetConnectionString("DefaultConnection")
            ));

        builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Mail"));
        builder.Services.AddSingleton<IMailSender, SmtpMailSender>();

        builder.WebHost.ConfigureKestrel((ctx, opts) =>
        {
            opts.ConfigureHttpsDefaults(https =>
            {
                var friendlyName = ctx.Configuration["Tls:DevCertFriendlyName"] ?? "VCS Dev SAN";
                using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);

                var cert = store.Certificates
                    .OfType<X509Certificate2>()
                    .Where(c => c.HasPrivateKey)
                    .Where(c => string.Equals(c.FriendlyName, friendlyName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(c => c.NotBefore)
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException($"Dev cert '{friendlyName}' не найден.");

                https.ServerCertificate = cert;
            });
        });

        if (builder.Configuration.GetValue("TaskEngine:Enabled", false))
        {
            builder.Services.AddTaskEngine(builder.Configuration);
        }

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            db.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
            db.Database.ExecuteSqlRaw("PRAGMA busy_timeout=8000;");
        }

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

            await db.Database.MigrateAsync();
            await AuthSeed.RunAsync(scope.ServiceProvider);

            foreach (var role in new[] { Roles.BaseUser, Roles.SupportAgent, Roles.SupportAdmin })
                if (!await roleMgr.RoleExistsAsync(role))
                    await roleMgr.CreateAsync(new IdentityRole(role));

            var sampleUserId = "6bbbcc2b-bcc8-4c20-b7ea-7993664339d2";
            var u = await userMgr.FindByIdAsync(sampleUserId);
            if (u != null && !await userMgr.IsInRoleAsync(u, Roles.SupportAgent))
                await userMgr.AddToRoleAsync(u, Roles.SupportAgent);
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"), branch =>
        {
            branch.UseExceptionHandler("/Errors/500");
            branch.UseStatusCodePagesWithReExecute("/Errors/{0}");
        });

        app.UseRouting();

        app.UseRateLimiter();
        app.UseSession();
        app.UseAuthentication();

        app.UseMiddleware<IdempotencyMiddleware>();


        var allowedAncestors =
     "https://vcs-docs.local:7120 https://localhost:7120 https://127.0.0.1:7120";

        app.Use(async (ctx, next) =>
        {
            bool needEmbedHeaders =
                ctx.Request.Path.StartsWithSegments("/Support", StringComparison.OrdinalIgnoreCase) ||
                ctx.Request.Path.StartsWithSegments("/Account/LoginSupport", StringComparison.OrdinalIgnoreCase);

            if (needEmbedHeaders)
            {
                ctx.Response.OnStarting(() =>
                {
                    var h = ctx.Response.Headers;
                    h.Remove("X-Frame-Options");
                    h.Remove("Content-Security-Policy");

                    h.Append("Content-Security-Policy",
                        $"frame-ancestors 'self' {allowedAncestors}");

                    return Task.CompletedTask;
                });
            }

            await next();
        });

        app.UseAuthorization();

        app.MapRazorPages();
        app.MapControllers();
        app.MapHub<SupportPresenceHub>("/hubs/userStatus");
        app.MapHub<TicketHub>("/hubs/ticket");

        app.Run();
    }
}