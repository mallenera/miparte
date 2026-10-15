using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Cierres;

/// <summary>Un mes cerrado con el nombre de quien lo cerró (null si ya no consta).</summary>
public sealed record CierreConAutor(MesCerrado Cierre, string? Autor);

/// <summary>Casos de uso del cierre de mes: congela los gastos del mes y solo quien tenga permiso puede reabrirlo.</summary>
public sealed class ServicioCierres(IRepositorioCierres repositorio, TimeProvider reloj)
{
    /// <summary>Meses cerrados del hogar, del más reciente al más antiguo, con el nombre de quien los cerró.</summary>
    public async Task<IReadOnlyList<CierreConAutor>> ListarAsync(CancellationToken ct)
    {
        var cierres = await repositorio.ListarAsync(ct);
        var autores = await repositorio.NombresPorUsuarioAsync(ct);
        return cierres.Select(c => new CierreConAutor(c, c.CerradoPor is { } u ? autores.GetValueOrDefault(u) : null)).ToList();
    }

    /// <summary>
    /// Cierra el mes que empieza en <paramref name="inicio"/> en nombre del miembro <paramref name="yo"/>. Inválido si el mes
    /// aún no ha terminado (se seguirían añadiendo gastos); conflicto si ya está cerrado.
    /// </summary>
    public async Task<Resultado<CierreConAutor>> CerrarAsync(Guid hogarId, DateOnly inicio, Miembro yo, CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
        if (inicio.AddMonths(1) > hoy) return Resultado<CierreConAutor>.Invalido("Solo se puede cerrar un mes que ya ha terminado.");
        if (await repositorio.EstaCerradoAsync(inicio, ct)) return Resultado<CierreConAutor>.Conflicto("Ese mes ya está cerrado.");

        var cierre = new MesCerrado
        {
            Id = Guid.NewGuid(), HogarId = hogarId, Mes = inicio, CerradoEn = reloj.GetUtcNow(), CerradoPor = yo.UserId,
        };
        // Dos cierres simultáneos del mismo mes: gana el primero y el índice único rechaza al segundo.
        return await repositorio.GuardarAsync(cierre, ct)
            ? Resultado<CierreConAutor>.Ok(new CierreConAutor(cierre, yo.Nombre))
            : Resultado<CierreConAutor>.Conflicto("Ese mes ya está cerrado.");
    }

    /// <summary>Reabre un mes cerrado; no encontrado si no lo estaba.</summary>
    public async Task<Resultado<bool>> ReabrirAsync(DateOnly inicio, CancellationToken ct)
        => await repositorio.EliminarAsync(inicio, ct) ? Resultado<bool>.Ok(true) : Resultado<bool>.NoEncontrado();
}
