using System.Net;
using Bunit;
using Microsoft.JSInterop;
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
            new("gasto_ahorro_adelantado", Guid.NewGuid(), new DateOnly(2026, 10, 7), 0m, AnaId, Luz.Id, "Fianza", -30m, 660m),
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
        Assert.Equal(6, m.Visibles.Count);
        Assert.Equal("retirada_ahorro", m.Visibles[0].Tipo);
    }

    [Theory]
    [InlineData("entradas", 3)]
    [InlineData("salidas", 2)]
    [InlineData("ingreso", 1)]
    [InlineData("gasto", 2)]
    [InlineData("deposito_ahorro", 1)]
    public async Task El_filtro_de_tipo_se_aplica_sin_volver_a_pedir(string tipo, int lineas)
    {
        var api = Api();
        var m = await Modelo(api);
        var pedidas = api.Recibidas.Count;

        m.FiltroTipo = tipo;

        Assert.Equal(lineas, m.Visibles.Count);
        Assert.Equal(pedidas, api.Recibidas.Count);
    }

    [Theory]
    [InlineData("cuenta_comun", new[] { "gasto", "ingreso", "aportacion" })]
    [InlineData("ahorro", new[] { "retirada_ahorro", "gasto_ahorro_adelantado", "deposito_ahorro", "aportacion" })]
    public async Task El_filtro_de_cuenta_separa_la_cuenta_comun_de_su_ahorro(string cuenta, string[] tipos)
    {
        var m = await Modelo(Api());

        m.FiltroCuenta = cuenta;

        // La aportación con parte de ahorro es de las dos cuentas.
        Assert.Equal(tipos, m.Visibles.Select(v => v.Tipo));
        Assert.True(m.HayFiltros);
        m.LimpiarFiltros();
        Assert.Equal("", m.FiltroCuenta);
    }

    [Fact]
    public async Task Un_gasto_del_ahorro_adelantado_se_describe_con_quien_lo_adelanta_y_no_cuenta_en_entradas_ni_salidas()
    {
        var m = await Modelo(Api());

        m.FiltroTipo = "gasto";

        var adelantado = Assert.Single(m.Visibles, v => v.Tipo == "gasto_ahorro_adelantado");
        Assert.Equal("Gasto desde el ahorro adelantado por Ana: Fianza", m.Descripcion(adelantado));
        Assert.True(ModeloMovimientosCuenta.EsAhorro(adelantado));
        Assert.Equal(80m, m.SalidasVisibles);
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

        Assert.Equal("Gasto: Luz", m.Descripcion(m.Visibles.Single(v => v.Tipo == "gasto")));
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
    public void Exportar_descarga_un_csv_con_las_lineas_filtradas()
    {
        Services.AddSingleton(Cliente(Api()));
        JSInterop.SetupVoid("descargarFichero", _ => true);
        var c = Render<PanelMovimientosCuenta>(p => p.Add(x => x.Hoy, Hoy));

        c.Find("select").Change("salidas");
        c.Find("button.btn-ghost").Click();

        var llamada = Assert.Single(JSInterop.Invocations["descargarFichero"]);
        Assert.Equal("movimientos-cuenta-comun_2026-10-01_2026-10-15.csv", llamada.Arguments[0]);
        var csv = (string)llamada.Arguments[1]!;
        Assert.Equal(3, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length); // cabecera + 2 salidas
        Assert.Contains("Gasto;Gasto: Luz", csv);
        Assert.Equal("text/csv;charset=utf-8", llamada.Arguments[2]);
    }

    [Fact]
    public async Task El_csv_conserva_el_orden_del_extracto_dentro_de_un_mismo_dia()
    {
        var dia = new DateOnly(2026, 10, 1);
        var extracto = new MovimientosCuentaComunResponse(dia, Hoy, 0m, 300m, 0m, 300m,
        [
            new("aportacion", null, dia, 100m, AnaId, null, null, 0m, 100m),
            new("aportacion", null, dia, 200m, LuisId, null, null, 0m, 300m),
        ]);
        var m = await Modelo(Api(extracto));

        var lineas = m.GenerarCsv().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.EndsWith(";100,00", lineas[1]);
        Assert.EndsWith(";300,00", lineas[2]);
    }

    [Fact]
    public void Sin_lineas_el_boton_de_exportar_esta_desactivado()
    {
        Services.AddSingleton(Cliente(Api(new MovimientosCuentaComunResponse(new DateOnly(2026, 10, 1), Hoy, 0m, 0m, 0m, 0m, []))));

        var c = Render<PanelMovimientosCuenta>(p => p.Add(x => x.Hoy, Hoy));

        Assert.True(c.Find("button.btn-ghost").HasAttribute("disabled"));
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
        Assert.Equal(6, c.FindAll("#movimientos-lista .gastorow").Count);
        Assert.Contains("del ahorro al efectivo, pendiente de reembolso", c.Markup); // el adelantado, sin mover el saldo
        Assert.Contains("cuenta común (ahorro)", c.Markup);

        c.Find("select").Change("salidas");

        Assert.Equal(2, c.FindAll("#movimientos-lista .gastorow").Count);
        Assert.Contains("(filtradas)", c.Markup);
    }

    [Fact]
    public void El_buscador_de_concepto_filtra_la_lista_al_escribir()
    {
        Services.AddSingleton(Cliente(Api()));

        var c = Render<PanelMovimientosCuenta>(p => p.Add(x => x.Hoy, Hoy));
        c.Find("input[type=search]").Input("cena");

        Assert.Single(c.FindAll("#movimientos-lista .gastorow"));
        Assert.Contains("Cena amigas", c.Markup);
    }

    [Fact]
    public void El_filtro_de_persona_actualiza_la_lista()
    {
        Services.AddSingleton(Cliente(Api()));

        var c = Render<PanelMovimientosCuenta>(p => p.Add(x => x.Hoy, Hoy));
        c.FindAll("select")[2].Change(AnaId.ToString());

        Assert.DoesNotContain("Retirada de ahorro de Luis", c.Markup);
        Assert.Contains("adelantado por Ana", c.Markup);
        Assert.Contains("Aportación de Ana", c.Markup);
    }
}
