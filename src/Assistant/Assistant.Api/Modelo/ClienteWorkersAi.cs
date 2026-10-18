using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace MiParte.Assistant.Api.Modelo;

/// <summary>
/// <see cref="IClienteModelo"/> sobre Cloudflare Workers AI (<c>POST /accounts/{cuenta}/ai/run/{modelo}</c>), con
/// cuota gratuita diaria. Cloudflare afirma que no usa el contenido procesado por Workers AI para entrenar modelos
/// y que no lo conserva más allá de la petición. El token sale solo de <c>CLOUDFLARE_API_TOKEN</c> (nunca del repo ni de
/// <c>appsettings</c>); sin él o sin la cuenta, cada llamada falla con <see cref="ModeloNoDisponibleException"/>.
/// </summary>
public sealed class ClienteWorkersAi : IClienteModelo
{
    /// <summary>Nombre del <see cref="HttpClient"/> registrado en <c>Program</c>.</summary>
    public const string NombreCliente = "cloudflare-ai";

    private static readonly Uri Base = new("https://api.cloudflare.com/client/v4/");

    private readonly IHttpClientFactory _fabrica;
    private readonly IConfiguration _config;
    private readonly OpcionesAsistente _opciones;

    /// <summary>Crea el cliente con la cuenta y el modelo configurados.</summary>
    /// <param name="fabrica">Fabrica de <see cref="HttpClient"/> (con el nombre <see cref="NombreCliente"/>).</param>
    /// <param name="opciones">Opciones del asistente (cuenta y modelo de Cloudflare).</param>
    /// <param name="config">Configuración de la que se lee el token (variables de entorno).</param>
    public ClienteWorkersAi(IHttpClientFactory fabrica, IOptions<OpcionesAsistente> opciones, IConfiguration config)
    {
        _fabrica = fabrica;
        _config = config;
        _opciones = opciones.Value;
    }

    /// <inheritdoc />
    public async Task<RespuestaModelo> CrearAsync(SolicitudModelo solicitud, CancellationToken ct)
    {
        var token = _config["CLOUDFLARE_API_TOKEN"];
        if (string.IsNullOrWhiteSpace(token))
            throw new ModeloNoDisponibleException("Falta la variable de entorno CLOUDFLARE_API_TOKEN.");
        if (string.IsNullOrWhiteSpace(_opciones.CloudflareCuenta))
            throw new ModeloNoDisponibleException("Falta Asistente__CloudflareCuenta (identificador de cuenta de Cloudflare).");

        var ruta = $"accounts/{Uri.EscapeDataString(_opciones.CloudflareCuenta)}/ai/run/{_opciones.CloudflareModelo}";
        var peticion = new HttpRequestMessage(HttpMethod.Post, new Uri(Base, ruta))
        {
            Content = JsonContent.Create(new PeticionCloudflare(
                ADeMensajes(solicitud), solicitud.Herramientas.Select(AHerramienta).ToList(), solicitud.MaxTokens)),
        };
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _fabrica.CreateClient(NombreCliente).SendAsync(peticion, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ModeloNoDisponibleException("No se pudo contactar con Cloudflare Workers AI.", ex);
        }

        using (respuesta)
        {
            // Solo el código: el cuerpo puede repetir fragmentos de la conversación.
            if (!respuesta.IsSuccessStatusCode)
                throw new ModeloNoDisponibleException($"Cloudflare Workers AI respondió {(int)respuesta.StatusCode}.");

            var sobre = await respuesta.Content.ReadFromJsonAsync<SobreCloudflare>(ct)
                ?? throw new ModeloNoDisponibleException("Cloudflare Workers AI devolvió una respuesta vacía.");
            if (!sobre.Success || sobre.Resultado is null)
                throw new ModeloNoDisponibleException("Cloudflare Workers AI devolvió un error.");
            return DeResultado(sobre.Resultado);
        }
    }

    private static RespuestaModelo DeResultado(ResultadoCloudflare r)
    {
        var bloques = new List<BloqueModelo>();
        if (!string.IsNullOrWhiteSpace(r.Respuesta))
            bloques.Add(new BloqueTexto(r.Respuesta));

        var llamadas = r.LlamadasHerramienta ?? [];
        for (var i = 0; i < llamadas.Count; i++)
        {
            var llamada = llamadas[i];
            // Los modelos no siempre dan id: se genera uno estable por posición para enlazar el resultado.
            var id = string.IsNullOrWhiteSpace(llamada.Id) ? $"cf-{i}-{Guid.NewGuid():N}" : llamada.Id;
            bloques.Add(new BloqueUsoHerramienta(id, llamada.Nombre, ArgumentosComoJson(llamada.Argumentos)));
        }

        var motivo = llamadas.Count > 0 ? MotivoParada.UsoDeHerramienta : MotivoParada.FinDeTurno;
        return new RespuestaModelo(bloques, motivo);
    }

    /// <summary>Los argumentos llegan como objeto o como cadena JSON según el modelo; se normalizan a objeto.</summary>
    private static JsonElement ArgumentosComoJson(JsonElement argumentos)
    {
        if (argumentos.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(argumentos.GetString()))
        {
            try
            {
                return JsonDocument.Parse(argumentos.GetString()!).RootElement.Clone();
            }
            catch (JsonException)
            {
                // Argumentos no válidos: se pasan vacíos y la validación del servicio responderá al modelo.
                return JsonDocument.Parse("{}").RootElement.Clone();
            }
        }
        return argumentos.ValueKind == JsonValueKind.Undefined ? JsonDocument.Parse("{}").RootElement.Clone() : argumentos.Clone();
    }

    private static List<MensajeCf> ADeMensajes(SolicitudModelo solicitud)
    {
        var mensajes = new List<MensajeCf> { new("system", solicitud.Sistema) };
        // Nombre de cada herramienta por id: el resultado debe indicar a qué función responde.
        var nombres = new Dictionary<string, string>();

        foreach (var m in solicitud.Mensajes)
        {
            foreach (var b in m.Bloques)
            {
                switch (b)
                {
                    case BloqueTexto t:
                        mensajes.Add(new MensajeCf(m.Rol == RolModelo.Usuario ? "user" : "assistant", t.Texto));
                        break;
                    case BloqueUsoHerramienta u:
                        nombres[u.Id] = u.Nombre;
                        mensajes.Add(new MensajeCf("assistant", null)
                        {
                            ToolCalls = [new LlamadaCf(u.Nombre, u.Entrada)],
                        });
                        break;
                    case BloqueResultadoHerramienta r:
                        mensajes.Add(new MensajeCf("tool", r.Contenido)
                        {
                            Name = nombres.GetValueOrDefault(r.IdUso, "herramienta"),
                        });
                        break;
                    // El razonamiento del proveedor de Anthropic no tiene equivalente aquí y se omite.
                }
            }
        }
        return mensajes;
    }

    private static HerramientaCf AHerramienta(DefinicionHerramienta h) => new(h.Nombre, h.Descripcion, h.EsquemaEntrada);

    private sealed record PeticionCloudflare(
        [property: JsonPropertyName("messages")] List<MensajeCf> Mensajes,
        [property: JsonPropertyName("tools")] List<HerramientaCf> Herramientas,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed record MensajeCf(
        [property: JsonPropertyName("role")] string Rol,
        [property: JsonPropertyName("content")] string? Contenido)
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("tool_calls")]
        public List<LlamadaCf>? ToolCalls { get; init; }
    }

    private sealed record LlamadaCf(
        [property: JsonPropertyName("name")] string Nombre,
        [property: JsonPropertyName("arguments")] JsonElement Argumentos);

    private sealed record HerramientaCf(
        [property: JsonPropertyName("name")] string Nombre,
        [property: JsonPropertyName("description")] string Descripcion,
        [property: JsonPropertyName("parameters")] JsonElement Parametros);

    private sealed class SobreCloudflare
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("result")]
        public ResultadoCloudflare? Resultado { get; set; }
    }

    private sealed class ResultadoCloudflare
    {
        [JsonPropertyName("response")]
        public string? Respuesta { get; set; }

        [JsonPropertyName("tool_calls")]
        public List<LlamadaRecibida>? LlamadasHerramienta { get; set; }
    }

    private sealed class LlamadaRecibida
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string Nombre { get; set; } = "";

        [JsonPropertyName("arguments")]
        public JsonElement Argumentos { get; set; }
    }
}
