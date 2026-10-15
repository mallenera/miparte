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
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, estado);
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
    public async Task Un_error_de_la_api_al_retirar_queda_en_Error_y_cancela_la_confirmacion()
    {
        var (m, api) = Crear(Estado());
        api.Error("POST /api/cuenta-comun/retiradas-ahorro", HttpStatusCode.Conflict, "El importe supera el ahorro disponible (100,00).");
        await m.CargarAsync();
        m.ImporteRetirada = "500";
        m.ConfirmandoEliminar = Guid.NewGuid();

        var aviso = await m.RetirarAhorroAsync();

        Assert.Null(aviso);
        Assert.Contains("ahorro disponible", m.Error);
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
}
