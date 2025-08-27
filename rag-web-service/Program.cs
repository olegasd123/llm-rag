using System.Text;
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

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.EnableAnnotations();
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

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(brokerUrl);
    });
});

builder.Services.AddAuthorization();

builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(cacheUrl));
builder.Services.AddSingleton(_ => new NpgsqlConnection(mainDbUrl));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
