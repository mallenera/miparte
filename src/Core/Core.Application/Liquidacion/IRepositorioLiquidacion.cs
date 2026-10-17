using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Liquidacion;

/// <summary>
/// Acceso a datos que necesitan el resumen, la liquidación y los pagos de un mes. Todo se refiere al hogar actual: quien
/// lo implementa se encarga del aislamiento por hogar.
/// </summary>
public interface IRepositorioLiquidacion
{
    /// <summary>Gastos con su reparto cuya fecha cae en el mes que empieza en <paramref name="inicio"/>.</summary>
    Task<IReadOnlyList<Gasto>> GastosDelMesAsync(DateOnly inicio, CancellationToken ct);

    /// <summary>Todos los miembros del hogar, activos o no.</summary>
    Task<IReadOnlyList<Miembro>> MiembrosAsync(CancellationToken ct);

    /// <summary>Nombre de cada categoría del hogar por id.</summary>
    Task<IReadOnlyDictionary<Guid, string>> NombresCategoriasAsync(CancellationToken ct);

    /// <summary>Histórico de la cuenta común hasta el fin del mes que empieza en <paramref name="inicio"/>.</summary>
    Task<HistoricoCuenta> HistoricoCuentaAsync(DateOnly inicio, CancellationToken ct);

    /// <summary>Pagos de liquidación del mes, por fecha.</summary>
    Task<IReadOnlyList<PagoLiquidacionRegistro>> PagosDelMesAsync(DateOnly inicio, CancellationToken ct);

    /// <summary>Si los dos miembros existen en el hogar.</summary>
    Task<bool> ExistenAmbosAsync(Guid uno, Guid otro, CancellationToken ct);

    /// <summary>Guarda un pago nuevo.</summary>
    Task AnadirPagoAsync(PagoLiquidacionRegistro pago, CancellationToken ct);

    /// <summary>Borra un pago; false si no existe en el hogar.</summary>
    Task<bool> EliminarPagoAsync(Guid id, CancellationToken ct);
}
