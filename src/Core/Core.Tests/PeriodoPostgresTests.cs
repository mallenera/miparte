using System.Net;
using System.Net.Http.Json;
using MiParte.Contracts;
using Npgsql;
using static MiParte.Core.Tests.ApiPostgresTests;

namespace MiParte.Core.Tests;

/// <summary>
/// Cierre de un viaje contra PostgreSQL real: los gastos pendientes se liquidan en un ciclo final y, cerrado, el rango de gastos
/// queda bloqueado hasta reabrir. Se salta sin MIPARTE_TEST_DB.
/// </summary>
public class PeriodoPostgresTests
{
    // Un mes pasado: el periodo abierto empieza ahí y el cierre lo termina hoy, así que los gastos caen dentro.
    private static readonly DateOnly Inicio = new(2020, 3, 1);
    private static readonly DateOnly FechaGasto = new(2020, 3, 10);

    [SkippableFact]
    public async Task CerrarViaje_EnPostgres_LiquidaLosPendientesYBloqueaLosGastos()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var user = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(user), "Viaje a Lisboa", "Ana");
        var c = e.Cliente(user, hogar.Id);
        var ana = (await Leer<List<MiembroDto>>(await c.GetAsync("/api/miembros"))).Single();
        var perfil = (await Leer<List<PerfilRepartoDto>>(await c.GetAsync("/api/perfiles"))).First(p => p.Modo == "individual");
        var categoria = (await Leer<List<CategoriaDto>>(await c.GetAsync("/api/categorias"))).First();
        await FijarPeriodoAbierto(hogar.Id, Inicio);

        var gasto = await Leer<GastoResponse>(
            await c.PostAsJsonAsync("/api/gastos", new GastoRequest(FechaGasto, 100m, categoria.Id, ana.Id, perfil.Id, "Hotel")), HttpStatusCode.Created);

        var cierre = await Leer<CierrePeriodoResponse>(await c.PostAsync("/api/hogar/cerrar-periodo", null));
        Assert.StartsWith("2020-03-01/", cierre.Periodo);
        Assert.NotEqual("2020-03-01/", cierre.Periodo);
        Assert.Equal(100m, cierre.Ciclo!.Total);
        Assert.Equal(1, cierre.Ciclo.Numero);

        var editar = await c.PutAsJsonAsync($"/api/gastos/{gasto.Id}", new GastoRequest(FechaGasto, 120m, categoria.Id, ana.Id, perfil.Id, "Hotel"));
        Assert.Equal(HttpStatusCode.Conflict, editar.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/gastos/{gasto.Id}")).StatusCode);

        // Cerrado otra vez: conflicto. Reabrir lo devuelve a editable y, sin cierre, no hay nada que reabrir.
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/api/hogar/cerrar-periodo", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/hogar/reabrir-periodo", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/hogar/reabrir-periodo", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/gastos/{gasto.Id}")).StatusCode);
    }

    /// <summary>Fija el periodo propio del hogar como abierto desde <paramref name="desde"/>; la API aún no lo expone.</summary>
    private static async Task FijarPeriodoAbierto(Guid hogar, DateOnly desde)
    {
        await using var conn = new NpgsqlConnection(Cadena);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("update public.hogar set periodo_desde = @d, periodo_hasta = null where id = @h", conn);
        cmd.Parameters.AddWithValue("d", desde);
        cmd.Parameters.AddWithValue("h", hogar);
        await cmd.ExecuteNonQueryAsync();
    }
}
