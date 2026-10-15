using System.Net;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Modelos;
using MiParte.Web.Reparto;

namespace MiParte.Web.Tests;

/// <summary>Orquestación de la pestaña Gastos probada sin renderizar componentes: solo el modelo y un Core.Api falso.</summary>
public class ModeloGastosTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 15);

    private readonly MiembroDto _ana = ApiFalsa.Miembro("Ana", "admin", esYo: true);
    private readonly CategoriaDto _casa = new(Guid.NewGuid(), "Casa", null, null);
    private readonly PerfilRepartoDto _individual = new(Guid.NewGuid(), "Individual", ModosReparto.Individual, []);
    private readonly PerfilRepartoDto _cuenta = new(Guid.NewGuid(), "Cuenta", ModosReparto.CuentaComun, []);

    private (ModeloGastos Modelo, ApiFalsa Api) Crear(params GastoResponse[] gastos)
    {
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { _ana })
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { _casa })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { _individual, _cuenta })
            .Responde("GET /api/gastos", HttpStatusCode.OK, gastos)
            .Responde("GET /api/cierres-mes", HttpStatusCode.OK, Array.Empty<MesCerradoDto>());
        var cliente = new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });
        return (new ModeloGastos(cliente, Hoy), api);
    }

    private GastoResponse Gasto(decimal importe, DateOnly? fecha = null, Guid? paga = null, bool personal = false) => new(
        Guid.NewGuid(), fecha ?? Hoy, importe, _casa.Id, paga ?? _ana.Id, _individual.Id, "Algo", null, [], false, false, personal);

    [Fact]
    public async Task Cargar_deja_los_datos_de_referencia_y_el_formulario_listo()
    {
        var (m, _) = Crear(Gasto(10m));

        await m.CargarAsync(cuentaActiva: false, ahorroActivo: false);

        Assert.False(m.Cargando);
        Assert.Null(m.ErrorCarga);
        Assert.Single(m.Adultos);
        Assert.Single(m.Gastos);
        Assert.Equal(_casa.Id, m.Categoria);
        Assert.Equal(_ana.Id, m.Pagador); // quien usa la aplicación
        Assert.Equal(_individual.Id, m.Perfil);
    }

    [Fact]
    public async Task Cargar_con_un_error_de_la_api_lo_deja_en_ErrorCarga()
    {
        var (m, api) = Crear();
        api.Error("GET /api/miembros", HttpStatusCode.InternalServerError, "Servidor caído");

        await m.CargarAsync(false, false);

        Assert.False(m.Cargando);
        Assert.Contains("Servidor caído", m.ErrorCarga);
    }

    [Fact]
    public async Task Sin_la_cuenta_activa_no_se_ofrece_el_perfil_de_cuenta_comun()
    {
        var (m, _) = Crear();
        await m.CargarAsync(cuentaActiva: false, ahorroActivo: false);
        Assert.DoesNotContain(m.PerfilesOfrecidos, p => p.Id == _cuenta.Id);

        var (m2, _) = Crear();
        await m2.CargarAsync(cuentaActiva: true, ahorroActivo: false);
        Assert.Contains(m2.PerfilesOfrecidos, p => p.Id == _cuenta.Id);
    }

    [Fact]
    public async Task Elegir_la_cuenta_comun_como_pagador_selecciona_su_perfil_y_quita_lo_personal()
    {
        var (m, _) = Crear();
        await m.CargarAsync(cuentaActiva: true, ahorroActivo: true);
        m.TipoGasto = "personal";

        m.OrigenPago = "";
        m.AlCambiarPagador();

        Assert.Null(m.Pagador);
        Assert.False(m.Personal);
        Assert.Equal(_cuenta.Id, m.Perfil);
    }

    [Fact]
    public async Task Pagar_desde_el_ahorro_deja_sin_pagador_y_con_la_marca()
    {
        var (m, _) = Crear();
        await m.CargarAsync(true, true);

        m.OrigenPago = "ahorro";

        Assert.True(m.DesdeAhorro);
        Assert.Null(m.Pagador);
        Assert.Equal("ahorro", m.OrigenPago);
    }

    [Fact]
    public async Task Guardar_con_un_importe_invalido_no_llama_a_la_api_y_muestra_el_error()
    {
        var (m, api) = Crear();
        await m.CargarAsync(false, false);
        m.Importe = "10,123";

        var aviso = await m.GuardarAsync();

        Assert.Null(aviso);
        Assert.Equal(EntradaImporte.MensajeInvalido, m.Error);
        Assert.DoesNotContain("POST /api/gastos", api.Recibidas);
    }

    [Fact]
    public async Task Guardar_crea_el_gasto_recarga_y_limpia_el_importe()
    {
        var (m, api) = Crear();
        api.Responde("POST /api/gastos", HttpStatusCode.Created, Gasto(12.5m));
        await m.CargarAsync(false, false);
        m.Importe = "12,50";
        m.Concepto = "  Pan ";

        var aviso = await m.GuardarAsync();

        Assert.Equal("Gasto añadido", aviso);
        Assert.Equal("", m.Importe);
        Assert.Contains("\"importe\":12.50", api.Cuerpos["POST /api/gastos"]);
        Assert.Contains("\"concepto\":\"Pan\"", api.Cuerpos["POST /api/gastos"]);
        Assert.Equal(2, api.Recibidas.Count(r => r == "GET /api/gastos")); // carga inicial + recarga
    }

    [Fact]
    public async Task Un_error_de_la_api_al_guardar_queda_en_Error_y_no_recarga()
    {
        var (m, api) = Crear();
        api.Error("POST /api/gastos", HttpStatusCode.Conflict, "Ese mes está cerrado.");
        await m.CargarAsync(false, false);
        m.Importe = "5";

        var aviso = await m.GuardarAsync();

        Assert.Null(aviso);
        Assert.Equal("Ese mes está cerrado.", m.Error);
        Assert.Equal(1, api.Recibidas.Count(r => r == "GET /api/gastos"));
    }

    [Fact]
    public async Task Editar_carga_el_gasto_en_el_formulario_y_guardar_usa_PUT()
    {
        var g = Gasto(40m, paga: _ana.Id, personal: true);
        var (m, api) = Crear(g);
        api.Responde($"PUT /api/gastos/{g.Id}", HttpStatusCode.OK, g);
        await m.CargarAsync(false, false);

        m.EmpezarEdicion(g);
        m.Importe = "41";
        var aviso = await m.GuardarAsync();

        Assert.Equal("Gasto guardado", aviso);
        Assert.Null(m.Editando); // vuelve al formulario de alta
        Assert.Contains($"PUT /api/gastos/{g.Id}", api.Recibidas);
    }

    [Fact]
    public async Task Eliminar_recarga_y_cancela_la_confirmacion()
    {
        var g = Gasto(40m);
        var (m, api) = Crear(g);
        api.Responde($"DELETE /api/gastos/{g.Id}", HttpStatusCode.NoContent);
        await m.CargarAsync(false, false);
        m.ConfirmandoEliminar = g.Id;

        var aviso = await m.EliminarAsync(g);

        Assert.Equal("Gasto eliminado", aviso);
        Assert.Null(m.ConfirmandoEliminar);
    }

    [Fact]
    public async Task Cambiar_de_mes_pide_el_mes_nuevo_y_cambiar_a_rango_conserva_el_periodo()
    {
        var (m, api) = Crear();
        await m.CargarAsync(false, false);

        await m.CambiarMesAsync(-1);
        Assert.Contains("/api/gastos?mes=2026-09", api.Consultas);

        await m.CambiarModoAsync(true);
        Assert.True(m.ModoRango);
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), (m.Desde, m.Hasta));
        Assert.Contains("/api/gastos?desde=2026-09-01&hasta=2026-09-30", api.Consultas);
    }

    [Fact]
    public async Task Un_mes_cerrado_se_detecta_y_bloquea_los_gastos_de_ese_mes()
    {
        var (m, api) = Crear(Gasto(10m));
        api.Responde("GET /api/cierres-mes", HttpStatusCode.OK, new[] { new MesCerradoDto("2026-10", DateTimeOffset.UtcNow, "Ana") });
        await m.CargarAsync(false, false);

        Assert.True(m.MesCerrado);
        Assert.True(m.CerradoGasto(m.Gastos.Single()));
    }

    [Fact]
    public async Task Abrir_desde_el_dashboard_aplica_los_filtros_y_el_rango()
    {
        var (m, api) = Crear();
        var persona = Guid.NewGuid();

        m.Abrir(_casa.Id, null, persona, soloCuenta: true, new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 31));
        await m.CargarAsync(false, false);

        Assert.True(m.ModoRango);
        Assert.Equal(persona, m.SoloPaga);
        Assert.False(m.SoloCuenta); // con pagador concreto no tiene sentido «solo la cuenta»
        Assert.True(m.HayFiltros);
        Assert.Contains("/api/gastos?desde=2026-08-01&hasta=2026-10-31", api.Consultas);

        await m.LimpiarFiltrosAsync();
        Assert.False(m.HayFiltros);
    }
}
