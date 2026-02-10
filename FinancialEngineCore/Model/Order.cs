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

    /// <summary>Símbolo do ativo (ex: PETR4, VALE3)</summary>
    public required string Symbol { get; set; }

    /// <summary>
    /// Símbolo de exchange para BMF (ex: WIN, WDO, IND, DOL, DI1F25).
    /// Usado para identificar tipo de contrato e vencimento.
    /// </summary>
    public string? ExchangeSymbol { get; set; }

    /// <summary>
    /// Classe do ativo: "EQUITY" ou "BMF".
    /// Determina qual lógica de cobrança aplicar.
    /// </summary>
    public required string AssetClass { get; set; }

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