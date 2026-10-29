using System.Net;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Demo;

namespace MiParte.Web.Tests;

/// <summary>La deuda de la cuenta común y la lista de la compra en el modo demo, que ejecuta los servicios reales en memoria.</summary>
public class ServidorDemoDeudaYListaTests
{
    private sealed class Puente(ServidorDemo servidor) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            servidor.ResponderAsync(request, ct);
    }

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private static readonly DateTimeOffset Ahora = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static CoreApiClient Cliente() =>
        new(new HttpClient(new Puente(new ServidorDemo(new RelojFijo(Ahora)))) { BaseAddress = new Uri("http://localhost:5001/") });

    private static async Task<MiembroDto> Yo(CoreApiClient api) => (await api.ListarMiembrosAsync()).Single(m => m.EsYo);

    // ----- Deuda con la cuenta común -----

    [Fact]
    public async Task Un_adelanto_de_la_cuenta_deja_a_la_persona_como_deudora_y_baja_el_efectivo()
    {
        var api = Cliente();
        var yo = await Yo(api);
        var antes = await api.ObtenerCuentaComunAsync("2026-10");

        await api.CrearDeudaAsync(new CrearDeudaCuentaRequest(yo.Id, "adelanto", 40m, null, "Fontanero"));
        var despues = await api.ObtenerCuentaComunAsync("2026-10");

        Assert.Equal(antes.Efectivo - 40m, despues.Efectivo);
        var deuda = Assert.Single(despues.Deudas!);
        Assert.Equal((yo.Id, 40m, false), (deuda.MiembroId, deuda.Importe, deuda.DesdeAhorro));
    }

    [Fact]
    public async Task Una_devolucion_no_puede_superar_la_deuda_y_al_saldarla_vuelve_el_efectivo()
    {
        var api = Cliente();
        var yo = await Yo(api);
        var inicial = (await api.ObtenerCuentaComunAsync("2026-10")).Efectivo;
        await api.CrearDeudaAsync(new CrearDeudaCuentaRequest(yo.Id, "adelanto", 40m, null, null));

        var error = await Assert.ThrowsAsync<ApiException>(() => api.CrearDeudaAsync(new CrearDeudaCuentaRequest(yo.Id, "devolucion", 41m, null, null)));
        Assert.Equal(HttpStatusCode.Conflict, error.Codigo);
        await api.CrearDeudaAsync(new CrearDeudaCuentaRequest(yo.Id, "devolucion", 40m, null, null));

        var cuenta = await api.ObtenerCuentaComunAsync("2026-10");
        Assert.Empty(cuenta.Deudas!);
        Assert.Equal(inicial, cuenta.Efectivo);
    }

    [Fact]
    public async Task Una_compensacion_resta_de_lo_pendiente_sin_mover_el_efectivo()
    {
        var api = Cliente();
        var pendiente = Assert.Single((await api.ObtenerCuentaComunAsync("2026-10")).Pendientes); // la demo debe 95 a alguien
        await api.CrearDeudaAsync(new CrearDeudaCuentaRequest(pendiente.MiembroId, "adelanto", 50m, null, null));
        var antes = await api.ObtenerCuentaComunAsync("2026-10");

        await api.CrearDeudaAsync(new CrearDeudaCuentaRequest(pendiente.MiembroId, "compensacion", 50m, null, null));
        var despues = await api.ObtenerCuentaComunAsync("2026-10");

        Assert.Equal(antes.Efectivo, despues.Efectivo);
        Assert.Equal(45m, Assert.Single(despues.Pendientes).Importe);
        Assert.Empty(despues.Deudas!);
    }

    [Fact]
    public async Task Un_tipo_desconocido_es_un_error_de_validacion()
    {
        var api = Cliente();
        var yo = await Yo(api);

        var error = await Assert.ThrowsAsync<ApiException>(() => api.CrearDeudaAsync(new CrearDeudaCuentaRequest(yo.Id, "prestamo", 10m, null, null)));

        Assert.Equal(HttpStatusCode.BadRequest, error.Codigo);
    }

    // ----- Lista de la compra -----

    [Fact]
    public async Task La_demo_trae_la_lista_de_la_compra_activada_y_vacia()
    {
        var api = Cliente();

        var lista = await api.ObtenerListaCompraAsync();
        var yo = await api.YoAsync();

        Assert.True(lista.Activa);
        Assert.Empty(lista.Productos);
        Assert.True(yo.HogarActual!.ListaCompraActiva);
    }

    [Fact]
    public async Task Se_pueden_anadir_marcar_y_borrar_productos()
    {
        var api = Cliente();

        var leche = await api.CrearProductoCompraAsync(new CrearProductoCompraRequest("Leche", 2));
        var pan = await api.CrearProductoCompraAsync(new CrearProductoCompraRequest("  Pan "));
        await api.MarcarProductoCompraAsync(leche.Id, true);

        var lista = await api.ObtenerListaCompraAsync();
        Assert.Equal(["Pan", "Leche"], lista.Productos.Select(p => p.Nombre)); // los pendientes primero
        Assert.Equal(1, lista.Productos[0].Cantidad);
        Assert.True(lista.Productos[1].Comprado);

        await api.EliminarProductoCompraAsync(pan.Id);
        Assert.Equal(["Leche"], (await api.ObtenerListaCompraAsync()).Productos.Select(p => p.Nombre));
    }

    [Fact]
    public async Task Un_producto_sin_nombre_o_con_cantidad_fuera_de_rango_no_se_acepta()
    {
        var api = Cliente();

        var sinNombre = await Assert.ThrowsAsync<ApiException>(() => api.CrearProductoCompraAsync(new CrearProductoCompraRequest(" ")));
        var cantidad = await Assert.ThrowsAsync<ApiException>(() => api.CrearProductoCompraAsync(new CrearProductoCompraRequest("Pan", 1000)));

        Assert.Equal(HttpStatusCode.BadRequest, sinNombre.Codigo);
        Assert.Equal(HttpStatusCode.BadRequest, cantidad.Codigo);
    }

    [Fact]
    public async Task El_enlace_qr_permite_ver_anadir_y_marcar_sin_sesion_hasta_que_se_revoca()
    {
        var api = Cliente();
        var existente = await api.CrearProductoCompraAsync(new CrearProductoCompraRequest("Leche"));
        var enlace = await api.CrearEnlaceListaCompraAsync();

        var qr = await api.GenerarQrListaCompraAsync($"https://app.test/compra/{enlace.Token}");
        Assert.Contains("<svg", qr.Svg);
        Assert.InRange(enlace.CaducaEn - Ahora, TimeSpan.FromDays(6.9), TimeSpan.FromDays(7.1));

        Assert.Single((await api.ObtenerListaPublicaAsync(enlace.Token)).Productos);
        await api.AnadirProductoPublicoAsync(enlace.Token, new CrearProductoCompraRequest("Pan", 3));
        await api.MarcarProductoPublicoAsync(enlace.Token, existente.Id, true);
        var lista = await api.ObtenerListaCompraAsync();
        Assert.Equal(2, lista.Productos.Count);
        Assert.True(lista.Productos.Single(p => p.Id == existente.Id).Comprado);

        await api.RevocarEnlaceListaCompraAsync(enlace.Id);
        var revocado = await Assert.ThrowsAsync<ApiException>(() => api.ObtenerListaPublicaAsync(enlace.Token));
        Assert.Equal(HttpStatusCode.Conflict, revocado.Codigo);
    }

    [Fact]
    public async Task Un_token_desconocido_no_encuentra_la_lista()
    {
        var api = Cliente();

        var error = await Assert.ThrowsAsync<ApiException>(() => api.ObtenerListaPublicaAsync("no-existe"));

        Assert.Equal(HttpStatusCode.NotFound, error.Codigo);
    }

    [Fact]
    public async Task Con_la_lista_desactivada_no_se_admiten_cambios_ni_siquiera_por_enlace()
    {
        var api = Cliente();
        var enlace = await api.CrearEnlaceListaCompraAsync();

        await api.ActivarListaCompraAsync(false);

        var propio = await Assert.ThrowsAsync<ApiException>(() => api.CrearProductoCompraAsync(new CrearProductoCompraRequest("Pan")));
        var publico = await Assert.ThrowsAsync<ApiException>(() => api.AnadirProductoPublicoAsync(enlace.Token, new CrearProductoCompraRequest("Pan")));
        Assert.Equal(HttpStatusCode.Conflict, propio.Codigo);
        Assert.Equal(HttpStatusCode.Conflict, publico.Codigo);
        Assert.False((await api.ObtenerListaCompraAsync()).Activa);
    }

    [Fact]
    public async Task El_texto_del_qr_no_puede_estar_vacio_ni_pasar_de_500_caracteres()
    {
        var api = Cliente();

        var vacio = await Assert.ThrowsAsync<ApiException>(() => api.GenerarQrListaCompraAsync(" "));
        var largo = await Assert.ThrowsAsync<ApiException>(() => api.GenerarQrListaCompraAsync(new string('a', 501)));

        Assert.Equal(HttpStatusCode.BadRequest, vacio.Codigo);
        Assert.Equal(HttpStatusCode.BadRequest, largo.Codigo);
    }
}
