using Hangfire;
using Hangfire.Dashboard;
using Hangfire.MemoryStorage;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System;
using System.Text;
using System.Text.Json.Serialization;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.Data.Repositories;
using ZimbabweTenderAPI.Services;
using static ZimbabweTenderAPI.Services.ITenderScraperService;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Supabase.local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables().AddCommandLine(args);
if (builder.Configuration.GetValue<bool>("ScraperOnly"))
{
    await SupabaseScraperHost.RunAsync(builder);
    return;
}

// ============ DATABASE CONFIGURATION ============
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=tenders.db";
var isSqlite = connectionString.Contains(".db") || connectionString.Contains("Data Source=");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (isSqlite)
    {
        options.UseSqlite(connectionString);
    }
    else
    {
        options.UseSqlServer(
            connectionString,
            sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
                sqlOptions.CommandTimeout(120);
            });
    }
});

// ============ AUTHENTICATION & AUTHORIZATION ============
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero,
        RoleClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
    };
    
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"Authentication failed: {context.Exception.Message}");
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            var claims = context.Principal?.Claims.Select(c => $"{c.Type}: {c.Value}");
            Console.WriteLine($"Token validated. Claims: {string.Join(", ", claims ?? new string[0])}");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    // Role-based policies
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("GuestAccess", policy => policy.RequireRole("Guest", "Employee", "Supervisor", "HR", "ProjectManager", "Admin", "Development"));
    options.AddPolicy("SupervisorAccess", policy => policy.RequireRole("Supervisor", "Admin"));
    options.AddPolicy("HRAccess", policy => policy.RequireRole("HR", "Admin"));
    options.AddPolicy("ProjectManagerAccess", policy => policy.RequireRole("ProjectManager", "Supervisor", "Admin"));
    options.AddPolicy("EmployeeAccess", policy => policy.RequireRole("Employee", "Supervisor", "ProjectManager", "HR", "Admin", "Development"));
    options.AddPolicy("DevelopmentAccess", policy => policy.RequireRole("Development", "Admin"));
    
    // Combined policies
    options.AddPolicy("ManagementAccess", policy => policy.RequireRole("Supervisor", "ProjectManager", "HR", "Admin"));
    options.AddPolicy("DataModification", policy => policy.RequireRole("Admin", "Development", "Supervisor"));
});

// ============ CONTROLLERS & JSON SERIALIZATION ============
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

// ============ REPOSITORIES ============
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<ITenderRepository, TenderRepository>();

// ============ SERVICES ============
builder.Services.AddHttpClient();
builder.Services.AddScoped<ITenderScraperService, TenderScraperService>();
builder.Services.AddScoped<IZambiaTenderScraperService, ZambiaTenderScraperService>();
builder.Services.AddScoped<IMultiSourceProcurementService, MultiSourceProcurementService>();
builder.Services.AddScoped<IGeminiAIService, GeminiAIService>();
builder.Services.AddScoped<IStringMatchingService, StringMatchingService>();
builder.Services.AddScoped<ITenderStatsService, TenderStatsService>();
builder.Services.AddScoped<IDatabaseSyncService, DatabaseSyncService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<TenderSimilarityService>();
builder.Services.AddScoped<BackgroundSyncService>();
builder.Services.AddScoped<ITenderAnalysisService, TenderAnalysisService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IEmailService, EmailService>();

// ============ HTTP CONTEXT ============
builder.Services.AddHttpContextAccessor();

// ============ MEMORY CACHE ============
builder.Services.AddMemoryCache();

// ============ LOGGING ============
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
    logging.AddEventSourceLogger();
});

// ============ HANGFIRE FOR BACKGROUND JOBS ============
builder.Services.AddHangfire(configuration =>
{
    configuration
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings();

    if (isSqlite)
    {
        configuration.UseMemoryStorage();
    }
    else
    {
        configuration.UseSqlServerStorage(connectionString,
            new SqlServerStorageOptions
            {
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero,
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true
            });
    }
});

builder.Services.AddHangfireServer();

// ============ SWAGGER/OPENAPI ============
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Define multiple API groups for better organization
    c.SwaggerDoc("database", new OpenApiInfo
    {
        Title = "Database APIs",
        Version = "v2.0",
        Description = "Endpoints for accessing data from the local database (Live Tenders, Closed Tenders, Award Notices, Procurement Plans)"
    });

    c.SwaggerDoc("scraping", new OpenApiInfo
    {
        Title = "EGP Scraping APIs",
        Version = "v2.0",
        Description = "Endpoints for direct scraping from Zimbabwe Electronic Government Procurement Portal"
    });

    c.SwaggerDoc("auth", new OpenApiInfo
    {
        Title = "Authentication & User Management",
        Version = "v2.0",
        Description = "User authentication, authorization, and role management endpoints"
    });

    c.SwaggerDoc("system", new OpenApiInfo
    {
        Title = "System & Management",
        Version = "v2.0",
        Description = "Statistics, insights, synchronization, and system management endpoints"
    });

    c.SwaggerDoc("workflow", new OpenApiInfo
    {
        Title = "Tender Workflow & Documents",
        Version = "v2.0",
        Description = "Tender assignments, document uploads, checklists, and approval workflow endpoints"
    });

    c.SwaggerDoc("zambia", new OpenApiInfo
    {
        Title = "Zambia & Multi-Source Procurement APIs",
        Version = "v2.0",
        Description = "Endpoints for scraping and aggregating tenders from Zambia (ZPPA e-GP, GoZambiaJobs, OnlineTenders) and International Development institutions (World Bank, UN, EU)"
    });

    // Group endpoints by ApiExplorerSettings GroupName or controller name
    c.DocInclusionPredicate((docName, apiDesc) =>
    {
        // Check if ApiExplorerSettings GroupName is set and matches
        if (!string.IsNullOrEmpty(apiDesc.GroupName))
        {
            return apiDesc.GroupName == docName;
        }

        // Fallback: match by controller name if no GroupName is set
        if (!apiDesc.ActionDescriptor.RouteValues.TryGetValue("controller", out var controllerName))
        {
            return false;
        }

        return docName switch
        {
            "database" => new[] { "LiveTenders", "ClosedTenders", "Awards", "ProcurementPlansDb", "ProcurementPlanItems", "BulkData" }
                .Contains(controllerName),

            "scraping" => new[] { "Tenders", "ProcurementPlans", "AwardNotices", "Search", "PastTenders", "Similarity" }
                .Contains(controllerName),

            "auth" => new[] { "Auth", "Users", "Roles" }
                .Contains(controllerName),

            "system" => new[] { "Statistics", "Insights", "Sync", "Settings", "Analytics" }
                .Contains(controllerName),

            "workflow" => new[] { "TenderAssignments", "TenderDocuments", "TenderChecklist", "TenderApprovals" }
                .Contains(controllerName),

            "zambia" => new[] { "ZambiaTenders", "MultiSourceProcurement" }
                .Contains(controllerName),

            _ => false
        };
    });

    // Configure file upload parameters for Swagger
    c.OperationFilter<FileUploadOperationFilter>();

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter your token below.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });
});

// ============ CORS CONFIGURATION ============
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// ============ DATABASE MIGRATION & SEEDING ============
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();

        if (isSqlite)
        {
            context.Database.EnsureCreated();
        }
        else if (context.Database.GetPendingMigrations().Any())
        {
            context.Database.Migrate();
        }

        var dbLogger = services.GetRequiredService<ILogger<Program>>();
        dbLogger.LogInformation("Database initialized successfully");
    }
    catch (Exception ex)
    {
        var dbLogger = services.GetRequiredService<ILogger<Program>>();
        dbLogger.LogError(ex, "An error occurred while migrating the database");
    }
}

// ============ CONFIGURE PIPELINE ============
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/database/swagger.json", "Database APIs");
        c.SwaggerEndpoint("/swagger/scraping/swagger.json", "EGP Scraping APIs");
        c.SwaggerEndpoint("/swagger/auth/swagger.json", "Authentication & User Management");
        c.SwaggerEndpoint("/swagger/system/swagger.json", "System & Management");
        c.SwaggerEndpoint("/swagger/workflow/swagger.json", "Tender Workflow & Documents");
        c.SwaggerEndpoint("/swagger/zambia/swagger.json", "Zambia & Multi-Source Procurement APIs");
        c.RoutePrefix = "swagger";
        c.ConfigObject.AdditionalItems["syntaxHighlight"] = new Dictionary<string, object>
        {
            ["activated"] = true
        };
        c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
    });
}

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = (IEnumerable<Hangfire.Dashboard.IDashboardAuthorizationFilter>)(new[] { new HangfireAuthorizationFilter() })
});

app.UseCors("AllowAll");
// In local dev, allow HTTP on port 8096 without 307 HTTPS redirect
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ============ HEALTH CHECK ENDPOINTS ============
app.MapGet("/health", () => new
{
    Status = "Healthy",
    Timestamp = DateTime.UtcNow,
    Version = "2.0",
    Database = "Connected"
});

app.MapGet("/health/db", async (ApplicationDbContext context) =>
{
    try
    {
        await context.Database.CanConnectAsync();
        return Results.Ok(new
        {
            Status = "Healthy",
            Database = "Connected",
            Timestamp = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Database Connection Failed"
        );
    }
});

// ============ CONFIGURE RECURRING JOBS WITH DATABASE SETTINGS ============
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("=== CONFIGURING BACKGROUND JOBS FROM DATABASE ===");

// Use a separate task to configure jobs without blocking app startup
_ = Task.Run(async () =>
{
    try
    {
        // Create a scope to access database settings
        using (var scope = app.Services.CreateScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            var taskLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            try
            {
                // Live Tenders Sync
                var liveTendersEnabled = await settingsService.GetSettingAsBoolAsync("BackgroundJobs:LiveTendersSync:Enabled", true);
                if (liveTendersEnabled)
                {
                    var intervalHours = await settingsService.GetSettingAsDoubleAsync("BackgroundJobs:LiveTendersSync:IntervalHours", 0.1667);
                    var cronExpression = HoursToCron(intervalHours);

                    RecurringJob.AddOrUpdate<BackgroundSyncService>(
                        "sync-live-tenders",
                        service => service.SyncLiveTendersAsync(),
                        cronExpression);

                    taskLogger.LogInformation($"✅ Live Tenders Sync: Scheduled every {intervalHours} hours ({cronExpression})");
                }

                // Closed Tenders Sync
                var closedTendersEnabled = await settingsService.GetSettingAsBoolAsync("BackgroundJobs:ClosedTendersSync:Enabled", true);
                if (closedTendersEnabled)
                {
                    var intervalHours = await settingsService.GetSettingAsDoubleAsync("BackgroundJobs:ClosedTendersSync:IntervalHours", 24);
                    var runAtHour = await settingsService.GetSettingAsIntAsync("BackgroundJobs:ClosedTendersSync:RunAtHour", 3);
                    var cronExpression = runAtHour > 0 ? DailyAtHourCron(runAtHour) : HoursToCron(intervalHours);

                    RecurringJob.AddOrUpdate<BackgroundSyncService>(
                        "sync-closed-tenders",
                        service => service.SyncClosedTendersAsync(),
                        cronExpression);

                    taskLogger.LogInformation($"✅ Closed Tenders Sync: Scheduled {(runAtHour > 0 ? $"daily at {runAtHour}:00" : $"every {intervalHours} hours")} ({cronExpression})");
                }

                // Award Notices Sync
                var awardNoticesEnabled = await settingsService.GetSettingAsBoolAsync("BackgroundJobs:AwardNoticesSync:Enabled", true);
                if (awardNoticesEnabled)
                {
                    var intervalHours = await settingsService.GetSettingAsDoubleAsync("BackgroundJobs:AwardNoticesSync:IntervalHours", 24);
                    var runAtHour = await settingsService.GetSettingAsIntAsync("BackgroundJobs:AwardNoticesSync:RunAtHour", 2);
                    var cronExpression = runAtHour > 0 ? DailyAtHourCron(runAtHour) : HoursToCron(intervalHours);

                    RecurringJob.AddOrUpdate<BackgroundSyncService>(
                        "sync-award-notices",
                        service => service.SyncAwardNoticesAsync(),
                        cronExpression);

                    taskLogger.LogInformation($"✅ Award Notices Sync: Scheduled {(runAtHour > 0 ? $"daily at {runAtHour}:00" : $"every {intervalHours} hours")} ({cronExpression})");
                }

                // Procurement Plans Sync
                var procurementPlansEnabled = await settingsService.GetSettingAsBoolAsync("BackgroundJobs:ProcurementPlansSync:Enabled", true);
                if (procurementPlansEnabled)
                {
                    var intervalHours = await settingsService.GetSettingAsDoubleAsync("BackgroundJobs:ProcurementPlansSync:IntervalHours", 168);
                    var runAtHour = await settingsService.GetSettingAsIntAsync("BackgroundJobs:ProcurementPlansSync:RunAtHour", 4);
                    var cronExpression = runAtHour > 0 ? DailyAtHourCron(runAtHour) : HoursToCron(intervalHours);

                    RecurringJob.AddOrUpdate<BackgroundSyncService>(
                        "sync-procurement-plans",
                        service => service.SyncProcurementPlansAsync(),
                        cronExpression);

                    taskLogger.LogInformation($"✅ Procurement Plans Sync: Scheduled {(runAtHour > 0 ? $"daily at {runAtHour}:00" : $"every {intervalHours} hours")} ({cronExpression})");
                }

                // Procurement Plan Items Sync
                var procurementItemsEnabled = await settingsService.GetSettingAsBoolAsync("BackgroundJobs:ProcurementPlanItemsSync:Enabled", true);
                if (procurementItemsEnabled)
                {
                    var intervalHours = await settingsService.GetSettingAsDoubleAsync("BackgroundJobs:ProcurementPlanItemsSync:IntervalHours", 168);
                    var runAtHour = await settingsService.GetSettingAsIntAsync("BackgroundJobs:ProcurementPlanItemsSync:RunAtHour", 5);
                    var cronExpression = runAtHour > 0 ? DailyAtHourCron(runAtHour) : HoursToCron(intervalHours);

                    RecurringJob.AddOrUpdate<BackgroundSyncService>(
                        "sync-procurement-plan-items",
                        service => service.SyncProcurementPlanItemsAsync(),
                        cronExpression);

                    taskLogger.LogInformation($"✅ Procurement Plan Items Sync: Scheduled {(runAtHour > 0 ? $"daily at {runAtHour}:00" : $"every {intervalHours} hours")} ({cronExpression})");
                }

                // Verify and Move Tenders
                var verifyAndMoveEnabled = await settingsService.GetSettingAsBoolAsync("BackgroundJobs:VerifyAndMoveTenders:Enabled", true);
                if (verifyAndMoveEnabled)
                {
                    var intervalHours = await settingsService.GetSettingAsDoubleAsync("BackgroundJobs:VerifyAndMoveTenders:IntervalHours", 6);
                    var cronExpression = HoursToCron(intervalHours);

                    RecurringJob.AddOrUpdate<BackgroundSyncService>(
                        "verify-and-move-tenders",
                        service => service.VerifyAndMoveTendersAsync(),
                        cronExpression);

                    taskLogger.LogInformation($"✅ Verify and Move Tenders: Scheduled every {intervalHours} hours ({cronExpression})");
                }

                taskLogger.LogInformation("=== ALL BACKGROUND JOBS CONFIGURED FROM DATABASE ===");
            }
            catch (Exception ex)
            {
                taskLogger.LogWarning($"Failed to load settings from database, falling back to appsettings.json: {ex.Message}");

                // Fallback to appsettings.json
                var backgroundJobsConfig = builder.Configuration.GetSection("BackgroundJobs");

                // Live Tenders Sync (fallback)
                if (backgroundJobsConfig.GetValue<bool>("LiveTendersSync:Enabled"))
                {
                    var intervalHours = backgroundJobsConfig.GetValue<double>("LiveTendersSync:IntervalHours");
                    var cronExpression = HoursToCron(intervalHours);
                    RecurringJob.AddOrUpdate<BackgroundSyncService>("sync-live-tenders", service => service.SyncLiveTendersAsync(), cronExpression);
                    taskLogger.LogInformation($"✅ Live Tenders Sync (fallback): every {intervalHours} hours");
                }

                taskLogger.LogInformation("=== BACKGROUND JOBS CONFIGURED FROM APPSETTINGS (FALLBACK) ===");
            }
        }
    }
    catch (Exception ex)
    {
        // Log any unhandled exceptions from the task itself
        var errorLogger = app.Services.GetRequiredService<ILogger<Program>>();
        errorLogger.LogError(ex, "❌ Critical error configuring background jobs");
    }
});

app.Run();

// ============ HELPER METHODS FOR CRON CONVERSION ============
static string HoursToCron(double hours)
{
    // Convert hours to cron expression
    if (hours < 1)
    {
        // For sub-hour intervals, use minutes
        int minutes = (int)(hours * 60);
        return $"*/{minutes} * * * *"; // Every X minutes
    }
    else if (hours == 24)
    {
        return "0 0 * * *"; // Daily at midnight
    }
    else if (hours % 24 == 0)
    {
        int days = (int)(hours / 24);
        return $"0 0 */{days} * *"; // Every X days
    }
    else if (24 % hours == 0)
    {
        // Evenly divides into 24 hours
        int interval = (int)hours;
        return $"0 */{interval} * * *"; // Every X hours
    }
    else
    {
        // For irregular intervals, use hours
        int interval = (int)Math.Ceiling(hours);
        return $"0 */{interval} * * *"; // Every X hours (rounded up)
    }
}

static string DailyAtHourCron(int hour)
{
    // Run daily at specific hour
    return $"0 {hour} * * *";
}

// ============ HANGFIRE AUTHORIZATION FILTER ============
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        // In production, implement proper authorization
        return true;
    }
}

// ============ SWAGGER FILE UPLOAD OPERATION FILTER ============
public class FileUploadOperationFilter : Swashbuckle.AspNetCore.SwaggerGen.IOperationFilter
{
    public void Apply(Microsoft.OpenApi.Models.OpenApiOperation operation, Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
    {
        var fileParams = context.MethodInfo.GetParameters()
            .Where(p => p.ParameterType == typeof(IFormFile) || 
                       p.ParameterType == typeof(IEnumerable<IFormFile>))
            .ToList();

        if (!fileParams.Any())
            return;

        // Remove auto-generated parameters to avoid conflicts
        operation.Parameters?.Clear();

        operation.RequestBody = new OpenApiRequestBody
        {
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        Properties = context.MethodInfo.GetParameters()
                            .ToDictionary(
                                p => p.Name ?? "unknown",
                                p => p.ParameterType == typeof(IFormFile)
                                    ? new OpenApiSchema { Type = "string", Format = "binary" }
                                    : new OpenApiSchema { Type = GetSchemaType(p.ParameterType) }
                            ),
                        Required = context.MethodInfo.GetParameters()
                            .Where(p => !p.IsOptional && !IsNullable(p.ParameterType))
                            .Select(p => p.Name ?? "unknown")
                            .ToHashSet()
                    }
                }
            }
        };
    }

    private static string GetSchemaType(Type type)
    {
        if (type == typeof(int) || type == typeof(long)) return "integer";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return "number";
        return "string";
    }

    private static bool IsNullable(Type type)
    {
        return Nullable.GetUnderlyingType(type) != null || 
               !type.IsValueType ||
               type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
    }
}
