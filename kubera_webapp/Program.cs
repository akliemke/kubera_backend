using FinancialEngineCore;
using FinancialEngineCore.Infra;
using KuberaWebApp.Controllers;

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
builder.Services.AddSingleton<BrokerRepository>();
var app = builder.Build();
builder.Services.AddHostedService<BillingWorker>();

app.UseCors("AllowAngular");

app.MapReportEndpoints();
app.MapBrokerEndpoints();
app.MapFinancialEndpoints();

app.MapGet("/", () => "Hello World!");

app.Run();
