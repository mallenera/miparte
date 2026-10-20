using System.Net;
using System.Net.Http.Json;
using Npgsql;
using MiParte.Contracts;
using static MiParte.Core.Tests.ApiPostgresTests;

namespace MiParte.Core.Tests;

/// <summary>
/// Gastos personales contra PostgreSQL real: solo los ve y los cambia quien paga, ni siquiera un admin, y la auditoría no los
/// muestra a otros. Se saltan sin MIPARTE_TEST_DB.
/// </summary>
public class PrivacidadPersonalesPostgresTests
{
    [SkippableFact]
    public async Task GastoPersonal_SoloLoVeQuienPaga_NiUnAdmin_EnLaApiYEnLaAuditoria()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var ana = await e.CrearUsuarioAsync();
        var marcos = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(ana), "Casa privada", "Ana");
        // Marcos es admin: el test demuestra que el rol no le da acceso a los personales de Ana.
        await InsertarMiembroAsync(hogar.Id, marcos, "Marcos", "admin");

        var cAna = e.Cliente(ana, hogar.Id);
        var cMarcos = e.Cliente(marcos, hogar.Id);
        var miembros = await Leer<List<MiembroDto>>(await cAna.GetAsync("/api/miembros"));
        var anaMiembro = miembros.Single(m => m.Nombre == "Ana").Id;
        var categoria = (await Leer<List<CategoriaDto>>(await cAna.GetAsync("/api/categorias"))).First().Id;
        var perfil = (await Leer<List<PerfilRepartoDto>>(await cAna.GetAsync("/api/perfiles"))).First(p => p.Modo != "cuenta_comun").Id;
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var mes = hoy.ToString("yyyy-MM");

        var gasto = await Leer<GastoResponse>(await cAna.PostAsJsonAsync("/api/gastos",
            new GastoRequest(hoy, 40m, categoria, anaMiembro, perfil, "Gimnasio", Personal: true)), HttpStatusCode.Created);

        // Ana lo ve; Marcos (admin) no aparece en su listado ni puede abrirlo.
        var listaAna = await Leer<List<GastoResponse>>(await cAna.GetAsync($"/api/gastos?mes={mes}"));
        Assert.Contains(listaAna, g => g.Id == gasto.Id);
        var listaMarcos = await Leer<List<GastoResponse>>(await cMarcos.GetAsync($"/api/gastos?mes={mes}"));
        Assert.DoesNotContain(listaMarcos, g => g.Id == gasto.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await cMarcos.GetAsync($"/api/gastos/{gasto.Id}")).StatusCode);

        // Marcos no puede cambiarlo ni borrarlo, ni anotar uno personal que pague Ana.
        var cambio = new GastoRequest(hoy, 10m, categoria, anaMiembro, perfil, "Gimnasio", Personal: true);
        Assert.Equal(HttpStatusCode.NotFound, (await cMarcos.PutAsJsonAsync($"/api/gastos/{gasto.Id}", cambio)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cMarcos.DeleteAsync($"/api/gastos/{gasto.Id}")).StatusCode);
        var ajeno = new GastoRequest(hoy, 15m, categoria, anaMiembro, perfil, "Masaje", Personal: true);
        Assert.Equal(HttpStatusCode.BadRequest, (await cMarcos.PostAsJsonAsync("/api/gastos", ajeno)).StatusCode);

        // Marcos tampoco ve el rastro del gasto en el historial; Ana sí.
        var auditoriaMarcos = await Leer<List<EventoAuditoriaDto>>(
            await cMarcos.GetAsync($"/api/auditoria?entidad=gasto&entidadId={gasto.Id}"));
        Assert.Empty(auditoriaMarcos);
        var auditoriaAna = await Leer<List<EventoAuditoriaDto>>(
            await cAna.GetAsync($"/api/auditoria?entidad=gasto&entidadId={gasto.Id}"));
        Assert.NotEmpty(auditoriaAna);

        // Quien paga lo sigue viendo y puede cambiarlo.
        Assert.Equal(HttpStatusCode.OK, (await cAna.GetAsync($"/api/gastos/{gasto.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cAna.PutAsJsonAsync($"/api/gastos/{gasto.Id}", cambio)).StatusCode);
    }

    /// <summary>Inserta un miembro con cuenta en el hogar (el alta normal pasa por invitación, que este test no necesita).</summary>
    private static async Task InsertarMiembroAsync(Guid hogar, Guid usuario, string nombre, string rol)
    {
        await using var conn = new NpgsqlConnection(Cadena);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "insert into public.miembro (id, hogar_id, nombre, tipo, rol, user_id, activo) values (@id, @h, @n, 'adulto', @r, @u, true)", conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("h", hogar);
        cmd.Parameters.AddWithValue("n", nombre);
        cmd.Parameters.AddWithValue("r", rol);
        cmd.Parameters.AddWithValue("u", usuario);
        await cmd.ExecuteNonQueryAsync();
    }
}
