using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Auth;
using Bandroom.Api.Features.Availability;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Scheduling;
using Bandroom.Api.Infrastructure.Email;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // preserveStaticLogger: each host builds its own logger instead of freezing
    // the shared bootstrap — required for multiple hosts in one process (tests).
    builder.Host.UseSerilog(
        (context, loggerConfiguration) => loggerConfiguration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console(),
        preserveStaticLogger: true);

    var connectionString = builder.Configuration.GetConnectionString("Database");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "Missing connection string 'Database'. Set ConnectionStrings__Database, or use appsettings.Development.json locally.");
    }

    var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
    if (authOptions.JwtSigningKey.Length < 32)
    {
        throw new InvalidOperationException(
            "Auth:JwtSigningKey must be at least 32 characters. Set it via configuration; never commit a production key.");
    }

    builder.Services.AddSingleton(authOptions);
    builder.Services.AddSingleton<IClock>(SystemClock.Instance);

    builder.Services.ConfigureHttpJsonOptions(json =>
    {
        json.SerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(connectionString, npgsql => npgsql.UseNodaTime()));

    builder.Services
        .AddIdentityCore<AppUser>(identity =>
        {
            identity.User.RequireUniqueEmail = true;
            // Length over composition rules (NIST-style) — no digit/symbol theatre.
            identity.Password.RequiredLength = 10;
            identity.Password.RequireDigit = false;
            identity.Password.RequireLowercase = false;
            identity.Password.RequireUppercase = false;
            identity.Password.RequireNonAlphanumeric = false;
            identity.Lockout.MaxFailedAccessAttempts = 5;
            identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        })
        .AddEntityFrameworkStores<AppDbContext>();

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(jwt =>
        {
            // Keep the raw claim names ("sub", not the legacy soap-era mapping).
            jwt.MapInboundClaims = false;
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = authOptions.Issuer,
                ValidAudience = authOptions.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.JwtSigningKey)),
                ClockSkew = TimeSpan.FromMinutes(1),
            };
        });
    builder.Services.AddAuthorization();

    builder.Services.AddScoped<TokenService>();
    builder.Services.AddSingleton<IAppEmailSender, LoggingEmailSender>();

    // One scoped instance serves both roles: the endpoint filter writes it, the
    // DbContext's query filters read it.
    builder.Services.AddScoped<BandContext>();
    builder.Services.AddScoped<IBandContext>(sp => sp.GetRequiredService<BandContext>());

    builder.Services.AddOpenApi();
    builder.Services.AddProblemDetails();
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSerilogRequestLogging();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapOpenApi();

    // Liveness must stay green while the database is down — coolify restarts on live,
    // and a db outage should never restart-loop the api.
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready"),
    });

    app.MapGet("/", () => Results.Ok(new { name = "bandroom-api", version = "0.1.0" }));

    app.MapAuthEndpoints();
    app.MapBandEndpoints();
    app.MapInviteEndpoints();
    app.MapAvailabilityEndpoints();
    app.MapPracticeFinderEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "api terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
