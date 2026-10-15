using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Auditoria;

/// <summary>Criterios de la lista del historial; <paramref name="Hasta"/> y <paramref name="DespuesDeId"/> son el cursor de paginación.</summary>
/// <param name="Entidad">Tipo de entidad (gasto, miembro...).</param>
/// <param name="EntidadId">Entidad concreta.</param>
/// <param name="Limite">Eventos por página (1-200; 50 si es null).</param>
/// <param name="Hasta">Instante del último evento recibido.</param>
/// <param name="DespuesDeId">Identificador del último evento recibido.</param>
public sealed record ConsultaAuditoria(string? Entidad, Guid? EntidadId, int? Limite, DateTimeOffset? Hasta, Guid? DespuesDeId);

/// <summary>Página del historial con el nombre de quien hizo cada cambio.</summary>
/// <param name="Eventos">Eventos de más reciente a más antiguo.</param>
/// <param name="Autores">Nombre del miembro por id de usuario.</param>
public sealed record PaginaAuditoria(IReadOnlyList<EventoAuditoria> Eventos, IReadOnlyDictionary<Guid, string> Autores);

/// <summary>Acceso de solo lectura al historial del hogar actual.</summary>
public interface IRepositorioAuditoria
{
    /// <summary>
    /// Eventos del más reciente al más antiguo (empates por id) que cumplen la consulta. El cursor es compuesto: solo con el
    /// instante se perderían los eventos de un mismo guardado, que comparten <c>cuando</c>.
    /// </summary>
    Task<IReadOnlyList<EventoAuditoria>> ListarAsync(ConsultaAuditoria consulta, int limite, CancellationToken ct);

    /// <summary>Nombre del miembro vinculado a cada usuario del hogar.</summary>
    Task<IReadOnlyDictionary<Guid, string>> AutoresAsync(CancellationToken ct);
}

/// <summary>Caso de uso del historial de cambios del hogar (solo lectura).</summary>
public sealed class ServicioAuditoria(IRepositorioAuditoria repositorio)
{
    /// <summary>Eventos devueltos por defecto en una página.</summary>
    public const int LimitePorDefecto = 50;

    /// <summary>Máximo de eventos por página.</summary>
    public const int LimiteMaximo = 200;

    /// <summary>Página del historial. Inválida si el límite no está entre 1 y <see cref="LimiteMaximo"/>.</summary>
    public async Task<Resultado<PaginaAuditoria>> ListarAsync(ConsultaAuditoria consulta, CancellationToken ct)
    {
        var n = consulta.Limite ?? LimitePorDefecto;
        if (n is < 1 or > LimiteMaximo) return Resultado<PaginaAuditoria>.Invalido($"El límite debe estar entre 1 y {LimiteMaximo}.");

        var eventos = await repositorio.ListarAsync(consulta, n, ct);
        return Resultado<PaginaAuditoria>.Ok(new PaginaAuditoria(eventos, await repositorio.AutoresAsync(ct)));
    }
}
