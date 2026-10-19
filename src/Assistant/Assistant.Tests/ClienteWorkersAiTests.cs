using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MiParte.Assistant.Api;
using MiParte.Assistant.Api.Modelo;

namespace MiParte.Assistant.Tests;

public class ClienteWorkersAiTests
{
    private const string Cuenta = "cuenta-de-prueba";

    /// <summary>Manejador que devuelve una respuesta fija y guarda la petición recibida.</summary>
    private sealed class Manejador(HttpStatusCode estado, string cuerpo) : HttpMessageHandler
    {
        public HttpRequestMessage? Peticion { get; private set; }
        public string? CuerpoPeticion { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Peticion = request;
            CuerpoPeticion = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
        }
    }

    private static ClienteWorkersAi Crear(Manejador manejador, bool conToken = true)
    {
        var servicios = new ServiceCollection();
        servicios.AddHttpClient(ClienteWorkersAi.NombreCliente).ConfigurePrimaryHttpMessageHandler(() => manejador);
        var proveedor = servicios.BuildServiceProvider();

        var valores = new Dictionary<string, string?>();
        if (conToken) valores["CLOUDFLARE_API_TOKEN"] = "token-de-prueba";
        var config = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

        var opciones = Options.Create(new OpcionesAsistente { CloudflareCuenta = Cuenta });
        return new ClienteWorkersAi(proveedor.GetRequiredService<IHttpClientFactory>(), opciones, config);
    }

    private static SolicitudModelo Solicitud(params MensajeModelo[] mensajes) => new(
        "Eres el asistente.",
        mensajes,
        [new DefinicionHerramienta("gasto_total", "Suma gastos.", JsonDocument.Parse("""{"type":"object","properties":{"mes":{"type":"string"}}}""").RootElement.Clone())],
        512);

    [Fact]
    public async Task SinToken_FallaSinLlamarALaRed()
    {
        var manejador = new Manejador(HttpStatusCode.OK, "{}");
        var cliente = Crear(manejador, conToken: false);

        var ex = await Assert.ThrowsAsync<ModeloNoDisponibleException>(() =>
            cliente.CrearAsync(Solicitud(MensajeModelo.DeTexto(RolModelo.Usuario, "hola")), default));

        Assert.Contains("CLOUDFLARE_API_TOKEN", ex.Message);
        Assert.Null(manejador.Peticion);
    }

    [Fact]
    public async Task RespuestaDeTexto_SeDevuelveComoBloqueYTerminaElTurno()
    {
        var manejador = new Manejador(HttpStatusCode.OK, """{"success":true,"result":{"choices":[{"message":{"content":"En junio gastaste 412,30 €.","tool_calls":[]}}]}}""");

        var r = await Crear(manejador).CrearAsync(Solicitud(MensajeModelo.DeTexto(RolModelo.Usuario, "¿Cuánto?")), default);

        var texto = Assert.IsType<BloqueTexto>(Assert.Single(r.Bloques));
        Assert.Equal("En junio gastaste 412,30 €.", texto.Texto);
        Assert.Equal(MotivoParada.FinDeTurno, r.Motivo);
    }

    [Fact]
    public async Task LlamadaAHerramienta_ConArgumentosComoCadena_SeNormalizaAObjeto()
    {
        var manejador = new Manejador(HttpStatusCode.OK, """
            {"success":true,"result":{"choices":[{"message":{"content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"gasto_total","arguments":"{\"mes\":\"2026-06\"}"}}]}}]}}
            """);

        var r = await Crear(manejador).CrearAsync(Solicitud(MensajeModelo.DeTexto(RolModelo.Usuario, "¿Cuánto?")), default);

        var uso = Assert.IsType<BloqueUsoHerramienta>(Assert.Single(r.Bloques));
        Assert.Equal("gasto_total", uso.Nombre);
        Assert.Equal("2026-06", uso.Entrada.GetProperty("mes").GetString());
        Assert.False(string.IsNullOrWhiteSpace(uso.Id));
        Assert.Equal(MotivoParada.UsoDeHerramienta, r.Motivo);
    }

    [Fact]
    public async Task PeticionIncluyeCuentaModeloTokenYHerramientasEnFormatoPlano()
    {
        var manejador = new Manejador(HttpStatusCode.OK, """{"success":true,"result":{"choices":[{"message":{"content":"ok"}}]}}""");

        await Crear(manejador).CrearAsync(Solicitud(MensajeModelo.DeTexto(RolModelo.Usuario, "hola")), default);

        Assert.Equal($"/client/v4/accounts/{Cuenta}/ai/run/@cf/zai-org/glm-4.7-flash", manejador.Peticion!.RequestUri!.AbsolutePath);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "token-de-prueba"), manejador.Peticion.Headers.Authorization);
        using var cuerpo = JsonDocument.Parse(manejador.CuerpoPeticion!);
        Assert.Equal("gasto_total", cuerpo.RootElement.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("system", cuerpo.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task ResultadoDeHerramienta_ViajaConElIdDeLaLlamada()
    {
        var manejador = new Manejador(HttpStatusCode.OK, """{"success":true,"result":{"choices":[{"message":{"content":"Son 412 €."}}]}}""");
        var historial = Solicitud(
            MensajeModelo.DeTexto(RolModelo.Usuario, "¿Cuánto?"),
            new MensajeModelo(RolModelo.Asistente, [new BloqueUsoHerramienta("u1", "gasto_total", JsonDocument.Parse("""{"mes":"2026-06"}""").RootElement.Clone())]),
            new MensajeModelo(RolModelo.Usuario, [new BloqueResultadoHerramienta("u1", """{"total":412.3}""", false)]));

        await Crear(manejador).CrearAsync(historial, default);

        using var cuerpo = JsonDocument.Parse(manejador.CuerpoPeticion!);
        var mensajes = cuerpo.RootElement.GetProperty("messages");
        var resultado = mensajes[mensajes.GetArrayLength() - 1];
        Assert.Equal("tool", resultado.GetProperty("role").GetString());
        Assert.Equal("u1", resultado.GetProperty("tool_call_id").GetString());
    }

    [Fact]
    public async Task ErrorDelProveedor_NoFiltraElCuerpoDeLaRespuesta()
    {
        var manejador = new Manejador(HttpStatusCode.InternalServerError, "secreto-de-la-conversacion");

        var ex = await Assert.ThrowsAsync<ModeloNoDisponibleException>(() =>
            Crear(manejador).CrearAsync(Solicitud(MensajeModelo.DeTexto(RolModelo.Usuario, "hola")), default));

        Assert.Contains("500", ex.Message);
        Assert.DoesNotContain("secreto-de-la-conversacion", ex.Message);
    }

    [Fact]
    public void ProveedorPorDefecto_EsAnthropic()
    {
        Assert.Equal("anthropic", new OpcionesAsistente().Proveedor);
    }
}
