using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Componentes;
using MiParte.Web.Demo;
using MiParte.Web.Hogares;

namespace MiParte.Web.Tests;

public class CierreMesTests : TestContext
{
    // Mes de 2020: siempre terminado, sin depender de la fecha actual.
    private static readonly DateOnly Marzo = new(2020, 3, 1);
    private static readonly MesCerradoDto Cierre = new("2020-03", new DateTimeOffset(2020, 4, 2, 10, 0, 0, TimeSpan.Zero), "Ana");

    private void Registrar(ApiFalsa api)
    {
        var cliente = new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });
        var hogar = new EstadoHogar(new AlmacenMemoria());
        Services.AddSingleton(cliente);
        Services.AddSingleton(hogar);
        PermisosDePrueba.Registrar(Services, cliente, hogar);
        Services.AddSingleton<ServicioAvisos>();
    }

    [Fact]
    public void Cualquier_miembro_cierra_un_mes_tras_confirmar()
    {
        var api = new ApiFalsa().Responde("POST /api/cierres-mes", HttpStatusCode.Created, Cierre);
        Registrar(api);
        var cambios = 0;

        var c = RenderComponent<TarjetaCierreMes>(p => p
            .Add(x => x.Mes, Marzo).Add(x => x.PuedeReabrir, false)
            .Add(x => x.Cambiado, () => cambios++));
        c.FindAll("button").First(b => b.TextContent.Trim() == "Cerrar mes").Click();
        Assert.DoesNotContain("POST /api/cierres-mes", api.Recibidas); // pide confirmación antes
        c.FindAll("button").First(b => b.TextContent.Contains("Sí, cerrar")).Click();

        Assert.Contains("\"mes\":\"2020-03\"", api.Cuerpos["POST /api/cierres-mes"]);
        Assert.Equal(1, cambios);
    }

    [Fact]
    public void Admin_reabre_un_mes_cerrado()
    {
        var api = new ApiFalsa().Responde("DELETE /api/cierres-mes/2020-03", HttpStatusCode.NoContent);
        Registrar(api);

        var c = RenderComponent<TarjetaCierreMes>(p => p.Add(x => x.Mes, Marzo).Add(x => x.PuedeReabrir, true).Add(x => x.Cierre, Cierre));
        Assert.Contains("Cerrado", c.Markup);
        c.FindAll("button").First(b => b.TextContent.Trim() == "Reabrir mes").Click();
        c.FindAll("button").First(b => b.TextContent.Contains("Sí, reabrir")).Click();

        Assert.Contains("DELETE /api/cierres-mes/2020-03", api.Recibidas);
    }

    [Fact]
    public void Quien_no_es_admin_ve_cerrar_pero_no_reabrir()
    {
        Registrar(new ApiFalsa());

        var abierto = RenderComponent<TarjetaCierreMes>(p => p.Add(x => x.Mes, Marzo).Add(x => x.PuedeReabrir, false));
        Assert.Contains(abierto.FindAll("button"), b => b.TextContent.Trim() == "Cerrar mes");

        var cerrado = RenderComponent<TarjetaCierreMes>(p => p.Add(x => x.Mes, Marzo).Add(x => x.PuedeReabrir, false).Add(x => x.Cierre, Cierre));
        Assert.Empty(cerrado.FindAll("button"));
        Assert.Contains("administrador", cerrado.Markup);
    }

    [Fact]
    public void Un_mes_sin_terminar_no_se_puede_cerrar()
    {
        Registrar(new ApiFalsa());
        var proximo = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1));

        var c = RenderComponent<TarjetaCierreMes>(p => p
            .Add(x => x.Mes, new DateOnly(proximo.Year, proximo.Month, 1)).Add(x => x.PuedeReabrir, true));

        Assert.Empty(c.FindAll("button"));
        Assert.Contains("ya ha terminado", c.Markup);
    }

    [Fact]
    public void Muestra_el_error_de_la_api_al_cerrar()
    {
        var api = new ApiFalsa().Error("POST /api/cierres-mes", HttpStatusCode.Conflict, "Ese mes ya está cerrado.");
        Registrar(api);

        var c = RenderComponent<TarjetaCierreMes>(p => p.Add(x => x.Mes, Marzo).Add(x => x.PuedeReabrir, false));
        c.FindAll("button").First(b => b.TextContent.Trim() == "Cerrar mes").Click();
        c.FindAll("button").First(b => b.TextContent.Contains("Sí, cerrar")).Click();

        Assert.Contains("ya está cerrado", c.Find("[role=alert]").TextContent);
    }

    [Fact]
    public void Gastos_de_un_mes_cerrado_no_ofrecen_editar_ni_eliminar()
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var ana = ApiFalsa.Miembro("Ana", esYo: true);
        var perfil = new PerfilRepartoDto(Guid.NewGuid(), "Individual", "individual", []);
        var cat = new CategoriaDto(Guid.NewGuid(), "Comida", null, perfil.Id);
        var gasto = new GastoResponse(Guid.NewGuid(), hoy, 12.5m, cat.Id, ana.Id, perfil.Id, "Pan", null, []);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { cat })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { perfil })
            .Responde("GET /api/gastos", HttpStatusCode.OK, new[] { gasto })
            .Responde("GET /api/cierres-mes", HttpStatusCode.OK, new[] { new MesCerradoDto($"{hoy:yyyy-MM}", DateTimeOffset.UtcNow, "Ana") });
        Registrar(api);

        var c = RenderComponent<VistaGastos>();

        Assert.Contains("cerrado", c.Markup);
        Assert.DoesNotContain(c.FindAll("button"), b => b.TextContent.Trim() is "Editar" or "Eliminar");
    }

    [Fact]
    public async Task El_modo_demo_cierra_reabre_y_bloquea_los_gastos_del_mes()
    {
        var reloj = new RelojFijo(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        var api = new CoreApiClient(new HttpClient(new Puente(new ServidorDemo(reloj))) { BaseAddress = new Uri("http://localhost:5001/") });
        var gasto = (await api.ListarGastosAsync("2026-09")).First();

        await api.CerrarMesAsync("2026-09");
        Assert.Equal("2026-09", Assert.Single(await api.ListarCierresMesAsync()).Mes);
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.GuardarGastoAsync(gasto.Id,
            new GastoRequest(gasto.Fecha, gasto.Importe, gasto.CategoriaId, gasto.PagadoPor, gasto.PerfilRepartoId, gasto.Concepto)));
        Assert.Contains("cerrado", ex.Message);
        await Assert.ThrowsAsync<ApiException>(() => api.CerrarMesAsync("2026-10")); // el mes en curso no ha terminado

        await api.ReabrirMesAsync("2026-09");
        Assert.Empty(await api.ListarCierresMesAsync());
        await api.GuardarGastoAsync(gasto.Id,
            new GastoRequest(gasto.Fecha, gasto.Importe, gasto.CategoriaId, gasto.PagadoPor, gasto.PerfilRepartoId, gasto.Concepto));
    }

    private sealed class Puente(ServidorDemo servidor) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            servidor.ResponderAsync(request, ct);
    }

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }
}
