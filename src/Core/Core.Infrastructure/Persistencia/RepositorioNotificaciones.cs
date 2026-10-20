using Microsoft.EntityFrameworkCore;
using MiParte.Core.Application.Notificaciones;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF Core de <see cref="IRepositorioNotificaciones"/>. Solo <see cref="EliminarAsync"/> aplica el filtro del hogar
/// actual. Los demás usan <c>IgnoreQueryFilters</c> a propósito: el envío en segundo plano no tiene hogar actual, pero siempre
/// filtra por el hogar del aviso, y los endpoints son únicos en todo el sistema.
/// </summary>
public sealed class RepositorioNotificaciones(MiParteDbContext db) : IRepositorioNotificaciones
{
    /// <inheritdoc />
    public async Task ReemplazarAsync(string endpoint, SuscripcionPush nueva, CancellationToken ct)
    {
        var previas = await db.SuscripcionesPush.IgnoreQueryFilters().Where(s => s.Endpoint == endpoint).ToListAsync(ct);
        db.SuscripcionesPush.RemoveRange(previas);
        db.SuscripcionesPush.Add(nueva);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> EliminarAsync(Guid miembroId, string endpoint, CancellationToken ct)
    {
        var fila = await db.SuscripcionesPush.FirstOrDefaultAsync(s => s.MiembroId == miembroId && s.Endpoint == endpoint, ct);
        if (fila is null) return false;
        db.SuscripcionesPush.Remove(fila);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SuscripcionPush>> DestinatariosAsync(Guid hogarId, Guid? excluirMiembroId, CancellationToken ct)
    {
        var consulta = db.SuscripcionesPush.IgnoreQueryFilters().AsNoTracking().Where(s => s.HogarId == hogarId);
        if (excluirMiembroId is { } excluir) consulta = consulta.Where(s => s.MiembroId != excluir);
        return await consulta.ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task EliminarCaducadaAsync(string endpoint, CancellationToken ct)
    {
        var caducadas = await db.SuscripcionesPush.IgnoreQueryFilters().Where(s => s.Endpoint == endpoint).ToListAsync(ct);
        if (caducadas.Count == 0) return;
        db.SuscripcionesPush.RemoveRange(caducadas);
        await db.SaveChangesAsync(ct);
    }
}
