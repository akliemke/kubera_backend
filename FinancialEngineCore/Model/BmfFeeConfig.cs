using System.Text.Json.Serialization;

namespace FinancialEngineCore.Model;

/// <summary>
/// Configuração de cobrança para BMF (futuros).
/// Cobrança por número de contratos com CAP mensal único.
/// </summary>
public class BmfFeeConfig
{
    public long Id { get; set; }
    public int Broker { get; set; }

    /// <summary>
    /// Se NULL = regra padrão do broker.
    /// Se preenchido = regra especial 100% custom para esta chave (trader ou entity, ver KeyType).
    /// </summary>
    public string? Trader { get; set; }

    /// <summary>Tipo da regra especial: TRADER ou ENTITY (NULL quando Trader é NULL)</summary>
    public string? KeyType { get; set; }

    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }

    /// <summary>Valor por contrato Mini (WIN, WDO)</summary>
    public decimal ValorMini { get; set; }

    /// <summary>Valor por contrato Cheio (IND, DOL)</summary>
    public decimal ValorCheio { get; set; }

    /// <summary>Valor DI1 para 1º ao 4º vencimento</summary>
    [JsonPropertyName("di1Venc1a4")]
    public decimal DI1Venc1a4 { get; set; }

    /// <summary>Valor DI1 para 5º vencimento</summary>
    [JsonPropertyName("di1Venc5")]
    public decimal DI1Venc5 { get; set; }

    /// <summary>Valor DI1 para 6º vencimento</summary>
    [JsonPropertyName("di1Venc6")]
    public decimal DI1Venc6 { get; set; }

    /// <summary>Valor DI1 para 7º vencimento</summary>
    [JsonPropertyName("di1Venc7")]
    public decimal DI1Venc7 { get; set; }

    /// <summary>Valor DI1 para 8º vencimento em diante</summary>
    [JsonPropertyName("di1Venc8Plus")]
    public decimal DI1Venc8Plus { get; set; }

    /// <summary>CAP mensal único (engloba Mini + Cheio + DI1). NULL = sem teto</summary>
    public decimal? CapMensal { get; set; }

    public bool IsActive { get; set; }
}
