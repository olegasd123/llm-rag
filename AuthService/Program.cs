using System.Text;
using System.IO;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Swashbuckle.AspNetCore.Annotations;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration;
var jwtSecret = configuration["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET not configured");
var dbConnectionString = configuration["MAIN_DB_CONNECTION_STRING"] ?? throw new InvalidOperationException("MAIN_DB_CONNECTION_STRING not configured");
var corsOrigin = configuration["CORS_ORIGIN"] ?? "http://localhost:3000";
var assessTokenExpiryInSeconds = 15 * 60; // 15 minutes
var refreshTokenExpiryInSeconds = 30 * 24 * 60 * 60; // 30 days

builder.Services.AddSingleton(new RagAuthService.TokenService(jwtSecret, assessTokenExpiryInSeconds, refreshTokenExpiryInSeconds));

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.EnableAnnotations();
});

// Persist DataProtection keys to a mounted volume to avoid ephemeral container storage
builder.Services.AddDataProtection()
    .SetApplicationName("rag")
    .PersistKeysToFileSystem(new DirectoryInfo("/keys"));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
