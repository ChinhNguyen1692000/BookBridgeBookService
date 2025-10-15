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
// builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

builder.Services.AddHttpClient();


// 3. JWT
var jwtKey = configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
var jwtIssuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
var jwtAudience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is not configured.");

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
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])),
        NameClaimType = "nameid"
    };

    // options.Events = new JwtBearerEvents
    // {
    //     OnTokenValidated = async context =>
    //     {
    //         var jti = context.Principal.FindFirstValue(JwtRegisteredClaimNames.Jti);

    //         if (string.IsNullOrEmpty(jti))
    //         {
    //             context.Fail("JWT missing jti.");
    //             return;
    //         }

    //         var cacheService = context.HttpContext.RequestServices.GetRequiredService<ICacheService>();

    //         if (await cacheService.IsBlacklistedAsync(jti))
    //         {
    //             context.Fail("This token has been revoked.");
    //         }

    //         await Task.CompletedTask;
    //     }
    // };

});


var app = builder.Build();

// Tự động áp dụng migrations VÀ XỬ LÝ LỖI - Cách 2
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider; // <-- chỉ tồn tại trong scope này
    try
    {
        // Lấy DbContext đã đăng ký
        var context = services.GetRequiredService<BookDBContext>();

        // Tự động áp dụng migration
        context.Database.Migrate();

        // -------------------------------
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

app.UseAuthorization();

app.MapControllers();

app.Run();
