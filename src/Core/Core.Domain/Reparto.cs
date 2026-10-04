namespace MiParte.Core.Domain;

/// <summary>Lógica pura de división de un importe entre miembros según pesos.</summary>
public static class Reparto
{
    /// <summary>
    /// Reparte un importe según pesos (porcentajes o partes). Redondea a 2 decimales
    /// y el último miembro absorbe el céntimo sobrante, de modo que la suma siempre
    /// es igual al importe.
    /// </summary>
    public static IReadOnlyList<decimal> Dividir(decimal importe, IReadOnlyList<decimal> pesos)
    {
        if (importe <= 0) throw new ArgumentOutOfRangeException(nameof(importe), "El importe debe ser positivo.");
        if (pesos.Count == 0) throw new ArgumentException("Debe haber al menos un miembro.", nameof(pesos));
        if (pesos.Any(p => p < 0)) throw new ArgumentException("Los pesos no pueden ser negativos.", nameof(pesos));

        var total = pesos.Sum();
        if (total <= 0) throw new ArgumentException("La suma de pesos debe ser positiva.", nameof(pesos));

        var resultado = new decimal[pesos.Count];
        var acumulado = 0m;
        for (var i = 0; i < pesos.Count - 1; i++)
        {
            resultado[i] = Math.Round(importe * pesos[i] / total, 2, MidpointRounding.AwayFromZero);
            acumulado += resultado[i];
        }
        resultado[^1] = importe - acumulado;
        return resultado;
    }
}
