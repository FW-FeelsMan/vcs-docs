// Program.cs
using System.Net.Security;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting; // для AddRateLimiter
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Configuration;
using VCS_DOCs.Data;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Support.Hubs;
using VCS_DOCs.Support.Infrastructure.Auth;
using VCS_DOCs.Support.Infrastructure.Email;
using VCS_DOCs.Support.Infrastructure.Provision;
using VCS_DOCs.Support.Integration;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // === DB + Identity ===
        builder.Services.AddDbContext<ApplicationDbContext>(o =>
            o.UseSqlite(
                builder.Configuration.GetConnectionString("DefaultConnection"),
                x => x.MigrationsAssembly("VCS-DOCs.Web")
            )
        );

        builder.Services.AddControllers()
            .ConfigureApplicationPartManager(apm =>
            {
                // вычищаем чужую сборку контроллеров, если вдруг подцепилась
                var dead = apm.ApplicationParts.Where(p => p.Name == "VCS-DOCs.Web").ToList();
                foreach (var part in dead) apm.ApplicationParts.Remove(part);
            });

        builder.Services
            .AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        // === SignalR ===
        builder.Services.AddSignalR(o => { o.EnableDetailedErrors = true; });

        // === Cookie-аутентификация ===
        builder.Services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = ".VcsDocs.Support.Auth";
            o.LoginPath = "/Account/LoginSupport";
            o.AccessDeniedPath = "/Errors/403";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Lax;

            //o.Events = new CookieAuthenticationEvents
            //{
            //    OnValidatePrincipal = async ctx =>
            //    {
            //        var userId = ctx.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            //        var sid = ctx.Principal.FindFirst("support_sid")?.Value;

            //        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(sid))
            //        {
            //            ctx.RejectPrincipal();
            //            await ctx.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            //            return;
            //        }

            //        var db = ctx.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            //        var row = await db.SupportUserSessions.AsNoTracking()
            //                      .FirstOrDefaultAsync(x => x.UserId == userId);

            //        var mismatch = row != null
            //                       && !string.IsNullOrEmpty(row.JwtId)
            //                       && !string.Equals(row.JwtId, sid, StringComparison.Ordinal);

            //        if (mismatch)
            //        {
            //            ctx.RejectPrincipal();
            //            await ctx.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            //        }
            //    }
            //};
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

        // === Опции путей пользователя (для аватаров и т.п.) ===
        builder.Services.Configure<UserDataPathOptions>(builder.Configuration.GetSection("UserDataPath"));

        // === Авторизация / политики ===
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("SupportOnly", p => p.RequireRole(Roles.SupportAdmin, Roles.SupportAgent));
            o.AddPolicy("SupportDeskAccess", p => p.RequireRole(Roles.SupportAdmin, Roles.SupportAgent, Roles.BaseUser));
        });

        // === Rate Limiter (для API) ===
        builder.Services.AddRateLimiter(options =>
        {
            // Политика на API: 10 запросов за 2 сек на пользователя (или IP)
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

        // === Razor Pages (ВОТ ЭТОГО НЕ ХВАТАЛО) ===
        builder.Services.AddRazorPages(o =>
        {
            // доступ к самому «деску»
            o.Conventions.AuthorizeFolder("/", "SupportDeskAccess");

            // пользовательские панели
            o.Conventions.AuthorizeFolder("/Content/Users", "SupportDeskAccess");

            // операторские панели
            o.Conventions.AuthorizeFolder("/Content/Operators", "SupportOnly");

            // анонимный доступ
            o.Conventions.AllowAnonymousToPage("/Account/LoginSupport");
            o.Conventions.AllowAnonymousToPage("/Errors/404");
            o.Conventions.AllowAnonymousToPage("/Errors/500");
        });

        // === кэш/сессии/HTTP client ===
        builder.Services.AddDistributedMemoryCache(); // для идемпотентности
        builder.Services.AddSession(o =>
        {
            o.IdleTimeout = TimeSpan.FromMinutes(30);
            o.Cookie.HttpOnly = true;
            o.Cookie.IsEssential = true;
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

        builder.Services.AddScoped<PresenceOrchestrator>();
        builder.Services.AddScoped<IUserService, SupportUserService>();
        builder.Services.AddScoped<ISupportUserProvisioning, SupportUserProvisioning>();

        builder.Services.AddSingleton<IExternalProjectAdapter>(sp =>
            new SqliteVDocsAdapter(
                builder.Configuration.GetConnectionString("VDocsDb")
                ?? builder.Configuration.GetConnectionString("DefaultConnection") // fallback
            ));
        builder.Services.Configure<VCS_DOCs.Support.Infrastructure.Email.SmtpOptions>(
            builder.Configuration.GetSection("Mail"));
        builder.Services.AddSingleton<IMailSender, VCS_DOCs.Support.Infrastructure.Mail.SmtpMailSender>();

        // === Kestrel / dev-cert ===
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

        var app = builder.Build();

        // === Инициализация БД/ролей ===
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

            // пример: гарантируем роль агенту
            var sampleUserId = "6bbbcc2b-bcc8-4c20-b7ea-7993664339d2";
            var u = await userMgr.FindByIdAsync(sampleUserId);
            if (u != null && !await userMgr.IsInRoleAsync(u, Roles.SupportAgent))
                await userMgr.AddToRoleAsync(u, Roles.SupportAgent);
        }

        // === пайплайн ===
        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"), branch =>
        {
            branch.UseExceptionHandler("/Errors/500");
            branch.UseStatusCodePagesWithReExecute("/Errors/{0}");
        });

        app.UseRouting();

        app.UseRateLimiter();     // включаем политики лимитирования
        app.UseSession();
        app.UseAuthentication();

        // идемпотентность после аутентификации (видим UserId)
        app.UseMiddleware<IdempotencyMiddleware>();

        // «single-login» проверка
        app.Use(async (ctx, next) =>
        {
            // не вмешиваемся в сам логин/ошибки, чтобы не зациклиться
            if (ctx.Request.Path.StartsWithSegments("/Account/LoginSupport", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }
            if (ctx.Request.Path.StartsWithSegments("/Support/Request", StringComparison.OrdinalIgnoreCase))
            {
                // X-Frame-Options блокирует кросс-доменные фреймы — убираем
                ctx.Response.Headers.Remove("X-Frame-Options");

                var csp = "frame-ancestors 'self' https://vcs-docs.local:7120";
                ctx.Response.Headers["Content-Security-Policy"] = csp;
            }

            if (ctx.User?.Identity?.IsAuthenticated == true)
            {
                var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var sid = ctx.User.FindFirst("support_sid")?.Value;

                if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(sid))
                {
                    var db = ctx.RequestServices.GetRequiredService<ApplicationDbContext>();
                    var row = await db.SupportUserSessions.AsNoTracking()
                                    .FirstOrDefaultAsync(x => x.UserId == userId);

                    var mismatch = row != null && !string.IsNullOrEmpty(row.JwtId)
                                   && !string.Equals(row.JwtId, sid, StringComparison.Ordinal);

                    if (mismatch)
                    {
                        await ctx.SignOutAsync(IdentityConstants.ApplicationScheme);

                        var returnUrl = Uri.EscapeDataString(ctx.Request.Path + ctx.Request.QueryString);
                        ctx.Response.Redirect($"/Account/LoginSupport?forced=1&ReturnUrl={returnUrl}");
                        return;
                    }
                }
            }

            await next();
        });

        app.UseAuthorization();

        app.MapRazorPages();          
        app.MapControllers();
        app.MapHub<SupportPresenceHub>("/hubs/userStatus");
        app.MapHub<TicketHub>("/hubs/tickets");

        app.Run();
    }
}
