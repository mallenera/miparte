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

    /// <summary>Modo de liquidación del hogar.</summary>
    Task<ModoLiquidacion> ModoAsync(CancellationToken ct);

    /// <summary>Cambia el modo de liquidación del hogar; no toca gastos ni ciclos.</summary>
    Task EstablecerModoAsync(ModoLiquidacion modo, CancellationToken ct);

    /// <summary>Gastos del hogar que aún no liquida ningún ciclo, con su reparto, sin filtrar por fecha.</summary>
    Task<IReadOnlyList<Gasto>> GastosPendientesAsync(CancellationToken ct);

    /// <summary>Número del último ciclo cerrado del hogar; 0 si no hay ninguno.</summary>
    Task<int> UltimoNumeroCicloAsync(CancellationToken ct);

    /// <summary>
    /// Guarda el ciclo y marca como liquidados los gastos indicados, en la misma operación. Solo debe llamarse dentro de
    /// <see cref="EnExclusionAsync{T}"/>.
    /// </summary>
    Task CerrarCicloAsync(CicloLiquidacion ciclo, IReadOnlyList<Guid> gastos, CancellationToken ct);

    /// <summary>Ciclos cerrados del hogar, del más reciente al más antiguo, con el nombre de quien los cerró.</summary>
    Task<IReadOnlyList<CicloConAutor>> ListarCiclosAsync(CancellationToken ct);

    /// <summary>
    /// Ejecuta <paramref name="operacion"/> bloqueando el hogar frente a otros cierres de ciclo concurrentes: dos cierres a la vez
    /// no pueden leer los mismos gastos pendientes ni el mismo número de ciclo.
    /// </summary>
    Task<T> EnExclusionAsync<T>(Func<Task<T>> operacion, CancellationToken ct);
}

/// <summary>Ciclo cerrado con el nombre de quien lo cerró (null si ya no consta).</summary>
public sealed record CicloConAutor(CicloLiquidacion Ciclo, string? Autor);

/// <summary>
/// Lanzada por el repositorio cuando los gastos que se iban a liquidar ya no coinciden con los marcados (otra petición los ha
/// borrado mientras se cerraba). El servicio la traduce a un conflicto y la transacción se deshace.
/// </summary>
public sealed class CierreConcurrenteException : Exception
{
}
