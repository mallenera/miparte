using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>Valor de un miembro para el reparto: porcentaje o partes, según el modo.</summary>
public sealed record MiembroReparto(Guid MiembroId, decimal Valor);

/// <summary>Importe que asume un miembro de un gasto.</summary>
public sealed record ParteAsumida(Guid MiembroId, decimal Importe);

/// <summary>Reparto de un gasto entre miembros reales según el modo del perfil.</summary>
public static class RepartoMiembros
{
    /// <summary>
    /// Reparte un importe entre N miembros según el modo. Porcentaje y Partes usan el valor como peso;
    /// Individual asigna el 100%
    /// a quien paga. Redondeo a 2 decimales y el último miembro absorbe el céntimo sobrante.
    /// </summary>
    public static IReadOnlyList<ParteAsumida> Repartir(
        decimal importe, ModoReparto modo, IReadOnlyList<MiembroReparto> miembros, Guid pagadoPor)
    {
        if (importe <= 0) throw new ArgumentOutOfRangeException(nameof(importe), "El importe debe ser positivo.");
        if (miembros.Count == 0) throw new ArgumentException("Debe haber al menos un miembro.", nameof(miembros));
        if (miembros.Select(m => m.MiembroId).Distinct().Count() != miembros.Count)
            throw new ArgumentException("Hay miembros repetidos.", nameof(miembros));

        if (modo == ModoReparto.Individual && miembros.All(m => m.MiembroId != pagadoPor))
            throw new ArgumentException("Quien paga debe ser uno de los miembros.", nameof(pagadoPor));

        // Hogar monopersonal: 100% para el único miembro, sea cual sea el modo.
        if (miembros.Count == 1) return [new ParteAsumida(miembros[0].MiembroId, importe)];

        IReadOnlyList<decimal> pesos;
        switch (modo)
        {
            case ModoReparto.Individual:
                if (miembros.All(m => m.MiembroId != pagadoPor))
                    throw new ArgumentException("Quien paga debe ser uno de los miembros.", nameof(pagadoPor));
                pesos = miembros.Select(m => m.MiembroId == pagadoPor ? 1m : 0m).ToList();
                break;
            case ModoReparto.CuentaComun:
                throw new ArgumentException("Un gasto de la cuenta común no se reparte entre personas.", nameof(modo));
            case ModoReparto.Porcentaje:
            case ModoReparto.Partes:
                pesos = miembros.Select(m => m.Valor).ToList();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(modo));
        }

        var importes = Reparto.Dividir(importe, pesos);
        return miembros.Select((m, i) => new ParteAsumida(m.MiembroId, importes[i])).ToList();
    }
}
