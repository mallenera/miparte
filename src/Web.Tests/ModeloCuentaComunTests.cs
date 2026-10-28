using System.Net;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Modelos;

namespace MiParte.Web.Tests;

/// <summary>Orquestación de la pestaña Cuenta común probada sin renderizar componentes.</summary>
public class ModeloCuentaComunTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 15);
    private readonly MiembroDto _ana = ApiFalsa.Miembro("Ana", "admin", esYo: true);
    private readonly MiembroDto _marcos = ApiFalsa.Miembro("Marcos");

    private CuentaComunResponse Estado(
        bool ahorroActivo = true, decimal ahorroDisponible = 100m, IReadOnlyList<PendienteCuentaDto>? pendientes = null,
        IReadOnlyList<AportacionCuentaDto>? aportaciones = null) => new(
        "2026-10", 250m, 500m, 100m, 400m, pendientes ?? [], 400m, aportaciones ?? [], [],
        AhorroDisponible: ahorroDisponible, Activa: true, AhorroActivo: ahorroActivo);

    private (ModeloCuentaComun Modelo, ApiFalsa Api) Crear(CuentaComunResponse estado)
    {
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { _ana, _marcos })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, estado)
            .Responde("GET /api/cuenta-comun/ingresos", HttpStatusCode.OK, Array.Empty<IngresoCuentaComunDto>());
        var cliente = new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });
        return (new ModeloCuentaComun(cliente, Hoy), api);
    }

    [Fact]
    public async Task Cargar_deja_a_quien_usa_la_app_como_aportante_y_pide_el_mes_actual()
    {
        var (m, api) = Crear(Estado());

        await m.CargarAsync();

        Assert.False(m.Cargando);
        Assert.Equal(_ana.Id, m.Aportante);
        Assert.Contains("/api/cuenta-comun?mes=2026-10", api.Consultas);
    }

    [Fact]
    public async Task El_importe_y_el_ahorro_vigentes_se_muestran_al_elegir_adulto()
    {
        var aportaciones = new[]
        {
            new AportacionCuentaDto(Guid.NewGuid(), Guid.Empty, new DateOnly(2026, 1, 1), 100m, 10m),
        };
        var ana = _ana.Id;
        var vigentes = new[]
        {
            new AportacionCuentaDto(Guid.NewGuid(), ana, new DateOnly(2026, 1, 1), 100m, 10m),
            new AportacionCuentaDto(Guid.NewGuid(), ana, new DateOnly(2026, 9, 1), 300m, 50m),
            new AportacionCuentaDto(Guid.NewGuid(), ana, new DateOnly(2026, 11, 1), 999m, 0m), // futura: no vale aún
        };
        var (m, _) = Crear(Estado(aportaciones: vigentes));
        await m.CargarAsync();

        Assert.Equal(300m, m.VigenteDe(ana));
        Assert.Equal(50m, m.AhorroVigenteDe(ana));
        Assert.Equal("300", m.ImporteAportacion);
        Assert.Equal("50", m.AhorroAportacion);
        Assert.NotEmpty(aportaciones);
    }

    [Theory]
    [InlineData("100", "30", true)]
    [InlineData("100", "100", true)]
    [InlineData("100", "100,01", false)]
    [InlineData("0", "", true)]
    [InlineData("-1", "", false)]
    [InlineData("100", "abc", false)]
    public async Task La_aportacion_es_valida_si_el_ahorro_cabe_en_el_importe(string importe, string ahorro, bool valida)
    {
        var (m, _) = Crear(Estado());
        await m.CargarAsync();

        m.ImporteAportacion = importe;
        m.AhorroAportacion = ahorro;

        Assert.Equal(valida, m.AportacionValida);
    }

    [Fact]
    public async Task Sin_ahorro_activo_el_campo_de_ahorro_no_cuenta_para_validar()
    {
        var (m, _) = Crear(Estado(ahorroActivo: false));
        await m.CargarAsync();
        m.ImporteAportacion = "100";
        m.AhorroAportacion = "basura";

        Assert.True(m.AportacionValida);
    }

    [Fact]
    public async Task Fijar_una_aportacion_envia_el_mes_mostrado_y_avisa()
    {
        var (m, api) = Crear(Estado());
        api.Responde("PUT /api/cuenta-comun/aportaciones", HttpStatusCode.OK, new AportacionCuentaDto(Guid.NewGuid(), _ana.Id, Hoy, 200m, 20m));
        await m.CargarAsync();
        m.ImporteAportacion = "200";
        m.AhorroAportacion = "20";

        var aviso = await m.FijarAportacionAsync();

        Assert.Equal("Aportación guardada", aviso);
        var cuerpo = api.Cuerpos["PUT /api/cuenta-comun/aportaciones"];
        Assert.Contains("\"desde\":\"2026-10-01\"", cuerpo);
        Assert.Contains("\"ahorro\":20", cuerpo);
    }

    [Fact]
    public async Task Un_ahorro_mayor_que_la_aportacion_no_llega_a_la_api()
    {
        var (m, api) = Crear(Estado());
        await m.CargarAsync();
        m.ImporteAportacion = "100";
        m.AhorroAportacion = "150";

        var aviso = await m.FijarAportacionAsync();

        Assert.Null(aviso);
        Assert.Contains("ahorro debe estar entre 0 y la aportación", m.Error);
        Assert.DoesNotContain("PUT /api/cuenta-comun/aportaciones", api.Recibidas);
    }

    [Fact]
    public async Task Fijar_el_saldo_inicial_envia_fecha_e_importe_con_coma_decimal()
    {
        var (m, api) = Crear(Estado());
        api.Responde("PUT /api/cuenta-comun/saldo-inicial", HttpStatusCode.OK, new SaldoInicialDto(new DateOnly(2026, 9, 30), 1200.5m));
        await m.CargarAsync();
        m.FechaSaldoInicial = new DateOnly(2026, 9, 30);
        m.ImporteSaldoInicial = "1200,50";

        var aviso = await m.FijarSaldoInicialAsync();

        Assert.Equal("Saldo inicial guardado", aviso);
        var cuerpo = api.Cuerpos["PUT /api/cuenta-comun/saldo-inicial"];
        Assert.Contains("\"fecha\":\"2026-09-30\"", cuerpo);
        Assert.Contains("\"importe\":1200.5", cuerpo);
    }

    [Fact]
    public async Task Un_saldo_inicial_negativo_no_llega_a_la_api()
    {
        var (m, api) = Crear(Estado());
        await m.CargarAsync();
        m.ImporteSaldoInicial = "-5";

        Assert.False(m.SaldoInicialValido);
        var aviso = await m.FijarSaldoInicialAsync();

        Assert.Null(aviso);
        Assert.Contains("máximo 2 decimales", m.Error);
        Assert.DoesNotContain("PUT /api/cuenta-comun/saldo-inicial", api.Recibidas);
    }

    [Fact]
    public async Task El_formulario_del_saldo_inicial_muestra_el_que_hay()
    {
        var estado = Estado() with { SaldoInicial = new SaldoInicialDto(new DateOnly(2026, 6, 1), 800m) };
        var (m, _) = Crear(estado);

        await m.CargarAsync();

        Assert.Equal(new DateOnly(2026, 6, 1), m.FechaSaldoInicial);
        Assert.Equal("800", m.ImporteSaldoInicial);
        Assert.True(m.SaldoInicialValido);
    }

    [Fact]
    public async Task Reembolsar_del_fondo_del_ahorro_lo_envia_con_su_fondo_y_no_mezcla_fondos()
    {
        var deHogar = new PendienteCuentaDto(_marcos.Id, "Marcos", 40m);
        var delAhorro = new PendienteCuentaDto(_marcos.Id, "Marcos", 60m, DesdeAhorro: true);
        var (m, api) = Crear(Estado(pendientes: [deHogar, delAhorro]));
        api.Responde("POST /api/cuenta-comun/reembolsos", HttpStatusCode.Created,
            new ReembolsoCuentaDto(Guid.NewGuid(), _marcos.Id, Hoy, 60m, null, true));
        await m.CargarAsync();
        Assert.Equal(ModeloCuentaComun.ClaveDe(deHogar), m.ClavePendiente);

        m.ElegirPendiente(ModeloCuentaComun.ClaveDe(delAhorro));
        m.ImporteReembolso = "60";
        await m.ReembolsarAsync();

        Assert.Contains("\"desdeAhorro\":true", api.Cuerpos["POST /api/cuenta-comun/reembolsos"]);
    }

    [Fact]
    public async Task Reembolsar_envia_el_concepto_recortado_y_limpia_el_formulario()
    {
        var pendiente = new PendienteCuentaDto(_marcos.Id, "Marcos", 95m);
        var (m, api) = Crear(Estado(pendientes: [pendiente]));
        api.Responde("POST /api/cuenta-comun/reembolsos", HttpStatusCode.Created,
            new ReembolsoCuentaDto(Guid.NewGuid(), _marcos.Id, Hoy, 95m, "Lavadora"));
        await m.CargarAsync();
        Assert.Equal(_marcos.Id, m.Reembolsado); // el primero con pendiente
        m.ImporteReembolso = "95";
        m.ConceptoReembolso = "  Lavadora ";

        var aviso = await m.ReembolsarAsync();

        Assert.Equal("Reembolso registrado", aviso);
        Assert.Contains("\"concepto\":\"Lavadora\"", api.Cuerpos["POST /api/cuenta-comun/reembolsos"]);
        Assert.Equal("", m.ImporteReembolso);
    }

    [Fact]
    public async Task Registrar_una_devolucion_del_ahorro_envia_su_tipo_y_fondo_y_limpia_el_formulario()
    {
        var (m, api) = Crear(Estado());
        api.Responde("POST /api/cuenta-comun/deudas", HttpStatusCode.Created,
            new MovimientoDeudaCuentaDto(Guid.NewGuid(), _marcos.Id, Hoy, 25m, "devolucion", true, null));
        await m.CargarAsync();
        m.Deudor = _marcos.Id;
        m.TipoDeuda = "devolucion";
        m.DeudaDesdeAhorro = true;
        m.ImporteDeuda = "25";

        var aviso = await m.RegistrarDeudaAsync();

        Assert.Equal("Devolución registrado", aviso);
        Assert.Contains("\"tipo\":\"devolucion\"", api.Cuerpos["POST /api/cuenta-comun/deudas"]);
        Assert.Contains("\"desdeAhorro\":true", api.Cuerpos["POST /api/cuenta-comun/deudas"]);
        Assert.Equal("", m.ImporteDeuda);
    }

    [Fact]
    public async Task Un_error_de_la_api_al_ingresar_al_ahorro_queda_en_Error_y_cancela_la_confirmacion()
    {
        var (m, api) = Crear(Estado());
        api.Error("POST /api/cuenta-comun/depositos-ahorro", HttpStatusCode.Conflict, "El ahorro no está activado en este hogar.");
        await m.CargarAsync();
        m.ImporteDeposito = "500";
        m.ConfirmandoEliminar = Guid.NewGuid();

        var aviso = await m.IngresarAhorroAsync();

        Assert.Null(aviso);
        Assert.Contains("ahorro no está activado", m.Error);
        Assert.Null(m.ConfirmandoEliminar);
        Assert.False(m.Enviando);
    }

    [Fact]
    public async Task Cambiar_de_mes_pide_el_mes_nuevo_y_los_errores_no_rompen_la_pantalla()
    {
        var (m, api) = Crear(Estado());
        await m.CargarAsync();

        await m.CambiarMesAsync(-1);
        Assert.Contains("/api/cuenta-comun?mes=2026-09", api.Consultas);
        Assert.Equal("septiembre 2026", m.NombreMes);

        api.Error("GET /api/cuenta-comun", HttpStatusCode.InternalServerError, "Caído");
        await m.CambiarMesAsync(1);
        Assert.Contains("Caído", m.Error);
    }

    [Fact]
    public void Porcentaje_admite_hasta_dos_decimales()
    {
        Assert.Equal("33,33 %", ModeloCuentaComun.Porcentaje(33.333m));
        Assert.Equal("50 %", ModeloCuentaComun.Porcentaje(50m));
    }

    [Fact]
    public async Task Ingresar_envia_el_miembro_y_el_concepto_y_limpia_el_formulario()
    {
        var (m, api) = Crear(Estado());
        api.Responde("POST /api/cuenta-comun/ingresos", HttpStatusCode.Created,
            new IngresoCuentaComunDto(Guid.NewGuid(), _marcos.Id, Hoy, 40m, "Cena amigas"));
        await m.CargarAsync();
        m.Ingresante = _marcos.Id;
        m.ImporteIngreso = "40";
        m.ConceptoIngreso = "  Cena amigas ";

        var aviso = await m.IngresarAsync();

        Assert.Equal("Ingreso registrado", aviso);
        var cuerpo = api.Cuerpos["POST /api/cuenta-comun/ingresos"];
        Assert.Contains($"\"miembroId\":\"{_marcos.Id}\"", cuerpo);
        Assert.Contains("\"concepto\":\"Cena amigas\"", cuerpo);
        Assert.DoesNotContain("\"fecha\":\"", cuerpo); // el mes actual: lo fecha la API (hoy)
        Assert.Equal("", m.ImporteIngreso);
        Assert.Equal("", m.ConceptoIngreso);
    }

    [Fact]
    public async Task Ingresar_en_otro_mes_lo_fecha_el_primer_dia_de_ese_mes()
    {
        var (m, api) = Crear(Estado());
        api.Responde("POST /api/cuenta-comun/ingresos", HttpStatusCode.Created,
            new IngresoCuentaComunDto(Guid.NewGuid(), _ana.Id, new DateOnly(2026, 9, 1), 40m, null));
        await m.CargarAsync();
        await m.CambiarMesAsync(-1);
        m.ImporteIngreso = "40";

        await m.IngresarAsync();

        Assert.Contains("\"fecha\":\"2026-09-01\"", api.Cuerpos["POST /api/cuenta-comun/ingresos"]);
    }

    [Fact]
    public async Task IngresosDelMes_solo_muestra_los_del_mes_mostrado_y_suma_su_total()
    {
        var (m, api) = Crear(Estado());
        api.Responde("GET /api/cuenta-comun/ingresos", HttpStatusCode.OK, new[]
        {
            new IngresoCuentaComunDto(Guid.NewGuid(), _ana.Id, new DateOnly(2026, 10, 3), 40m, "Cena amigas"),
            new IngresoCuentaComunDto(Guid.NewGuid(), _marcos.Id, new DateOnly(2026, 10, 20), 15m, null),
            new IngresoCuentaComunDto(Guid.NewGuid(), _ana.Id, new DateOnly(2026, 9, 28), 99m, "Otro mes"),
        });

        await m.CargarAsync();

        Assert.Equal(2, m.IngresosDelMes.Count);
        Assert.Equal(55m, m.TotalIngresosDelMes);
    }

    [Fact]
    public async Task Eliminar_un_ingreso_recarga_y_avisa()
    {
        var ingreso = new IngresoCuentaComunDto(Guid.NewGuid(), _ana.Id, Hoy, 40m, "Cena amigas");
        var (m, api) = Crear(Estado());
        api.Responde($"DELETE /api/cuenta-comun/ingresos/{ingreso.Id}", HttpStatusCode.NoContent);
        await m.CargarAsync();

        var aviso = await m.EliminarIngresoAsync(ingreso);

        Assert.Equal("Ingreso eliminado", aviso);
        Assert.Contains($"DELETE /api/cuenta-comun/ingresos/{ingreso.Id}", api.Recibidas);
    }
}
