using System.Text;
using System.IO;
using Microsoft.AspNetCore.DataProtection;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Swashbuckle.AspNetCore.Annotations;
using StackExchange.Redis;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration;

var jwtSecret = configuration["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET not configured");
var brokerUrl = configuration["MESSAGE_BROKER_URL"] ?? throw new InvalidOperationException("MESSAGE_BROKER_URL not configured");
var cacheUrl = configuration["DATA_CACHE_URL"] ?? throw new InvalidOperationException("DATA_CACHE_URL not configured");
var mainDbUrl = configuration["MAIN_DB_URL"] ?? throw new InvalidOperationException("MAIN_DB_URL not configured");
var corsOrigin = configuration["CORS_ORIGIN"] ?? "http://localhost:3000";

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.EnableAnnotations();
});

// Persist DataProtection keys to a mounted volume to avoid ephemeral container storage
builder.Services.AddDataProtection()
    .SetApplicationName("rag")
    .PersistKeysToFileSystem(new DirectoryInfo("/keys"));

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

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(brokerUrl);
    });
});

builder.Services.AddAuthorization();

var redisOptions = ConfigurationOptions.Parse(cacheUrl);
redisOptions.AbortOnConnectFail = false; // keep retrying until Redis is ready
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
builder.Services.AddSingleton(_ => new NpgsqlConnection(mainDbUrl));

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
