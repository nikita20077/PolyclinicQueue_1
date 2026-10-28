using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MySql.Data.MySqlClient;
using PolyclinicQueue.API.Data;
using PolyclinicQueue.API.Services;

// Русская культура по умолчанию (даты, числа в PDF и отчётах)
var ru = new CultureInfo("ru-RU");
CultureInfo.DefaultThreadCurrentCulture = ru;
CultureInfo.DefaultThreadCurrentUICulture = ru;

var builder = WebApplication.CreateBuilder(args);

// ── Сервисы приложения ───────────────────────────────────
builder.Services.AddSingleton<DatabaseHelper>();          // Data/DatabaseHelper.cs      (часть 4)
builder.Services.AddSingleton<JwtService>();              // Services/JwtService.cs      (часть 5)
builder.Services.AddSingleton<PdfGeneratorService>();     // Services/PdfGeneratorService.cs (часть 17)

builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping; // кириллица без \uXXXX
    });
builder.Services.AddEndpointsApiExplorer();

// ── JWT-аутентификация ───────────────────────────────────
var jwt = builder.Configuration.GetSection("Jwt");
var key = jwt["Key"] ?? throw new InvalidOperationException("Jwt:Key не задан в appsettings.json");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwt["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

// ── Swagger с кнопкой Authorize ──────────────────────────
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PolyclinicQueue.API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Вставьте JWT токен (без слова Bearer)",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ── Проверка подключения к БД при старте ─────────────────
var db = app.Services.GetRequiredService<DatabaseHelper>();
if (await db.TestConnectionAsync())
{
    app.Logger.LogInformation("Подключение к MySQL (PolyclinicQueue) успешно.");
    await DbInitializer.EnsureAsync(db, app.Logger);   // новые колонки и Reviews, если их ещё нет
}
else
    app.Logger.LogError("НЕ УДАЛОСЬ подключиться к MySQL. Проверьте строку подключения в appsettings.json.");

// ── Глобальный обработчик ошибок: всегда JSON {message}, фронтенд покажет красный toast ──
app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    var error = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (error != null)
        ctx.RequestServices.GetRequiredService<ILogger<Program>>().LogError(error, "Необработанная ошибка");

    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await ctx.Response.WriteAsJsonAsync(new
    {
        message = error is MySqlException ? "Ошибка подключения к базе данных" : "Внутренняя ошибка сервера"
    });
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();   // index.html по умолчанию
app.UseStaticFiles();    // wwwroot

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
