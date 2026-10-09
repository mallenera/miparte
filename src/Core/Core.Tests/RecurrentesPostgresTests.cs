using System.Net;
using System.Net.Http.Json;
using Npgsql;
using MiParte.Contracts;
using static MiParte.Core.Tests.ApiPostgresTests;

namespace MiParte.Core.Tests;

/// <summary>
/// Pruebas contra PostgreSQL real de la migración 20261006: unicidad de gastos recurrentes por mes
/// (generación concurrente) y borrado de usuarios aceptantes de invitaciones. Se saltan sin MIPARTE_TEST_DB.
/// </summary>
public class RecurrentesPostgresTests
{
    [SkippableFact]
    public async Task GenerarRecurrentes_DosPeticionesSimultaneas_NoDuplicanNiDan500_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(ApiPostgresTests.Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var user = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(user), "Casa recurrentes", "Ana");
        var c = e.Cliente(user, hogar.Id);

        var ana = (await Leer<List<MiembroDto>>(await c.GetAsync("/api/miembros"))).Single();
        var perfil = (await Leer<List<PerfilRepartoDto>>(await c.GetAsync("/api/perfiles"))).First(p => p.Modo != "cuenta_comun");
        var categoria = (await Leer<List<CategoriaDto>>(await c.GetAsync("/api/categorias"))).First();

        var plantilla = await Leer<GastoRecurrenteResponse>(await c.PostAsJsonAsync("/api/gastos-recurrentes",
            new GastoRecurrenteRequest(50m, categoria.Id, ana.Id, perfil.Id, 5, "Internet")), HttpStatusCode.Created);

        // Varias rondas para aumentar la probabilidad de que las peticiones se solapen de verdad.
        for (var mes = 1; mes <= 3; mes++)
        {
            var ruta = $"/api/gastos-recurrentes/generar?mes=2027-{mes:00}";
            var respuestas = await Task.WhenAll(c.PostAsync(ruta, null), c.PostAsync(ruta, null));

            Assert.All(respuestas, r => Assert.NotEqual(HttpStatusCode.InternalServerError, r.StatusCode));
            var resultados = new List<GenerarRecurrentesResponse>();
            foreach (var r in respuestas) resultados.Add(await Leer<GenerarRecurrentesResponse>(r));
            // Cada plantilla se crea una sola vez entre las dos respuestas; la otra la ve como ya existente.
            Assert.Equal(1, resultados.Sum(x => x.Creados));
            Assert.All(resultados, x => Assert.Equal(1, x.Creados + x.YaExistentes));

            var gastos = await Leer<List<GastoResponse>>(await c.GetAsync($"/api/gastos?mes=2027-{mes:00}"));
            Assert.Equal(1, gastos.Count(g => g.GastoRecurrenteId == plantilla.Id));
        }
    }

    [SkippableFact]
    public async Task BorrarUsuarioAceptante_NoFalla_YLaInvitacionQuedaUsada_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(ApiPostgresTests.Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var admin = await e.CrearUsuarioAsync();
        var invitado = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(admin), "Casa borrado", "Ana");

        var invitacion = await Leer<InvitacionCreada>(
            await e.Cliente(admin, hogar.Id).PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest(null)),
            HttpStatusCode.Created);
        await Leer<HogarResumen>(await e.Cliente(invitado).PostAsJsonAsync(
            "/api/invitaciones/aceptar", new AceptarInvitacionRequest(invitacion.Token, "Beto")));

        await using var conn = new NpgsqlConnection(ApiPostgresTests.Cadena);
        await conn.OpenAsync();

        // Antes fallaba por el check (usada_en is null) = (usada_por is null).
        await using (var del = new NpgsqlCommand("delete from auth.users where id = @u", conn))
        {
            del.Parameters.AddWithValue("u", invitado);
            Assert.Equal(1, await del.ExecuteNonQueryAsync());
        }

        await using var sel = new NpgsqlCommand(
            "select usada_en is not null, usada_por is null from public.invitacion_hogar where id = @i", conn);
        sel.Parameters.AddWithValue("i", invitacion.Id);
        await using var rd = await sel.ExecuteReaderAsync();
        Assert.True(await rd.ReadAsync());
        Assert.True(rd.GetBoolean(0), "usada_en debe seguir informado");
        Assert.True(rd.GetBoolean(1), "usada_por debe quedar nulo");
    }
}
