using kubera_webapp.Controllers;
using kubera_webapp.Repository;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular",
        builder => builder.WithOrigins("http://localhost:4200")
                          .AllowAnyMethod()
                          .AllowAnyHeader());
});

builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
builder.Services.AddSingleton<BrokerRepository>();

var app = builder.Build();
app.UseCors("AllowAngular");

app.MapReportEndpoints();
app.MapBrokerEndpoints();
app.MapFinancialEndpoints();

app.MapGet("/", () => "Hello World!");

app.Run();
