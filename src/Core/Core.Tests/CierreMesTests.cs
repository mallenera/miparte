using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

public class CierreMesTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // Meses de 2020: siempre pasados, para no depender de la fecha actual.
    private const string Marzo = "2020-03";

    private sealed record Escenario(HttpClient Cliente, Guid Categoria, Guid Perfil, Guid Ana);

    private static async Task<Escenario> Montar(RolMiembro rol)
    {
        var f = Crear(Secreto);
        var user = Guid.NewGuid();
        var hogar = Guid.NewGuid();
        var ana = Guid.NewGuid();
        var cat = Guid.NewGuid();
        var perfil = Guid.NewGuid();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
            db.Hogares.Add(new Hogar { Id = hogar, Nombre = "Casa" });
            db.Miembros.Add(new Miembro { Id = ana, HogarId = hogar, Nombre = "Ana", Tipo = TipoMiembro.Adulto, UserId = user, Rol = rol });
            db.Categorias.Add(new Categoria { Id = cat, HogarId = hogar, Nombre = "Comida" });
            db.PerfilesReparto.Add(new PerfilReparto { Id = perfil, HogarId = hogar, Nombre = "Individual", Modo = ModoReparto.Individual });
            await db.SaveChangesAsync();
        }
        return new Escenario(Cliente(f, Token(Hs256(), user)), cat, perfil, ana);
    }

    private static GastoRequest Gasto(Escenario e, string fecha)
        => new(DateOnly.Parse(fecha), 10m, e.Categoria, e.Ana, e.Perfil, "Compra");

    private static async Task<GastoResponse> CrearGasto(Escenario e, string fecha)
    {
        var r = await e.Cliente.PostAsJsonAsync("/api/gastos", Gasto(e, fecha));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<GastoResponse>(Web))!;
    }

    private static Task<HttpResponseMessage> Cerrar(Escenario e, string mes)
        => e.Cliente.PostAsJsonAsync("/api/cierres-mes", new CerrarMesRequest(mes));

    [Fact]
    public async Task Admin_CierraUnMes_YApareceEnElListado()
    {
        var e = await Montar(RolMiembro.Admin);

        var r = await Cerrar(e, Marzo);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cierre = (await r.Content.ReadFromJsonAsync<MesCerradoDto>(Web))!;
        Assert.Equal(Marzo, cierre.Mes);
        Assert.Equal("Ana", cierre.CerradoPor);

        var lista = (await e.Cliente.GetFromJsonAsync<List<MesCerradoDto>>("/api/cierres-mes", Web))!;
        Assert.Equal(Marzo, Assert.Single(lista).Mes);
    }

    [Fact]
    public async Task Cerrar_UnMesYaCerrado_409()
    {
        var e = await Montar(RolMiembro.Admin);
        await Cerrar(e, Marzo);
        Assert.Equal(HttpStatusCode.Conflict, (await Cerrar(e, Marzo)).StatusCode);
    }

    [Fact]
    public async Task NoAdmin_NoPuedeCerrarNiReabrir_403()
    {
        var e = await Montar(RolMiembro.Miembro);
        Assert.Equal(HttpStatusCode.Forbidden, (await Cerrar(e, Marzo)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Cliente.DeleteAsync($"/api/cierres-mes/{Marzo}")).StatusCode);
        // Listar sí puede cualquier miembro.
        Assert.Equal(HttpStatusCode.OK, (await e.Cliente.GetAsync("/api/cierres-mes")).StatusCode);
    }

    [Theory]
    [InlineData("2020-3")]
    [InlineData("2020-13")]
    [InlineData("")]
    public async Task Cerrar_MesInvalido_400(string mes)
    {
        var e = await Montar(RolMiembro.Admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await Cerrar(e, mes)).StatusCode);
    }

    [Fact]
    public async Task Cerrar_MesQueNoHaTerminado_400()
    {
        var e = await Montar(RolMiembro.Admin);
        var actual = DateTime.UtcNow.AddMonths(1);
        Assert.Equal(HttpStatusCode.BadRequest, (await Cerrar(e, $"{actual.Year:0000}-{actual.Month:00}")).StatusCode);
        var hoy = DateTime.UtcNow;
        Assert.Equal(HttpStatusCode.BadRequest, (await Cerrar(e, $"{hoy.Year:0000}-{hoy.Month:00}")).StatusCode);
    }

    [Fact]
    public async Task MesCerrado_NoAdmiteCrearEditarNiBorrarGastos()
    {
        var e = await Montar(RolMiembro.Admin);
        var existente = await CrearGasto(e, $"{Marzo}-10");
        var deOtroMes = await CrearGasto(e, "2020-04-10");
        await Cerrar(e, Marzo);

        Assert.Equal(HttpStatusCode.Conflict, (await e.Cliente.PostAsJsonAsync("/api/gastos", Gasto(e, $"{Marzo}-20"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Cliente.PutAsJsonAsync($"/api/gastos/{existente.Id}", Gasto(e, $"{Marzo}-11"))).StatusCode);
        // Sacar un gasto del mes cerrado, o meter uno en él, también es cambiarlo.
        Assert.Equal(HttpStatusCode.Conflict, (await e.Cliente.PutAsJsonAsync($"/api/gastos/{existente.Id}", Gasto(e, "2020-04-11"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Cliente.PutAsJsonAsync($"/api/gastos/{deOtroMes.Id}", Gasto(e, $"{Marzo}-11"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Cliente.DeleteAsync($"/api/gastos/{existente.Id}")).StatusCode);

        // El gasto sigue ahí y los demás meses no se ven afectados.
        var lista = (await e.Cliente.GetFromJsonAsync<List<GastoResponse>>($"/api/gastos?mes={Marzo}", Web))!;
        Assert.Single(lista);
        Assert.Equal(HttpStatusCode.OK, (await e.Cliente.PutAsJsonAsync($"/api/gastos/{deOtroMes.Id}", Gasto(e, "2020-04-12"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await e.Cliente.PostAsJsonAsync("/api/gastos", Gasto(e, "2020-05-01"))).StatusCode);
    }

    [Fact]
    public async Task MesCerrado_NoAdmiteGenerarRecurrentes()
    {
        var e = await Montar(RolMiembro.Admin);
        await Cerrar(e, Marzo);
        var r = await e.Cliente.PostAsync($"/api/gastos-recurrentes/generar?mes={Marzo}", null);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task Reabrir_PermiteVolverAEditar_YSegundaVez404()
    {
        var e = await Montar(RolMiembro.Admin);
        var g = await CrearGasto(e, $"{Marzo}-10");
        await Cerrar(e, Marzo);

        Assert.Equal(HttpStatusCode.NoContent, (await e.Cliente.DeleteAsync($"/api/cierres-mes/{Marzo}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await e.Cliente.PutAsJsonAsync($"/api/gastos/{g.Id}", Gasto(e, $"{Marzo}-11"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await e.Cliente.DeleteAsync($"/api/gastos/{g.Id}")).StatusCode);
        Assert.Empty((await e.Cliente.GetFromJsonAsync<List<MesCerradoDto>>("/api/cierres-mes", Web))!);

        Assert.Equal(HttpStatusCode.NotFound, (await e.Cliente.DeleteAsync($"/api/cierres-mes/{Marzo}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Cliente.DeleteAsync("/api/cierres-mes/marzo")).StatusCode);
    }
}
