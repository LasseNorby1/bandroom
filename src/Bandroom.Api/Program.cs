using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Auth;
using Bandroom.Api.Features.Availability;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Calendar;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Features.Jobs;
using Bandroom.Api.Features.Scheduling;
using Bandroom.Api.Infrastructure.Email;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
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
            // Browsers can't set headers on websocket upgrades — SignalR sends the
            // token as ?access_token= for hub routes only.
            jwt.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken) &&
                        context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                },
            };
        });
    builder.Services.AddAuthorization();

    builder.Services.AddScoped<TokenService>();
    builder.Services.AddSingleton<IAppEmailSender, LoggingEmailSender>();

    // One scoped instance serves both roles: the endpoint filter writes it, the
    // DbContext's query filters read it.
    builder.Services.AddScoped<BandContext>();
    builder.Services.AddScoped<IBandContext>(sp => sp.GetRequiredService<BandContext>());

    builder.Services.AddSignalR();
    builder.Services.AddSingleton<BandNotifier>();

    // Production runs same-origin behind traefik (no cors at all); dev allows the
    // next dev server. AllowCredentials because the signalr client sends them.
    var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (corsOrigins.Length > 0)
    {
        builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
            .WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));
    }

    // Jobs:Enabled=false keeps Hangfire (storage, server, schedules) out of test
    // hosts; the job classes stay registered so tests can drive them directly.
    var jobsEnabled = builder.Configuration.GetValue("Jobs:Enabled", true);
    if (jobsEnabled)
    {
        builder.Services.AddHangfire(hangfire => hangfire
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(storage => storage.UseNpgsqlConnection(connectionString)));
        builder.Services.AddHangfireServer();
    }

    builder.Services.AddScoped<ReminderJob>();
    builder.Services.AddScoped<DigestJob>();

    builder.Services.AddOpenApi(openApi =>
    {
        // NodaTime values serialize as iso strings, but the generator can't see
        // through their converters and emits `unknown` — map them explicitly so
        // generated clients get real string types.
        openApi.AddSchemaTransformer((schema, context, _) =>
        {
            var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
            if (type == typeof(Instant))
            {
                schema.Type = JsonSchemaType.String;
                schema.Format = "date-time";
            }
            else if (type == typeof(LocalDate))
            {
                schema.Type = JsonSchemaType.String;
                schema.Format = "date";
            }
            else if (type == typeof(LocalTime))
            {
                schema.Type = JsonSchemaType.String;
                schema.Format = "time";
            }

            return Task.CompletedTask;
        });

        // Route values consumed only by endpoint filters (bandId) never appear in
        // handler signatures, so the generator misses them — declare every route
        // template placeholder as a path parameter explicitly.
        openApi.AddOperationTransformer((operation, context, _) =>
        {
            var template = context.Description.RelativePath;
            if (template is null)
            {
                return Task.CompletedTask;
            }

            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(template, @"\{(\w+)[^}]*\}"))
            {
                var name = match.Groups[1].Value;
                if (operation.Parameters?.Any(p => p.Name == name && p.In == ParameterLocation.Path) == true)
                {
                    continue;
                }

                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = name,
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                });
            }

            return Task.CompletedTask;
        });
    });
    builder.Services.AddProblemDetails();
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSerilogRequestLogging();

    if (corsOrigins.Length > 0)
    {
        app.UseCors();
    }

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
    app.MapEventEndpoints();
    app.MapCalendarEndpoints();
    app.MapHub<BandHub>("/hubs/band");

    if (jobsEnabled)
    {
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = [new DevelopmentOnlyDashboardFilter(app.Environment.IsDevelopment())],
        });
        RecurringJob.AddOrUpdate<ReminderJob>(
            "event-reminders", job => job.RunAsync(CancellationToken.None), "0 * * * *");
        RecurringJob.AddOrUpdate<DigestJob>(
            "weekly-digest", job => job.RunAsync(CancellationToken.None), "0 8 * * MON");
    }

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
