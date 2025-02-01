using Microsoft.EntityFrameworkCore;
using VCS_DOCs;

var builder = WebApplication.CreateBuilder(args);

// Добавление Razor Pages
builder.Services.AddRazorPages();

// Добавление контекста базы данных
builder.Services.AddDbContext<ApplicationDbContext>(options =>
	options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// Добавление сервисов для сессий
builder.Services.AddDistributedMemoryCache();  // Использование в памяти кеша для сессий
builder.Services.AddSession(options =>
{
	options.IdleTimeout = TimeSpan.FromMinutes(30);  // Время жизни сессии
	options.Cookie.HttpOnly = true;  // Защищает cookie от доступа через JavaScript
	options.Cookie.IsEssential = true;  // Обязательность cookie для работы с сессиями
});

// Добавление аутентификации и авторизации
builder.Services.AddAuthentication("Identity.Application")
	.AddCookie("Identity.Application", options =>
	{
		options.LoginPath = "/Login"; // Путь для входа
		options.LogoutPath = "/Logout"; // Путь для выхода
	});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error");
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Включение сессий
app.UseSession();

// Включение аутентификации и авторизации
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();
