using System.Net.Http.Json;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Infra;

/// <summary>
/// Client da API externa que informa quais usuários têm conta vinculada a cada broker (estado atual).
/// Contrato assumido (ajustar aqui quando o contrato real for definido):
///   GET {BaseUrl}/accounts -> [{ "broker": 935, "user": "JOAO" }, ...]
/// </summary>
public class UserExchangeAccountApiClient
{
    private readonly HttpClient _http;

    public UserExchangeAccountApiClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>False quando UserExchangeAccountsApi:BaseUrl não está configurado.</summary>
    public bool IsConfigured => _http.BaseAddress is not null;

    public async Task<List<UserExchangeAccount>> GetActiveAccountsAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("UserExchangeAccountsApi:BaseUrl não configurado.");

        var items = await _http.GetFromJsonAsync<List<AccountDto>>("accounts", cancellationToken) ?? [];

        return items
            .Where(i => i.Broker > 0 && !string.IsNullOrWhiteSpace(i.User))
            .Select(i => new UserExchangeAccount(i.Broker, i.User!.Trim()))
            .ToList();
    }

    private sealed record AccountDto(int Broker, string? User);
}
