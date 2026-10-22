using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Componentes;
using MiParte.Web.Modelos;

namespace MiParte.Web.Tests;

/// <summary>Extracto de movimientos de la cuenta común: periodo pedido, filtros en el cliente y pintado.</summary>
public class MovimientosCuentaTests : BunitContext
{
    private static readonly DateOnly Hoy = new(2026, 10, 15);
    private static readonly Guid AnaId = Guid.NewGuid(), LuisId = Guid.NewGuid();
    private static readonly CategoriaDto Luz = new(Guid.NewGuid(), "Luz", null, null);

    private static MovimientosCuentaComunResponse Extracto() => new(
        new DateOnly(2026, 10, 1), Hoy, 100m, 640m, 140m, 600m,
        [
            new("aportacion", null, new DateOnly(2026, 10, 1), 500m, AnaId, null, null, 100m, 600m),
            new("ingreso", Guid.NewGuid(), new DateOnly(2026, 10, 3), 40m, LuisId, null, "Cena amigas", 0m, 640m),
            new("deposito_ahorro", Guid.NewGuid(), new DateOnly(2026, 10, 4), 100m, AnaId, null, "Lotería", 0m, 740m),
            new("gasto", Guid.NewGuid(), new DateOnly(2026, 10, 5), -80m, null, Luz.Id, null, 0m, 660m),
            new("retirada_ahorro", Guid.NewGuid(), new DateOnly(2026, 10, 9), -60m, LuisId, null, null, 0m, 600m),
        ], EfectivoFinal: 460m, AhorroFinal: 140m);

    private ApiFalsa Api(MovimientosCuentaComunResponse? extracto = null) => new ApiFalsa()
        .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ApiFalsa.Miembro("Ana", id: AnaId), ApiFalsa.Miembro("Luis", id: LuisId) })
        .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { Luz })
        .Responde("GET /api/cuenta-comun/movimientos", HttpStatusCode.OK, extracto ?? Extracto());

    private static CoreApiClient Cliente(ApiFalsa api) => new(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });

    private static async Task<ModeloMovimientosCuenta> Modelo(ApiFalsa api)
    {
        var m = new ModeloMovimientosCuenta(Cliente(api), Hoy);
        await m.CargarAsync();
        return m;
    }

    [Fact]
    public async Task Por_defecto_pide_del_dia_1_del_mes_a_hoy_y_lista_lo_mas_reciente_primero()
    {
        var api = Api();

        var m = await Modelo(api);

        Assert.Contains("/api/cuenta-comun/movimientos?desde=2026-10-01&hasta=2026-10-15", api.Consultas);
        Assert.Equal(5, m.Visibles.Count);
        Assert.Equal("retirada_ahorro", m.Visibles[0].Tipo);
    }

    [Theory]
    [InlineData("entradas", 3)]
    [InlineData("salidas", 2)]
    [InlineData("ahorro", 2)]
    [InlineData("ingreso", 1)]
    [InlineData("gasto", 1)]
    public async Task El_filtro_de_tipo_se_aplica_sin_volver_a_pedir(string tipo, int lineas)
    {
        var api = Api();
        var m = await Modelo(api);
        var pedidas = api.Recibidas.Count;

        m.FiltroTipo = tipo;

        Assert.Equal(lineas, m.Visibles.Count);
        Assert.Equal(pedidas, api.Recibidas.Count);
    }

    [Fact]
    public async Task Filtrar_por_persona_y_texto_conserva_el_saldo_real_de_cada_linea()
    {
        var m = await Modelo(Api());

        m.FiltroMiembro = LuisId.ToString();
        m.FiltroTexto = "cena";

        var linea = Assert.Single(m.Visibles);
        Assert.Equal(640m, linea.Saldo);
        Assert.Equal(40m, m.EntradasVisibles);
        Assert.True(m.HayFiltros);

        m.LimpiarFiltros();
        Assert.False(m.HayFiltros);
    }

    [Fact]
    public async Task Un_gasto_sin_concepto_se_describe_con_su_categoria()
    {
        var m = await Modelo(Api());

        m.FiltroTipo = "gasto";

        Assert.Equal("Gasto: Luz", m.Descripcion(m.Visibles[0]));
    }

    [Fact]
    public async Task Con_desde_posterior_a_hasta_no_pide_nada()
    {
        var api = Api();
        var m = await Modelo(api);
        var pedidas = api.Recibidas.Count;

        m.Desde = new DateOnly(2026, 11, 1);
        await m.AplicarPeriodoAsync();

        Assert.False(m.PeriodoValido);
        Assert.Equal(pedidas, api.Recibidas.Count);
    }

    [Fact]
    public async Task Un_error_de_la_api_queda_en_Error()
    {
        var api = Api().Error("GET /api/cuenta-comun/movimientos", HttpStatusCode.BadRequest, "Fecha mala");

        var m = await Modelo(api);

        Assert.Equal("Fecha mala", m.Error);
        Assert.Null(m.Extracto);
    }

    [Fact]
    public void El_panel_pinta_saldos_signos_y_nombres()
    {
        Services.AddSingleton(Cliente(Api()));

        var c = Render<PanelMovimientosCuenta>(p => p.Add(x => x.Hoy, Hoy));

        Assert.Contains("Aportación de Ana", c.Markup);
        Assert.Contains("Retirada de ahorro de Luis", c.Markup);
        Assert.Contains("Cena amigas", c.Markup);
        Assert.Contains("−", c.Find(".g-importe.neg").TextContent);
        Assert.Contains("600,00", c.Find("#movimientos-saldo-final").TextContent);
        Assert.Contains("460,00", c.Find("#movimientos-efectivo").TextContent);
        Assert.Contains("140,00", c.Find("#movimientos-ahorro").TextContent);
        Assert.Equal(5, c.FindAll("#movimientos-lista .gastorow").Count);

        c.Find("select").Change("salidas");

        Assert.Equal(2, c.FindAll("#movimientos-lista .gastorow").Count);
        Assert.Contains("(filtradas)", c.Markup);
    }
}
