using MiParte.Core.Application.Liquidacion;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Periodos;

/// <summary>Resultado de cerrar un periodo: el periodo ya congelado y el ciclo final, si había gastos por liquidar.</summary>
/// <param name="Periodo">Periodo tras el cierre (rango con fecha de fin).</param>
/// <param name="CerradoEn">Momento del cierre.</param>
/// <param name="Ciclo">Liquidación final del periodo; null si no quedaban gastos pendientes.</param>
public sealed record CierrePeriodoResultado(Periodo Periodo, DateTimeOffset CerradoEn, CicloConAutor? Ciclo);

/// <summary>
/// Casos de uso del periodo propio de un viaje o evento: cerrarlo (congela sus gastos y deja la liquidación final) y reabrirlo.
/// </summary>
public sealed class ServicioPeriodos(IRepositorioPeriodos repositorio, IRepositorioLiquidacion liquidacion, TimeProvider reloj)
{
    /// <summary>
    /// Cierra el periodo propio del hogar en nombre de <paramref name="yo"/>. Un periodo abierto termina hoy; uno de rango conserva
    /// su fin. Desde el cierre, los gastos del rango no se pueden editar ni borrar. Genera la liquidación final con los gastos
    /// pendientes, como un ciclo, en cualquier modo de liquidación. Inválido si el hogar no tiene periodo propio o no ha empezado;
    /// conflicto si ya estaba cerrado o si otro cierre de liquidación se completa a la vez.
    /// </summary>
    public async Task<Resultado<CierrePeriodoResultado>> CerrarAsync(Miembro yo, CancellationToken ct)
    {
        // Comprobación previa, sin bloquear: los errores de estado no necesitan el cierre de liquidación. Se repite bajo el bloqueo.
        if (ErrorDeCierre(await repositorio.HogarAsync(ct), reloj.GetUtcNow()) is { } previo) return previo;
        try
        {
            return await liquidacion.EnExclusionAsync(() => CerrarBajoExclusionAsync(yo, ct), ct);
        }
        catch (CierreConcurrenteException)
        {
            return Resultado<CierrePeriodoResultado>.Conflicto(Mensajes.CierreConcurrente);
        }
    }

    /// <summary>Reabre un periodo cerrado; no encontrado si no estaba cerrado. Los gastos del rango vuelven a poder cambiarse.</summary>
    public async Task<Resultado<bool>> ReabrirAsync(CancellationToken ct)
    {
        var hogar = await repositorio.HogarAsync(ct);
        if (hogar is null || hogar.PeriodoCerradoEn is null) return Resultado<bool>.NoEncontrado(Mensajes.PeriodoNoCerrado);

        hogar.PeriodoCerradoEn = null;
        await repositorio.GuardarAsync(ct);
        return Resultado<bool>.Ok(true);
    }

    /// <summary>Error que impide cerrar el periodo del hogar, o null si se puede cerrar.</summary>
    private static Resultado<CierrePeriodoResultado>? ErrorDeCierre(Hogar? hogar, DateTimeOffset ahora)
    {
        if (hogar is null) return Resultado<CierrePeriodoResultado>.NoEncontrado();
        if (hogar.PeriodoCerradoEn is not null) return Resultado<CierrePeriodoResultado>.Conflicto(Mensajes.PeriodoYaCerrado);
        return CierrePeriodo.Cerrar(hogar.PeriodoPropio, DateOnly.FromDateTime(ahora.UtcDateTime), out var error) is null
            ? Resultado<CierrePeriodoResultado>.Invalido(error!)
            : null;
    }

    private async Task<Resultado<CierrePeriodoResultado>> CerrarBajoExclusionAsync(Miembro yo, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var hogar = await repositorio.HogarAsync(ct);
        // Otro cierre pudo completarse entre la comprobación previa y el bloqueo: se vuelve a mirar con el hogar bloqueado.
        if (ErrorDeCierre(hogar, ahora) is { } error) return error;

        var periodo = CierrePeriodo.Cerrar(hogar!.PeriodoPropio, DateOnly.FromDateTime(ahora.UtcDateTime), out _)!;

        hogar.PeriodoPropio = periodo;
        hogar.PeriodoCerradoEn = ahora;

        // Los gastos pendientes se liquidan en un ciclo; CerrarCicloAsync guarda también el hogar, dentro de la misma transacción.
        var pendientes = CalculoCiclo.Pendientes(await liquidacion.GastosPendientesAsync(ct));
        if (pendientes.Count == 0)
        {
            await repositorio.GuardarAsync(ct);
            return Resultado<CierrePeriodoResultado>.Ok(new CierrePeriodoResultado(periodo, ahora, null));
        }

        var (total, desde, hasta) = CalculoCiclo.Resumir(pendientes);
        var ciclo = new CicloLiquidacion
        {
            Id = Guid.NewGuid(), HogarId = yo.HogarId, Numero = await liquidacion.UltimoNumeroCicloAsync(ct) + 1,
            CerradoEn = ahora, CerradoPor = yo.Id, DesdeGastoFecha = desde, HastaFecha = hasta, Total = total,
        };
        await liquidacion.CerrarCicloAsync(ciclo, pendientes.Select(g => g.Id).ToList(), ct);
        return Resultado<CierrePeriodoResultado>.Ok(new CierrePeriodoResultado(periodo, ahora, new CicloConAutor(ciclo, yo.Nombre)));
    }
}
