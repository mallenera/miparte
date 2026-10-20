using System.Net;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Modelos;

namespace MiParte.Web.Tests;

/// <summary>Orquestación de la pestaña Recurrentes probada sin renderizar componentes.</summary>
public class ModeloRecurrentesTests
{
    private DateTime _hoy = new(2026, 10, 20, 12, 0, 0);

    private readonly MiembroDto _ana = ApiFalsa.Miembro("Ana", "admin", esYo: true);
    private readonly CategoriaDto _casa = new(Guid.NewGuid(), "Casa", null, null);
    private readonly PerfilRepartoDto _perfil = new(Guid.NewGuid(), "Individual", "individual", []);

    private (ModeloRecurrentes Modelo, ApiFalsa Api) Crear(params GastoRecurrenteResponse[] plantillas)
    {
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { _ana })
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { _casa })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { _perfil })
            .Responde("GET /api/gastos-recurrentes", HttpStatusCode.OK, plantillas);
        var cliente = new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });
        return (new ModeloRecurrentes(cliente, () => _hoy), api);
    }

    private GastoRecurrenteResponse Plantilla(string concepto, short dia = 5, bool activo = true) =>
        new(Guid.NewGuid(), 60m, _casa.Id, _ana.Id, _perfil.Id, dia, concepto, activo);

    [Fact]
    public async Task Cargar_ordena_las_plantillas_por_dia_y_deja_el_formulario_listo()
    {
        var (m, _) = Crear(Plantilla("Gas", 20), Plantilla("Alquiler", 1));

        await m.CargarAsync();

        Assert.Equal(["Alquiler", "Gas"], m.Plantillas.Select(p => p.Concepto));
        Assert.Equal(_casa.Id, m.Categoria);
        Assert.Equal(_ana.Id, m.Pagador);
        Assert.Equal("octubre 2026", m.NombreMes);
        Assert.False(m.FormularioValido); // sin importe
    }

    [Fact]
    public async Task El_formulario_exige_importe_valido_y_dia_entre_1_y_28()
    {
        var (m, _) = Crear();
        await m.CargarAsync();

        m.Importe = "10";
        m.Dia = 28;
        Assert.True(m.FormularioValido);

        m.Dia = 29;
        Assert.False(m.FormularioValido);
    }

    [Fact]
    public async Task Crear_una_plantilla_con_el_dia_ya_pasado_ofrece_crear_el_gasto_de_este_mes()
    {
        var (m, api) = Crear();
        api.Responde("POST /api/gastos-recurrentes", HttpStatusCode.Created, Plantilla("Internet", 3));
        await m.CargarAsync();
        m.Concepto = "Internet";
        m.Importe = "39,90";
        m.Dia = 3; // hoy es día 20

        var aviso = await m.GuardarAsync();

        Assert.Equal("Plantilla añadida", aviso);
        Assert.Equal(("Internet", 3), m.DiaPasado);
    }

    [Fact]
    public async Task Crear_con_el_dia_aun_por_llegar_no_ofrece_nada()
    {
        var (m, api) = Crear();
        api.Responde("POST /api/gastos-recurrentes", HttpStatusCode.Created, Plantilla("Gas", 25));
        await m.CargarAsync();
        m.Importe = "10";
        m.Dia = 25;

        await m.GuardarAsync();

        Assert.Null(m.DiaPasado);
    }

    [Fact]
    public async Task Crear_como_personal_envia_el_indicador_en_la_peticion()
    {
        var (m, api) = Crear();
        api.Responde("POST /api/gastos-recurrentes", HttpStatusCode.Created, Plantilla("Gimnasio", 3));
        await m.CargarAsync();
        m.Concepto = "Gimnasio";
        m.Importe = "40";
        m.Dia = 3;
        m.Personal = true;

        await m.GuardarAsync();

        Assert.Contains("\"personal\":true", api.Cuerpos["POST /api/gastos-recurrentes"]);
    }

    [Fact]
    public async Task Pausar_envia_la_plantilla_con_activo_invertido()
    {
        var p = Plantilla("Gas");
        var (m, api) = Crear(p);
        api.Responde($"PUT /api/gastos-recurrentes/{p.Id}", HttpStatusCode.OK, p);
        await m.CargarAsync();

        var aviso = await m.AlternarActivaAsync(p);

        Assert.Equal("Plantilla pausada", aviso);
        Assert.Contains("\"activo\":false", api.Cuerpos[$"PUT /api/gastos-recurrentes/{p.Id}"]);
    }

    [Fact]
    public async Task Eliminar_con_gastos_generados_sugiere_pausarla()
    {
        var p = Plantilla("Gas");
        var (m, api) = Crear(p);
        api.Error($"DELETE /api/gastos-recurrentes/{p.Id}", HttpStatusCode.Conflict, "Ya tiene gastos generados.");
        await m.CargarAsync();
        m.ConfirmandoEliminar = p.Id;

        var aviso = await m.EliminarAsync(p);

        Assert.Null(aviso);
        Assert.Contains("pausa la plantilla", m.Error);
        Assert.Null(m.ConfirmandoEliminar);
    }

    [Fact]
    public async Task Generar_describe_lo_creado_y_lo_que_ya_existia()
    {
        var (m, api) = Crear(Plantilla("Gas"));
        api.Responde("POST /api/gastos-recurrentes/generar", HttpStatusCode.OK, new GenerarRecurrentesResponse("2026-10", 2, 1));
        await m.CargarAsync();

        var aviso = await m.GenerarAsync();

        Assert.Equal("Gastos generados", aviso);
        Assert.Equal("Se han creado 2 gastos en octubre 2026 (1 ya existían).", m.Resultado);
        Assert.False(m.Generando);
    }

    [Fact]
    public async Task Generar_el_mes_actual_cambia_al_mes_en_curso()
    {
        var (m, api) = Crear(Plantilla("Gas"));
        api.Responde("POST /api/gastos-recurrentes/generar", HttpStatusCode.OK, new GenerarRecurrentesResponse("2026-10", 0, 1));
        await m.CargarAsync();
        m.CambiarMes(-3);

        await m.GenerarMesActualAsync();

        Assert.Contains("/api/gastos-recurrentes/generar?mes=2026-10", api.Consultas);
        Assert.Contains("ya tenía su gasto", m.Resultado);
    }

    [Fact]
    public async Task Crear_con_el_dia_de_hoy_tambien_ofrece_crear_el_gasto_del_mes()
    {
        var (m, api) = Crear();
        api.Responde("POST /api/gastos-recurrentes", HttpStatusCode.Created, Plantilla("Gas", 20));
        await m.CargarAsync();
        m.Importe = "10";
        m.Dia = 20; // hoy es día 20

        await m.GuardarAsync();

        Assert.Equal(20, m.DiaPasado!.Value.Dia);
        Assert.Equal(20, m.DiaHoy);
    }
}
