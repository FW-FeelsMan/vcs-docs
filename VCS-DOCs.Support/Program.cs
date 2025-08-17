using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Infrastructure.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddIdentity<User, IdentityRole>()              
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = ".VcsDocs.Auth";
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Errors/403";
});
builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        x => x.MigrationsAssembly("VCS-DOCs.Web") 
    ));

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("SupportOnly",
        p => p.RequireRole(Roles.SupportAgent, Roles.SupportAdmin));
});

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/", "SupportOnly");
    o.Conventions.AllowAnonymousToPage("/Account/Login");

    o.Conventions.AllowAnonymousToPage("/Errors/404");
    o.Conventions.AllowAnonymousToPage("/Errors/500");
});
builder.Services.AddRazorPages()
    .AddRazorPagesOptions(o =>
    {
        o.Conventions.AddPageRoute("/Errors/404", "/Errors/{code:int}");
    });
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
            .FirstOrDefault() ?? throw new InvalidOperationException($"Dev cert '{friendlyName}' не найден.");

        https.ServerCertificate = cert;
    });
});

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    await VCS_DOCs.Support.Infrastructure.Auth.AuthSeed.RunAsync(scope.ServiceProvider);
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

    foreach (var role in new[] { "SupportAgent", "SupportAdmin" })
        if (!await roleMgr.RoleExistsAsync(role))
            await roleMgr.CreateAsync(new IdentityRole(role));

    var userId = "6bbbcc2b-bcc8-4c20-b7ea-7993664339d2";
    var user = await userMgr.FindByIdAsync(userId);
    if (user != null && !await userMgr.IsInRoleAsync(user, "SupportAgent"))
        await userMgr.AddToRoleAsync(user, "SupportAgent");
}


if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseExceptionHandler("/Errors/500"); 

app.UseStatusCodePagesWithReExecute("/Errors/{0}");

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();   
app.UseAuthorization();

app.MapRazorPages();

app.Run();
