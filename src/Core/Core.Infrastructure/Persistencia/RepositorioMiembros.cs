using Microsoft.EntityFrameworkCore;
using MiParte.Core.Application;
using MiParte.Core.Application.Miembros;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Auditoria;

namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF Core de <see cref="IRepositorioMiembros"/>. El <see cref="MiParteDbContext"/> filtra por el hogar actual;
/// las operaciones previas a pertenecer a un hogar (aceptar una invitación) usan <c>IgnoreQueryFilters</c> a propósito.
/// </summary>
public sealed class RepositorioMiembros(MiParteDbContext db) : IRepositorioMiembros
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Miembro>> ListarAsync(bool incluirInactivos, CancellationToken ct)
        => await db.Miembros.AsNoTracking().Where(m => incluirInactivos || m.Activo).OrderBy(m => m.Nombre).ToListAsync(ct);

    /// <inheritdoc />
    public Task<Miembro?> MiembroActivoDeUsuarioAsync(Guid userId, CancellationToken ct)
        => db.Miembros.FirstOrDefaultAsync(m => m.UserId == userId && m.Activo, ct);

    /// <inheritdoc />
    public Task<Miembro?> ObtenerAsync(Guid id, CancellationToken ct) => db.Miembros.FirstOrDefaultAsync(m => m.Id == id, ct);

    /// <inheritdoc />
    public async Task AnadirAsync(Miembro miembro, CancellationToken ct)
    {
        db.Miembros.Add(miembro);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public Task<bool> HayOtroAdminActivoAsync(Guid excluirId, CancellationToken ct)
        => db.Miembros.AnyAsync(x => x.Id != excluirId && x.Rol == RolMiembro.Admin && x.Activo && x.UserId != null, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Miembro>> OtrosConCuentaActivosAsync(Guid excluirId, CancellationToken ct)
        => await db.Miembros.Where(x => x.Id != excluirId && x.Activo && x.UserId != null).ToListAsync(ct);

    /// <inheritdoc />
    public Task<bool> TieneACargoActivosAsync(Guid responsableId, CancellationToken ct)
        => db.Miembros.AnyAsync(x => x.ResponsableId == responsableId && x.Activo, ct);

    /// <inheritdoc />
    public Task GuardarAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    /// <inheritdoc />
    public async Task<Resultado<T>> EjecutarExclusivoAsync<T>(Guid hogarId, Func<Task<Resultado<T>>> operacion, CancellationToken ct)
    {
        // El bloqueo consultivo transaccional de Postgres se libera al confirmar o revertir; la segunda petición espera y sus
        // lecturas ya ven el cambio de la primera (READ COMMITTED). InMemory no admite transacciones ni SQL crudo: allí no se aplica.
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (tx is not null)
            await db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtext({hogarId.ToString()}))", ct);

        var resultado = await operacion();
        if (tx is not null && resultado.Error is null) await tx.CommitAsync(ct);
        return resultado;
    }

    /// <inheritdoc />
    public async Task GuardarInvitacionAsync(InvitacionHogar invitacion, CancellationToken ct)
    {
        db.Invitaciones.Add(invitacion);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public Task<InvitacionHogar?> InvitacionPorHashAsync(string hash, CancellationToken ct)
        => db.Invitaciones.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.TokenHash == hash, ct);

    /// <inheritdoc />
    public Task<bool> UsuarioEnHogarAsync(Guid hogarId, Guid userId, CancellationToken ct)
        => db.Miembros.IgnoreQueryFilters().AnyAsync(m => m.HogarId == hogarId && m.UserId == userId, ct);

    /// <inheritdoc />
    public Task<Miembro?> MiembroDeHogarAsync(Guid hogarId, Guid miembroId, CancellationToken ct)
        => db.Miembros.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.HogarId == hogarId && x.Id == miembroId, ct);

    /// <inheritdoc />
    public Task<Hogar> HogarAsync(Guid id, CancellationToken ct) => db.Hogares.IgnoreQueryFilters().FirstAsync(x => x.Id == id, ct);

    /// <inheritdoc />
    public async Task<ResultadoConsumo> ConsumirInvitacionAsync(
        InvitacionHogar inv, Guid userId, Miembro? nuevo, DateTimeOffset ahora, CancellationToken ct)
    {
        if (nuevo is not null) db.Miembros.Add(nuevo);

        if (!db.Database.IsRelational())
        {
            // Proveedor InMemory (tests): no admite ExecuteUpdate ni transacciones, se marca con el entity tracking y una sola
            // SaveChanges. En Postgres la garantía viene del UPDATE condicional.
            if (inv.MiembroId is { } existente)
            {
                var m = await db.Miembros.IgnoreQueryFilters().FirstAsync(x => x.HogarId == inv.HogarId && x.Id == existente, ct);
                m.UserId = userId;
            }
            inv.UsadaEn = ahora;
            inv.UsadaPor = userId;
            await db.SaveChangesAsync(ct);
            return ResultadoConsumo.Consumida;
        }

        // Postgres: el UPDATE condicional (UsadaEn IS NULL y no caducada) es atómico, así que de dos aceptaciones simultáneas del
        // mismo token solo una afecta una fila (equivale al "for update" de aceptar_invitacion en SQL). La otra se revierte entera.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var marcadas = await db.Invitaciones.IgnoreQueryFilters()
            .Where(i => i.Id == inv.Id && i.UsadaEn == null && i.CaducaEn > ahora)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.UsadaEn, ahora)
                .SetProperty(i => i.UsadaPor, userId), ct);
        if (marcadas == 0) return ResultadoConsumo.InvitacionNoDisponible;

        if (inv.MiembroId is Guid destino)
        {
            // Dos invitaciones distintas pueden apuntar al mismo miembro: la lectura previa no bloquea, así que el vínculo se hace
            // condicional y atómico (user_id IS NULL y activo). Si otra aceptación se adelantó afecta 0 filas y se revierte todo.
            var vinculados = await db.Miembros.IgnoreQueryFilters()
                .Where(x => x.HogarId == inv.HogarId && x.Id == destino && x.UserId == null && x.Activo && x.Tipo == TipoMiembro.Adulto)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, userId), ct);
            if (vinculados == 0) return ResultadoConsumo.MiembroNoDisponible;
            db.Auditoria.Add(RegistroAuditoria.Manual(
                inv.HogarId, userId, RegistroAuditoria.Vincular, "miembro", destino, despues: new { userId }));
        }

        // Los UPDATE condicionales no los ve el ChangeTracker: el rastro se añade a mano (el miembro nuevo sí se registra solo).
        db.Auditoria.Add(RegistroAuditoria.Manual(
            inv.HogarId, userId, RegistroAuditoria.Usar, "invitacion", inv.Id, despues: new { usadaPor = userId, usadaEn = ahora }));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ResultadoConsumo.Consumida;
    }
}
