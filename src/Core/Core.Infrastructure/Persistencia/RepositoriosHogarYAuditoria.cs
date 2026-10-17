using Microsoft.EntityFrameworkCore;
using MiParte.Core.Application.Auditoria;
using MiParte.Core.Application.Hogares;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Auditoria;

namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>Implementación EF Core de <see cref="IRepositorioHogares"/>.</summary>
public sealed class RepositorioHogares(MiParteDbContext db) : IRepositorioHogares
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Hogar>> DelUsuarioAsync(Guid userId, CancellationToken ct)
        // Se proyecta a columnas y se mapea después: un Distinct sobre un record no se traduce a SQL en Npgsql. No hace falta
        // Distinct: miembro tiene unique (hogar_id, user_id).
        => (await db.Hogares.IgnoreQueryFilters()
                .Where(h => db.Miembros.IgnoreQueryFilters().Any(m => m.HogarId == h.Id && m.UserId == userId && m.Activo))
                .OrderBy(h => h.Nombre)
                .Select(h => new { h.Id, h.Nombre, h.CuentaComunActiva, h.AhorroActivo, h.Tipo, h.RecurrentesActivas, h.LiquidacionModo })
                .ToListAsync(ct))
            .Select(h => new Hogar
            {
                Id = h.Id, Nombre = h.Nombre, CuentaComunActiva = h.CuentaComunActiva, AhorroActivo = h.AhorroActivo, Tipo = h.Tipo,
                RecurrentesActivas = h.RecurrentesActivas, LiquidacionModo = h.LiquidacionModo,
            })
            .ToList();

    /// <inheritdoc />
    public Task<int> ContarPertenenciasAsync(Guid userId, CancellationToken ct)
        => db.Miembros.IgnoreQueryFilters().CountAsync(m => m.UserId == userId, ct);

    /// <inheritdoc />
    public async Task CrearConSemillaAsync(Hogar hogar, Miembro creador, Guid userId, CancellationToken ct)
    {
        // El modelo EF solo conoce dos relaciones (las FK compuestas las aplica el SQL), así que no ordena los INSERT por padres e
        // hijos: se guarda por etapas (hogar → miembro → perfiles → detalle y categorías) dentro de una transacción, y si algo falla
        // se deshace todo. InMemory no admite transacciones.
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

        // La semilla son decenas de filas que no aportan: se resume en un solo evento.
        db.AuditoriaActiva = false;
        db.Hogares.Add(hogar);
        await db.SaveChangesAsync(ct);
        db.Miembros.Add(creador);
        await db.SaveChangesAsync(ct);
        await SembrarAsync(hogar.Id, hogar.Tipo, creador.Id, ct);
        db.AuditoriaActiva = true;
        db.Auditoria.Add(RegistroAuditoria.Manual(
            hogar.Id, userId, RegistroAuditoria.Crear, "hogar", hogar.Id, despues: new { nombre = hogar.Nombre }));
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }

    /// <summary>Crea los 4 perfiles, los detalles del creador y las categorías de ejemplo del tipo, guardando por etapas.</summary>
    private async Task SembrarAsync(Guid hogarId, TipoGrupo tipo, Guid creadorId, CancellationToken ct)
    {
        PerfilReparto Perfil(string nombre, ModoReparto modo)
        {
            var p = new PerfilReparto { Id = Guid.NewGuid(), HogarId = hogarId, Nombre = nombre, Modo = modo };
            db.PerfilesReparto.Add(p);
            return p;
        }

        var porNombre = new Dictionary<string, PerfilReparto>
        {
            [SemillaHogar.PerfilCuentaComun] = Perfil(SemillaHogar.PerfilCuentaComun, ModoReparto.CuentaComun),
            [SemillaHogar.PerfilPartes] = Perfil(SemillaHogar.PerfilPartes, ModoReparto.Partes),
            [SemillaHogar.PerfilPorcentaje] = Perfil(SemillaHogar.PerfilPorcentaje, ModoReparto.Porcentaje),
            [SemillaHogar.PerfilIndividual] = Perfil(SemillaHogar.PerfilIndividual, ModoReparto.Individual),
        };
        await db.SaveChangesAsync(ct); // perfiles antes que sus detalles y categorías

        db.PerfilesRepartoDetalle.Add(new PerfilRepartoDetalle
            { Id = Guid.NewGuid(), HogarId = hogarId, PerfilId = porNombre[SemillaHogar.PerfilPartes].Id, MiembroId = creadorId, Valor = 1 });
        db.PerfilesRepartoDetalle.Add(new PerfilRepartoDetalle
            { Id = Guid.NewGuid(), HogarId = hogarId, PerfilId = porNombre[SemillaHogar.PerfilPorcentaje].Id, MiembroId = creadorId, Valor = 100 });
        foreach (var (nombre, perfil) in SemillaHogar.CategoriasDe(tipo))
            db.Categorias.Add(new Categoria
                { Id = Guid.NewGuid(), HogarId = hogarId, Nombre = nombre, PerfilRepartoId = porNombre[perfil].Id });
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public Task<Hogar?> ActualAsync(CancellationToken ct) => db.Hogares.FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task EliminarAsync(Hogar hogar, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            // Borrado directo: la base de datos elimina en cascada el resto de filas (y la auditoría del hogar, que solo admite ese borrado).
            await db.Hogares.IgnoreQueryFilters().Where(x => x.Id == hogar.Id).ExecuteDeleteAsync(ct);
        }
        else
        {
            // Proveedor InMemory (tests): no admite ExecuteDelete ni borrado en cascada; se quitan el hogar y sus miembros.
            db.Miembros.RemoveRange(await db.Miembros.ToListAsync(ct));
            db.Hogares.Remove(hogar);
            await db.SaveChangesAsync(ct);
        }
    }
}

/// <summary>Implementación EF Core de <see cref="IRepositorioAuditoria"/>.</summary>
public sealed class RepositorioAuditoria(MiParteDbContext db) : IRepositorioAuditoria
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<EventoAuditoria>> ListarAsync(ConsultaAuditoria c, int limite, CancellationToken ct)
    {
        var q = db.Auditoria.AsQueryable();
        if (!string.IsNullOrWhiteSpace(c.Entidad)) q = q.Where(e => e.Entidad == c.Entidad);
        if (c.EntidadId is { } id) q = q.Where(e => e.EntidadId == id);
        if (c.Hasta is { } corte)
        {
            // Mismo orden que el OrderBy (cuando desc, id asc): lo siguiente es más antiguo, o igual de reciente con id mayor.
            q = c.DespuesDeId is { } cursorId
                ? q.Where(e => e.Cuando < corte || (e.Cuando == corte && e.Id.CompareTo(cursorId) > 0))
                : q.Where(e => e.Cuando < corte);
        }

        return await q.OrderByDescending(e => e.Cuando).ThenBy(e => e.Id).Take(limite).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> AutoresAsync(CancellationToken ct)
        => await db.Miembros.Where(m => m.UserId != null).ToDictionaryAsync(m => m.UserId!.Value, m => m.Nombre, ct);
}
