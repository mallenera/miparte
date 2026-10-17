using Microsoft.EntityFrameworkCore;
using MiParte.Core.Application.Periodos;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF Core de <see cref="IRepositorioPeriodos"/>. El <see cref="MiParteDbContext"/> ya filtra por el hogar actual.
/// </summary>
public sealed class RepositorioPeriodos(MiParteDbContext db) : IRepositorioPeriodos
{
    /// <inheritdoc />
    public Task<Hogar?> HogarAsync(CancellationToken ct) => db.Hogares.FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task GuardarAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
