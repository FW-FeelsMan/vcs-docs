using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Infrastructure.Auth;

var builder = WebApplication.CreateBuilder(args);

// === DB + Identity ===
builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        x => x.MigrationsAssembly("VCS-DOCs.Web")
    ));
builder.Services.AddControllers()
    .ConfigureApplicationPartManager(apm =>
    {
        var dead = apm.ApplicationParts
            .Where(p => p.Name == "VCS-DOCs.Web")
            .ToList();
        foreach (var part in dead) apm.ApplicationParts.Remove(part);
    });

builder.Services
    .AddIdentity<User, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// === Cookies / маршруты отказов ===
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = ".VcsDocs.Auth";
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Errors/403";
});

// === Авторизация: саппорт-политика ===
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("SupportOnly",
        p => p.RequireRole(Roles.SupportAgent, Roles.SupportAdmin));
});

// === Razor Pages ===
builder.Services
    .AddRazorPages(o =>
    {
        // всё закрыто для посторонних...
        o.Conventions.AuthorizeFolder("/", "SupportOnly");
        // ...кроме логина/ошибок/публичной формы обращения
        o.Conventions.AllowAnonymousToPage("/Account/Login");
        o.Conventions.AllowAnonymousToPage("/Errors/404");
        o.Conventions.AllowAnonymousToPage("/Errors/500");
        o.Conventions.AllowAnonymousToPage("/Support/Request");
    })
    .AddRazorPagesOptions(o =>
    {
        o.Conventions.AddPageRoute("/Errors/404", "/Errors/{code:int}");
    });

// === Контроллеры для /api/Support/... ===
builder.Services.AddControllers();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromMinutes(30);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
});
builder.Services.AddMemoryCache();

// === ВАЖНО для reCAPTCHA: фабрика HttpClient ===
builder.Services.AddHttpClient();

// === Kestrel / Dev-certificate ===
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
    await db.Database.MigrateAsync();

    await VCS_DOCs.Support.Infrastructure.Auth.AuthSeed.RunAsync(scope.ServiceProvider);

    var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

    foreach (var role in new[] { Roles.SupportAgent, Roles.SupportAdmin })
        if (!await roleMgr.RoleExistsAsync(role))
            await roleMgr.CreateAsync(new IdentityRole(role));

    var sampleUserId = "6bbbcc2b-bcc8-4c20-b7ea-7993664339d2";
    var u = await userMgr.FindByIdAsync(sampleUserId);
    if (u != null && !await userMgr.IsInRoleAsync(u, Roles.SupportAgent))
        await userMgr.AddToRoleAsync(u, Roles.SupportAgent);
}

// === Ошибки/пайплайн ===
//app.UseExceptionHandler("/Errors/500");
//app.UseStatusCodePagesWithReExecute("/Errors/{0}");

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"), branch =>
{
    branch.UseExceptionHandler("/Errors/500");
    branch.UseStatusCodePagesWithReExecute("/Errors/{0}");
});
app.UseRouting();

app.UseSession();          
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();     

app.Run();
