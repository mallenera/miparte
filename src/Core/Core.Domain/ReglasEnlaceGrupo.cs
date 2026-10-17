using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>Estado de un enlace de grupo en un momento dado.</summary>
public enum EstadoEnlaceGrupo
{
    /// <summary>Admite uniones.</summary>
    Activo,

    /// <summary>Revocado por un miembro con permiso: ya no admite uniones.</summary>
    Revocado,

    /// <summary>Ha llegado a su fecha de caducidad.</summary>
    Caducado,
}

/// <summary>Reglas de los enlaces de grupo que no dependen de persistencia.</summary>
public static class ReglasEnlaceGrupo
{
    /// <summary>
    /// Estado del enlace: revocado tiene prioridad sobre caducado, porque la revocación es lo que la persona hizo y la caducidad
    /// solo lo que pasó después. Un enlace sin fecha de caducidad no caduca nunca.
    /// </summary>
    /// <param name="enlace">Enlace a evaluar.</param>
    /// <param name="ahora">Instante de la comprobación.</param>
    public static EstadoEnlaceGrupo Estado(EnlaceGrupo enlace, DateTimeOffset ahora)
    {
        if (!enlace.Activo) return EstadoEnlaceGrupo.Revocado;
        if (enlace.CaducaEn is { } caduca && caduca <= ahora) return EstadoEnlaceGrupo.Caducado;
        return EstadoEnlaceGrupo.Activo;
    }

    /// <summary>Texto del estado para la API (<c>activo</c>, <c>revocado</c>, <c>caducado</c>).</summary>
    /// <param name="estado">Estado a traducir.</param>
    public static string TextoEstado(EstadoEnlaceGrupo estado) => estado switch
    {
        EstadoEnlaceGrupo.Activo => "activo",
        EstadoEnlaceGrupo.Revocado => "revocado",
        EstadoEnlaceGrupo.Caducado => "caducado",
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };
}
