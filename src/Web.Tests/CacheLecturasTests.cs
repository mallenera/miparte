using System.Net;
using Microsoft.Extensions.Time.Testing;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Autenticacion;
using MiParte.Web.Hogares;

namespace MiParte.Web.Tests;

public class CacheLecturasTests
{
    private static readonly HogarResumen Casa = new(Guid.NewGuid(), "Casa");
    private static readonly HogarResumen Otra = new(Guid.NewGuid(), "Otra");

    private readonly ApiFalsa _api = new ApiFalsa()
        .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ApiFalsa.Miembro("Ana", esYo: true) })
        .Responde("GET /api/categorias", HttpStatusCode.OK, Array.Empty<CategoriaDto>())
        .Responde("GET /api/perfiles", HttpStatusCode.OK, Array.Empty<PerfilRepartoDto>())
        .Responde("GET /api/cierres-mes", HttpStatusCode.OK, Array.Empty<MesCerradoDto>())
        .Responde("POST /api/categorias", HttpStatusCode.Created, new CategoriaDto(Guid.NewGuid(), "Nueva", null, null))
        .Responde("GET /api/gastos", HttpStatusCode.OK, Array.Empty<GastoResponse>());

    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));
    private readonly EstadoHogar _hogar = new(new AlmacenMemoria());
    private readonly AlmacenMemoria _almacen = new();
    private ServicioSesion _sesion = null!;

    private async Task<CoreApiClient> ClienteAsync()
    {
        await _hogar.AplicarAsync(new YoResponse(null, [Casa, Otra], Casa));
        await _hogar.SeleccionarAsync(Casa.Id);
        var auth = new SupabaseAuthClient(new HttpClient(new ManejadorFalso(_ => throw new InvalidOperationException()))
            { BaseAddress = new Uri("https://x.supabase.co/auth/v1/") }, _reloj);
        _sesion = new ServicioSesion(auth, _almacen, _reloj);
        await _sesion.IniciarDemoAsync(); // una sesión cualquiera: lo que cuenta aquí es el cambio de usuario
        var cache = new CacheLecturas(_hogar, _sesion, _reloj);
        return new CoreApiClient(new HttpClient(_api) { BaseAddress = new Uri("http://localhost:5001/") }, cache);
    }

    private int Peticiones(string metodoYRuta) => _api.Recibidas.Count(r => r == metodoYRuta);

    [Fact]
    public async Task Los_enlaces_de_la_lista_de_la_compra_no_se_cachean_y_se_ven_nada_mas_crear_uno()
    {
        _api.Responde("GET /api/lista-compra/enlaces", HttpStatusCode.OK, Array.Empty<EnlaceListaDto>())
            .Responde("POST /api/lista-compra/enlaces", HttpStatusCode.Created, new EnlaceListaCreado(Guid.NewGuid(), "t", DateTimeOffset.UtcNow));
        var api = await ClienteAsync();

        await api.ListarEnlacesListaCompraAsync();
        await api.CrearEnlaceListaCompraAsync();
        await api.ListarEnlacesListaCompraAsync();

        // Con caché, la segunda lectura saldría de la memoria y no mostraría el enlace recién creado.
        Assert.Equal(2, _api.Recibidas.Count(r => r == "GET /api/lista-compra/enlaces"));
    }

    [Fact]
    public async Task Repetir_la_lectura_no_vuelve_a_pedirla_a_la_api()
    {
        var api = await ClienteAsync();

        await api.ListarMiembrosAsync();
        await api.ListarMiembrosAsync();
        await api.ListarCategoriasAsync();
        await api.ListarCategoriasAsync();

        Assert.Equal(1, Peticiones("GET /api/miembros"));
        Assert.Equal(1, Peticiones("GET /api/categorias"));
    }

    [Fact]
    public async Task Dos_pantallas_que_piden_lo_mismo_a_la_vez_comparten_una_peticion()
    {
        var api = await ClienteAsync();

        await Task.WhenAll(api.ListarMiembrosAsync(), api.ListarMiembrosAsync(), api.ListarMiembrosAsync());

        Assert.Equal(1, Peticiones("GET /api/miembros"));
    }

    [Fact]
    public async Task Cada_llamante_recibe_su_propia_lista_y_modificarla_no_afecta_a_los_demas()
    {
        var api = await ClienteAsync();

        var primera = await api.ListarMiembrosAsync();
        primera.Clear();
        var segunda = await api.ListarMiembrosAsync();

        Assert.Single(segunda);
    }

    [Fact]
    public async Task Los_miembros_con_y_sin_inactivos_son_lecturas_distintas()
    {
        var api = await ClienteAsync();
        _api.Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ApiFalsa.Miembro("Ana", esYo: true) });

        await api.ListarMiembrosAsync();
        await api.ListarMiembrosAsync(incluirInactivos: true);

        Assert.Equal(2, Peticiones("GET /api/miembros"));
    }

    [Fact]
    public async Task Escribir_una_categoria_invalida_las_categorias_pero_no_los_miembros()
    {
        var api = await ClienteAsync();
        await api.ListarMiembrosAsync();
        await api.ListarCategoriasAsync();

        await api.CrearCategoriaAsync(new GuardarCategoriaRequest("Nueva", null, null, false));
        await api.ListarMiembrosAsync();
        await api.ListarCategoriasAsync();

        Assert.Equal(1, Peticiones("GET /api/miembros"));
        Assert.Equal(2, Peticiones("GET /api/categorias"));
    }

    [Fact]
    public async Task Una_escritura_que_falla_tambien_invalida_la_lectura()
    {
        var api = await ClienteAsync();
        _api.Error("POST /api/categorias", HttpStatusCode.Conflict, "Ya existe.");
        await api.ListarCategoriasAsync();

        await Assert.ThrowsAsync<ApiException>(() => api.CrearCategoriaAsync(new GuardarCategoriaRequest("Nueva", null, null, false)));
        await api.ListarCategoriasAsync();

        Assert.Equal(2, Peticiones("GET /api/categorias"));
    }

    [Fact]
    public async Task Cambiar_de_hogar_no_reutiliza_las_lecturas_del_anterior()
    {
        var api = await ClienteAsync();
        await api.ListarCategoriasAsync();

        await _hogar.SeleccionarAsync(Otra.Id);
        await api.ListarCategoriasAsync();
        await _hogar.SeleccionarAsync(Casa.Id);
        await api.ListarCategoriasAsync();

        Assert.Equal(3, Peticiones("GET /api/categorias"));
    }

    [Fact]
    public async Task Pasada_la_vigencia_se_vuelve_a_pedir()
    {
        var api = await ClienteAsync();
        await api.ListarCierresMesAsync();

        _reloj.Advance(CacheLecturas.Vigencia - TimeSpan.FromSeconds(1));
        await api.ListarCierresMesAsync();
        _reloj.Advance(TimeSpan.FromSeconds(2));
        await api.ListarCierresMesAsync();

        Assert.Equal(2, Peticiones("GET /api/cierres-mes"));
    }

    [Fact]
    public async Task Un_fallo_no_se_recuerda_y_el_siguiente_intento_vuelve_a_pedir()
    {
        var api = await ClienteAsync();
        _api.Error("GET /api/perfiles", HttpStatusCode.InternalServerError, "Fallo.");
        await Assert.ThrowsAsync<ApiException>(() => api.ListarPerfilesAsync());

        _api.Responde("GET /api/perfiles", HttpStatusCode.OK, Array.Empty<PerfilRepartoDto>());
        var perfiles = await api.ListarPerfilesAsync();

        Assert.Empty(perfiles);
        Assert.Equal(2, Peticiones("GET /api/perfiles"));
    }

    [Fact]
    public async Task Las_lecturas_que_cambian_a_menudo_no_pasan_por_la_cache()
    {
        var api = await ClienteAsync();

        await api.ListarGastosAsync("2026-10");
        await api.ListarGastosAsync("2026-10");

        Assert.Equal(2, Peticiones("GET /api/gastos"));
    }

    /// <summary>La lista de miembros lleva `EsYo` y permisos del que pregunta: no puede servirse a otra persona del mismo navegador.</summary>
    [Fact]
    public async Task Al_cerrar_sesion_o_entrar_otro_usuario_no_se_reutilizan_las_lecturas_del_anterior()
    {
        var api = await ClienteAsync();
        await api.ListarMiembrosAsync();

        await _sesion.DescartarAsync(); // p. ej. un 401: la sesión se descarta sin pasar por EstadoHogar.LimpiarAsync
        await api.ListarMiembrosAsync();
        Assert.Equal(2, Peticiones("GET /api/miembros"));

        await _sesion.IniciarDemoAsync();
        await api.ListarMiembrosAsync();
        Assert.Equal(3, Peticiones("GET /api/miembros"));
    }

    /// <summary>Renovar el token no cambia de usuario: la caché se conserva.</summary>
    [Fact]
    public async Task Con_el_mismo_usuario_la_cache_se_conserva_al_cambiar_la_sesion()
    {
        var api = await ClienteAsync();
        await api.ListarMiembrosAsync();

        await _sesion.IniciarDemoAsync(); // mismo usuario de la demo: SesionCambiada se dispara con el mismo UserId
        await api.ListarMiembrosAsync();

        Assert.Equal(1, Peticiones("GET /api/miembros"));
    }

    [Fact]
    public async Task Borrar_un_perfil_tambien_olvida_las_categorias_guardadas()
    {
        var perfil = Guid.NewGuid();
        _api.Responde($"DELETE /api/perfiles/{perfil}", HttpStatusCode.NoContent);
        var api = await ClienteAsync();
        await api.ListarCategoriasAsync();
        await api.ListarPerfilesAsync();

        // Sus categorías pasan al perfil de destino: la lista guardada apuntaría a un perfil que ya no existe.
        await api.EliminarPerfilAsync(perfil, Guid.NewGuid());
        await api.ListarCategoriasAsync();
        await api.ListarPerfilesAsync();

        Assert.Equal(2, Peticiones("GET /api/categorias"));
        Assert.Equal(2, Peticiones("GET /api/perfiles"));
    }

    [Fact]
    public async Task Aceptar_una_invitacion_olvida_miembros_y_categorias()
    {
        _api.Responde("POST /api/invitaciones/aceptar", HttpStatusCode.OK, Casa);
        var api = await ClienteAsync();
        await api.ListarMiembrosAsync();
        await api.ListarCategoriasAsync();

        await api.AceptarInvitacionAsync(new AceptarInvitacionRequest("token"));
        await api.ListarMiembrosAsync();
        await api.ListarCategoriasAsync();

        Assert.Equal(2, Peticiones("GET /api/miembros"));
        Assert.Equal(2, Peticiones("GET /api/categorias"));
    }

    [Fact]
    public async Task Unirse_por_enlace_olvida_miembros_y_categorias()
    {
        _api.Responde("POST /api/enlaces-grupo/unirse", HttpStatusCode.OK, Casa);
        var api = await ClienteAsync();
        await api.ListarMiembrosAsync();
        await api.ListarCategoriasAsync();

        await api.UnirseEnlaceGrupoAsync(new UnirseEnlaceGrupoRequest("codigo", "Nueva"));
        await api.ListarMiembrosAsync();
        await api.ListarCategoriasAsync();

        Assert.Equal(2, Peticiones("GET /api/miembros"));
        Assert.Equal(2, Peticiones("GET /api/categorias"));
    }

    [Fact]
    public async Task Crear_un_hogar_olvida_todas_las_lecturas_del_anterior()
    {
        _api.Responde("POST /api/hogares", HttpStatusCode.Created, Otra);
        var api = await ClienteAsync();
        await api.ListarMiembrosAsync();
        await api.ListarCategoriasAsync();
        await api.ListarPerfilesAsync();

        await api.CrearHogarAsync(new CrearHogarRequest("Otra", "Yo"));
        await api.ListarMiembrosAsync();
        await api.ListarCategoriasAsync();
        await api.ListarPerfilesAsync();

        Assert.Equal(2, Peticiones("GET /api/miembros"));
        Assert.Equal(2, Peticiones("GET /api/categorias"));
        Assert.Equal(2, Peticiones("GET /api/perfiles"));
    }

    [Fact]
    public async Task Reabrir_un_mes_olvida_los_cierres_pero_no_las_categorias()
    {
        _api.Responde("DELETE /api/cierres-mes/2026-09", HttpStatusCode.NoContent);
        var api = await ClienteAsync();
        await api.ListarCierresMesAsync();
        await api.ListarCategoriasAsync();

        await api.ReabrirMesAsync("2026-09");
        await api.ListarCierresMesAsync();
        await api.ListarCategoriasAsync();

        Assert.Equal(2, Peticiones("GET /api/cierres-mes"));
        Assert.Equal(1, Peticiones("GET /api/categorias"));
    }
}
