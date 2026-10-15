using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Cierres;

/// <summary>Acceso a datos de los meses cerrados del hogar actual (el implementador aplica el aislamiento por hogar).</summary>
public interface IRepositorioCierres
{
    /// <summary>Meses cerrados del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<MesCerrado>> ListarAsync(CancellationToken ct);

    /// <summary>Nombre del miembro vinculado a cada usuario del hogar, para mostrar quién cerró.</summary>
    Task<IReadOnlyDictionary<Guid, string>> NombresPorUsuarioAsync(CancellationToken ct);

    /// <summary>Si el mes (primer día) ya está cerrado.</summary>
    Task<bool> EstaCerradoAsync(DateOnly inicio, CancellationToken ct);

    /// <summary>Guarda el cierre; false si otro cierre simultáneo del mismo mes se adelantó (índice único).</summary>
    Task<bool> GuardarAsync(MesCerrado cierre, CancellationToken ct);

    /// <summary>Reabre el mes borrando su cierre; false si no estaba cerrado.</summary>
    Task<bool> EliminarAsync(DateOnly inicio, CancellationToken ct);
}
