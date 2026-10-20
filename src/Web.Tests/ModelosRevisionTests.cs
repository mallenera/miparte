using System.Net;
using System.Net.Http.Json;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Modelos;
using MiParte.Web.Reparto;

namespace MiParte.Web.Tests;

/// <summary>Carreras y reglas que salieron de la revisión del PR: respuestas tardías, doble envío e importes ambiguos.</summary>
public class ModelosRevisionTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 15);

    /// <summary>Manejador que deja decidir a cada prueba cuándo contesta cada petición.</summary>
    private sealed class Manejador(Func<HttpRequestMessage, Task<HttpResponseMessage>> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => f(request);
    }

    private static CoreApiClient Cliente(Func<HttpRequestMessage, Task<HttpResponseMessage>> f)
        => new(new HttpClient(new Manejador(f)) { BaseAddress = new Uri("http://localhost:5001/") });

    private static HttpResponseMessage Ok(object cuerpo) => new(HttpStatusCode.OK) { Content = JsonContent.Create(cuerpo) };

    [Theory]
    [InlineData("1,230")]
    [InlineData("1.230")]
    [InlineData("0,001")]
    public void Un_importe_con_tres_decimales_en_el_texto_se_rechaza(string texto)
        => Assert.False(EntradaImporte.TryParse(texto, out _));

    [Theory]
    [InlineData("1,23")]
    [InlineData("1.5")]
    [InlineData("1230")]
    public void Un_importe_con_hasta_dos_decimales_se_acepta(string texto)
        => Assert.True(EntradaImporte.TryParse(texto, out _));

    [Fact]
    public async Task Resumen_una_respuesta_tardia_de_otro_mes_no_pisa_el_mes_mostrado()
    {
        var septiembre = new TaskCompletionSource<HttpResponseMessage>();
        var cliente = Cliente(r =>
        {
            var ruta = r.RequestUri!.PathAndQuery;
            if (ruta.StartsWith("/api/resumen-liquidacion?mes=2026-09")) return septiembre.Task;
            if (ruta.StartsWith("/api/resumen-liquidacion"))
                return Task.FromResult(Ok(new ResumenYLiquidacionResponse(
                    new ResumenMensualResponse("2026-10", 100m, [], [], null, 0m), new LiquidacionResponse("2026-10", [], [], []))));
            return Task.FromResult(Ok(Array.Empty<object>()));
        });
        var m = new ModeloResumen(cliente, Hoy);
        await m.CargarAsync();

        var tardio = m.CambiarMesAsync(-1);   // septiembre: queda pendiente
        await m.CambiarMesAsync(1);           // vuelve a octubre, que responde enseguida
        septiembre.SetResult(Ok(new ResumenYLiquidacionResponse(
            new ResumenMensualResponse("2026-09", 999m, [], [], null, 0m), new LiquidacionResponse("2026-09", [], [], []))));
        await tardio;

        Assert.Equal("2026-10", m.Resumen!.Mes);
        Assert.Equal(100m, m.Resumen.GastosTotales);
        Assert.Null(m.Error);
    }

    [Fact]
    public async Task Resumen_un_error_de_una_peticion_superada_no_se_muestra()
    {
        var septiembre = new TaskCompletionSource<HttpResponseMessage>();
        var cliente = Cliente(r =>
        {
            var ruta = r.RequestUri!.PathAndQuery;
            if (ruta.StartsWith("/api/resumen-liquidacion?mes=2026-09")) return septiembre.Task;
            if (ruta.StartsWith("/api/resumen-liquidacion"))
                return Task.FromResult(Ok(new ResumenYLiquidacionResponse(
                    new ResumenMensualResponse("2026-10", 100m, [], [], null, 0m), new LiquidacionResponse("2026-10", [], [], []))));
            return Task.FromResult(Ok(Array.Empty<object>()));
        });
        var m = new ModeloResumen(cliente, Hoy);
        await m.CargarAsync();

        var tardio = m.CambiarMesAsync(-1);
        await m.CambiarMesAsync(1);
        septiembre.SetResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = JsonContent.Create(new { error = "Caído" }),
        });
        await tardio;

        Assert.Null(m.Error);
        Assert.Equal("2026-10", m.Resumen!.Mes);
    }

    [Fact]
    public async Task CuentaComun_una_respuesta_tardia_de_otro_mes_no_pisa_el_estado()
    {
        var yo = ApiFalsa.Miembro("Ana", "admin", esYo: true);
        var septiembre = new TaskCompletionSource<HttpResponseMessage>();
        CuentaComunResponse Estado(string mes, decimal aportado) => new(mes, 0m, aportado, 0m, 0m, [], 0m, [], []);
        var cliente = Cliente(r =>
        {
            var ruta = r.RequestUri!.PathAndQuery;
            if (ruta.StartsWith("/api/miembros")) return Task.FromResult(Ok(new[] { yo }));
            if (ruta.StartsWith("/api/cuenta-comun?mes=2026-09")) return septiembre.Task;
            if (ruta.StartsWith("/api/cuenta-comun/ingresos")) return Task.FromResult(Ok(Array.Empty<IngresoCuentaComunDto>()));
            return Task.FromResult(Ok(Estado("2026-10", 500m)));
        });
        var m = new ModeloCuentaComun(cliente, Hoy);
        await m.CargarAsync();

        var tardio = m.CambiarMesAsync(-1);
        await m.CambiarMesAsync(1);
        septiembre.SetResult(Ok(Estado("2026-09", 7m)));
        await tardio;

        Assert.Equal("2026-10", m.Estado!.Mes);
        Assert.Equal(500m, m.Estado.Aportado);
    }

    [Fact]
    public async Task Gastos_un_segundo_guardado_mientras_hay_uno_en_curso_se_ignora()
    {
        var yo = ApiFalsa.Miembro("Ana", "admin", esYo: true);
        var casa = new CategoriaDto(Guid.NewGuid(), "Casa", null, null);
        var perfil = new PerfilRepartoDto(Guid.NewGuid(), "Individual", "individual", []);
        var puerta = new TaskCompletionSource<HttpResponseMessage>();
        var posts = 0;
        var cliente = Cliente(r =>
        {
            var ruta = r.RequestUri!.AbsolutePath;
            if (r.Method == HttpMethod.Post && ruta == "/api/gastos")
            {
                posts++;
                return puerta.Task;
            }

            return Task.FromResult(ruta switch
            {
                "/api/miembros" => Ok(new[] { yo }),
                "/api/categorias" => Ok(new[] { casa }),
                "/api/perfiles" => Ok(new[] { perfil }),
                _ => Ok(Array.Empty<object>()),
            });
        });
        var m = new ModeloGastos(cliente, Hoy);
        await m.CargarAsync(false, false);
        m.Importe = "10";

        var primero = m.GuardarAsync();
        var segundo = await m.GuardarAsync(); // doble clic: se ignora
        puerta.SetResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new GastoResponse(
                Guid.NewGuid(), Hoy, 10m, casa.Id, yo.Id, perfil.Id, null, null, [], false, false, false)),
        });
        var aviso = await primero;

        Assert.Null(segundo);
        Assert.Equal("Gasto añadido", aviso);
        Assert.Equal(1, posts);
        Assert.False(m.Guardando);
    }
}
