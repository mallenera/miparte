using System.Net;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Modelos;
using MiParte.Web.Reparto;

namespace MiParte.Web.Tests;

/// <summary>Periodo, carga y selección cruzada del Dashboard probados sin renderizar componentes.</summary>
public class ModeloDashboardTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 20);
    private readonly Guid _ana = Guid.NewGuid();
    private readonly CategoriaDto _casa = new(Guid.NewGuid(), "Casa", null, null);
    private readonly CategoriaDto _ocio = new(Guid.NewGuid(), "Ocio", null, null);

    private GastoResponse Gasto(DateOnly fecha, decimal importe, CategoriaDto cat, Guid? paga = null) => new(
        Guid.NewGuid(), fecha, importe, cat.Id, paga ?? _ana, Guid.NewGuid(), "x", null,
        [new RepartoGastoDto(paga ?? _ana, importe)], false, false, false);

    private (ModeloDashboard Modelo, ApiFalsa Api) Crear(params GastoResponse[] gastos)
    {
        var api = new ApiFalsa()
            .Responde("GET /api/gastos", HttpStatusCode.OK, gastos)
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { _casa, _ocio })
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ApiFalsa.Miembro("Ana", "admin", esYo: true, id: _ana) });
        var cliente = new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });
        return (new ModeloDashboard(cliente, () => Hoy), api);
    }

    [Fact]
    public async Task Al_iniciar_pide_el_mes_en_curso_y_el_anterior_de_igual_duracion_en_una_sola_peticion()
    {
        var (m, api) = Crear(Gasto(new DateOnly(2026, 10, 5), 100m, _casa));

        await m.IniciarAsync();

        Assert.Equal((new DateOnly(2026, 10, 1), Hoy), (m.Desde, m.Hasta));
        // 20 días: el periodo anterior de igual duración empieza el 11 de septiembre.
        Assert.Contains("/api/gastos?desde=2026-09-11&hasta=2026-10-20", api.Consultas);
        Assert.Equal(1, api.Recibidas.Count(r => r == "GET /api/gastos"));
        Assert.Equal(100m, m.R!.Total);
        Assert.False(m.Cargando);
    }

    [Theory]
    [InlineData(PeriodoRapido.EsteMes, "2026-10-01", "2026-10-20")]
    [InlineData(PeriodoRapido.MesAnterior, "2026-09-01", "2026-09-30")]
    [InlineData(PeriodoRapido.Ultimos30, "2026-09-21", "2026-10-20")]
    [InlineData(PeriodoRapido.EsteAnio, "2026-01-01", "2026-10-20")]
    public async Task Los_periodos_rapidos_fijan_las_fechas_esperadas(PeriodoRapido p, string desde, string hasta)
    {
        var (m, _) = Crear();
        await m.IniciarAsync();

        await m.ElegirAsync(p);

        Assert.Equal((DateOnly.Parse(desde), DateOnly.Parse(hasta)), (m.Desde, m.Hasta));
    }

    [Fact]
    public async Task Un_periodo_invertido_se_rechaza_sin_llamar_a_la_api()
    {
        var (m, api) = Crear();
        await m.IniciarAsync();
        var antes = api.Recibidas.Count;

        m.Desde = new DateOnly(2026, 10, 20);
        m.Hasta = new DateOnly(2026, 10, 1);
        await m.AplicarFechasAsync();

        Assert.Contains("no puede ser posterior", m.ErrorPeriodo);
        Assert.Equal(antes, api.Recibidas.Count);
    }

    [Fact]
    public async Task Elegir_una_categoria_la_filtra_y_pinchar_otra_vez_la_quita()
    {
        var (m, _) = Crear(Gasto(new DateOnly(2026, 10, 5), 100m, _casa), Gasto(new DateOnly(2026, 10, 6), 40m, _ocio));
        await m.IniciarAsync();
        Assert.Equal(140m, m.R!.Total);

        m.ElegirCategoria(_casa.Id);
        Assert.True(m.HayFiltros);
        Assert.Equal(100m, m.R!.Total);
        Assert.Equal(140m, m.RCat!.Total); // el gráfico de categorías no se filtra con su propia selección

        m.ElegirCategoria(_casa.Id);
        Assert.False(m.HayFiltros);
        Assert.Equal(140m, m.R!.Total);
    }

    [Fact]
    public async Task Otras_categorias_no_se_puede_elegir()
    {
        var (m, _) = Crear(Gasto(Hoy, 10m, _casa));
        await m.IniciarAsync();

        m.ElegirCategoria(Guid.Empty);

        Assert.False(m.HayFiltros);
    }

    [Fact]
    public async Task Elegir_la_cuenta_comun_y_una_persona_son_excluyentes()
    {
        var (m, _) = Crear(Gasto(Hoy, 10m, _casa));
        await m.IniciarAsync();

        m.ElegirMiembro(_ana);
        Assert.Equal(_ana, m.Filtro.Miembro);

        m.ElegirMiembro(null);
        Assert.True(m.Filtro.CuentaComun);
        Assert.Null(m.Filtro.Miembro);
        Assert.True(m.HayMiembroElegido);
    }

    [Fact]
    public async Task La_url_a_gastos_lleva_el_tramo_la_categoria_y_el_pagador()
    {
        var (m, _) = Crear(Gasto(new DateOnly(2026, 10, 5), 100m, _casa));
        await m.IniciarAsync();
        Assert.Equal("gastos?desde=2026-10-01&hasta=2026-10-20", m.UrlGastos);

        m.ElegirCategoria(_casa.Id);
        m.ElegirMiembro(_ana);
        m.ElegirTramo(m.RSerie!.Serie[4]); // 5 de octubre

        var url = m.UrlGastos;
        Assert.StartsWith("gastos?desde=2026-10-05&hasta=2026-10-05", url);
        Assert.Contains($"&categoria={_casa.Id}", url);
        Assert.Contains($"&pagador={_ana}", url);
    }

    [Fact]
    public async Task Cambiar_de_periodo_reinicia_la_seleccion()
    {
        var (m, _) = Crear(Gasto(new DateOnly(2026, 10, 5), 100m, _casa));
        await m.IniciarAsync();
        m.ElegirCategoria(_casa.Id);

        await m.ElegirAsync(PeriodoRapido.EsteAnio);

        Assert.False(m.HayFiltros);
    }

    [Fact]
    public async Task Un_error_de_la_api_deja_el_mensaje_y_sin_resultados()
    {
        var (m, api) = Crear();
        api.Error("GET /api/gastos", HttpStatusCode.InternalServerError, "Caído");

        await m.IniciarAsync();

        Assert.Contains("Caído", m.ErrorCarga);
        Assert.Null(m.R);
        Assert.False(m.Cargando);
    }
}

public class GraficoLineaTests
{
    private static PuntoSerie P(int dia, decimal importe)
        => new(new DateOnly(2026, 10, dia), new DateOnly(2026, 10, dia), Granularidad.Dia, importe);

    [Fact]
    public void El_importe_maximo_toca_el_borde_superior_y_el_cero_el_inferior()
    {
        var g = new GraficoLinea([P(1, 0m), P(2, 50m), P(3, 100m)]);

        Assert.Equal(GraficoLinea.MargenSup, g.Y(100m), 6);
        Assert.Equal(GraficoLinea.MargenSup + GraficoLinea.AltoUtil, g.Y(0m), 6);
        Assert.Equal(GraficoLinea.MargenIzq, g.X(0), 6);
        Assert.Equal(GraficoLinea.Ancho - GraficoLinea.MargenDer, g.X(2), 6);
    }

    [Fact]
    public void Un_solo_tramo_se_centra()
    {
        var g = new GraficoLinea([P(1, 10m)]);

        Assert.Equal(GraficoLinea.MargenIzq + GraficoLinea.AnchoUtil / 2, g.X(0), 6);
    }

    [Fact]
    public void Una_serie_a_cero_no_divide_por_cero()
    {
        var g = new GraficoLinea([P(1, 0m), P(2, 0m)]);

        Assert.Equal(1.0, g.MaxSerie);
        Assert.False(double.IsNaN(g.Y(0m)));
    }

    [Fact]
    public void La_linea_empieza_con_M_y_el_area_se_cierra_en_el_eje()
    {
        var g = new GraficoLinea([P(1, 10m), P(2, 20m)]);

        Assert.StartsWith("M", g.Linea);
        Assert.Contains(" L", g.Linea);
        Assert.EndsWith("Z", g.Area);
    }

    [Fact]
    public void Se_rotula_el_primero_el_ultimo_y_como_mucho_un_cuarto_del_resto()
    {
        var g = new GraficoLinea(Enumerable.Range(1, 29).Select(d => P(d, 1m)).ToList());

        Assert.Equal(7, g.PasoEtiquetas);
        Assert.True(g.RotulaEje(0));
        Assert.True(g.RotulaEje(28));
        Assert.False(g.RotulaEje(3));
        Assert.Equal("start", g.AnclaEje(0));
        Assert.Equal("end", g.AnclaEje(28));
        Assert.Equal("middle", g.AnclaEje(5));
    }

    [Fact]
    public void Las_guias_son_cero_mitad_y_maximo()
    {
        var g = new GraficoLinea([P(1, 0m), P(2, 200m)]);

        var textos = g.Guias.Select(x => x.Texto).ToList();

        Assert.Equal(3, textos.Count);
        Assert.StartsWith("0", textos[0]);
        Assert.StartsWith("100", textos[1]);
        Assert.StartsWith("200", textos[2]);
    }

    [Fact]
    public void Las_rodajas_suman_100_y_marcan_la_elegida()
    {
        var ana = Guid.NewGuid();
        var porMiembro = new[]
        {
            new GastoPorMiembro(ana, 75m, 0m),
            new GastoPorMiembro(null, 25m, 0m),
            new GastoPorMiembro(Guid.NewGuid(), 0m, 10m), // sin dinero adelantado: sin arco
        };

        var rodajas = GraficoLinea.Rodajas(porMiembro, _ => 3, new FiltroAnalisis(Miembro: ana)).ToList();

        Assert.Equal(2, rodajas.Count);
        Assert.Equal(["dn-m3", "dn-comun"], rodajas.Select(r => r.Clase));
        Assert.Equal([true, false], rodajas.Select(r => r.Elegida));
        Assert.Equal("75 25", rodajas[0].Trazo);
        Assert.Equal("25", rodajas[0].Desfase);
        Assert.Equal("-50", rodajas[1].Desfase); // empieza donde acaba la primera (25 - 75)
    }
}
