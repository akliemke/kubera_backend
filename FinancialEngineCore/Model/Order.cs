namespace FinancialEngineCore.Model;

/// <summary>
/// Operação de bolsa vinda de sistema externo.
/// </summary>
public class Order
{
    /// <summary>ID da ordem no sistema externo</summary>
    public long Id { get; set; }

    /// <summary>Quantidade executada</summary>
    public decimal CumQty { get; set; }

    /// <summary>Preço médio de execução</summary>
    public decimal AvgPx { get; set; }

    /// <summary>Símbolo do ativo (ex: PETR4, WINZ25, DI1F27, BTGDOLZ26). DI1 usa o vencimento do Symbol.</summary>
    public required string Symbol { get; set; }

    /// <summary>
    /// Raiz do ativo (ex: PETR, WIN, DI1). Identifica a classe e o tipo de contrato BMF.
    /// Em sintético a view devolve uma linha por perna com Asset = perna (ex: WDO, DOL).
    /// </summary>
    public string? Asset { get; set; }

    /// <summary>
    /// Classe do ativo: "EQUITY", "BMF" ou "UNKNOWN". Não vem do banco:
    /// preenchida por <c>AssetClassifier</c> na leitura das ordens.
    /// </summary>
    public string AssetClass { get; set; } = AssetClasses.Unknown;

    /// <summary>Código do trader que executou a ordem</summary>
    public required string EnteringTrader { get; set; }

    /// <summary>Entidade/mesa</summary>
    public required string Entity { get; set; }

    /// <summary>Data da operação</summary>
    public DateOnly TradeDate { get; set; }

    /// <summary>Código do broker (corretora)</summary>
    public required string EnteringFirm { get; set; }

    /// <summary>Instância do OMS de origem</summary>
    public required string OmsInstance { get; set; }

    /// <summary>Lado da operação (BUY/SELL)</summary>
    public required string Side { get; set; }

    /// <summary>
    /// Volume financeiro calculado (Qty × Price).
    /// Usado para cobrança de EQUITY.
    /// </summary>
    public decimal Notional => CumQty * AvgPx;
}