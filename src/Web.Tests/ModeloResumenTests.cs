using System.Net;
using MiParte.Contracts;
using MiParte.Web.Modelos;
using MiParte.Web.Api;

namespace MiParte.Web.Tests;

/// <summary>Orquestación de la pestaña Resumen probada sin renderizar componentes.</summary>
public class ModeloResumenTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 15);
    private readonly Guid _ana = Guid.NewGuid(), _marcos = Guid.NewGuid();

    private ResumenYLiquidacionResponse Datos(decimal pendiente = 50m)
    {
        var resumen = new ResumenMensualResponse("2026-10", 100m, [], [], null, 0m);
        var liquidacion = new LiquidacionResponse(
            "2026-10",
            [new SaldoMiembroDto(_ana, "Ana", pendiente), new SaldoMiembroDto(_marcos, "Marcos", -pendiente)],
            [new TransferenciaDto(_marcos, _ana, pendiente)],
            []);
        return new ResumenYLiquidacionResponse(resumen, liquidacion);
    }

    private (ModeloResumen Modelo, ApiFalsa Api) Crear(ResumenYLiquidacionResponse? datos = null)
    {
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ApiFalsa.Miembro("Ana", "admin", esYo: true, id: _ana), ApiFalsa.Miembro("Marcos", id: _marcos) })
            .Responde("GET /api/categorias", HttpStatusCode.OK, Array.Empty<CategoriaDto>())
            .Responde("GET /api/resumen-liquidacion", HttpStatusCode.OK, datos ?? Datos())
            .Responde("GET /api/cierres-mes", HttpStatusCode.OK, Array.Empty<MesCerradoDto>());
        var cliente = new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });
        return (new ModeloResumen(cliente, Hoy), api);
    }

    private static TransferenciaDto Transferencia(ResumenYLiquidacionResponse d) => d.Liquidacion.Transferencias.Single();

    [Fact]
    public async Task Cargar_pide_resumen_y_liquidacion_juntos_para_el_mes_actual()
    {
        var (m, api) = Crear();

        await m.CargarAsync();

        Assert.False(m.Cargando);
        Assert.Contains("/api/resumen-liquidacion?mes=2026-10", api.Consultas);
        Assert.Equal(100m, m.Resumen!.GastosTotales);
        Assert.Equal(2, m.Miembros.Count);
    }

    [Fact]
    public async Task Si_fallan_las_categorias_o_los_cierres_el_resumen_se_sigue_viendo()
    {
        var (m, api) = Crear();
        api.Error("GET /api/categorias", HttpStatusCode.InternalServerError, "x");
        api.Error("GET /api/cierres-mes", HttpStatusCode.InternalServerError, "x");

        await m.CargarAsync();

        Assert.Null(m.ErrorCarga);
        Assert.NotNull(m.Resumen);
        Assert.Empty(m.Categorias);
        Assert.Null(m.CierreDelMes);
    }

    [Fact]
    public async Task El_importe_sugerido_es_el_pendiente_y_se_puede_editar_hasta_ese_maximo()
    {
        var datos = Datos(50m);
        var (m, _) = Crear(datos);
        await m.CargarAsync();
        var t = Transferencia(datos);

        Assert.Equal("50,00", m.ImporteEscrito(t).Replace('.', ','));
        Assert.True(m.ImporteValido(t));

        m.EscribirImporte(t, "20,5");
        Assert.True(m.ImporteValido(t));
        m.EscribirImporte(t, "50,01");
        Assert.False(m.ImporteValido(t));
        m.EscribirImporte(t, "1,230");
        Assert.False(m.ImporteValido(t)); // tres decimales aunque valga lo mismo que 1,23
        m.EscribirImporte(t, "0");
        Assert.False(m.ImporteValido(t));
    }

    [Fact]
    public async Task Registrar_un_pago_parcial_lo_avisa_y_recarga()
    {
        var datos = Datos(50m);
        var (m, api) = Crear(datos);
        api.Responde("POST /api/pagos-liquidacion", HttpStatusCode.Created,
            new PagoLiquidacionDto(Guid.NewGuid(), Hoy, _marcos, _ana, 20m, Hoy, null));
        await m.CargarAsync();
        var t = Transferencia(datos);
        m.EscribirImporte(t, "20");

        var aviso = await m.RegistrarPagoAsync(t);

        Assert.Equal("Pago parcial registrado", aviso);
        Assert.Equal(2, api.Recibidas.Count(r => r == "GET /api/resumen-liquidacion"));
        Assert.Equal("50,00", m.ImporteEscrito(t).Replace('.', ',')); // el campo vuelve al sugerido
    }

    [Fact]
    public async Task Registrar_el_importe_completo_avisa_pago_registrado()
    {
        var datos = Datos(50m);
        var (m, api) = Crear(datos);
        api.Responde("POST /api/pagos-liquidacion", HttpStatusCode.Created,
            new PagoLiquidacionDto(Guid.NewGuid(), Hoy, _marcos, _ana, 50m, Hoy, null));
        await m.CargarAsync();

        Assert.Equal("Pago registrado", await m.RegistrarPagoAsync(Transferencia(datos)));
    }

    [Fact]
    public async Task Un_importe_invalido_no_llega_a_la_api()
    {
        var datos = Datos(50m);
        var (m, api) = Crear(datos);
        await m.CargarAsync();
        var t = Transferencia(datos);
        m.EscribirImporte(t, "999");

        var aviso = await m.RegistrarPagoAsync(t);

        Assert.Null(aviso);
        Assert.Contains("sin pasar de lo pendiente", m.Error);
        Assert.DoesNotContain("POST /api/pagos-liquidacion", api.Recibidas);
        Assert.False(m.Guardando);
    }

    [Fact]
    public async Task Cambiar_de_mes_pide_el_nuevo_y_descarta_los_importes_escritos()
    {
        var datos = Datos(50m);
        var (m, api) = Crear(datos);
        await m.CargarAsync();
        var t = Transferencia(datos);
        m.EscribirImporte(t, "10");

        await m.CambiarMesAsync(-1);

        Assert.Contains("/api/resumen-liquidacion?mes=2026-09", api.Consultas);
        Assert.Equal("50,00", m.ImporteEscrito(t).Replace('.', ','));
        Assert.Equal("septiembre 2026", m.NombreMes);
    }

    [Fact]
    public async Task El_filtro_de_persona_solo_deja_lo_que_afecta_a_esa_persona()
    {
        var (m, _) = Crear();
        await m.CargarAsync();
        Assert.True(m.FiltraMiembro(_ana));
        Assert.True(m.FiltraMiembro(_marcos));

        m.FiltroMiembro = _ana.ToString();

        Assert.True(m.FiltraMiembro(_ana));
        Assert.False(m.FiltraMiembro(_marcos));
    }

    [Fact]
    public async Task Las_categorias_madre_se_despliegan_y_contraen()
    {
        var padre = new CategoriaDto(Guid.NewGuid(), "Comida", null, null);
        var hija = new CategoriaDto(Guid.NewGuid(), "Super", padre.Id, null);
        var resumen = new ResumenMensualResponse(
            "2026-10", 30m, [],
            [new ResumenCategoriaDto(padre.Id, "Comida", 10m, []), new ResumenCategoriaDto(hija.Id, "Super", 20m, [])], null, 0m);
        var (m, api) = Crear(new ResumenYLiquidacionResponse(resumen, Datos().Liquidacion));
        api.Responde("GET /api/categorias", HttpStatusCode.OK, new[] { padre, hija });
        await m.CargarAsync();

        var cerrada = m.FilasCategorias().ToList();
        Assert.Single(cerrada);
        Assert.Equal(30m, cerrada[0].Nodo.Total); // la madre acumula lo suyo y lo de su hija

        m.Alternar(padre.Id);
        var abierta = m.FilasCategorias().ToList();
        Assert.Equal(3, abierta.Count); // madre, su gasto directo y la hija
        Assert.Contains(abierta, f => f.Directo);

        m.Alternar(padre.Id);
        Assert.Single(m.FilasCategorias());
    }

    [Theory]
    [InlineData(25, 100, "25")]
    [InlineData(1, 3, "33")]
    public async Task El_porcentaje_se_redondea_a_entero_sobre_el_total_del_mes(double parte, double total, string esperado)
    {
        var resumen = new ResumenMensualResponse("2026-10", (decimal)total, [], [], null, 0m);
        var (m, _) = Crear(new ResumenYLiquidacionResponse(resumen, Datos().Liquidacion));
        await m.CargarAsync();

        Assert.Equal(esperado, m.Porcentaje((decimal)parte));
    }
}
