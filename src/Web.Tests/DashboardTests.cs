using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Componentes;
using MiParte.Web.Demo;
using MiParte.Web.Hogares;
using MiParte.Web.Reparto;

namespace MiParte.Web.Tests;

public class DashboardTests : BunitContext
{
    private static readonly Guid AnaId = Guid.NewGuid(), LuisId = Guid.NewGuid();
    private static readonly CategoriaDto Comida = new(Guid.NewGuid(), "Comida", null, null);
    private static readonly CategoriaDto Super = new(Guid.NewGuid(), "Supermercado", Comida.Id, null);
    private static readonly CategoriaDto Ocio = new(Guid.NewGuid(), "Ocio", null, null);
    private static readonly Guid Perfil = Guid.NewGuid();

    private static GastoResponse Gasto(DateOnly fecha, decimal importe, CategoriaDto categoria, Guid? pagador = null, bool personal = false,
        string? concepto = null) =>
        new(Guid.NewGuid(), fecha, importe, categoria.Id, pagador, Perfil, concepto, null,
            pagador is { } p ? [new RepartoGastoDto(p, importe)] : [], false, false, personal);

    // ───── Cálculo ─────

    [Fact]
    public void El_periodo_anterior_dura_lo_mismo_y_termina_el_dia_antes()
    {
        var desde = new DateOnly(2026, 10, 1);
        var hasta = new DateOnly(2026, 10, 7);

        Assert.Equal(7, AnalisisGastos.Dias(desde, hasta));
        Assert.Equal(new DateOnly(2026, 9, 24), AnalisisGastos.InicioAnterior(desde, hasta));
    }

    [Fact]
    public void Los_gastos_personales_no_entran_en_el_total_ni_en_las_categorias()
    {
        var desde = new DateOnly(2026, 10, 1);
        var gastos = new[]
        {
            Gasto(desde, 100m, Comida, AnaId),
            Gasto(desde.AddDays(1), 25m, Ocio, LuisId, personal: true),
        };

        var r = AnalisisGastos.Calcular(gastos, [Comida, Ocio], desde, desde.AddDays(9));

        Assert.Equal(100m, r.Total);
        Assert.Equal(25m, r.Personales);
        Assert.Equal(1, r.NumeroGastos);
        Assert.Equal(10m, r.MediaDiaria);
        Assert.DoesNotContain(r.PorCategoria, c => c.CategoriaId == Ocio.Id);
    }

    [Fact]
    public void Las_subcategorias_se_suman_a_su_categoria_de_primer_nivel()
    {
        var desde = new DateOnly(2026, 10, 1);
        var gastos = new[]
        {
            Gasto(desde, 40m, Super, AnaId),
            Gasto(desde, 10m, Comida, AnaId),
            Gasto(desde, 30m, Ocio, AnaId),
        };

        var r = AnalisisGastos.Calcular(gastos, [Comida, Super, Ocio], desde, desde.AddDays(5));

        Assert.Equal(["Comida", "Ocio"], r.PorCategoria.Select(c => c.Nombre));
        Assert.Equal(50m, r.CategoriaPrincipal!.Importe);
        Assert.Equal(2, r.CategoriaPrincipal.Gastos);
    }

    [Fact]
    public void La_variacion_compara_con_el_periodo_anterior_de_igual_duracion()
    {
        var desde = new DateOnly(2026, 10, 8);
        var hasta = new DateOnly(2026, 10, 14);
        var gastos = new[]
        {
            Gasto(new DateOnly(2026, 10, 1), 100m, Ocio, AnaId), // periodo anterior: 1-7 oct
            Gasto(new DateOnly(2026, 10, 9), 150m, Ocio, AnaId),
        };

        var r = AnalisisGastos.Calcular(gastos, [Ocio], desde, hasta);

        Assert.Equal(100m, r.TotalAnterior);
        Assert.Equal(0.5m, r.Variacion);
        Assert.Equal(150m, r.Total);
    }

    [Fact]
    public void Sin_gasto_anterior_no_hay_variacion()
    {
        var desde = new DateOnly(2026, 10, 8);

        var r = AnalisisGastos.Calcular([Gasto(desde, 10m, Ocio, AnaId)], [Ocio], desde, desde.AddDays(6));

        Assert.Null(r.Variacion);
    }

    [Theory]
    [InlineData(31, Granularidad.Dia)]
    [InlineData(32, Granularidad.Semana)]
    [InlineData(120, Granularidad.Semana)]
    [InlineData(121, Granularidad.Mes)]
    public void La_serie_se_agrupa_segun_la_duracion_del_periodo(int dias, Granularidad esperada) =>
        Assert.Equal(esperada, AnalisisGastos.GranularidadPara(dias));

    [Fact]
    public void La_serie_incluye_los_tramos_sin_gasto_y_suma_el_total()
    {
        var desde = new DateOnly(2026, 10, 1);
        var gastos = new[] { Gasto(desde, 10m, Ocio, AnaId), Gasto(desde.AddDays(4), 5m, Ocio, AnaId), Gasto(desde.AddDays(4), 2m, Ocio, AnaId) };

        var r = AnalisisGastos.Calcular(gastos, [Ocio], desde, desde.AddDays(6));

        Assert.Equal(7, r.Serie.Count);
        Assert.Equal(17m, r.Serie.Sum(p => p.Importe));
        Assert.Equal(7m, r.Serie[4].Importe);
        Assert.Equal(0m, r.Serie[1].Importe);
    }

    [Fact]
    public void La_serie_semanal_empieza_en_el_inicio_del_periodo_aunque_la_semana_empiece_antes()
    {
        var desde = new DateOnly(2026, 10, 7); // miércoles
        var r = AnalisisGastos.Calcular([Gasto(desde, 10m, Ocio, AnaId)], [Ocio], desde, desde.AddDays(40));

        Assert.Equal(desde, r.Serie[0].Inicio);
        Assert.Equal(new DateOnly(2026, 10, 12), r.Serie[1].Inicio); // lunes siguiente
        Assert.Equal(10m, r.Serie[0].Importe);
    }

    [Fact]
    public void Lo_pagado_por_la_cuenta_comun_aparece_sin_miembro()
    {
        var desde = new DateOnly(2026, 10, 1);
        var gastos = new[] { Gasto(desde, 30m, Ocio, AnaId), Gasto(desde, 70m, Ocio, null) };

        var r = AnalisisGastos.Calcular(gastos, [Ocio], desde, desde);

        Assert.Equal(70m, r.PorMiembro.First(m => m.MiembroId is null).Pagado);
        Assert.Equal(30m, r.PorMiembro.First(m => m.MiembroId == AnaId).Asumido);
    }

    [Fact]
    public void Filtrar_por_categoria_de_primer_nivel_incluye_sus_subcategorias_y_por_quien_paga()
    {
        var desde = new DateOnly(2026, 10, 1);
        var gastos = new[]
        {
            Gasto(desde, 40m, Super, AnaId),
            Gasto(desde, 10m, Comida, LuisId),
            Gasto(desde, 30m, Ocio, AnaId),
            Gasto(desde, 5m, Ocio, null),
        };

        Assert.Equal(50m, AnalisisGastos.Filtrar(gastos, [Comida, Super, Ocio], new FiltroAnalisis(Categoria: Comida.Id)).Sum(g => g.Importe));
        Assert.Equal(70m, AnalisisGastos.Filtrar(gastos, [Comida, Super, Ocio], new FiltroAnalisis(Miembro: AnaId)).Sum(g => g.Importe));
        Assert.Equal(5m, AnalisisGastos.Filtrar(gastos, [Comida, Super, Ocio], new FiltroAnalisis(CuentaComun: true)).Sum(g => g.Importe));
        Assert.Equal(30m, AnalisisGastos.Filtrar(gastos, [Comida, Super, Ocio], new FiltroAnalisis(Categoria: Ocio.Id, Miembro: AnaId)).Sum(g => g.Importe));
        Assert.Equal(4, AnalisisGastos.Filtrar(gastos, [Comida, Super, Ocio], new FiltroAnalisis()).Count);
    }

    // ───── Componente ─────

    private ApiFalsa Registrar(IReadOnlyList<GastoResponse> gastos)
    {
        var api = new ApiFalsa()
            .Responde("GET /api/gastos", HttpStatusCode.OK, gastos)
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { Comida, Super, Ocio })
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[]
            {
                ApiFalsa.Miembro("Ana", rol: "admin", esYo: true, id: AnaId), ApiFalsa.Miembro("Luis", id: LuisId),
            });
        var cliente = new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });
        var hogar = new EstadoHogar(new AlmacenMemoria());
        Services.AddSingleton(cliente);
        Services.AddSingleton(hogar);
        Services.AddSingleton<TimeProvider>(new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero)));
        return api;
    }

    [Fact]
    public void Por_defecto_muestra_el_mes_en_curso_hasta_hoy_y_pide_tambien_el_periodo_anterior()
    {
        var api = Registrar([]);

        var c = Render<VistaDashboard>();

        Assert.Equal("2026-10-01", c.FindAll("input[type=date]")[0].GetAttribute("value"));
        Assert.Equal("2026-10-07", c.FindAll("input[type=date]")[1].GetAttribute("value"));
        Assert.Contains("/api/gastos?desde=2026-09-24&hasta=2026-10-07", api.Consultas);
        Assert.Contains("No hay gastos del hogar entre estas fechas", c.Markup);
    }

    [Fact]
    public void Muestra_los_indicadores_y_los_graficos()
    {
        Registrar(
        [
            Gasto(new DateOnly(2026, 9, 28), 100m, Ocio, AnaId, concepto: "Cine"),
            Gasto(new DateOnly(2026, 10, 2), 60m, Super, AnaId, concepto: "Compra"),
            Gasto(new DateOnly(2026, 10, 5), 140m, Ocio, LuisId, concepto: "Concierto"),
        ]);

        var c = Render<VistaDashboard>();

        Assert.Contains("200,00", c.Find("#kpi-total").TextContent);
        Assert.Contains("+100,0", c.Find("#kpi-variacion").TextContent); // +100 % frente a los 100 € del periodo anterior
        Assert.Contains("Ocio", c.Find("#kpi-categoria").TextContent);
        Assert.Contains("140,00", c.Find("#kpi-mayor").TextContent);
        Assert.Contains("28,57", c.Find("#kpi-media").TextContent); // 200 / 7 días
        Assert.NotEmpty(c.FindAll("svg.dash-svg path.gr-linea"));
        Assert.NotEmpty(c.FindAll("svg.dash-rosco circle.dn-m0, svg.dash-rosco circle.dn-m1"));
        Assert.Equal(2, c.FindAll("progress.barra").Count);
    }

    [Fact]
    public void Un_periodo_rapido_cambia_las_fechas_y_recarga()
    {
        var api = Registrar([]);
        var c = Render<VistaDashboard>();

        c.FindAll("button.chip-btn").First(b => b.TextContent == "Mes anterior").Click();

        Assert.Equal("2026-09-01", c.FindAll("input[type=date]")[0].GetAttribute("value"));
        Assert.Equal("2026-09-30", c.FindAll("input[type=date]")[1].GetAttribute("value"));
        Assert.Contains("/api/gastos?desde=2026-08-02&hasta=2026-09-30", api.Consultas);
    }

    [Fact]
    public void Cambiar_la_fecha_desde_recarga_con_el_nuevo_rango()
    {
        var api = Registrar([]);
        var c = Render<VistaDashboard>();

        c.FindAll("input[type=date]")[0].Change("2026-10-05");

        Assert.Contains("/api/gastos?desde=2026-10-02&hasta=2026-10-07", api.Consultas); // 5-7 oct son 3 días: el anterior empieza el 2
    }

    [Fact]
    public void Un_rango_invertido_avisa_y_no_pide_datos()
    {
        var api = Registrar([]);
        var c = Render<VistaDashboard>();
        var antes = api.Consultas.Count;

        c.FindAll("input[type=date]")[0].Change("2026-10-20");

        Assert.Contains("no puede ser posterior", c.Find("[role=alert]").TextContent);
        Assert.Equal(antes, api.Consultas.Count);
    }

    private static readonly GastoResponse[] Datos =
    [
        Gasto(new DateOnly(2026, 9, 28), 100m, Ocio, AnaId, concepto: "Cine"),
        Gasto(new DateOnly(2026, 10, 2), 60m, Super, AnaId, concepto: "Compra"),
        Gasto(new DateOnly(2026, 10, 5), 140m, Ocio, LuisId, concepto: "Concierto"),
    ];

    private static string Total(IRenderedComponent<VistaDashboard> c) => c.Find("#kpi-total").TextContent;

    [Fact]
    public void Pinchar_una_categoria_recalcula_los_indicadores_y_conserva_el_resto_de_categorias()
    {
        Registrar(Datos);
        var c = Render<VistaDashboard>();

        c.FindAll("button.dash-click").First(b => b.TextContent.Contains("Ocio")).Click();

        Assert.Contains("140,00", Total(c));
        Assert.Contains("Ocio", c.Find("#kpi-categoria").TextContent);
        Assert.Contains("Categoría: Ocio", c.Find(".dash-filtros").TextContent);
        Assert.Equal("true", c.FindAll("button.dash-click").First(b => b.TextContent.Contains("Ocio")).GetAttribute("aria-pressed"));
        Assert.Contains(c.FindAll("button.dash-click"), b => b.TextContent.Contains("Comida")); // la lista no pierde categorías
        Assert.Single(c.FindAll("button.dash-click.sel"), b => b.TextContent.Contains("Ocio"));

        c.FindAll("button.dash-click").First(b => b.TextContent.Contains("Ocio")).Click(); // segunda pulsación: quita el filtro
        Assert.Contains("200,00", Total(c));
        Assert.Empty(c.FindAll(".dash-filtros"));
    }

    [Fact]
    public void Pinchar_una_persona_filtra_por_quien_paga_y_se_combina_con_la_categoria()
    {
        Registrar(Datos);
        var c = Render<VistaDashboard>();

        c.FindAll("button.dash-miembro").First(b => b.TextContent.Contains("Ana")).Click();
        Assert.Contains("60,00", Total(c)); // lo que pagó Ana este periodo
        Assert.Contains(c.FindAll("button.dash-click"), b => b.TextContent.Contains("Comida"));

        c.FindAll("button.dash-click").First(b => b.TextContent.Contains("Comida")).Click();

        Assert.Contains("60,00", Total(c));
        Assert.Contains("Comida", c.Find(".dash-filtros").TextContent);
        Assert.Contains("Ana", c.Find(".dash-filtros").TextContent);
    }

    [Fact]
    public void Pinchar_un_punto_de_la_evolucion_limita_los_indicadores_a_ese_dia()
    {
        Registrar(Datos);
        var c = Render<VistaDashboard>();

        c.FindAll("circle.gr-hit").First(p => p.GetAttribute("aria-label")!.StartsWith("2 oct 2026")).Click();

        Assert.Contains("60,00", Total(c));
        Assert.Contains("Fechas:", c.Find(".dash-filtros").TextContent);
        Assert.Equal(7, c.FindAll("circle.gr-hit").Count); // la gráfica sigue mostrando todo el periodo
    }

    [Fact]
    public void Quitar_todos_los_filtros_y_cambiar_de_periodo_limpian_la_seleccion()
    {
        Registrar(Datos);
        var c = Render<VistaDashboard>();
        c.FindAll("button.dash-click").First(b => b.TextContent.Contains("Ocio")).Click();

        c.FindAll(".dash-filtros button").First(b => b.TextContent == "Quitar todos").Click();
        Assert.Contains("200,00", Total(c));

        c.FindAll("button.dash-click").First(b => b.TextContent.Contains("Ocio")).Click();
        c.FindAll("button.chip-btn").First(b => b.TextContent == "Este mes").Click();
        Assert.Empty(c.FindAll(".dash-filtros"));
    }

    // ───── Servidor demo ─────

    [Fact]
    public async Task El_modo_demo_filtra_los_gastos_por_rango_de_fechas()
    {
        var reloj = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        var api = new CoreApiClient(new HttpClient(new Puente(new ServidorDemo(reloj))) { BaseAddress = new Uri("http://localhost:5001/") });
        var todos = await api.ListarGastosAsync();
        var algunDia = todos.OrderBy(g => g.Fecha).Skip(todos.Count / 2).First().Fecha;

        var rango = await api.ListarGastosPorRangoAsync(algunDia, algunDia);

        Assert.NotEmpty(rango);
        Assert.All(rango, g => Assert.Equal(algunDia, g.Fecha));
        Assert.Equal(todos.Count(g => g.Fecha == algunDia), rango.Count);
        await Assert.ThrowsAsync<ApiException>(() => api.ListarGastosPorRangoAsync(algunDia.AddDays(1), algunDia));
    }

    private sealed class Puente(ServidorDemo servidor) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            servidor.ResponderAsync(request, ct);
    }
}
