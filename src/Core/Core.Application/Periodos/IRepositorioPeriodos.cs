using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Periodos;

/// <summary>
/// Acceso al periodo propio del hogar actual (el implementador aplica el aislamiento por hogar). El cierre de ciclos y
/// sus gastos pasan por <see cref="Liquidacion.IRepositorioLiquidacion"/>, dentro de la misma transacción.
/// </summary>
public interface IRepositorioPeriodos
{
    /// <summary>El hogar actual, con su periodo; null si no hay hogar. Los cambios se persisten con <see cref="GuardarAsync"/>.</summary>
    Task<Hogar?> HogarAsync(CancellationToken ct);

    /// <summary>Guarda los cambios pendientes del hogar.</summary>
    Task GuardarAsync(CancellationToken ct);
}
