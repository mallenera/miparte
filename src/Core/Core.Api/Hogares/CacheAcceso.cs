using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Hogares;

/// <summary>
/// Caché en memoria de los miembros activos de cada usuario (uno por hogar al que pertenece). Cada petición necesitaba
/// consultarlos para resolver el hogar, y las escrituras volvían a hacerlo para comprobar permisos: dos consultas por
/// petición que casi nunca devuelven nada nuevo. Es singleton y está acotada por <see cref="Vigencia"/>; además se
/// invalida de inmediato cuando una petición modifica miembros, invitaciones o hogares (ver <see cref="HogarActualMiddleware"/>),
/// de modo que quitar un permiso o desactivar a alguien surte efecto en la siguiente petición de esta instancia.
/// </summary>
/// <param name="reloj">Reloj para la vigencia.</param>
public sealed class CacheAcceso(TimeProvider reloj)
{
    /// <summary>Tiempo máximo que se reutilizan los miembros de un usuario; es también el desfase máximo si cambian desde otra instancia o directamente en la base de datos.</summary>
    public static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, Entrada> _usuarios = new();

    /// <summary>Miembros activos de un usuario y cuándo se leyeron.</summary>
    private sealed record Entrada(DateTimeOffset Leida, IReadOnlyList<Miembro> Miembros);

    /// <summary>
    /// Miembros activos vinculados al usuario, uno por hogar (sin seguimiento de cambios: son de solo lectura). Los lee de la base
    /// de datos solo si no hay una lectura vigente.
    /// </summary>
    /// <param name="userId">Usuario autenticado (claim <c>sub</c>).</param>
    /// <param name="db">Contexto con el que leer si hace falta.</param>
    /// <param name="ct">Cancelación de la petición.</param>
    public async Task<IReadOnlyList<Miembro>> MiembrosActivosAsync(Guid userId, MiParteDbContext db, CancellationToken ct)
    {
        if (_usuarios.TryGetValue(userId, out var e) && reloj.GetUtcNow() - e.Leida < Vigencia) return e.Miembros;

        var momento = reloj.GetUtcNow();
        var miembros = await db.Miembros
            .IgnoreQueryFilters() // aquí aún no hay hogar: es justo lo que se resuelve
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.Activo)
            .ToListAsync(ct);
        _usuarios[userId] = new Entrada(momento, miembros);
        return miembros;
    }

    /// <summary>Olvida lo guardado del usuario, p. ej. cuando acaba de crear un hogar o de aceptar una invitación.</summary>
    /// <param name="userId">Usuario cuyos hogares pueden haber cambiado.</param>
    public void InvalidarUsuario(Guid userId) => _usuarios.TryRemove(userId, out _);

    /// <summary>Olvida lo guardado de todos los usuarios con algún miembro en el hogar: cambió un permiso, un rol o un estado.</summary>
    /// <param name="hogarId">Hogar cuyos miembros pueden haber cambiado.</param>
    public void InvalidarHogar(Guid hogarId)
    {
        foreach (var (usuario, entrada) in _usuarios)
            if (entrada.Miembros.Any(m => m.HogarId == hogarId)) _usuarios.TryRemove(usuario, out _);
    }
}
