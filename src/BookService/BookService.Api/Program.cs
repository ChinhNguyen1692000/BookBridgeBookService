using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BookService.Application.Interface;
using BookService.Application.Services;
using BookService.Infracstructure.DBContext;
using BookService.Infracstructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;


var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<BookDBContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BookServiceConnection")));
builder.Services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());
builder.Services.AddScoped<IBookImageServices, BookImageServices>();
builder.Services.AddScoped<IBookTypeServices, BookTypeServices>();
builder.Services.AddScoped<IBookServices, BookServices>();
builder.Services.AddScoped<BookImageRepository>();
builder.Services.AddScoped<BookRepository>();
builder.Services.AddScoped<BookTypeRepository>();
builder.Services.AddSingleton<ICloudinaryService, CloudinaryService>();
builder.Services.AddScoped<ICacheService, RedisCacheService>();
builder.Services.AddHttpClient();



// 3. Cấu hình JWT Validation (Đã được làm sạch và sử dụng đúng cấu hình)
// var jwtKey = configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
var myHardcodedKey = "BookbridgeJWTKey_2025_Secure_ABC123!";
var jwtIssuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
var jwtAudience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is not configured.");
Console.WriteLine($"[DEBUG RUNTIME] Key loaded: {myHardcodedKey.Substring(0, 5)}..., Issuer: {jwtIssuer}");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(myHardcodedKey))
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var jti = context.Principal.FindFirstValue(JwtRegisteredClaimNames.Jti);

            if (string.IsNullOrEmpty(jti))
            {
                context.Fail("JWT missing jti.");
                return;
            }

            var cacheService = context.HttpContext.RequestServices.GetRequiredService<ICacheService>();

            if (await cacheService.IsBlacklistedAsync(jti))
            {
                context.Fail("This token has been revoked.");
            }

            await Task.CompletedTask;
        }
    };
});


builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Your API Name", Version = "v1" });

    // 1. Định nghĩa Security Scheme (Security Definition)
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Vui lòng nhập Bearer Token vào trường text bên dưới. Ví dụ: 'Bearer {token}'",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer" // Phải là "Bearer"
    });

    // 2. Yêu cầu Security (Security Requirement)
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer" // Tham chiếu đến tên của Security Scheme ở trên
                }
            },
            new string[] {}
        }
    });
});


// 4. Redis
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? Environment.GetEnvironmentVariable("ConnectionStrings__Redis");

if (redisConnection.StartsWith("redis://"))
{
    redisConnection = redisConnection.Replace("redis://", "");
}

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    try
    {
        return ConnectionMultiplexer.Connect(redisConnection);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Redis connection failed: {ex.Message}");
        throw;
    }
});


builder.Services.AddAuthorization();


var app = builder.Build();

// Tự động áp dụng migrations VÀ XỬ LÝ LỖI
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<BookDBContext>();
        context.Database.Migrate();

        Console.WriteLine("Database migration applied successfully.");
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred during database migration.");
    }
}


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();

// Phải đặt UseAuthentication trước UseAuthorization

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();