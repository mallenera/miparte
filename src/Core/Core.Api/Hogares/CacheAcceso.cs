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

    // Se incrementa en cada invalidación. Una lectura que empezó antes de una invalidación puede haber visto los datos
    // anteriores al cambio: su resultado se usa para esa petición pero no se publica en la caché.
    private long _generacion;

    // Instante de la última purga de entradas caducadas (ticks de reloj): la purga es oportunista y como mucho una por vigencia.
    private long _ultimaPurga;

    /// <summary>Miembros activos de un usuario y cuándo se leyeron.</summary>
    private sealed record Entrada(DateTimeOffset Leida, IReadOnlyList<Miembro> Miembros);

    /// <summary>
    /// Miembros activos vinculados al usuario, uno por hogar (sin seguimiento de cambios: son de solo lectura). Los lee de la base
    /// de datos solo si no hay una lectura vigente.
    /// </summary>
    /// <param name="userId">Usuario autenticado (claim <c>sub</c>).</param>
    /// <param name="db">Contexto con el que leer si hace falta.</param>
    /// <param name="ct">Cancelación de la petición.</param>
    public Task<IReadOnlyList<Miembro>> MiembrosActivosAsync(Guid userId, MiParteDbContext db, CancellationToken ct)
        => MiembrosActivosAsync(userId, async () => await db.Miembros
            .IgnoreQueryFilters() // aquí aún no hay hogar: es justo lo que se resuelve
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.Activo)
            .ToListAsync(ct));

    /// <summary>Número de usuarios con una lectura guardada (caducada o no, hasta que la purga la quite); para diagnóstico y pruebas.</summary>
    public int Entradas => _usuarios.Count;

    /// <summary>
    /// Igual que la sobrecarga con contexto, pero con la lectura de la base de datos como función: separa la política de la caché
    /// (vigencia, purga e invalidación) del acceso a datos.
    /// </summary>
    /// <param name="userId">Usuario autenticado (claim <c>sub</c>).</param>
    /// <param name="leer">Lee de la base de datos los miembros activos del usuario; solo se llama si no hay una lectura vigente.</param>
    public async Task<IReadOnlyList<Miembro>> MiembrosActivosAsync(Guid userId, Func<Task<List<Miembro>>> leer)
    {
        if (_usuarios.TryGetValue(userId, out var e) && reloj.GetUtcNow() - e.Leida < Vigencia) return e.Miembros;

        var momento = reloj.GetUtcNow();
        var generacion = Interlocked.Read(ref _generacion);
        var miembros = await leer();

        // Si mientras se leía hubo una invalidación, el resultado pudo ser anterior al cambio (un permiso ya retirado): no se guarda.
        if (Interlocked.Read(ref _generacion) == generacion) _usuarios[userId] = new Entrada(momento, miembros);
        PurgarCaducadas(momento);
        return miembros;
    }

    /// <summary>
    /// Quita las entradas caducadas para que la memoria no crezca con todos los usuarios atendidos desde el arranque (la vigencia
    /// solo impedía reutilizarlas). Se hace como mucho una vez por <see cref="Vigencia"/>, al guardar una lectura.
    /// </summary>
    private void PurgarCaducadas(DateTimeOffset ahora)
    {
        var anterior = Interlocked.Read(ref _ultimaPurga);
        if (ahora.UtcTicks - anterior < Vigencia.Ticks || Interlocked.CompareExchange(ref _ultimaPurga, ahora.UtcTicks, anterior) != anterior) return;
        foreach (var (usuario, entrada) in _usuarios)
            if (ahora - entrada.Leida >= Vigencia) _usuarios.TryRemove(usuario, out _);
    }

    /// <summary>Olvida lo guardado del usuario, p. ej. cuando acaba de crear un hogar o de aceptar una invitación.</summary>
    /// <param name="userId">Usuario cuyos hogares pueden haber cambiado.</param>
    public void InvalidarUsuario(Guid userId)
    {
        Interlocked.Increment(ref _generacion);
        _usuarios.TryRemove(userId, out _);
    }

    /// <summary>Olvida lo guardado de todos los usuarios con algún miembro en el hogar: cambió un permiso, un rol o un estado.</summary>
    /// <param name="hogarId">Hogar cuyos miembros pueden haber cambiado.</param>
    public void InvalidarHogar(Guid hogarId)
    {
        Interlocked.Increment(ref _generacion);
        foreach (var (usuario, entrada) in _usuarios)
            if (entrada.Miembros.Any(m => m.HogarId == hogarId)) _usuarios.TryRemove(usuario, out _);
    }
}
