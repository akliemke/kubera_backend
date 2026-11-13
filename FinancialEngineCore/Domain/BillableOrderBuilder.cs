using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain;

/// <summary>Trade do dia de uma ordem (só o necessário para GTC/GTD e sintéticos).</summary>
public record DayTrade(long OrderId, decimal LastQty, decimal LastPx, string? UniqueTradeId);

/// <summary>
/// Monta as ordens cobráveis do dia a partir de history.orders, do mapa Symbol → Asset do dia e dos trades
/// do dia das ordens que precisam deles:
/// - Normal: passa direto, Asset do mapa (null se o Symbol não teve trade: o AssetClassifier deduz pelo Symbol)
/// - GTC/GTD: CumQty é cumulativo entre dias, usa os trades do dia (Σ LastQty, VWAP). Sem trade no dia: não entra
/// - Sintético (Symbol não começa com o Asset, ex: BTGDOLZ26/DOL): uma linha por perna (sufixo do UniqueTradeId),
///   LastQty no padrão do cheio convertido pelo fator da perna. Sem trade no dia: não entra
/// </summary>
public static class BillableOrderBuilder
{
    /// <summary>Perna de trade sem sufixo no UniqueTradeId (não classificada: não cobrada, gera warning)</summary>
    public const string UnknownLeg = "?";

    /// <summary>Contratos da perna por contrato cheio (1 DOL = 5 WDO). Perna sem fator = 1</summary>
    private static readonly Dictionary<string, decimal> LegFactors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WDO"] = 5,
        ["DOL"] = 1
    };

    public static bool IsGoodTillOrder(Order order) =>
        order.TimeInForce is { } tif &&
        (tif.Equals("GTC", StringComparison.OrdinalIgnoreCase) || tif.Equals("GTD", StringComparison.OrdinalIgnoreCase));

    public static bool IsSynthetic(Order order, string? asset) =>
        !string.IsNullOrWhiteSpace(asset) &&
        !order.Symbol.Trim().StartsWith(asset.Trim(), StringComparison.OrdinalIgnoreCase);

    public static string? GetAsset(Order order, IReadOnlyDictionary<string, string> symbolAssets) =>
        symbolAssets.TryGetValue(order.Symbol.Trim(), out var asset) ? asset : null;

    /// <summary>Ordens que precisam dos trades do dia (GTC/GTD ou sintético).</summary>
    public static bool NeedsTrades(Order order, IReadOnlyDictionary<string, string> symbolAssets) =>
        IsGoodTillOrder(order) || IsSynthetic(order, GetAsset(order, symbolAssets));

    public static List<Order> Build(
        IEnumerable<Order> orders,
        IReadOnlyDictionary<string, string> symbolAssets,
        IEnumerable<DayTrade> trades)
    {
        var tradesByOrder = trades.ToLookup(t => t.OrderId);
        var result = new List<Order>();

        foreach (var order in orders)
        {
            var asset = GetAsset(order, symbolAssets);

            if (IsSynthetic(order, asset))
            {
                foreach (var leg in tradesByOrder[order.Id].GroupBy(t => GetLeg(t.UniqueTradeId)))
                {
                    var (qty, vwap) = Aggregate(leg);
                    result.Add(Copy(order, leg.Key, qty * LegFactors.GetValueOrDefault(leg.Key, 1), vwap));
                }
            }
            else if (IsGoodTillOrder(order))
            {
                var dayTrades = tradesByOrder[order.Id].ToList();
                if (dayTrades.Count == 0)
                    continue;

                var (qty, vwap) = Aggregate(dayTrades);
                result.Add(Copy(order, asset, qty, vwap));
            }
            else
            {
                order.Asset = asset;
                result.Add(order);
            }
        }

        return result;
    }

    /// <summary>Perna = sufixo após o último '-' do UniqueTradeId (1234-WDO -> WDO). Sem sufixo: "?"</summary>
    private static string GetLeg(string? uniqueTradeId)
    {
        var dash = uniqueTradeId?.LastIndexOf('-') ?? -1;
        if (dash < 0 || dash == uniqueTradeId!.Length - 1)
            return UnknownLeg;

        return uniqueTradeId[(dash + 1)..].Trim().ToUpperInvariant();
    }

    private static (decimal Qty, decimal Vwap) Aggregate(IEnumerable<DayTrade> trades)
    {
        decimal qty = 0, value = 0;
        foreach (var t in trades)
        {
            qty += t.LastQty;
            value += t.LastQty * t.LastPx;
        }
        return (qty, qty == 0 ? 0 : value / qty);
    }

    private static Order Copy(Order order, string? asset, decimal cumQty, decimal avgPx) => new()
    {
        Id = order.Id,
        CumQty = cumQty,
        AvgPx = avgPx,
        Symbol = order.Symbol,
        Asset = asset,
        EnteringTrader = order.EnteringTrader,
        Entity = order.Entity,
        TradeDate = order.TradeDate,
        EnteringFirm = order.EnteringFirm,
        OmsInstance = order.OmsInstance,
        Side = order.Side,
        TimeInForce = order.TimeInForce
    };
}
