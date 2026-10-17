using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Gastos;

/// <summary>Filtro de la lista de gastos; las fechas son inclusivas y todos los criterios se combinan con «y».</summary>
/// <param name="Desde">Primer día incluido; null sin límite.</param>
/// <param name="Hasta">Último día incluido; null sin límite.</param>
/// <param name="CategoriaId">Solo esa categoría.</param>
/// <param name="MiembroId">Solo gastos que paga o en los que asume una parte mayor que 0.</param>
/// <param name="Texto">Texto contenido en el concepto, sin distinguir mayúsculas.</param>
public sealed record FiltroGastos(DateOnly? Desde, DateOnly? Hasta, Guid? CategoriaId, Guid? MiembroId, string? Texto);

/// <summary>
/// Acceso a datos de los gastos del hogar actual (el implementador aplica el aislamiento por hogar). Los gastos que devuelven
/// <see cref="ObtenerParaEditarAsync"/> y <see cref="ObtenerParaBorrarAsync"/> se modifican en memoria y se persisten con
/// <see cref="GuardarAsync"/>.
/// </summary>
public interface IRepositorioGastos
{
    /// <summary>Gastos con su reparto que cumplen el filtro, del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<Gasto>> ListarAsync(FiltroGastos filtro, CancellationToken ct);

    /// <summary>Un gasto con su reparto, solo para leer; null si no existe en el hogar.</summary>
    Task<Gasto?> ObtenerAsync(Guid id, CancellationToken ct);

    /// <summary>Un gasto con su reparto para modificarlo; null si no existe en el hogar.</summary>
    Task<Gasto?> ObtenerParaEditarAsync(Guid id, CancellationToken ct);

    /// <summary>Si la categoría existe en el hogar.</summary>
    Task<bool> CategoriaExisteAsync(Guid id, CancellationToken ct);

    /// <summary>Si el miembro existe en el hogar, está activo y es adulto.</summary>
    Task<bool> EsAdultoActivoAsync(Guid id, CancellationToken ct);

    /// <summary>El perfil de reparto con sus detalles; null si no existe en el hogar.</summary>
    Task<PerfilReparto?> ObtenerPerfilAsync(Guid id, CancellationToken ct);

    /// <summary>Adultos activos del hogar, ordenados por nombre y id.</summary>
    Task<IReadOnlyList<Miembro>> AdultosActivosAsync(CancellationToken ct);

    /// <summary>Si la fecha cae dentro del periodo propio del hogar, y ese periodo está cerrado.</summary>
    Task<bool> FechaEnPeriodoCerradoAsync(DateOnly fecha, CancellationToken ct);

    /// <summary>Si el hogar tiene activada la cuenta común.</summary>
    Task<bool> CuentaComunActivaAsync(CancellationToken ct);

    /// <summary>Si el hogar tiene activado el ahorro.</summary>
    Task<bool> AhorroActivoAsync(CancellationToken ct);

    /// <summary>Datos del ahorro; <paramref name="excluirGasto"/> deja fuera un gasto que se está editando.</summary>
    Task<DatosAhorro> DatosAhorroAsync(Guid? excluirGasto, CancellationToken ct);

    /// <summary>Si el mes de la fecha está cerrado en el hogar.</summary>
    Task<bool> MesCerradoAsync(DateOnly fecha, CancellationToken ct);

    /// <summary>Marca un gasto nuevo para guardarlo con <see cref="GuardarAsync"/>.</summary>
    void Anadir(Gasto gasto);

    /// <summary>Marca un gasto y su reparto para borrarlos con <see cref="GuardarAsync"/>.</summary>
    void Eliminar(Gasto gasto);

    /// <summary>
    /// Guarda los cambios pendientes en una transacción. Devuelve false, sin guardar nada, si la base de datos rechaza la
    /// escritura porque otra petición cerró el mes después de comprobarlo.
    /// </summary>
    Task<bool> GuardarAsync(CancellationToken ct);

    /// <summary>
    /// Ejecuta <paramref name="operacion"/> bloqueando el hogar frente a los cierres de ciclo concurrentes: un gasto no puede
    /// cambiar ni borrarse mientras otra petición lo incluye en un ciclo que se está cerrando.
    /// </summary>
    Task<T> EnExclusionAsync<T>(Func<Task<T>> operacion, CancellationToken ct);
}
