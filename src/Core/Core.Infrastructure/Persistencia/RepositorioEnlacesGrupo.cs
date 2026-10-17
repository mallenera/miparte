using Microsoft.EntityFrameworkCore;
using MiParte.Core.Application.Miembros;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Auditoria;

namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF Core de <see cref="IRepositorioEnlacesGrupo"/>. El <see cref="MiParteDbContext"/> filtra por el hogar actual;
/// <see cref="PorHashAsync"/> usa <c>IgnoreQueryFilters</c> a propósito, porque quien se une aún no pertenece al hogar.
/// </summary>
public sealed class RepositorioEnlacesGrupo(MiParteDbContext db) : IRepositorioEnlacesGrupo
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<EnlaceGrupo>> ListarAsync(CancellationToken ct)
        => await db.EnlacesGrupo.AsNoTracking().OrderByDescending(e => e.CreadoEn).ThenBy(e => e.Id).ToListAsync(ct);

    /// <inheritdoc />
    public Task<EnlaceGrupo?> ObtenerAsync(Guid id, CancellationToken ct) => db.EnlacesGrupo.FirstOrDefaultAsync(e => e.Id == id, ct);

    /// <inheritdoc />
    public async Task AnadirAsync(EnlaceGrupo enlace, CancellationToken ct)
    {
        db.EnlacesGrupo.Add(enlace);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public Task GuardarAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    /// <inheritdoc />
    public Task<EnlaceGrupo?> PorHashAsync(string hash, CancellationToken ct)
        => db.EnlacesGrupo.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(e => e.TokenHash == hash, ct);

    /// <inheritdoc />
    public async Task<ResultadoUsoEnlace> UsarAsync(EnlaceGrupo enlace, Guid userId, Miembro nuevo, DateTimeOffset ahora, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            // Proveedor InMemory (tests): sin transacciones ni ExecuteUpdate. Una sola SaveChanges; la garantía atómica viene de Postgres.
            var tracked = await db.EnlacesGrupo.IgnoreQueryFilters().FirstAsync(e => e.Id == enlace.Id, ct);
            if (!tracked.Activo || (tracked.CaducaEn is { } c && c <= ahora)) return ResultadoUsoEnlace.EnlaceNoDisponible;
            db.Miembros.Add(nuevo);
            tracked.Usos++;
            await db.SaveChangesAsync(ct);
            return ResultadoUsoEnlace.Usado;
        }

        // Postgres: el UPDATE condicional (activo, no caducado) cuenta el uso de forma atómica. Dos uniones simultáneas
        // se serializan en la fila del enlace; la que pierde afecta 0 filas y se revierte entera al salir sin confirmar.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var contados = await db.EnlacesGrupo.IgnoreQueryFilters()
            .Where(e => e.Id == enlace.Id && e.Activo && (e.CaducaEn == null || e.CaducaEn > ahora))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Usos, e => e.Usos + 1), ct);
        if (contados == 0) return ResultadoUsoEnlace.EnlaceNoDisponible;

        // El UPDATE condicional no lo ve el ChangeTracker: el rastro se añade a mano (el miembro nuevo sí se registra solo).
        db.Auditoria.Add(RegistroAuditoria.Manual(
            enlace.HogarId, userId, RegistroAuditoria.Usar, "enlace_grupo", enlace.Id, despues: new { usadoPor = userId, usadoEn = ahora }));

        db.Miembros.Add(nuevo);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Otra unión del mismo usuario se confirmó antes (miembro_hogar_id_user_id_key). Sin COMMIT la transacción se
            // deshace entera: el uso contado arriba tampoco queda y la auditoría no se escribe.
            return ResultadoUsoEnlace.UsuarioYaEnHogar;
        }
        await tx.CommitAsync(ct);
        return ResultadoUsoEnlace.Usado;
    }
}
