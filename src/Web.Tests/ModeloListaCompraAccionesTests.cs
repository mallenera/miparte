using System.Net;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Modelos;

namespace MiParte.Web.Tests;

/// <summary>Acciones del modelo de la lista de la compra: marcar, borrar, activar, enlaces QR, modo público y errores.</summary>
public class ModeloListaCompraAccionesTests
{
    private static readonly Guid LecheId = Guid.NewGuid(), PanId = Guid.NewGuid();
    private static readonly ProductoCompraDto Leche = new(LecheId, "Leche", 2, false);
    private static readonly ProductoCompraDto Pan = new(PanId, "Pan", 1, true);

    private static CoreApiClient Cliente(ApiFalsa api) => new(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });

    private static ApiFalsa Api(bool activa = true) =>
        new ApiFalsa().Responde("GET /api/lista-compra", HttpStatusCode.OK, new ListaCompraResponse(activa, [Leche, Pan]));

    [Fact]
    public async Task Marcar_envia_el_estado_y_recarga_la_lista()
    {
        var api = Api().Responde($"PUT /api/lista-compra/productos/{LecheId}", HttpStatusCode.OK, Leche with { Comprado = true });
        var m = new ModeloListaCompra(Cliente(api));
        await m.CargarAsync();

        var aviso = await m.MarcarAsync(Leche, true);

        Assert.Equal("Marcado como comprado", aviso);
        Assert.Contains("\"comprado\":true", api.Cuerpos[$"PUT /api/lista-compra/productos/{LecheId}"]);
        Assert.Equal(2, api.Recibidas.Count(r => r == "GET /api/lista-compra")); // la carga y la recarga tras marcar
    }

    [Fact]
    public async Task Desmarcar_avisa_de_que_vuelve_a_la_lista()
    {
        var api = Api().Responde($"PUT /api/lista-compra/productos/{PanId}", HttpStatusCode.OK, Pan with { Comprado = false });
        var m = new ModeloListaCompra(Cliente(api));
        await m.CargarAsync();

        Assert.Equal("Vuelve a la lista", await m.MarcarAsync(Pan, false));
    }

    [Fact]
    public async Task Eliminar_borra_el_producto_y_recarga()
    {
        var api = Api().Responde($"DELETE /api/lista-compra/productos/{LecheId}", HttpStatusCode.NoContent);
        var m = new ModeloListaCompra(Cliente(api));
        await m.CargarAsync();

        var aviso = await m.EliminarAsync(Leche);

        Assert.Equal("Producto borrado", aviso);
        Assert.Contains($"DELETE /api/lista-compra/productos/{LecheId}", api.Recibidas);
    }

    [Theory]
    [InlineData(true, "Lista de la compra activada")]
    [InlineData(false, "Lista de la compra desactivada")]
    public async Task Activar_envia_la_peticion_y_avisa(bool activa, string aviso)
    {
        var api = Api(!activa).Responde("PUT /api/lista-compra/activacion", HttpStatusCode.OK, new { activa });
        var m = new ModeloListaCompra(Cliente(api));
        await m.CargarAsync();

        Assert.Equal(aviso, await m.ActivarAsync(activa));
        Assert.Contains($"\"activa\":{activa.ToString().ToLowerInvariant()}", api.Cuerpos["PUT /api/lista-compra/activacion"]);
    }

    [Fact]
    public async Task Crear_un_enlace_genera_el_qr_de_la_direccion_publica_y_revocarlo_lo_olvida()
    {
        var enlaceId = Guid.NewGuid();
        var api = Api()
            .Responde("POST /api/lista-compra/enlaces", HttpStatusCode.Created, new EnlaceListaCreado(enlaceId, "tok-1", DateTimeOffset.UtcNow.AddDays(7)))
            .Responde("POST /api/lista-compra/qr", HttpStatusCode.OK, new QrListaResponse("<svg/>"))
            .Responde($"DELETE /api/lista-compra/enlaces/{enlaceId}", HttpStatusCode.NoContent);
        var m = new ModeloListaCompra(Cliente(api));
        await m.CargarAsync();

        Assert.Equal("Enlace QR creado", await m.CrearEnlaceAsync("https://mi.app/"));
        Assert.Contains("\"texto\":\"https://mi.app/compra/tok-1\"", api.Cuerpos["POST /api/lista-compra/qr"]);
        Assert.Equal("tok-1", m.Enlace!.Token);
        Assert.Equal("<svg/>", m.Qr);

        Assert.Equal("Enlace revocado", await m.RevocarEnlaceAsync());
        Assert.Null(m.Enlace);
        Assert.Null(m.Qr);
        Assert.Equal("No hay enlace que revocar", await m.RevocarEnlaceAsync());
    }

    [Fact]
    public async Task Un_error_al_crear_el_enlace_deja_el_mensaje_y_no_guarda_enlace()
    {
        var api = Api().Error("POST /api/lista-compra/enlaces", HttpStatusCode.Conflict, "La lista de la compra no está activada en este hogar.");
        var m = new ModeloListaCompra(Cliente(api));
        await m.CargarAsync();

        Assert.Null(await m.CrearEnlaceAsync("https://mi.app"));

        Assert.Contains("no está activada", m.Error);
        Assert.Null(m.Enlace);
        Assert.False(m.Enviando);
    }

    [Fact]
    public async Task En_modo_publico_todas_las_peticiones_van_a_las_rutas_publicas_con_el_token()
    {
        var api = new ApiFalsa()
            .Responde("GET /api/lista-compra/publica", HttpStatusCode.OK, new ListaCompraResponse(true, [Leche]))
            .Responde("POST /api/lista-compra/publica/productos", HttpStatusCode.Created, new ProductoCompraDto(Guid.NewGuid(), "Pan", 3, false))
            .Responde($"PUT /api/lista-compra/publica/productos/{LecheId}", HttpStatusCode.OK, Leche with { Comprado = true });
        var m = new ModeloListaCompra(Cliente(api), "tok-9");

        await m.CargarAsync();
        m.Nombre = "Pan";
        m.Cantidad = "3";
        await m.AnadirAsync();
        await m.MarcarAsync(Leche, true);

        Assert.True(m.Publica);
        Assert.All(api.Recibidas, r => Assert.Contains("/publica", r));
        Assert.All(api.Enlaces.Values, t => Assert.Equal("tok-9", t));
        Assert.Equal(3, api.Enlaces.Count);
        Assert.DoesNotContain("GET /api/lista-compra", api.Recibidas);
    }

    [Fact]
    public async Task Un_enlace_invalido_deja_el_error_y_la_carga_termina()
    {
        var api = new ApiFalsa().Error("GET /api/lista-compra/publica", HttpStatusCode.Conflict, "El enlace de la lista ha caducado o se ha revocado.");
        var m = new ModeloListaCompra(Cliente(api), "viejo");

        await m.CargarAsync();

        Assert.False(m.Cargando);
        Assert.Contains("caducado", m.Error);
        Assert.Empty(m.Productos);
    }

    [Fact]
    public async Task Un_fallo_puntual_al_refrescar_no_tapa_la_lista_ni_pone_error()
    {
        var falla = false;
        var api = new ApiFalsa().Responde("GET /api/lista-compra", _ =>
            falla ? ApiFalsa.Json(HttpStatusCode.InternalServerError, new { error = "boom" }) : ApiFalsa.Json(HttpStatusCode.OK, new ListaCompraResponse(true, [Leche])));
        var m = new ModeloListaCompra(Cliente(api));
        await m.CargarAsync();

        falla = true;
        await m.RefrescarAsync();

        Assert.Null(m.Error);
        Assert.Equal([LecheId], m.Productos.Select(p => p.Id));
    }

    [Fact]
    public async Task El_refresco_trae_los_cambios_de_otras_personas()
    {
        var productos = new List<ProductoCompraDto> { Leche };
        var api = new ApiFalsa().Responde("GET /api/lista-compra", _ => ApiFalsa.Json(HttpStatusCode.OK, new ListaCompraResponse(true, productos.ToArray())));
        var m = new ModeloListaCompra(Cliente(api));
        await m.CargarAsync();
        Assert.Single(m.Productos);

        productos.Add(Pan);
        await m.RefrescarAsync();

        Assert.Equal(2, m.Productos.Count);
        Assert.Equal([LecheId], m.Pendientes.Select(p => p.Id));
        Assert.Equal([PanId], m.Comprados.Select(p => p.Id));
    }

    [Fact]
    public async Task Una_cantidad_no_numerica_no_se_envia()
    {
        var api = Api();
        var m = new ModeloListaCompra(Cliente(api));
        await m.CargarAsync();
        m.Nombre = "Pan";
        m.Cantidad = "abc";

        Assert.False(m.NuevoValido);
        var aviso = await m.AnadirAsync();

        Assert.Null(aviso);
        Assert.Contains("cantidad", m.Error);
        Assert.DoesNotContain("POST /api/lista-compra/productos", api.Recibidas);
    }

    private static readonly Guid AnaId = Guid.NewGuid();

    private static EnlaceListaDto Enlace(Guid id, string estado, DateTimeOffset? caduca = null)
        => new(id, AnaId, new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero), caduca, estado);

    [Fact]
    public async Task Al_cargar_una_lista_activa_trae_los_enlaces_y_los_nombres_de_quien_los_creo()
    {
        var id = Guid.NewGuid();
        var api = Api()
            .Responde("GET /api/lista-compra/enlaces", HttpStatusCode.OK, new[] { Enlace(id, "vigente") })
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ApiFalsa.Miembro("Ana", id: AnaId) });
        var m = new ModeloListaCompra(Cliente(api));

        await m.CargarAsync();

        var enlace = Assert.Single(m.Enlaces);
        Assert.Equal("Ana", m.Creador(enlace));
        Assert.Equal([id], m.EnlacesVigentes.Select(e => e.Id));
    }

    [Fact]
    public async Task Una_lista_desactivada_no_pide_los_enlaces()
    {
        var api = Api(activa: false);
        var m = new ModeloListaCompra(Cliente(api));

        await m.CargarAsync();

        Assert.Empty(m.Enlaces);
        Assert.DoesNotContain("GET /api/lista-compra/enlaces", api.Recibidas);
    }

    [Fact]
    public async Task Si_fallan_los_enlaces_o_los_miembros_la_lista_sigue_sirviendo()
    {
        var api = Api()
            .Error("GET /api/lista-compra/enlaces", HttpStatusCode.InternalServerError, "boom")
            .Error("GET /api/miembros", HttpStatusCode.InternalServerError, "boom");
        var m = new ModeloListaCompra(Cliente(api));

        await m.CargarAsync();

        Assert.Null(m.Error);
        Assert.Equal(2, m.Productos.Count);
        Assert.Empty(m.Enlaces);
    }

    [Fact]
    public async Task Se_puede_revocar_un_enlace_antiguo_y_la_lista_de_enlaces_se_recarga()
    {
        var viejo = Guid.NewGuid();
        var revocado = false;
        var api = Api()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ApiFalsa.Miembro("Ana", id: AnaId) })
            .Responde("GET /api/lista-compra/enlaces", _ => ApiFalsa.Json(HttpStatusCode.OK, new[] { Enlace(viejo, revocado ? "revocado" : "vigente") }))
            .Responde($"DELETE /api/lista-compra/enlaces/{viejo}", _ =>
            {
                revocado = true;
                return ApiFalsa.Json(HttpStatusCode.NoContent, null);
            });
        var m = new ModeloListaCompra(Cliente(api));
        await m.CargarAsync();
        Assert.Single(m.EnlacesVigentes);

        var aviso = await m.RevocarEnlaceAsync(viejo);

        Assert.Equal("Enlace revocado", aviso);
        Assert.Empty(m.EnlacesVigentes);
        Assert.Equal("revocado", Assert.Single(m.Enlaces).Estado);
    }

    [Fact]
    public async Task Revocar_el_enlace_recien_creado_borra_tambien_su_qr()
    {
        var id = Guid.NewGuid();
        var api = Api()
            .Responde("POST /api/lista-compra/enlaces", HttpStatusCode.Created, new EnlaceListaCreado(id, "tok", DateTimeOffset.UtcNow.AddDays(7)))
            .Responde("POST /api/lista-compra/qr", HttpStatusCode.OK, new QrListaResponse("<svg/>"))
            .Responde($"DELETE /api/lista-compra/enlaces/{id}", HttpStatusCode.NoContent);
        var m = new ModeloListaCompra(Cliente(api));
        await m.CargarAsync();
        await m.CrearEnlaceAsync("https://mi.app");

        await m.RevocarEnlaceAsync(id);

        Assert.Null(m.Enlace);
        Assert.Null(m.Qr);
        // Al crear y al revocar se vuelve a pedir la lista de enlaces.
        Assert.True(api.Recibidas.Count(r => r == "GET /api/lista-compra/enlaces") >= 3);
    }

    [Fact]
    public async Task Revocar_un_enlace_antiguo_no_toca_el_qr_del_que_se_acaba_de_crear()
    {
        var nuevo = Guid.NewGuid();
        var viejo = Guid.NewGuid();
        var api = Api()
            .Responde("POST /api/lista-compra/enlaces", HttpStatusCode.Created, new EnlaceListaCreado(nuevo, "tok", DateTimeOffset.UtcNow.AddDays(7)))
            .Responde("POST /api/lista-compra/qr", HttpStatusCode.OK, new QrListaResponse("<svg/>"))
            .Responde($"DELETE /api/lista-compra/enlaces/{viejo}", HttpStatusCode.NoContent);
        var m = new ModeloListaCompra(Cliente(api));
        await m.CargarAsync();
        await m.CrearEnlaceAsync("https://mi.app");

        await m.RevocarEnlaceAsync(viejo);

        Assert.Equal(nuevo, m.Enlace!.Id);
        Assert.Equal("<svg/>", m.Qr);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("999", true)]
    [InlineData("  7 ", true)]
    [InlineData("0", false)]
    [InlineData("1000", false)]
    [InlineData("1,5", false)]
    [InlineData("-2", false)]
    public void La_cantidad_valida_es_un_entero_de_1_a_999(string cantidad, bool valida)
    {
        var m = new ModeloListaCompra(Cliente(Api())) { Nombre = "Pan", Cantidad = cantidad };

        Assert.Equal(valida, m.NuevoValido);
    }

    [Fact]
    public void Un_nombre_de_mas_de_100_caracteres_no_es_valido()
    {
        var m = new ModeloListaCompra(Cliente(Api())) { Nombre = new string('a', 101) };

        Assert.False(m.NuevoValido);
        m.Nombre = new string('a', 100);
        Assert.True(m.NuevoValido);
    }
}
