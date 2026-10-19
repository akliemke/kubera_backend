using FinancialEngineCore.Domain;
using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KuberaWebApp.Services;

public class MonthlyPdfReportService
{
    private static readonly string[] MonthNames =
        ["Janeiro", "Fevereiro", "Marco", "Abril", "Maio", "Junho",
         "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"];

    public byte[] Generate(
        BrokerModel broker,
        int year,
        int month,
        IReadOnlyList<TraderMonthSummary> traderSummaries,
        List<OrderBillingDetail> orderDetails,
        IEnumerable<FixedMonthlyFee>? fixedFees = null,
        IEnumerable<EntityBillingConfig>? entityConfigs = null,
        IEnumerable<MarketDataBilling>? marketDataBilling = null)
    {
        var monthName = MonthNames[month - 1];
        var totalFeeVariavel = traderSummaries.Sum(s => s.TotalFee);
        var fixedFeeList = fixedFees?.ToList() ?? [];
        var totalFeeFixo = fixedFeeList.Sum(f => f.Amount);

        // Floor fee adjustments per entity
        var floorAdjustments = FloorFeeCalculator.Calculate(traderSummaries, entityConfigs ?? []);
        var totalFloorAdjustment = floorAdjustments.Sum(a => a.Adjustment);

        // Market Data: one charge per linked user, added at the end of the broker's billing
        var marketDataList = marketDataBilling?.OrderBy(m => m.Trader).ToList() ?? [];
        var totalMarketData = marketDataList.Sum(m => m.Amount);

        var totalFee = totalFeeVariavel + totalFeeFixo + totalFloorAdjustment + totalMarketData;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.MarginHorizontal(30);
                page.MarginVertical(25);
                page.DefaultTextStyle(x => x.FontSize(9));

                // Header
                page.Header().Column(col =>
                {
                    col.Item().Text("RELATORIO MENSAL SOLUTIONS")
                        .Bold().FontSize(16);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"Corretora: {broker.Code} - {broker.Name}")
                            .FontSize(11);
                        row.RelativeItem().AlignRight().Text($"Periodo: {monthName}/{year}")
                            .FontSize(11);
                    });

                    col.Item().PaddingTop(5).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                    col.Item().PaddingBottom(5);
                });

                // Content
                page.Content().Column(col =>
                {
                    // Section 1: Trader/Entity Summary
                    col.Item().Text("RESUMO POR TRADER/ENTIDADE").Bold().FontSize(11);
                    col.Item().PaddingTop(5);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3); // Trader
                            columns.RelativeColumn(2); // AssetClass
                            columns.RelativeColumn(3); // Notional
                            columns.RelativeColumn(2); // Qty
                            columns.RelativeColumn(3); // Fee
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                .Text("Trader/Entidade").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                .Text("Classe").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                .AlignRight().Text("Notional").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                .AlignRight().Text("Qty").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                .AlignRight().Text("Fee (R$)").Bold();
                        });

                        foreach (var summary in traderSummaries.OrderBy(s => s.Trader).ThenBy(s => s.AssetClass))
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                .Text(summary.Trader);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                .Text(summary.AssetClass);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                .AlignRight().Text(summary.TotalNotional.ToString("N2"));
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                .AlignRight().Text(summary.TotalQty.ToString("N0"));
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                .AlignRight().Text(summary.TotalFee.ToString("N2"));
                        }

                        // Total row
                        table.Cell().ColumnSpan(4).Background(Colors.Grey.Lighten4).Padding(4)
                            .AlignRight().Text("TOTAL").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(4)
                            .AlignRight().Text(totalFee.ToString("N2")).Bold();
                    });

                    col.Item().PaddingVertical(10);

                    // Section 2: Fixed Monthly Fees
                    if (fixedFeeList.Count > 0)
                    {
                        col.Item().Text("COBRANCAS FIXAS MENSAIS").Bold().FontSize(11);
                        col.Item().PaddingTop(5);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(5); // Descricao
                                columns.RelativeColumn(3); // Valor
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                    .Text("Descricao").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                    .AlignRight().Text("Valor (R$)").Bold();
                            });

                            foreach (var fee in fixedFeeList.OrderBy(f => f.Description))
                            {
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text(fee.Description);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .AlignRight().Text(fee.Amount.ToString("N2"));
                            }

                            // Subtotal row
                            table.Cell().Background(Colors.Grey.Lighten4).Padding(4)
                                .AlignRight().Text("SUBTOTAL").Bold();
                            table.Cell().Background(Colors.Grey.Lighten4).Padding(4)
                                .AlignRight().Text(totalFeeFixo.ToString("N2")).Bold();
                        });

                        col.Item().PaddingVertical(10);
                    }

                    // Section: Floor fee adjustments per entity
                    if (floorAdjustments.Count > 0)
                    {
                        col.Item().Text("AJUSTES FLOOR POR ENTIDADE").Bold().FontSize(11);
                        col.Item().PaddingTop(5);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3); // Entity
                                columns.RelativeColumn(2); // Floor Fee
                                columns.RelativeColumn(2); // Calculated Fee
                                columns.RelativeColumn(2); // Adjustment
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                    .Text("Entidade").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                    .AlignRight().Text("Floor (R$)").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                    .AlignRight().Text("Calculado (R$)").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                    .AlignRight().Text("Ajuste (R$)").Bold();
                            });

                            foreach (var adj in floorAdjustments.OrderBy(a => a.Entity))
                            {
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text(adj.Entity);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .AlignRight().Text(adj.FloorFee.ToString("N2"));
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .AlignRight().Text(adj.CalculatedFee.ToString("N2"));
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .AlignRight().Text(adj.Adjustment.ToString("N2"));
                            }

                            // Subtotal row
                            table.Cell().ColumnSpan(3).Background(Colors.Grey.Lighten4).Padding(4)
                                .AlignRight().Text("SUBTOTAL").Bold();
                            table.Cell().Background(Colors.Grey.Lighten4).Padding(4)
                                .AlignRight().Text(totalFloorAdjustment.ToString("N2")).Bold();
                        });

                        col.Item().PaddingVertical(10);
                    }

                    // Section: Market Data (last charge of the broker's billing)
                    if (marketDataList.Count > 0)
                    {
                        col.Item().Text($"MARKET DATA ({marketDataList.Count} usuarios)").Bold().FontSize(11);
                        col.Item().PaddingTop(5);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(5); // Usuario
                                columns.RelativeColumn(3); // Valor
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                    .Text("Usuario").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                    .AlignRight().Text("Valor (R$)").Bold();
                            });

                            foreach (var charge in marketDataList)
                            {
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text(charge.Trader);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .AlignRight().Text(charge.Amount.ToString("N2"));
                            }

                            // Subtotal row
                            table.Cell().Background(Colors.Grey.Lighten4).Padding(4)
                                .AlignRight().Text("SUBTOTAL").Bold();
                            table.Cell().Background(Colors.Grey.Lighten4).Padding(4)
                                .AlignRight().Text(totalMarketData.ToString("N2")).Bold();
                        });

                        col.Item().PaddingVertical(10);
                    }

                    // Section 3: Order details grouped by (Trader, AssetClass)
                    col.Item().Text("DETALHAMENTO POR ORDEM").Bold().FontSize(11);
                    col.Item().PaddingTop(5);

                    var groups = orderDetails
                        .GroupBy(o => new { o.Trader, o.AssetClass })
                        .OrderBy(g => g.Key.Trader)
                        .ThenBy(g => g.Key.AssetClass);

                    foreach (var group in groups)
                    {
                        var groupFee = group.Sum(o => o.FeeAmount);

                        col.Item().PaddingTop(5).Text($"Trader: {group.Key.Trader} | {group.Key.AssetClass}")
                            .SemiBold().FontSize(10);

                        col.Item().PaddingTop(3).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2); // OrderId
                                columns.RelativeColumn(2); // Symbol
                                columns.RelativeColumn(1); // Side
                                columns.RelativeColumn(2); // TradeDate
                                columns.RelativeColumn(2); // Qty
                                columns.RelativeColumn(3); // Notional
                                columns.RelativeColumn(2); // Fee
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(3)
                                    .Text("Ordem").Bold().FontSize(8);
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(3)
                                    .Text("Ativo").Bold().FontSize(8);
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(3)
                                    .Text("Lado").Bold().FontSize(8);
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(3)
                                    .Text("Data").Bold().FontSize(8);
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(3)
                                    .AlignRight().Text("Qty").Bold().FontSize(8);
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(3)
                                    .AlignRight().Text("Notional").Bold().FontSize(8);
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(3)
                                    .AlignRight().Text("Fee (R$)").Bold().FontSize(8);
                            });

                            foreach (var order in group)
                            {
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(2)
                                    .Text(order.OrderId.ToString()).FontSize(8);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(2)
                                    .Text(order.Symbol ?? "-").FontSize(8);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(2)
                                    .Text(order.Side ?? "-").FontSize(8);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(2)
                                    .Text(order.TradeDate.ToString("dd/MM")).FontSize(8);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(2)
                                    .AlignRight().Text(order.Qty.ToString("N0")).FontSize(8);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(2)
                                    .AlignRight().Text(order.Notional.ToString("N2")).FontSize(8);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(2)
                                    .AlignRight().Text(order.FeeAmount.ToString("N2")).FontSize(8);
                            }

                            // Group subtotal
                            table.Cell().ColumnSpan(6).Background(Colors.Grey.Lighten4).Padding(2)
                                .AlignRight().Text("Subtotal").SemiBold().FontSize(8);
                            table.Cell().Background(Colors.Grey.Lighten4).Padding(2)
                                .AlignRight().Text(groupFee.ToString("N2")).SemiBold().FontSize(8);
                        });
                    }
                });

                // Footer
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Pagina ");
                    text.CurrentPageNumber();
                    text.Span(" de ");
                    text.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }
}
