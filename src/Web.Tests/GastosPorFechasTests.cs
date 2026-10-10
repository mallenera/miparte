using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Componentes;
using MiParte.Web.Hogares;

namespace MiParte.Web.Tests;

public class GastosPorFechasTests : BunitContext
{
    private static readonly Guid AnaId = Guid.NewGuid();
    private static readonly CategoriaDto Comida = new(Guid.NewGuid(), "Comida", null, null);
    private static readonly CategoriaDto Super = new(Guid.NewGuid(), "Supermercado", Comida.Id, null);
    private static readonly CategoriaDto Ocio = new(Guid.NewGuid(), "Ocio", null, null);
    private static readonly PerfilRepartoDto Perfil = new(Guid.NewGuid(), "Individual", "individual", []);

    private static GastoResponse Gasto(DateOnly fecha, decimal importe, CategoriaDto categoria, string concepto) =>
        new(Guid.NewGuid(), fecha, importe, categoria.Id, AnaId, Perfil.Id, concepto, null, [new RepartoGastoDto(AnaId, importe)]);

    private ApiFalsa Registrar(params GastoResponse[] gastos)
    {
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ApiFalsa.Miembro("Ana", rol: "admin", esYo: true, id: AnaId) })
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { Comida, Super, Ocio })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { Perfil })
            .Responde("GET /api/gastos", HttpStatusCode.OK, gastos)
            .Responde("GET /api/cierres-mes", HttpStatusCode.OK, Array.Empty<MesCerradoDto>())
            .Responde("GET /api/cuenta-comun", HttpStatusCode.NotFound);
        var cliente = new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });
        var hogar = new EstadoHogar(new AlmacenMemoria());
        Services.AddSingleton(cliente);
        Services.AddSingleton(hogar);
        PermisosDePrueba.Registrar(Services, cliente, hogar);
        Services.AddSingleton<ServicioAvisos>();
        return api;
    }

    private IRenderedComponent<VistaGastos> Abrir(Guid? categoria = null) => Render<VistaGastos>(p => p
        .Add(x => x.DesdeInicial, new DateOnly(2026, 8, 1))
        .Add(x => x.HastaInicial, new DateOnly(2026, 10, 31))
        .Add(x => x.CategoriaInicial, categoria));

    [Fact]
    public void Con_desde_y_hasta_pide_el_rango_y_lista_los_gastos_de_varios_meses()
    {
        var api = Registrar(
            Gasto(new DateOnly(2026, 8, 3), 10m, Ocio, "Cine"),
            Gasto(new DateOnly(2026, 10, 9), 20m, Ocio, "Teatro"));

        var c = Abrir();

        Assert.Contains("/api/gastos?desde=2026-08-01&hasta=2026-10-31", api.Consultas);
        Assert.Contains("Cine", c.Markup);
        Assert.Contains("Teatro", c.Markup);
        Assert.Contains("Total del periodo", c.Markup);
        Assert.Equal("2026-08-01", c.FindAll("input[type=date]")[0].GetAttribute("value"));
    }

    [Fact]
    public void Filtrar_por_categoria_incluye_las_subcategorias_y_deja_fuera_las_demas()
    {
        Registrar(
            Gasto(new DateOnly(2026, 8, 3), 10m, Comida, "Fruta"),
            Gasto(new DateOnly(2026, 9, 3), 30m, Super, "Compra semanal"),
            Gasto(new DateOnly(2026, 10, 9), 20m, Ocio, "Teatro"));

        var c = Abrir(Comida.Id);

        Assert.Contains("Fruta", c.Markup);
        Assert.Contains("Compra semanal", c.Markup);
        Assert.DoesNotContain("Teatro", c.Markup);
        Assert.Contains("40,00", c.Find("p.muted").TextContent); // total filtrado: 10 + 30
    }

    [Fact]
    public void Se_puede_editar_un_gasto_desde_la_lista_por_fechas_y_el_formulario_sube()
    {
        var gasto = Gasto(new DateOnly(2026, 9, 3), 30m, Super, "Compra semanal");
        var api = Registrar(gasto)
            .Responde("PUT /api/gastos/" + gasto.Id, HttpStatusCode.OK, gasto with { Importe = 35m });
        var c = Abrir(Comida.Id);

        c.Find("button[aria-label^='Editar gasto']").Click();

        Assert.Contains("Editar gasto", c.Find("form.addcat.editando").TextContent);
        c.Find("form.addcat input[inputmode=decimal]").Input("35,00");
        c.Find("form.addcat").Submit();
        Assert.Contains("\"importe\":35", api.Cuerpos["PUT /api/gastos/" + gasto.Id]);
        Assert.Contains("/api/gastos?desde=2026-08-01&hasta=2026-10-31", api.Consultas.TakeLast(2)); // recarga el mismo rango
    }

    [Fact]
    public void Con_pagador_solo_lista_lo_que_paga_esa_persona_y_no_lo_que_asume()
    {
        var luis = Guid.NewGuid();
        var pagadoPorLuisYCompartido = Gasto(new DateOnly(2026, 9, 3), 40m, Ocio, "Cena") with
        {
            PagadoPor = luis, Repartos = [new RepartoGastoDto(luis, 20m), new RepartoGastoDto(AnaId, 20m)],
        };
        var api = Registrar(Gasto(new DateOnly(2026, 9, 3), 10m, Ocio, "Cine"), pagadoPorLuisYCompartido);

        var c = Render<VistaGastos>(p => p
            .Add(x => x.DesdeInicial, new DateOnly(2026, 8, 1)).Add(x => x.HastaInicial, new DateOnly(2026, 10, 31))
            .Add(x => x.PagadorInicial, AnaId));

        Assert.Contains("Cine", c.Markup);
        Assert.DoesNotContain("Cena", c.Markup); // Ana solo asume una parte
        Assert.Contains("Solo lo que paga", c.Markup);
        Assert.DoesNotContain(api.Consultas, q => q.Contains("miembroId="));
    }

    [Fact]
    public void Con_cuenta_comun_solo_lista_lo_que_no_paga_ninguna_persona()
    {
        var delaCuenta = Gasto(new DateOnly(2026, 9, 3), 40m, Ocio, "Hipoteca") with { PagadoPor = null, Repartos = [] };
        Registrar(Gasto(new DateOnly(2026, 9, 3), 10m, Ocio, "Cine"), delaCuenta);

        var c = Render<VistaGastos>(p => p
            .Add(x => x.DesdeInicial, new DateOnly(2026, 8, 1)).Add(x => x.HastaInicial, new DateOnly(2026, 10, 31))
            .Add(x => x.CuentaComunInicial, true));

        Assert.Contains("Hipoteca", c.Markup);
        Assert.DoesNotContain("Cine", c.Markup);
        Assert.Contains("cuenta común o el ahorro", c.Markup);
    }

    [Fact]
    public void Cambiar_las_fechas_vuelve_a_pedir_los_gastos()
    {
        var api = Registrar();
        var c = Abrir();

        c.FindAll("input[type=date]")[0].Change("2026-09-15");

        Assert.Contains("/api/gastos?desde=2026-09-15&hasta=2026-10-31", api.Consultas);
    }

    [Fact]
    public void Un_rango_invertido_avisa_y_no_pide_datos()
    {
        var api = Registrar();
        var c = Abrir();
        var antes = api.Consultas.Count;

        c.FindAll("input[type=date]")[0].Change("2026-12-01");

        Assert.Contains("no puede ser posterior", c.Markup);
        Assert.Equal(antes, api.Consultas.Count);
    }

    [Fact]
    public void Un_gasto_de_un_mes_cerrado_no_se_puede_editar_ni_borrar()
    {
        var api = Registrar(Gasto(new DateOnly(2026, 9, 3), 30m, Super, "Compra semanal"), Gasto(new DateOnly(2026, 10, 3), 5m, Ocio, "Café"))
            .Responde("GET /api/cierres-mes", HttpStatusCode.OK,
                new[] { new MesCerradoDto("2026-09", new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), "Ana") });

        var c = Abrir();

        Assert.Equal(1, c.FindAll("button[aria-label^='Editar gasto']").Count); // solo el de octubre
        Assert.Contains("meses cerrados", c.Markup);
    }

    [Fact]
    public void Se_puede_pasar_de_por_mes_a_entre_fechas_conservando_el_mes()
    {
        var api = Registrar();
        var c = Render<VistaGastos>();

        c.FindAll("button.chip-btn").First(b => b.TextContent == "Entre fechas").Click();

        var hoy = DateTime.Today;
        var inicio = new DateOnly(hoy.Year, hoy.Month, 1);
        Assert.Equal(inicio.ToString("yyyy-MM-dd"), c.FindAll("input[type=date]")[0].GetAttribute("value"));
        Assert.Contains($"/api/gastos?desde={inicio:yyyy-MM-dd}&hasta={inicio.AddMonths(1).AddDays(-1):yyyy-MM-dd}", api.Consultas);
    }
}
