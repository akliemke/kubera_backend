namespace FinancialEngineCore;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddHostedService<BillingWorker>();

        var host = builder.Build();
        host.Run();
    }
}