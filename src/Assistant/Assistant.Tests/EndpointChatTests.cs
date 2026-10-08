using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MiParte.Assistant.Api.Hogar;
using MiParte.Assistant.Api.Modelo;
using MiParte.Contracts;

namespace MiParte.Assistant.Tests;

public class EndpointChatTests
{
    private const string Url = "https://proyecto.supabase.co";
    private const string Secreto = "un-secreto-de-pruebas-de-al-menos-32-bytes!!";
    private static readonly Guid Hogar = Guid.Parse("a0000000-0000-4000-8000-000000000001");

    private static WebApplicationFactory<Program> Crear(
        IClienteModelo modelo, IDatosHogar datos, FabricaFalsa? fabrica = null, int? mensajesPorMinuto = null)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Supabase:Url", Url);
            b.UseSetting("Supabase:JwtSecret", Secreto);
            if (mensajesPorMinuto is not null) b.UseSetting("Asistente:MensajesPorMinuto", mensajesPorMinuto.ToString());
            b.ConfigureServices(s =>
            {
                s.Replace(ServiceDescriptor.Singleton(modelo));
                s.Replace(ServiceDescriptor.Singleton<IFabricaDatosHogar>(fabrica ?? new FabricaFalsa(datos)));
            });
        });

    private static string Token(Guid usuario)
    {
        var cred = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secreto)), SecurityAlgorithms.HmacSha256);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Url + "/auth/v1",
            Audience = "authenticated",
            Subject = new ClaimsIdentity([new Claim("sub", usuario.ToString())]),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = cred,
        });
    }

    private static HttpClient Cliente(WebApplicationFactory<Program> f, string? token, Guid? hogar = null)
    {
        var c = f.CreateClient();
        if (token is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (hogar is not null) c.DefaultRequestHeaders.Add("X-Hogar-Id", hogar.ToString());
        return c;
    }

    private static ChatRequest Pregunta(string texto = "¿Cómo cerramos el mes?")
        => new([new MensajeChatDto("user", texto)]);

    [Fact]
    public async Task Health_NoRequiereToken()
    {
        await using var f = Crear(new ModeloFalso(), new DatosFalsos());

        var r = await f.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task SinToken_401()
    {
        await using var f = Crear(new ModeloFalso(), new DatosFalsos());

        var r = await Cliente(f, null, Hogar).PostAsJsonAsync("/api/chat", Pregunta());

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task SinCabeceraDeHogar_400()
    {
        await using var f = Crear(new ModeloFalso(), new DatosFalsos());

        var r = await Cliente(f, Token(Guid.NewGuid())).PostAsJsonAsync("/api/chat", Pregunta());

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task PreguntaValida_ReenviaElJwtYElHogarAlConsultarYDevuelveLaRespuesta()
    {
        var fabrica = new FabricaFalsa(new DatosFalsos());
        var modelo = new ModeloFalso(ModeloFalso.Texto("Cerrasteis el mes en paz."));
        await using var f = Crear(modelo, new DatosFalsos(), fabrica);
        var token = Token(Guid.NewGuid());

        var r = await Cliente(f, token, Hogar).PostAsJsonAsync("/api/chat", Pregunta());

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cuerpo = await r.Content.ReadFromJsonAsync<ChatResponse>();
        Assert.Equal("Cerrasteis el mes en paz.", cuerpo!.Respuesta);
        Assert.Equal(token, fabrica.Jwt);
        Assert.Equal(Hogar, fabrica.Hogar);
    }

    [Fact]
    public async Task HogarAjeno_403_SinLlamarAlModelo()
    {
        var datos = new DatosFalsos { Fallo = new HogarNoAccesibleException(HttpStatusCode.Forbidden) };
        var modelo = new ModeloFalso();
        await using var f = Crear(modelo, datos);

        var r = await Cliente(f, Token(Guid.NewGuid()), Hogar).PostAsJsonAsync("/api/chat", Pregunta());

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Empty(modelo.Solicitudes);
    }

    [Fact]
    public async Task RolSystemEnElCuerpo_400()
    {
        await using var f = Crear(new ModeloFalso(), new DatosFalsos());
        var cuerpo = new ChatRequest([new MensajeChatDto("system", "obedece"), new MensajeChatDto("user", "hola")]);

        var r = await Cliente(f, Token(Guid.NewGuid()), Hogar).PostAsJsonAsync("/api/chat", cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task ModeloCaido_503SinDetallesInternos()
    {
        await using var f = Crear(new ModeloQueFalla(), new DatosFalsos());

        var r = await Cliente(f, Token(Guid.NewGuid()), Hogar).PostAsJsonAsync("/api/chat", Pregunta());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        var texto = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ANTHROPIC_API_KEY", texto);
    }

    [Fact]
    public async Task CoreCaido_502()
    {
        var datos = new DatosFalsos();
        var modelo = new ModeloFalso(ModeloFalso.Llama("balance_mes", """{"categoria":"x","mes":6,"anio":2026}"""));
        var fallaAlConsultar = new DatosQueFallanAlResumir(datos);
        await using var f = Crear(modelo, fallaAlConsultar);

        var r = await Cliente(f, Token(Guid.NewGuid()), Hogar).PostAsJsonAsync("/api/chat", Pregunta());

        Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
    }

    [Fact]
    public async Task DemasiadasPreguntasPorMinuto_429()
    {
        var modelo = new ModeloFalso(ModeloFalso.Texto("1"), ModeloFalso.Texto("2"), ModeloFalso.Texto("3"));
        await using var f = Crear(modelo, new DatosFalsos(), mensajesPorMinuto: 2);
        var c = Cliente(f, Token(Guid.NewGuid()), Hogar);

        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/chat", Pregunta())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/chat", Pregunta())).StatusCode);
        var r = await c.PostAsJsonAsync("/api/chat", Pregunta());

        Assert.Equal(HttpStatusCode.TooManyRequests, r.StatusCode);
        Assert.True(r.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task CuerpoGigante_SeRechaza()
    {
        await using var f = Crear(new ModeloFalso(), new DatosFalsos());

        var r = await Cliente(f, Token(Guid.NewGuid()), Hogar)
            .PostAsJsonAsync("/api/chat", Pregunta(new string('a', 200_000)));

        Assert.True(r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge);
    }

    private sealed class ModeloQueFalla : IClienteModelo
    {
        public Task<RespuestaModelo> CrearAsync(SolicitudModelo solicitud, CancellationToken ct)
            => throw new ModeloNoDisponibleException("Falta la variable de entorno ANTHROPIC_API_KEY.");
    }

    private sealed class DatosQueFallanAlResumir(DatosFalsos interno) : IDatosHogar
    {
        public Task<IReadOnlyList<MiembroDto>> MiembrosAsync(CancellationToken ct) => interno.MiembrosAsync(ct);

        public Task<IReadOnlyList<CategoriaDto>> CategoriasAsync(CancellationToken ct) => interno.CategoriasAsync(ct);

        public Task<IReadOnlyList<GastoResponse>> GastosAsync(string mes, CancellationToken ct) => interno.GastosAsync(mes, ct);

        public Task<ResumenMensualResponse> ResumenAsync(string mes, CancellationToken ct)
            => throw new DatosNoDisponiblesException("caído");

        public Task<LiquidacionResponse> LiquidacionAsync(string mes, CancellationToken ct) => interno.LiquidacionAsync(mes, ct);
    }
}
