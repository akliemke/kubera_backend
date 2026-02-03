using FinancialEngineCore;
using FinancialEngineCore.Domain.Services;
using FinancialEngineCore.Domain.Strategies;
using FinancialEngineCore.Infra;
using HealthChecks.NpgSql;
using KuberaWebApp.Controllers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// add appsettings configuration file to the configuration sources

var connectionString = builder.Configuration.GetConnectionString("Default");

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("Connection string 'Default' is not configured.");
}


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular",
        builder => builder.WithOrigins("http://localhost:4200")
                          .AllowAnyMethod()
                          .AllowAnyHeader());
});

builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
builder.Services.AddSingleton<DbConnectionFactory>();

// Repositories
builder.Services.AddSingleton<BrokerRepository>();
builder.Services.AddSingleton<ProcessBillingRepository>();
builder.Services.AddSingleton<OrdersRepository>();
builder.Services.AddSingleton<EquityFeeBracketRepository>();
builder.Services.AddSingleton<BmfFeeConfigRepository>();
builder.Services.AddSingleton<DailyBillingRepository>();
builder.Services.AddSingleton<OrderBillingRepository>();
builder.Services.AddSingleton<BracketFeeTrackingRepository>();

// Strategies
builder.Services.AddSingleton<ICalculationStrategy, EquityCalculationStrategy>();
builder.Services.AddSingleton<ICalculationStrategy, BmfCalculationStrategy>();
builder.Services.AddSingleton<CalculationStrategyRegistry>();

// Services
builder.Services.AddSingleton<IBillingProcessingService, BillingProcessingService>();

// Background Worker
builder.Services.AddHostedService<BillingWorker>();

// Health Checks
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgresql", tags: ["db", "database"]);

var app = builder.Build();

app.UseCors("AllowAngular");

app.MapReportEndpoints();
app.MapBrokerEndpoints();
app.MapFinancialEndpoints();
app.MapEquityFeeBracketEndpoints();
app.MapBmfFeeConfigEndpoints();

app.MapGet("/", () => "Hello World!");

// Health Check Endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            duration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                error = e.Value.Exception?.Message
            })
        };
        await context.Response.WriteAsJsonAsync(result);
    }
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // No checks, just returns healthy if app is running
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("db")
});

app.Run();
