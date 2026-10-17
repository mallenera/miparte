using System.Net;
using System.Net.Http.Json;
using MiParte.Contracts;
using static MiParte.Core.Tests.ApiPostgresTests;

namespace MiParte.Core.Tests;

/// <summary>
/// Liquidación por ciclos contra PostgreSQL real: el cierre marca los gastos con la clave foránea a ciclo_liquidacion, el
/// siguiente ciclo solo recoge lo nuevo y cambiar de modo no toca lo cerrado. Se salta sin MIPARTE_TEST_DB.
/// </summary>
public class LiquidacionCiclosPostgresTests
{
    private static readonly DateOnly Fecha = new(2026, 9, 10);

    [SkippableFact]
    public async Task Ciclos_EnPostgres_CierranYMarcanLosGastosSinRepetirlos()
    {
        Skip.If(string.IsNullOrEmpty(ApiPostgresTests.Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var user = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(user), "Casa ciclos", "Ana");
        var c = e.Cliente(user, hogar.Id);
        var ana = (await Leer<List<MiembroDto>>(await c.GetAsync("/api/miembros"))).Single();
        var perfil = (await Leer<List<PerfilRepartoDto>>(await c.GetAsync("/api/perfiles"))).First(p => p.Modo == "individual");
        var categoria = (await Leer<List<CategoriaDto>>(await c.GetAsync("/api/categorias"))).First();

        // Por meses (el valor por defecto) no hay ciclos que cerrar.
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/api/liquidacion/ciclos", null)).StatusCode);

        await Leer<ModoLiquidacionResponse>(await c.PutAsJsonAsync("/api/hogar/liquidacion", new ModoLiquidacionRequest("ciclos")));
        var yo = await Leer<YoResponse>(await c.GetAsync("/api/yo"));
        Assert.Equal(ModosLiquidacion.Ciclos, yo.HogarActual!.LiquidacionModo);

        // Sin gastos pendientes el ciclo no se cierra.
        var vacio = await c.PostAsync("/api/liquidacion/ciclos", null);
        Assert.Equal(HttpStatusCode.Conflict, vacio.StatusCode);

        await Leer<GastoResponse>(await c.PostAsJsonAsync("/api/gastos", new GastoRequest(Fecha, 100m, categoria.Id, ana.Id, perfil.Id, "A")), HttpStatusCode.Created);
        var primero = await Leer<CicloLiquidacionDto>(await c.PostAsync("/api/liquidacion/ciclos", null), HttpStatusCode.Created);
        Assert.Equal(1, primero.Numero);
        Assert.Equal(100m, primero.Total);
        Assert.Equal(Fecha, primero.DesdeGastoFecha);

        // El gasto sigue consultable; el saldo pendiente ya no lo tiene en cuenta.
        var gastos = await Leer<List<GastoResponse>>(await c.GetAsync("/api/gastos?mes=2026-09"));
        Assert.Single(gastos);
        var pendiente = await Leer<LiquidacionResponse>(await c.GetAsync("/api/liquidacion?mes=2026-09"));
        Assert.Empty(pendiente.Transferencias);

        // Un gasto nuevo entra en el siguiente ciclo y el primero no cambia.
        await Leer<GastoResponse>(await c.PostAsJsonAsync("/api/gastos", new GastoRequest(Fecha.AddDays(2), 40m, categoria.Id, ana.Id, perfil.Id, "B")), HttpStatusCode.Created);
        var segundo = await Leer<CicloLiquidacionDto>(await c.PostAsync("/api/liquidacion/ciclos", null), HttpStatusCode.Created);
        Assert.Equal(2, segundo.Numero);
        Assert.Equal(40m, segundo.Total);

        var lista = await Leer<List<CicloLiquidacionDto>>(await c.GetAsync("/api/liquidacion/ciclos"));
        Assert.Equal(new[] { 2, 1 }, lista.Select(x => x.Numero));
        Assert.Equal(100m, lista[1].Total);

        // Con ciclos cerrados no se vuelve a mensual: sus gastos quedarían fuera del cálculo mensual. Los ciclos no cambian.
        var cambio = await c.PutAsJsonAsync("/api/hogar/liquidacion", new ModoLiquidacionRequest("mensual"));
        Assert.Equal(HttpStatusCode.Conflict, cambio.StatusCode);
        Assert.Equal(2, (await Leer<List<CicloLiquidacionDto>>(await c.GetAsync("/api/liquidacion/ciclos"))).Count);
    }
}
