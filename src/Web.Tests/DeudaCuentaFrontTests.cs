using System.Net;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Hogares;
using MiParte.Web.Modelos;
using MiParte.Web.Reparto;

namespace MiParte.Web.Tests;

/// <summary>
/// Lo que la web hace con la deuda de las personas con la cuenta común (textos del extracto, CSV, formulario) y con la activación de
/// la lista de la compra del hogar.
/// </summary>
public class DeudaCuentaFrontTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 15);
    private static readonly Guid AnaId = Guid.NewGuid(), LuisId = Guid.NewGuid();

    private static MovimientosCuentaComunResponse Extracto() => new(
        new DateOnly(2026, 10, 1), Hoy, 0m, 0m, 0m, 0m,
        [
            new("adelanto_persona", null, new DateOnly(2026, 10, 2), -40m, AnaId, null, null, 0m, 560m),
            new("adelanto_persona_ahorro", null, new DateOnly(2026, 10, 3), -15m, LuisId, null, null, 0m, 545m),
            new("devolucion_deuda", null, new DateOnly(2026, 10, 4), 25m, AnaId, null, null, 0m, 570m),
            new("devolucion_deuda_ahorro", null, new DateOnly(2026, 10, 5), 5m, LuisId, null, null, 0m, 575m),
            new("compensacion_deuda", null, new DateOnly(2026, 10, 6), 0m, AnaId, null, null, 0m, 575m),
        ], EfectivoFinal: 575m, AhorroFinal: 0m);

    private static CoreApiClient Cliente(ApiFalsa api) => new(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });

    private static async Task<ModeloMovimientosCuenta> Movimientos()
    {
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ApiFalsa.Miembro("Ana", id: AnaId), ApiFalsa.Miembro("Luis", id: LuisId) })
            .Responde("GET /api/categorias", HttpStatusCode.OK, Array.Empty<CategoriaDto>())
            .Responde("GET /api/cuenta-comun/movimientos", HttpStatusCode.OK, Extracto());
        var m = new ModeloMovimientosCuenta(Cliente(api), Hoy);
        await m.CargarAsync();
        return m;
    }

    [Fact]
    public async Task El_extracto_describe_cada_movimiento_de_deuda_con_su_persona()
    {
        var m = await Movimientos();
        string Texto(string tipo) => m.Descripcion(m.Visibles.Single(v => v.Tipo == tipo));

        Assert.Equal("Adelanto de la cuenta por Ana", Texto("adelanto_persona"));
        Assert.Equal("Adelanto del ahorro por Luis", Texto("adelanto_persona_ahorro"));
        Assert.Equal("Devolución de Ana a la cuenta", Texto("devolucion_deuda"));
        Assert.Equal("Devolución de Luis al ahorro", Texto("devolucion_deuda_ahorro"));
        Assert.Equal("Compensación de la deuda de Ana", Texto("compensacion_deuda"));
    }

    [Theory]
    [InlineData("adelanto_persona_ahorro", true)]
    [InlineData("devolucion_deuda_ahorro", true)]
    [InlineData("adelanto_persona", false)]
    [InlineData("devolucion_deuda", false)]
    [InlineData("compensacion_deuda", false)]
    public async Task Solo_los_movimientos_de_deuda_del_ahorro_se_marcan_como_ahorro(string tipo, bool esAhorro)
    {
        var m = await Movimientos();

        Assert.Equal(esAhorro, ModeloMovimientosCuenta.EsAhorro(m.Visibles.Single(v => v.Tipo == tipo)));
    }

    [Fact]
    public async Task Los_adelantos_cuentan_como_salidas_las_devoluciones_como_entradas_y_la_compensacion_no_mueve_nada()
    {
        var m = await Movimientos();

        Assert.Equal(55m, m.SalidasVisibles);
        Assert.Equal(30m, m.EntradasVisibles);
    }

    [Theory]
    [InlineData("adelanto_persona", "Adelanto a persona")]
    [InlineData("adelanto_persona_ahorro", "Adelanto del ahorro a persona")]
    [InlineData("devolucion_deuda", "Devolución de deuda")]
    [InlineData("devolucion_deuda_ahorro", "Devolución de deuda al ahorro")]
    [InlineData("compensacion_deuda", "Compensación de deuda")]
    public void El_csv_nombra_cada_tipo_de_deuda(string tipo, string nombre)
        => Assert.Equal(nombre, CsvMovimientos.NombreTipo(tipo));

    [Fact]
    public void El_csv_incluye_las_lineas_de_deuda_con_su_signo()
    {
        var ana = ApiFalsa.Miembro("Ana", id: AnaId);

        var csv = CsvMovimientos.Generar(Extracto().Movimientos, m => $"Desc {m.Tipo}", [ana]);

        Assert.Contains("02/10/2026;Adelanto a persona;Desc adelanto_persona;Ana;;-40,00;;560,00", csv);
        Assert.Contains("04/10/2026;Devolución de deuda;Desc devolucion_deuda;Ana;;25,00;;570,00", csv);
    }

    // ----- Formulario de deuda de la pestaña Cuenta común -----

    private static (ModeloCuentaComun Modelo, ApiFalsa Api) Cuenta()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var luis = ApiFalsa.Miembro("Luis", id: LuisId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { luis, ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK,
                new CuentaComunResponse("2026-10", 0m, 0m, 0m, 0m, [], 0m, [], [], Deudas: [new DeudaCuentaDto(AnaId, "Ana", 40m)]))
            .Responde("POST /api/cuenta-comun/deudas", HttpStatusCode.Created,
                new MovimientoDeudaCuentaDto(Guid.NewGuid(), AnaId, Hoy, 10m, "devolucion", false, "Parte de la luz"));
        return (new ModeloCuentaComun(Cliente(api), Hoy), api);
    }

    [Fact]
    public async Task La_persona_de_la_deuda_empieza_siendo_quien_usa_la_app_y_el_tipo_un_adelanto()
    {
        var (m, _) = Cuenta();

        await m.CargarAsync();

        Assert.Equal(AnaId, m.Deudor);
        Assert.Equal("adelanto", m.TipoDeuda);
        Assert.False(m.DeudaDesdeAhorro);
        var deuda = Assert.Single(m.Estado!.Deudas!);
        Assert.Equal(40m, deuda.Importe);
    }

    [Theory]
    [InlineData("adelanto", "Adelanto registrado")]
    [InlineData("devolucion", "Devolución registrada")]
    [InlineData("compensacion", "Compensación registrada")]
    public async Task Cada_tipo_avisa_con_su_texto_y_limpia_el_formulario(string tipo, string aviso)
    {
        var (m, api) = Cuenta();
        await m.CargarAsync();
        m.TipoDeuda = tipo;
        m.ImporteDeuda = "10,50";
        m.ConceptoDeuda = "  Parte de la luz ";

        var resultado = await m.RegistrarDeudaAsync();

        Assert.Equal(aviso, resultado);
        var cuerpo = api.Cuerpos["POST /api/cuenta-comun/deudas"];
        Assert.Contains($"\"tipo\":\"{tipo}\"", cuerpo);
        Assert.Contains("\"importe\":10.5", cuerpo);
        Assert.Contains("\"concepto\":\"Parte de la luz\"", cuerpo);
        Assert.Equal("", m.ImporteDeuda);
        Assert.Equal("", m.ConceptoDeuda);
    }

    [Fact]
    public async Task Un_importe_con_mas_de_dos_decimales_no_se_envia()
    {
        var (m, api) = Cuenta();
        await m.CargarAsync();
        m.ImporteDeuda = "10,555";

        Assert.False(m.DeudaValida);
        var resultado = await m.RegistrarDeudaAsync();

        Assert.Null(resultado);
        Assert.NotNull(m.Error);
        Assert.DoesNotContain("POST /api/cuenta-comun/deudas", api.Recibidas);
    }

    [Fact]
    public async Task Un_error_de_la_api_queda_en_Error_y_conserva_lo_escrito()
    {
        var (m, api) = Cuenta();
        api.Error("POST /api/cuenta-comun/deudas", HttpStatusCode.Conflict, "El importe supera lo que debe la persona (40,00).");
        await m.CargarAsync();
        m.TipoDeuda = "devolucion";
        m.ImporteDeuda = "100";

        var resultado = await m.RegistrarDeudaAsync();

        Assert.Null(resultado);
        Assert.Contains("supera lo que debe", m.Error);
        Assert.Equal("100", m.ImporteDeuda);
        Assert.False(m.Enviando);
    }

    [Theory]
    [InlineData("adelanto", "Adelanto eliminado")]
    [InlineData("devolucion", "Devolución eliminado")]
    [InlineData("compensacion", "Compensación eliminado")]
    public async Task Borrar_un_movimiento_de_deuda_lo_envia_y_avisa(string tipo, string aviso)
    {
        var (m, api) = Cuenta();
        var id = Guid.NewGuid();
        api.Responde($"DELETE /api/cuenta-comun/deudas/{id}", HttpStatusCode.NoContent);
        await m.CargarAsync();
        m.ConfirmandoEliminar = id;

        var resultado = await m.EliminarDeudaAsync(new MovimientoDeudaCuentaDto(id, AnaId, Hoy, 10m, tipo, false, null));

        Assert.Equal(aviso, resultado);
        Assert.Contains($"DELETE /api/cuenta-comun/deudas/{id}", api.Recibidas);
        Assert.Null(m.ConfirmandoEliminar);
    }

    [Fact]
    public async Task Un_rechazo_al_borrar_una_deuda_queda_en_Error_y_no_cierra_la_confirmacion_a_medias()
    {
        var (m, api) = Cuenta();
        var id = Guid.NewGuid();
        api.Error($"DELETE /api/cuenta-comun/deudas/{id}", HttpStatusCode.Conflict, "No se puede borrar este adelanto.");
        await m.CargarAsync();
        m.ConfirmandoEliminar = id;

        var resultado = await m.EliminarDeudaAsync(new MovimientoDeudaCuentaDto(id, AnaId, Hoy, 10m, "adelanto", false, null));

        Assert.Null(resultado);
        Assert.Contains("No se puede borrar", m.Error);
        Assert.False(m.Enviando);
    }

    [Theory]
    [InlineData("adelanto", "Adelanto")]
    [InlineData("devolucion", "Devolución")]
    [InlineData("compensacion", "Compensación")]
    [InlineData("otro", "Adelanto")]
    public void Los_tipos_de_deuda_tienen_nombre_visible(string tipo, string nombre)
        => Assert.Equal(nombre, ModeloCuentaComun.NombreTipoDeuda(tipo));

    // ----- Activación de la lista de la compra en el estado del hogar -----

    [Fact]
    public async Task Activar_la_lista_cambia_el_hogar_actual_y_avisa_a_quien_escucha()
    {
        var casa = new HogarResumen(Guid.NewGuid(), "Casa");
        var estado = new EstadoHogar(new AlmacenMemoria());
        await estado.AplicarAsync(new YoResponse("u1", [casa], null));
        var avisos = 0;
        estado.Cambiado += () => avisos++;
        Assert.False(estado.HogarActual!.ListaCompraActiva);

        estado.ActualizarListaCompra(true);

        Assert.True(estado.HogarActual!.ListaCompraActiva);
        Assert.True(estado.Hogares.Single().ListaCompraActiva);
        Assert.Equal(1, avisos);

        estado.ActualizarListaCompra(false);
        Assert.False(estado.HogarActual!.ListaCompraActiva);
    }

    [Fact]
    public void Sin_hogar_actual_activar_la_lista_no_hace_nada()
    {
        var estado = new EstadoHogar(new AlmacenMemoria());
        var avisos = 0;
        estado.Cambiado += () => avisos++;

        estado.ActualizarListaCompra(true);

        Assert.Null(estado.HogarActual);
        Assert.Equal(0, avisos);
    }
}
