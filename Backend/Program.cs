using Drop1.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ✅ 1) Add DbContext (SQL Server)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ✅ 2) Add Controllers
builder.Services.AddControllers();

builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

// ✅ 3) Enable Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✅ 4) Enable Session (Fixed for cross-site)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(
        builder.Configuration.GetValue<int>("Session:IdleTimeoutMinutes", 30)
    );
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // ✅ must be secure
    options.Cookie.SameSite = SameSiteMode.None;             // ✅ allow cross-site
    options.Cookie.Name = "Drop1.Session";
    options.Cookie.Path = "/";
});

// ✅ 5) Enable CORS (Fixed for credentials)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "https://localhost:3000") // ✅ explicit only
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ✅ Logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

var app = builder.Build();

// ✅ Middleware Order
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 🔧 Request logging for debugging
app.Use(async (context, next) =>
{
    Console.WriteLine($"Request: {context.Request.Method} {context.Request.Path}");
    Console.WriteLine($"Origin: {context.Request.Headers["Origin"]}");
    Console.WriteLine($"User-Agent: {context.Request.Headers["User-Agent"]}");

    await next();

    Console.WriteLine($"Response: {context.Response.StatusCode}");
    if (context.Response.Headers.ContainsKey("Set-Cookie"))
    {
        Console.WriteLine($"Set-Cookie: {context.Response.Headers["Set-Cookie"]}");
    }
});

app.UseCors("AllowFrontend");   // ✅ must come first
app.UseHttpsRedirection();
app.UseSession();               // ✅ before controllers
app.MapControllers();

Console.WriteLine("🚀 Backend started with session + CORS fixes");
Console.WriteLine("📡 CORS enabled for http://localhost:3000 and https://localhost:3000");
Console.WriteLine("🍪 Session cookie: Drop1.Session, SameSite=None, Secure=Always");

app.Run();
