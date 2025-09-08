using System.Text;
using Microsoft.AspNetCore.DataProtection;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using Npgsql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.AspNetCore.Mvc.ApiExplorer;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration;

var jwtSecret = configuration["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET not configured");
var brokerUrl = configuration["MESSAGE_BROKER_URL"] ?? throw new InvalidOperationException("MESSAGE_BROKER_URL not configured");
var cacheUrl = configuration["DATA_CACHE_URL"] ?? throw new InvalidOperationException("DATA_CACHE_URL not configured");
var mainDbUrl = configuration["MAIN_DB_URL"] ?? throw new InvalidOperationException("MAIN_DB_URL not configured");
var corsOrigin = configuration["CORS_ORIGIN"] ?? "http://localhost:3000";

builder.Services.AddControllers();

// API Versioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("x-api-version"),
        new QueryStringApiVersionReader("api-version")
    );
});

builder.Services.AddVersionedApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV"; // v1, v1.1, etc
    options.SubstituteApiVersionInUrl = true;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.EnableAnnotations();
});
builder.Services.ConfigureOptions<RagWebService.Swagger.ConfigureSwaggerOptions>();

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
    var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
    app.UseSwaggerUI(options =>
    {
        foreach (var description in provider.ApiVersionDescriptions)
        {
            options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json", description.GroupName.ToUpperInvariant());
        }
    });
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
