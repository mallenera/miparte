using System.Net;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Hogares;
using MiParte.Web.Modelos;

namespace MiParte.Web.Tests;

/// <summary>Orquestación de la configuración del hogar probada sin renderizar componentes.</summary>
public class ModeloConfiguracionHogarTests
{
    private readonly MiembroDto _ana = ApiFalsa.Miembro("Ana", "admin", esYo: true);
    private readonly MiembroDto _marcos = ApiFalsa.Miembro("Marcos", vinculado: false);

    private (ModeloConfiguracionHogar Modelo, ApiFalsa Api, EstadoHogar Hogar) Crear(bool cuenta = false, bool ahorro = false)
    {
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { _ana, _marcos })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, Array.Empty<PerfilRepartoDto>());
        var cliente = new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });
        var hogar = new EstadoHogar(new AlmacenMemoria());
        var resumen = new HogarResumen(Guid.NewGuid(), "Casa", cuenta, ahorro);
        hogar.AplicarAsync(new YoResponse(Guid.NewGuid().ToString(), [resumen], resumen)).GetAwaiter().GetResult();
        var permisos = new ServicioPermisos(cliente, hogar);
        return (new ModeloConfiguracionHogar(cliente, hogar, permisos, new ServicioArranque(cliente, hogar)), api, hogar);
    }

    [Fact]
    public async Task Cargar_trae_miembros_y_perfiles()
    {
        var (m, _, _) = Crear();

        await m.CargarAsync();

        Assert.False(m.Cargando);
        Assert.Equal(2, m.Miembros.Count);
        Assert.Equal(2, m.Adultos.Count());
        Assert.Equal("Ana", m.NombreDe(_ana.Id));
        Assert.Equal("—", m.NombreDe(Guid.NewGuid()));
    }

    [Fact]
    public async Task Habilitar_la_cuenta_comun_actualiza_el_hogar_y_cambia_la_clave_de_la_casilla()
    {
        var (m, api, hogar) = Crear();
        api.Responde("PUT /api/cuenta-comun/activacion", HttpStatusCode.OK, new { activa = true });
        await m.CargarAsync();
        var clave = m.ClaveCuenta;

        var aviso = await m.CambiarCuentaComunAsync(true);

        Assert.Equal("Cuenta común habilitada", aviso);
        Assert.True(hogar.HogarActual!.CuentaComunActiva);
        Assert.NotEqual(clave, m.ClaveCuenta);
        Assert.False(m.GuardandoFunciones);
    }

    [Fact]
    public async Task Si_la_api_rechaza_el_cambio_de_funciones_el_hogar_no_cambia_pero_la_casilla_se_recrea()
    {
        var (m, api, hogar) = Crear();
        api.Error("PUT /api/cuenta-comun/activacion", HttpStatusCode.Forbidden, "No tienes permiso.");
        await m.CargarAsync();
        var clave = m.ClaveCuenta;

        var aviso = await m.CambiarCuentaComunAsync(true);

        Assert.Null(aviso);
        Assert.Equal("No tienes permiso.", m.ErrorFunciones);
        Assert.False(hogar.HogarActual!.CuentaComunActiva);
        Assert.NotEqual(clave, m.ClaveCuenta);
    }

    [Fact]
    public async Task Activar_el_ahorro_conserva_el_estado_de_la_cuenta_comun()
    {
        var (m, api, hogar) = Crear(cuenta: true);
        api.Responde("PUT /api/cuenta-comun/ahorro/activacion", HttpStatusCode.OK, new { activo = true });
        await m.CargarAsync();

        await m.CambiarAhorroAsync(true);

        Assert.True(hogar.HogarActual!.CuentaComunActiva);
        Assert.True(hogar.HogarActual.AhorroActivo);
    }

    [Fact]
    public async Task Activar_la_lista_de_la_compra_la_guarda_y_no_toca_las_demas_funciones()
    {
        var (m, api, hogar) = Crear(cuenta: true);
        api.Responde("PUT /api/lista-compra/activacion", HttpStatusCode.OK, new { activa = true });
        await m.CargarAsync();
        Assert.False(m.ListaCompraActiva);
        var clave = m.ClaveListaCompra;

        var aviso = await m.CambiarListaCompraAsync(true);

        Assert.Equal("Lista de la compra activada", aviso);
        Assert.Equal("{\"activa\":true}", api.Cuerpos["PUT /api/lista-compra/activacion"]);
        Assert.True(hogar.HogarActual!.ListaCompraActiva);
        Assert.True(hogar.HogarActual.CuentaComunActiva);
        Assert.NotEqual(clave, m.ClaveListaCompra);
    }

    [Fact]
    public async Task Desactivar_las_recurrentes_las_guarda_sin_tocar_las_demas_funciones()
    {
        var (m, api, hogar) = Crear(cuenta: true, ahorro: true);
        api.Responde("PUT /api/hogar/recurrentes", HttpStatusCode.OK, new { activas = false });
        await m.CargarAsync();
        Assert.True(m.RecurrentesActivas);

        var aviso = await m.CambiarRecurrentesAsync(false);

        Assert.Equal("Recurrentes desactivadas", aviso);
        Assert.Equal("{\"activas\":false}", api.Cuerpos["PUT /api/hogar/recurrentes"]);
        Assert.False(hogar.HogarActual!.RecurrentesActivas);
        Assert.True(hogar.HogarActual.CuentaComunActiva);
        Assert.True(hogar.HogarActual.AhorroActivo);
    }

    [Fact]
    public async Task Un_miembro_a_cargo_exige_responsable_para_poder_anadirse()
    {
        var (m, _, _) = Crear();
        await m.CargarAsync();

        m.NuevoNombre = "Lucía";
        Assert.True(m.NuevoMiembroValido);

        m.NuevoTipo = TiposMiembro.ACargo;
        Assert.False(m.NuevoMiembroValido);

        m.NuevoResponsable = _ana.Id.ToString();
        Assert.True(m.NuevoMiembroValido);

        m.NuevoNombre = "  ";
        Assert.False(m.NuevoMiembroValido);
    }

    [Fact]
    public async Task Anadir_un_miembro_a_cargo_envia_el_responsable_y_limpia_el_formulario()
    {
        var (m, api, _) = Crear();
        api.Responde("POST /api/miembros", HttpStatusCode.Created, ApiFalsa.Miembro("Lucia", tipo: "a_cargo", responsable: _ana.Id));
        await m.CargarAsync();
        m.NuevoNombre = " Lucia ";
        m.NuevoTipo = TiposMiembro.ACargo;
        m.NuevoResponsable = _ana.Id.ToString();

        var aviso = await m.AnadirMiembroAsync();

        Assert.Equal("Lucia añadido", aviso);
        var cuerpo = api.Cuerpos["POST /api/miembros"];
        Assert.Contains("\"nombre\":\"Lucia\"", cuerpo);
        Assert.Contains($"\"responsableId\":\"{_ana.Id}\"", cuerpo);
        Assert.Equal("", m.NuevoNombre);
        Assert.Equal(2, api.Recibidas.Count(r => r == "GET /api/miembros")); // carga + recarga
    }

    [Fact]
    public async Task Renombrar_parte_del_nombre_actual_y_guarda_recortado()
    {
        var (m, api, _) = Crear();
        api.Responde($"PUT /api/miembros/{_marcos.Id}", HttpStatusCode.OK, _marcos);
        await m.CargarAsync();

        m.EmpezarRenombrar(_marcos);
        Assert.Equal("Marcos", m.NombreNuevo);
        Assert.Equal(_marcos.Id, m.Renombrando);

        m.NombreNuevo = "  Marc ";
        var aviso = await m.GuardarNombreAsync(_marcos);

        Assert.Equal("Nombre actualizado", aviso);
        Assert.Null(m.Renombrando);
        Assert.Contains("\"nombre\":\"Marc\"", api.Cuerpos[$"PUT /api/miembros/{_marcos.Id}"]);
    }

    [Fact]
    public async Task Cambiar_el_rol_alterna_entre_admin_y_miembro()
    {
        var (m, api, _) = Crear();
        api.Responde($"PUT /api/miembros/{_marcos.Id}", HttpStatusCode.OK, _marcos);
        await m.CargarAsync();

        var aviso = await m.CambiarRolAsync(_marcos);

        Assert.Equal("Marcos ahora es admin", aviso);
        Assert.Contains("\"rol\":\"admin\"", api.Cuerpos[$"PUT /api/miembros/{_marcos.Id}"]);
    }

    [Fact]
    public async Task Un_error_de_la_api_con_los_miembros_queda_en_ErrorMiembros()
    {
        var (m, api, _) = Crear();
        api.Error($"PUT /api/miembros/{_marcos.Id}", HttpStatusCode.Conflict, "El hogar debe conservar al menos un administrador.");
        await m.CargarAsync();

        var aviso = await m.DesactivarAsync(_marcos);

        Assert.Null(aviso);
        Assert.Contains("al menos un administrador", m.ErrorMiembros);
    }

    [Fact]
    public async Task Invitar_a_un_miembro_guarda_el_token_y_su_nombre()
    {
        var (m, api, _) = Crear();
        var caduca = DateTimeOffset.UtcNow.AddDays(7);
        api.Responde("POST /api/invitaciones", HttpStatusCode.Created, new InvitacionCreada(Guid.NewGuid(), "token-secreto", caduca));
        await m.CargarAsync();

        var aviso = await m.InvitarAsync(_marcos.Id);

        Assert.Equal("Invitación creada", aviso);
        Assert.Equal("token-secreto", m.Invitacion!.Token);
        Assert.Equal("Marcos", m.InvitacionPara);
        Assert.False(m.CreandoInvitacion);

        m.OcultarInvitacion();
        Assert.Null(m.Invitacion);
        Assert.Null(m.InvitacionPara);
    }

    [Fact]
    public async Task Eliminar_el_hogar_exige_escribir_su_nombre()
    {
        var (m, api, _) = Crear();
        await m.CargarAsync();
        m.ConfirmarNombre = "Otro";

        var r = await m.EliminarHogarAsync();

        Assert.Null(r);
        Assert.False(m.NombreCoincide);
        Assert.DoesNotContain(api.Recibidas, x => x.StartsWith("DELETE /api/hogar"));
    }

    [Fact]
    public async Task Eliminar_el_hogar_devuelve_el_destino_segun_los_hogares_que_le_quedan()
    {
        var (m, api, _) = Crear();
        api.Responde("DELETE /api/hogar", HttpStatusCode.NoContent);
        api.Responde("GET /api/yo", HttpStatusCode.OK, new YoResponse(Guid.NewGuid().ToString(), [], null));
        await m.CargarAsync();
        m.ConfirmarNombre = " Casa ";

        var r = await m.EliminarHogarAsync();

        Assert.NotNull(r);
        Assert.Contains("Casa", r!.Aviso);
        Assert.Equal(DestinoArranque.SinHogar, r.Destino);
        Assert.False(m.Eliminando);
    }

    [Fact]
    public async Task Cambiar_la_frecuencia_de_aportaciones_la_guarda_en_el_hogar()
    {
        var (m, api, hogar) = Crear(cuenta: true);
        api.Responde("PUT /api/hogar/aportaciones", HttpStatusCode.OK, new FrecuenciaAportacionResponse("bimestral"));
        await m.CargarAsync();
        var clave = m.ClaveFrecuencia;

        var aviso = await m.CambiarFrecuenciaAsync("bimestral");

        Assert.Equal("Frecuencia de aportaciones guardada", aviso);
        Assert.Equal("bimestral", hogar.HogarActual!.AportacionFrecuencia);
        Assert.NotEqual(clave, m.ClaveFrecuencia);
    }

    [Fact]
    public async Task Cerrar_el_viaje_marca_el_periodo_como_cerrado_y_sale_de_la_confirmacion()
    {
        var (m, api, hogar) = Crear();
        var actual = hogar.HogarActual! with { Periodo = "2026-03-01/" };
        await hogar.AplicarAsync(new YoResponse(actual.Id.ToString(), [actual], actual));
        api.Responde("POST /api/hogar/cerrar-periodo", HttpStatusCode.OK, new CierrePeriodoResponse("2026-03-01/2026-03-15", DateTimeOffset.UtcNow, null));
        m.ConfirmandoCierre = true;

        var aviso = await m.CerrarPeriodoAsync();

        Assert.Equal("Viaje cerrado", aviso);
        Assert.True(m.PeriodoCerrado);
        Assert.False(m.PuedeCerrarPeriodo);
        Assert.False(m.ConfirmandoCierre);
    }

    [Fact]
    public async Task Si_la_api_rechaza_el_cierre_del_viaje_el_motivo_queda_en_ErrorPeriodo()
    {
        var (m, api, hogar) = Crear();
        var actual = hogar.HogarActual! with { Periodo = "2026-03-01/" };
        await hogar.AplicarAsync(new YoResponse(actual.Id.ToString(), [actual], actual));
        api.Error("POST /api/hogar/cerrar-periodo", HttpStatusCode.Conflict, "El periodo ya está cerrado.");

        var aviso = await m.CerrarPeriodoAsync();

        Assert.Null(aviso);
        Assert.Contains("ya está cerrado", m.ErrorPeriodo);
        Assert.False(hogar.HogarActual!.PeriodoCerrado);
    }
}
