using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Autenticacion;
using MiParte.Web.Hogares;

namespace MiParte.Web.Tests;

public class CoreApiTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Monta cliente + manejador reales sobre un servidor falso de Core.Api.</summary>
    private static async Task<(CoreApiClient api, EstadoHogar hogar, ServicioSesion sesion)> Crear(
        ManejadorFalso servidor, bool conSesion = true)
    {
        var reloj = new FakeTimeProvider(Ahora);
        var almacen = new AlmacenMemoria();
        if (conSesion)
            almacen.Datos["miparte.sesion"] = JsonSerializer.Serialize(new SesionSupabase("jwt", "r1", Ahora.AddHours(1), "u1", "a@b.com"));
        var auth = new SupabaseAuthClient(new HttpClient(new ManejadorFalso(_ => throw new InvalidOperationException()))
            { BaseAddress = new Uri("https://x.supabase.co/auth/v1/") }, reloj);
        var sesion = new ServicioSesion(auth, almacen, reloj);
        await sesion.InicializarAsync();
        var hogar = new EstadoHogar(almacen);
        var manejador = new ManejadorCoreApi(sesion, hogar) { InnerHandler = servidor };
        return (new CoreApiClient(new HttpClient(manejador) { BaseAddress = new Uri("http://localhost:5001/") }), hogar, sesion);
    }

    [Fact]
    public async Task Envia_el_bearer_y_el_hogar_actual()
    {
        var casa = new HogarResumen(Guid.NewGuid(), "Casa");
        var servidor = ManejadorFalso.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new YoResponse("u1", [casa], casa)));
        var (api, hogar, _) = await Crear(servidor);
        await hogar.AplicarAsync(new YoResponse("u1", [casa], null));

        var yo = await api.YoAsync();

        Assert.Equal("u1", yo.UserId);
        var peticion = servidor.Peticiones.Single();
        Assert.Equal("Bearer jwt", peticion.Headers.Authorization!.ToString());
        Assert.Equal(casa.Id.ToString(), peticion.Headers.GetValues("X-Hogar-Id").Single());
        Assert.Equal("http://localhost:5001/api/yo", peticion.RequestUri!.ToString());
    }

    [Fact]
    public async Task Sin_hogar_seleccionado_no_envia_la_cabecera()
    {
        var servidor = ManejadorFalso.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new YoResponse("u1", [], null)));
        var (api, _, _) = await Crear(servidor);

        await api.YoAsync();

        Assert.False(servidor.Peticiones.Single().Headers.Contains("X-Hogar-Id"));
    }

    [Fact]
    public async Task Un_401_descarta_la_sesion_local()
    {
        var servidor = ManejadorFalso.Json(HttpStatusCode.Unauthorized, "");
        var (api, _, sesion) = await Crear(servidor);

        var e = await Assert.ThrowsAsync<ApiException>(() => api.YoAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, e.Codigo);
        Assert.Null(sesion.SesionActual);
    }

    [Fact]
    public async Task Usa_el_mensaje_de_error_de_la_api()
    {
        var servidor = ManejadorFalso.Json(HttpStatusCode.BadRequest, """{"error":"El nombre del hogar es obligatorio."}""");
        var (api, _, _) = await Crear(servidor);

        var e = await Assert.ThrowsAsync<ApiException>(() => api.CrearHogarAsync(new CrearHogarRequest("", "Ana")));

        Assert.Equal("El nombre del hogar es obligatorio.", e.Message);
        Assert.Equal(HttpStatusCode.BadRequest, e.Codigo);
    }

    [Fact]
    public async Task Sin_conexion_lanza_una_ApiException_amigable()
    {
        var servidor = new ManejadorFalso(_ => throw new HttpRequestException("sin red"));
        var (api, _, _) = await Crear(servidor);

        var e = await Assert.ThrowsAsync<ApiException>(() => api.YoAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, e.Codigo);
    }

    [Fact]
    public async Task Una_respuesta_que_no_es_json_se_convierte_en_ApiException_en_vez_de_romper()
    {
        // Api:CoreUrl apuntando al propio front: responde 200 con index.html.
        var servidor = new ManejadorFalso(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("<!DOCTYPE html><html></html>", System.Text.Encoding.UTF8, "text/html") });
        var (api, _, _) = await Crear(servidor);

        var e = await Assert.ThrowsAsync<ApiException>(() => api.YoAsync());

        Assert.Equal(HttpStatusCode.BadGateway, e.Codigo);
        Assert.Contains("Api:CoreUrl", e.Message);
    }

    [Fact]
    public async Task El_arranque_aplica_yo_al_estado_del_hogar()
    {
        var casa = new HogarResumen(Guid.NewGuid(), "Casa");
        var servidor = ManejadorFalso.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new YoResponse("u1", [casa], casa)));
        var (api, hogar, _) = await Crear(servidor);

        var destino = await new ServicioArranque(api, hogar).CargarAsync();

        Assert.Equal(DestinoArranque.Listo, destino);
        Assert.Equal(casa, hogar.HogarActual);
    }
}
