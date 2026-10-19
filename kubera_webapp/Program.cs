using System.Data;
using Dapper;
using FinancialEngineCore;
using FinancialEngineCore.Domain.Services;
using FinancialEngineCore.Domain.Strategies;
using FinancialEngineCore.Infra;
using HealthChecks.NpgSql;
using KuberaWebApp.Controllers;
using KuberaWebApp.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using QuestPDF.Infrastructure;

SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());

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
        builder => builder.WithOrigins("http://localhost:4200").WithOrigins("http://127.0.0.1:49152")
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
builder.Services.AddSingleton<PriceModificationFactorRepository>();
builder.Services.AddSingleton<EntityBillingConfigRepository>();
builder.Services.AddSingleton<FixedMonthlyFeeRepository>();
builder.Services.AddSingleton<UserExchangeAccountSnapshotRepository>();
builder.Services.AddSingleton<MarketDataFeeConfigRepository>();
builder.Services.AddSingleton<MarketDataExclusionRepository>();
builder.Services.AddSingleton<MarketDataBillingRepository>();

// External API: users with accounts linked to each broker (Market Data billing)
builder.Services.AddHttpClient<UserExchangeAccountApiClient>(client =>
{
    var baseUrl = builder.Configuration["UserExchangeAccountsApi:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(baseUrl))
        client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
    client.Timeout = TimeSpan.FromSeconds(60);
});

// Strategies
builder.Services.AddSingleton<ICalculationStrategy, EquityCalculationStrategy>();
builder.Services.AddSingleton<ICalculationStrategy, BmfCalculationStrategy>();
builder.Services.AddSingleton<CalculationStrategyRegistry>();

// Services
builder.Services.AddSingleton<MarketDataService>();
builder.Services.AddSingleton<IBillingProcessingService, BillingProcessingService>();
builder.Services.AddSingleton<MonthlyPdfReportService>();
builder.Services.AddSingleton<RevenueReportService>();

// Background Worker
builder.Services.AddHostedService<BillingWorker>();
builder.Services.AddHostedService<UserExchangeAccountSyncWorker>();

// Health Checks
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgresql", tags: ["db", "database"]);

QuestPDF.Settings.License = LicenseType.Community;

//builder.Services.AddCors(options =>
//{
//    options.AddPolicy("DevCors", policy =>
//    {
//        policy
//            .WithOrigins(
//                "http://127.0.0.1:49152",
//                "http://localhost:49152"
//            )
//            .AllowAnyHeader()
//            .AllowAnyMethod();
//    });
//});

var app = builder.Build();

app.UseCors("AllowAngular");

app.MapReportEndpoints();
app.MapBrokerEndpoints();
app.MapFinancialEndpoints();
app.MapEquityFeeBracketEndpoints();
app.MapBmfFeeConfigEndpoints();
app.MapPriceModificationFactorEndpoints();
app.MapEntityBillingConfigEndpoints();
app.MapFixedMonthlyFeeEndpoints();
app.MapMarketDataEndpoints();
app.MapSeedDataEndpoints();

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

public class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override DateOnly Parse(object value) => value switch
    {
        DateTime dt => DateOnly.FromDateTime(dt),
        DateOnly d => d,
        _ => DateOnly.FromDateTime(Convert.ToDateTime(value))
    };

    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        parameter.DbType = DbType.Date;
    }
}
