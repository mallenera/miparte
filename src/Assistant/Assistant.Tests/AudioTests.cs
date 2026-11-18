using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MiParte.Assistant.Api;
using MiParte.Assistant.Api.Chat;
using MiParte.Assistant.Api.Hogar;
using MiParte.Assistant.Api.Modelo;
using MiParte.Contracts;

namespace MiParte.Assistant.Tests;

public class NotaDeVozTests
{
    [Theory]
    [InlineData("audio/webm", true)]
    [InlineData("audio/webm;codecs=opus", true)]
    [InlineData("audio/ogg; codecs=opus", true)]
    [InlineData("audio/mp4", true)]
    [InlineData("AUDIO/WEBM", true)]
    [InlineData("audio/wav", false)]
    [InlineData("application/json", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TipoPermitido_SoloLosFormatosDeMediaRecorder(string? tipo, bool esperado)
        => Assert.Equal(esperado, NotaDeVoz.TipoPermitido(tipo));

    [Fact]
    public void FirmaValida_ReconoceWebmOggYMp4()
    {
        Assert.True(NotaDeVoz.FirmaValida(AudioPrueba.Webm()));
        Assert.True(NotaDeVoz.FirmaValida(AudioPrueba.Ogg()));
        Assert.True(NotaDeVoz.FirmaValida(AudioPrueba.Mp4()));
    }

    [Fact]
    public void FirmaValida_RechazaOtrosFicherosYLosMuyCortos()
    {
        Assert.False(NotaDeVoz.FirmaValida("%PDF-1.7 esto no es audio"u8));
        Assert.False(NotaDeVoz.FirmaValida(new byte[] { 0x1A, 0x45, 0xDF }));
        Assert.False(NotaDeVoz.FirmaValida([]));
    }

    [Fact]
    public void Limpiar_QuitaControlesNormalizaEspaciosYRecorta()
    {
        Assert.Equal("Doce euros de café", NotaDeVoz.Limpiar("  Doce\u0000 euros\n\tde   café  ", 1500));
        Assert.Equal("Doce", NotaDeVoz.Limpiar("Doce euros", 5));
        Assert.Equal("", NotaDeVoz.Limpiar("   ", 1500));
        Assert.Equal("", NotaDeVoz.Limpiar(null, 1500));
    }
}

public class EndpointAudioTests
{
    private const string Url = "https://proyecto.supabase.co";
    private const string Secreto = "un-secreto-de-pruebas-de-al-menos-32-bytes!!";
    private const string Ruta = "/api/chat/audio";
    private static readonly Guid Hogar = Guid.Parse("a0000000-0000-4000-8000-000000000001");

    private static WebApplicationFactory<Program> Crear(
        ITranscriptor transcriptor, IDatosHogar? datos = null, int? preguntasPorDia = null, long? maxAudio = null)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Supabase:Url", Url);
            b.UseSetting("Supabase:JwtSecret", Secreto);
            if (preguntasPorDia is not null) b.UseSetting("Asistente:PreguntasPorDia", preguntasPorDia.ToString());
            if (maxAudio is not null) b.UseSetting("Asistente:MaxAudioBytes", maxAudio.ToString());
            b.ConfigureServices(s =>
            {
                s.Replace(ServiceDescriptor.Singleton<IClienteModelo>(new ModeloFalso()));
                s.Replace(ServiceDescriptor.Singleton(transcriptor));
                s.Replace(ServiceDescriptor.Singleton<IFabricaDatosHogar>(new FabricaFalsa(datos ?? new DatosFalsos())));
            });
        });

    private static string Token()
    {
        var cred = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secreto)), SecurityAlgorithms.HmacSha256);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Url + "/auth/v1",
            Audience = "authenticated",
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = cred,
        });
    }

    private static HttpClient Cliente(WebApplicationFactory<Program> f, bool conToken = true, bool conHogar = true)
    {
        var c = f.CreateClient();
        if (conToken) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        if (conHogar) c.DefaultRequestHeaders.Add("X-Hogar-Id", Hogar.ToString());
        return c;
    }

    private static ByteArrayContent Audio(byte[] bytes, string tipo = "audio/webm;codecs=opus")
    {
        var contenido = new ByteArrayContent(bytes);
        contenido.Headers.ContentType = MediaTypeHeaderValue.Parse(tipo);
        return contenido;
    }

    [Fact]
    public async Task NotaValida_DevuelveElTextoLimpioYNoGuardaNada()
    {
        var transcriptor = new TranscriptorFalso("  Doce euros de café\nhoy ");
        var datos = new DatosFalsos();
        await using var f = Crear(transcriptor, datos);
        var audio = AudioPrueba.Webm();

        var r = await Cliente(f).PostAsync(Ruta, Audio(audio));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cuerpo = await r.Content.ReadFromJsonAsync<TranscripcionResponse>();
        Assert.Equal("Doce euros de café hoy", cuerpo!.Texto);
        Assert.Equal(audio, Assert.Single(transcriptor.Audios));
        Assert.Empty(datos.GastosCreados);
    }

    [Fact]
    public async Task SinToken_401SinTranscribir()
    {
        var transcriptor = new TranscriptorFalso();
        await using var f = Crear(transcriptor);

        var r = await Cliente(f, conToken: false).PostAsync(Ruta, Audio(AudioPrueba.Webm()));

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Empty(transcriptor.Audios);
    }

    [Fact]
    public async Task SinHogar_400()
    {
        await using var f = Crear(new TranscriptorFalso());

        var r = await Cliente(f, conHogar: false).PostAsync(Ruta, Audio(AudioPrueba.Webm()));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task NoEsMiembroDelHogar_403SinTranscribir()
    {
        var transcriptor = new TranscriptorFalso();
        await using var f = Crear(transcriptor, new DatosFalsos { Fallo = new HogarNoAccesibleException(HttpStatusCode.Forbidden) });

        var r = await Cliente(f).PostAsync(Ruta, Audio(AudioPrueba.Webm()));

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Empty(transcriptor.Audios);
    }

    [Fact]
    public async Task TipoNoAdmitido_415SinTranscribir()
    {
        var transcriptor = new TranscriptorFalso();
        await using var f = Crear(transcriptor);

        var r = await Cliente(f).PostAsync(Ruta, Audio(AudioPrueba.Webm(), "audio/wav"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, r.StatusCode);
        Assert.Empty(transcriptor.Audios);
    }

    [Fact]
    public async Task FirmaQueNoEsAudio_400SinTranscribir()
    {
        var transcriptor = new TranscriptorFalso();
        await using var f = Crear(transcriptor);

        var r = await Cliente(f).PostAsync(Ruta, Audio(Encoding.UTF8.GetBytes("%PDF-1.7 no soy una nota de voz")));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty(transcriptor.Audios);
    }

    [Fact]
    public async Task AudioMayorQueElTope_413SinTranscribir()
    {
        var transcriptor = new TranscriptorFalso();
        await using var f = Crear(transcriptor, maxAudio: 1000);

        var r = await Cliente(f).PostAsync(Ruta, Audio(AudioPrueba.Webm(2000)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
        Assert.Empty(transcriptor.Audios);
    }

    [Fact]
    public async Task AudioMayorQueElLimiteDelChat_SeAceptaHastaSuPropioTope()
    {
        // 64 KB es el límite del JSON del chat; una nota de voz normal lo supera.
        var transcriptor = new TranscriptorFalso();
        await using var f = Crear(transcriptor);

        var r = await Cliente(f).PostAsync(Ruta, Audio(AudioPrueba.Webm(200 * 1024)));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task SinVoz_422()
    {
        await using var f = Crear(new TranscriptorFalso("   "));

        var r = await Cliente(f).PostAsync(Ruta, Audio(AudioPrueba.Webm()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
    }

    [Fact]
    public async Task ProveedorCaido_503ConMensajeGenerico()
    {
        var transcriptor = new TranscriptorFalso { Fallo = new ModeloNoDisponibleException("Cloudflare Workers AI (voz) respondió 500.") };
        await using var f = Crear(transcriptor);

        var r = await Cliente(f).PostAsync(Ruta, Audio(AudioPrueba.Webm()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.DoesNotContain("Cloudflare", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CuentaEnLaCuotaDiaria()
    {
        var transcriptor = new TranscriptorFalso();
        await using var f = Crear(transcriptor, preguntasPorDia: 1);
        var cliente = Cliente(f);

        var primera = await cliente.PostAsync(Ruta, Audio(AudioPrueba.Webm()));
        var segunda = await cliente.PostAsync(Ruta, Audio(AudioPrueba.Webm()));

        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, segunda.StatusCode);
        Assert.Single(transcriptor.Audios);
    }
}

public class TranscriptorWorkersAiTests
{
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

    private static TranscriptorWorkersAi Crear(Manejador manejador, bool conToken = true)
    {
        var servicios = new ServiceCollection();
        servicios.AddHttpClient(ClienteWorkersAi.NombreCliente).ConfigurePrimaryHttpMessageHandler(() => manejador);
        var proveedor = servicios.BuildServiceProvider();
        var valores = new Dictionary<string, string?>();
        if (conToken) valores["CLOUDFLARE_API_TOKEN"] = "token-de-prueba";
        var config = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        return new TranscriptorWorkersAi(
            proveedor.GetRequiredService<IHttpClientFactory>(), Options.Create(new OpcionesAsistente { CloudflareCuenta = "cuenta" }), config);
    }

    [Fact]
    public async Task EnviaElAudioEnBase64EnEspanolAlModeloDeVozYDevuelveElTexto()
    {
        var manejador = new Manejador(HttpStatusCode.OK, """{"success":true,"result":{"text":"Doce euros de café","word_count":4}}""");
        var audio = AudioPrueba.Webm();

        var texto = await Crear(manejador).TranscribirAsync(audio, default);

        Assert.Equal("Doce euros de café", texto);
        Assert.Equal(
            "https://api.cloudflare.com/client/v4/accounts/cuenta/ai/run/@cf/openai/whisper-large-v3-turbo",
            manejador.Peticion!.RequestUri!.ToString());
        Assert.Equal("Bearer", manejador.Peticion.Headers.Authorization!.Scheme);
        using var cuerpo = JsonDocument.Parse(manejador.CuerpoPeticion!);
        Assert.Equal(Convert.ToBase64String(audio), cuerpo.RootElement.GetProperty("audio").GetString());
        Assert.Equal("es", cuerpo.RootElement.GetProperty("language").GetString());
        Assert.Equal("transcribe", cuerpo.RootElement.GetProperty("task").GetString());
    }

    [Fact]
    public async Task SinToken_FallaSinLlamarALaRed()
    {
        var manejador = new Manejador(HttpStatusCode.OK, "{}");

        var ex = await Assert.ThrowsAsync<ModeloNoDisponibleException>(() => Crear(manejador, conToken: false).TranscribirAsync(AudioPrueba.Webm(), default));

        Assert.Contains("CLOUDFLARE_API_TOKEN", ex.Message);
        Assert.Null(manejador.Peticion);
    }

    [Fact]
    public async Task ErrorDelProveedor_ModeloNoDisponibleSinElCuerpo()
    {
        var manejador = new Manejador(HttpStatusCode.BadRequest, """{"errors":[{"message":"lo que dijo la persona"}]}""");

        var ex = await Assert.ThrowsAsync<ModeloNoDisponibleException>(() => Crear(manejador).TranscribirAsync(AudioPrueba.Webm(), default));

        Assert.DoesNotContain("lo que dijo", ex.Message);
    }

    [Fact]
    public async Task SinTextoEnLaRespuesta_DevuelveVacio()
    {
        var manejador = new Manejador(HttpStatusCode.OK, """{"success":true,"result":{}}""");

        Assert.Equal("", await Crear(manejador).TranscribirAsync(AudioPrueba.Webm(), default));
    }
}

/// <summary>Cabeceras mínimas de cada contenedor seguidas de relleno: bastan para la comprobación de firma.</summary>
internal static class AudioPrueba
{
    public static byte[] Webm(int tamano = 64) => ConCabecera([0x1A, 0x45, 0xDF, 0xA3], tamano);

    public static byte[] Ogg() => ConCabecera("OggS"u8.ToArray(), 64);

    public static byte[] Mp4() => ConCabecera([0x00, 0x00, 0x00, 0x20, .. "ftypM4A "u8.ToArray()], 64);

    private static byte[] ConCabecera(byte[] cabecera, int tamano)
    {
        var bytes = new byte[Math.Max(tamano, cabecera.Length)];
        cabecera.CopyTo(bytes, 0);
        return bytes;
    }
}
