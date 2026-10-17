using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Recurrentes;

/// <summary>Resultado de guardar los gastos generados de un mes.</summary>
public enum ResultadoGuardadoGenerados
{
    /// <summary>Todos los gastos se guardaron.</summary>
    Guardado,

    /// <summary>La base de datos rechazó la escritura porque otra petición cerró el mes tras comprobarlo.</summary>
    MesCerrado,

    /// <summary>Otra petición generó alguno de los gastos antes; no se guardó nada.</summary>
    Duplicado,
}

/// <summary>
/// Acceso a datos de las plantillas de gasto recurrente del hogar actual (el implementador aplica el aislamiento por hogar).
/// Las consultas de miembros, categorías y perfiles las aporta <c>IRepositorioGastos</c>.
/// </summary>
public interface IRepositorioRecurrentes
{
    /// <summary>Plantillas ordenadas por día del mes.</summary>
    Task<IReadOnlyList<GastoRecurrente>> ListarAsync(CancellationToken ct);

    /// <summary>Una plantilla para leerla; null si no existe en el hogar.</summary>
    Task<GastoRecurrente?> ObtenerAsync(Guid id, CancellationToken ct);

    /// <summary>Una plantilla para modificarla con <see cref="GuardarAsync"/>; null si no existe en el hogar.</summary>
    Task<GastoRecurrente?> ObtenerParaEditarAsync(Guid id, CancellationToken ct);

    /// <summary>Marca una plantilla nueva para guardarla con <see cref="GuardarAsync"/>.</summary>
    void Anadir(GastoRecurrente plantilla);

    /// <summary>Marca una plantilla para borrarla con <see cref="GuardarAsync"/>.</summary>
    void Eliminar(GastoRecurrente plantilla);

    /// <summary>Guarda los cambios pendientes de plantillas.</summary>
    Task GuardarAsync(CancellationToken ct);

    /// <summary>Si la plantilla ya ha generado algún gasto.</summary>
    Task<bool> TieneGastosGeneradosAsync(Guid id, CancellationToken ct);

    /// <summary>Plantillas activas ordenadas por día del mes.</summary>
    Task<IReadOnlyList<GastoRecurrente>> ActivasAsync(CancellationToken ct);

    /// <summary>Identificadores de las plantillas que ya generaron gasto en el mes que empieza en <paramref name="inicio"/>.</summary>
    Task<IReadOnlySet<Guid>> GeneradasEnElMesAsync(DateOnly inicio, CancellationToken ct);

    /// <summary>Los perfiles de reparto del hogar con sus detalles, por id.</summary>
    Task<IReadOnlyDictionary<Guid, PerfilReparto>> PerfilesAsync(CancellationToken ct);

    /// <summary>
    /// Guarda todos los gastos generados o ninguno. Si ya existe alguno (otra petición se adelantó) deja el contexto limpio para
    /// poder reintentar con una lectura nueva.
    /// </summary>
    Task<ResultadoGuardadoGenerados> GuardarGeneradosAsync(IReadOnlyList<Gasto> gastos, CancellationToken ct);
}
