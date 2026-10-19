namespace FinancialEngineCore.Domain;

/// <summary>
/// Distribui um fee total entre itens proporcionalmente a um peso,
/// garantindo que a soma das parcelas (em centavos) feche com o total arredondado.
/// </summary>
public static class FeeDistribution
{
    /// <summary>
    /// Método do maior resto: cada item recebe o valor truncado em centavos e os centavos
    /// restantes vão para os itens com maior parte fracionária (empate: maior peso, depois ordem).
    /// </summary>
    /// <returns>Parcelas na mesma ordem dos pesos. Σ parcelas = Math.Round(totalFee, 2).</returns>
    public static decimal[] Distribute(IReadOnlyList<decimal> weights, decimal totalFee)
    {
        var shares = new decimal[weights.Count];
        var totalWeight = weights.Sum();

        if (weights.Count == 0 || totalWeight <= 0 || totalFee == 0)
            return shares;

        var totalCents = Math.Round(totalFee * 100m, MidpointRounding.AwayFromZero);
        var remainders = new decimal[weights.Count];
        decimal allocatedCents = 0;

        for (var i = 0; i < weights.Count; i++)
        {
            var exactCents = weights[i] / totalWeight * totalCents;
            var floorCents = Math.Floor(exactCents);
            shares[i] = floorCents;
            remainders[i] = exactCents - floorCents;
            allocatedCents += floorCents;
        }

        var leftover = (int)(totalCents - allocatedCents);
        foreach (var i in Enumerable.Range(0, weights.Count)
                     .OrderByDescending(i => remainders[i])
                     .ThenByDescending(i => weights[i])
                     .Take(leftover))
        {
            shares[i] += 1;
        }

        for (var i = 0; i < shares.Length; i++)
            shares[i] /= 100m;

        return shares;
    }
}
