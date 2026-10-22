using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Ai;
using Bandroom.Api.Features.Auth;
using Bandroom.Api.Features.Availability;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Billing;
using Bandroom.Api.Features.Calendar;
using Bandroom.Api.Features.Chat;
using Bandroom.Api.Features.Comments;
using Bandroom.Api.Features.Demos;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Features.Jobs;
using Bandroom.Api.Features.Push;
using Bandroom.Api.Features.Scheduling;
using Bandroom.Api.Features.Songs;
using Bandroom.Api.Infrastructure;
using Bandroom.Api.Infrastructure.Email;
using Bandroom.Api.Infrastructure.Storage;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    // `--vapid`: print a fresh web-push key pair and exit. Needs no config at
    // all, so it runs before the host is built (deploy.sh mints into .env).
    if (args.Contains("--vapid"))
    {
        var (publicKey, privateKey) = Vapid.GenerateKeys();
        Console.WriteLine($"PUSH_VAPID_PUBLIC_KEY={publicKey}");
        Console.WriteLine($"PUSH_VAPID_PRIVATE_KEY={privateKey}");
        return;
    }

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

    // Behind a reverse proxy (traefik in production) the client ip lives in
    // X-Forwarded-For; without this the per-ip auth rate limiter would put every
    // user in the proxy's single bucket. Off by default: the api port is only
    // reachable through the proxy in production, and dev/test talk to it directly.
    var behindProxy = builder.Configuration.GetValue("ForwardedHeaders:Enabled", false);
    if (behindProxy)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
        });
    }

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
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

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

    var emailOptions = builder.Configuration.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
    builder.Services.AddSingleton(emailOptions);
    if (!string.IsNullOrEmpty(emailOptions.SmtpHost))
    {
        builder.Services.AddSingleton<IAppEmailSender, SmtpEmailSender>();
    }
    else
    {
        builder.Services.AddSingleton<IAppEmailSender, LoggingEmailSender>();
    }

    // Config-gated so test hosts skip it; per-ip fixed window on /auth only.
    var rateLimitingEnabled = builder.Configuration.GetValue("RateLimiting:Enabled", true);
    if (rateLimitingEnabled)
    {
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPerMinute", 20),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
            // Nobody creates five accounts an hour from one address; bots do.
            limiter.AddPolicy("register", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = builder.Configuration.GetValue("RateLimiting:RegisterPerHour", 5),
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                }));
        });
    }

    // One scoped instance serves both roles: the endpoint filter writes it, the
    // DbContext's query filters read it.
    builder.Services.AddScoped<BandContext>();
    builder.Services.AddScoped<IBandContext>(sp => sp.GetRequiredService<BandContext>());

    builder.Services.AddSignalR();
    builder.Services.AddSingleton<BandPresence>();
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
    builder.Services.AddScoped<CalendarImporter>();
    builder.Services.AddScoped<CalendarImportJob>();
    builder.Services.AddHttpClient("calendar", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Bandroom/1.0 (+https://bandroom.io)");
    });

    var storageOptions = builder.Configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
    if (string.IsNullOrEmpty(storageOptions.Endpoint))
    {
        throw new InvalidOperationException(
            "Missing Storage:Endpoint (s3-compatible object storage — minio in dev, r2 in prod).");
    }

    builder.Services.AddSingleton(storageOptions);
    builder.Services.AddSingleton<IFileStorage>(new S3FileStorage(storageOptions));

    builder.Services.AddSingleton(
        builder.Configuration.GetSection("Entitlements").Get<EntitlementOptions>() ?? new EntitlementOptions());
    builder.Services.AddSingleton(
        builder.Configuration.GetSection("Billing").Get<BillingOptions>() ?? new BillingOptions());
    var pushOptions = builder.Configuration.GetSection("Push").Get<PushOptions>() ?? new PushOptions();
    builder.Services.AddSingleton(pushOptions);
    builder.Services.AddScoped<PushDeliveryJob>();
    builder.Services.AddHttpClient("push", client => client.Timeout = TimeSpan.FromSeconds(20));
    if (jobsEnabled && pushOptions.IsConfigured)
    {
        builder.Services.AddSingleton<IPushSender, HangfirePushSender>();
    }
    else
    {
        builder.Services.AddSingleton<IPushSender, NoopPushSender>();
    }

    var aiOptions = builder.Configuration.GetSection("Ai").Get<AiOptions>() ?? new AiOptions();
    builder.Services.AddSingleton(aiOptions);
    builder.Services.AddSingleton<IAiClient>(new AnthropicAiClient(aiOptions));
    BandEndpoints.AiAvailable = !string.IsNullOrEmpty(aiOptions.ApiKey);
    builder.Services.AddSingleton(
        builder.Configuration.GetSection("Worker").Get<WorkerOptions>() ?? new WorkerOptions());
    builder.Services.AddHttpClient("worker", client => client.Timeout = TimeSpan.FromMinutes(15));
    builder.Services.AddScoped<PolishJobRunner>();
    builder.Services.AddScoped<AnalysisJobRunner>();
    builder.Services.AddScoped<Bandroom.Api.Features.Songs.SheetDraftRunner>();
    builder.Services.AddScoped<RecurringPracticeJob>();
    if (jobsEnabled)
    {
        builder.Services.AddSingleton<IPolishQueue, HangfirePolishQueue>();
        builder.Services.AddSingleton<IAnalysisQueue, HangfireAnalysisQueue>();
        builder.Services.AddSingleton<Bandroom.Api.Features.Songs.ISheetDraftQueue, Bandroom.Api.Features.Songs.HangfireSheetDraftQueue>();
    }
    else
    {
        builder.Services.AddSingleton<IPolishQueue, NoopPolishQueue>();
        builder.Services.AddSingleton<IAnalysisQueue, NoopAnalysisQueue>();
        builder.Services.AddSingleton<Bandroom.Api.Features.Songs.ISheetDraftQueue, Bandroom.Api.Features.Songs.NoopSheetDraftQueue>();
    }

    builder.Services.AddOpenApi(openApi =>
    {
        // NodaTime values serialize as iso strings, but the generator can't see
        // through their converters and emits `unknown` — map them explicitly so
        // generated clients get real string types.
        openApi.AddSchemaTransformer((schema, context, _) =>
        {
            var underlying = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type);
            var isNullable = underlying is not null;
            var type = underlying ?? context.JsonTypeInfo.Type;
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
            else if (type == typeof(long) || type == typeof(int))
            {
                // The generator unions integers/doubles with string (precision/NaN
                // hedging); this api never sends either — keep clients numeric.
                schema.Type = isNullable ? JsonSchemaType.Integer | JsonSchemaType.Null : JsonSchemaType.Integer;
                schema.Format = type == typeof(long) ? "int64" : "int32";
                schema.Pattern = null;
            }
            else if (type == typeof(double) || type == typeof(float))
            {
                schema.Type = isNullable ? JsonSchemaType.Number | JsonSchemaType.Null : JsonSchemaType.Number;
                schema.Pattern = null;
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

    // `--migrate`: apply pending migrations and exit. Deploys run this as an
    // explicit step before the new api starts — never on app start against prod
    // (foundation rules). Nothing is listening and no hosted service runs here.
    if (args.Contains("--migrate"))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        Log.Information("applying {Count} pending migration(s): {Names}", pending.Count, pending);
        await db.Database.MigrateAsync();
        Log.Information("migrations applied");
        return;
    }

    if (behindProxy)
    {
        app.UseForwardedHeaders();
    }

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSerilogRequestLogging();

    if (corsOrigins.Length > 0)
    {
        app.UseCors();
    }

    if (rateLimitingEnabled)
    {
        app.UseRateLimiter();
    }

    app.UseAuthentication();
    app.UseAuthorization();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    // Liveness must stay green while the database is down — coolify restarts on live,
    // and a db outage should never restart-loop the api.
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready"),
    });

    app.MapGet("/", () => Results.Ok(new { name = "bandroom-api", version = "0.1.0" }));

    app.MapAuthEndpoints();
    app.MapAccountEndpoints();
    app.MapBandEndpoints();
    app.MapMemberEndpoints();
    app.MapBillingEndpoints();
    app.MapAiEndpoints();
    app.MapPushEndpoints();
    app.MapInviteEndpoints();
    app.MapAvailabilityEndpoints();
    app.MapPracticeFinderEndpoints();
    app.MapEventEndpoints();
    app.MapSongEndpoints();
    app.MapSongFileEndpoints();
    app.MapDemoEndpoints();
    app.MapReactionEndpoints();
    app.MapPolishEndpoints();
    app.MapReferenceEndpoints();
    app.MapCommentEndpoints();
    app.MapChatEndpoints();
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
        RecurringJob.AddOrUpdate<RecurringPracticeJob>(
            "standing-practices", job => job.RunAsync(CancellationToken.None), "0 6 * * *");
        RecurringJob.AddOrUpdate<CalendarImportJob>(
            "calendar-import", job => job.RunAsync(CancellationToken.None), "15 */6 * * *");
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
