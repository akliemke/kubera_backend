var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Hello World!");
app.MapGet("/corretoras", () => "List of brokers");
app.MapGet("/corretoras/{codigo}", (string codigo) => $"Detalhes de uma corretora {codigo}");
app.MapGet("/relatorios/cobranca/mensal/{ano_mes}", (string ano_mes) =>
{
    return $"Relatório mensal para o ano e mês: {ano_mes}";
});
app.MapGet("/relatorios/cobranca/anual/{ano}", (string ano) =>
{
    return $"Detalhes do relatório anual para o ano: {ano}";
});
app.MapGet("/relatorios/cobranca/diario/{data}", (string data) =>
{
    return $"Detalhes do relatório diário para a data: {data}";
});
app.MapGet("/processamento/{tipo}/{id}", (string tipo, string id) =>
{
    return $"Processamento do tipo {tipo} com ID {id}";
});


app.Run();
