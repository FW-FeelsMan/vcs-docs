using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs;
using VCS_DOCs.Data;
using VCS_DOCs.Services;
using AspNetCoreRateLimit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
	options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
);

builder.Services
	.AddSignalR(options => {
		options.EnableDetailedErrors = true;
	})
	.AddHubOptions<UserStatusHub>(options => {
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

// Настройка защиты от CSRF-атак
builder.Services.AddAntiforgery(options =>
{
	options.HeaderName = "X-CSRF-TOKEN";
});

// Настройка UserIdentifier для SignalR
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
builder.Services.Configure<IpRateLimitOptions>(options =>
{
	options.GeneralRules = new List<RateLimitRule>
	{
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
		},
		new RateLimitRule
		{
			Endpoint = "/css/*",
			Limit = 0,
			Period = "1s"
		},
		new RateLimitRule
		{
			Endpoint = "/js/*", 
			Limit = 0,
			Period = "1s"
		}
	};
});
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

var app = builder.Build();
app.UseIpRateLimiting();

if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error");
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions
{
	OnPrepareResponse = ctx =>
	{
		if (ctx.Context.Request.Path.StartsWithSegments("/js"))
		{
			ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=31536000");
		}
	}
});

app.UseSession();

app.UseRouting();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

// Включение защиты от CSRF-атак
app.Use((context, next) =>
{
	var tokens = app.Services.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
	context.Response.Cookies.Append("CSRF-TOKEN", tokens.RequestToken!,
		new CookieOptions { HttpOnly = false, Secure = true, SameSite = SameSiteMode.Strict });
	return next(context);
});

app.MapHub<UserStatusHub>("/Data/userStatusHub");
app.UseAntiforgery();
app.MapRazorPages();

app.Run();
