using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace MiParte.Assistant.Api.Modelo;

/// <summary>
/// <see cref="IClienteModelo"/> sobre Cloudflare Workers AI (<c>POST /accounts/{cuenta}/ai/run/{modelo}</c>), con
/// cuota gratuita diaria. Usa el formato de chat completions de OpenAI que devuelve el endpoint. Cloudflare afirma que
/// no usa el contenido procesado por Workers AI para entrenar modelos y que no lo conserva más allá de la petición.
/// El token sale solo de <c>CLOUDFLARE_API_TOKEN</c> (nunca del repo ni de <c>appsettings</c>); sin él o sin la cuenta,
/// cada llamada falla con <see cref="ModeloNoDisponibleException"/>.
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
            var mensaje = sobre.Resultado?.Opciones?.FirstOrDefault()?.Mensaje
                ?? throw new ModeloNoDisponibleException("Cloudflare Workers AI no devolvió ninguna respuesta.");
            return DeMensaje(mensaje);
        }
    }

    private static RespuestaModelo DeMensaje(MensajeRecibido m)
    {
        var bloques = new List<BloqueModelo>();
        if (!string.IsNullOrWhiteSpace(m.Contenido))
            bloques.Add(new BloqueTexto(m.Contenido));

        var llamadas = m.LlamadasHerramienta ?? [];
        foreach (var llamada in llamadas)
        {
            // Si el modelo no da id, se genera uno: el resultado de la herramienta lo necesita para enlazarse.
            var id = string.IsNullOrWhiteSpace(llamada.Id) ? $"cf-{Guid.NewGuid():N}" : llamada.Id;
            bloques.Add(new BloqueUsoHerramienta(id, llamada.Funcion.Nombre, ArgumentosComoJson(llamada.Funcion.Argumentos)));
        }

        var motivo = llamadas.Count > 0 ? MotivoParada.UsoDeHerramienta : MotivoParada.FinDeTurno;
        return new RespuestaModelo(bloques, motivo);
    }

    /// <summary>Los argumentos llegan como cadena JSON (formato OpenAI) o como objeto; se normalizan a objeto.</summary>
    private static JsonElement ArgumentosComoJson(JsonElement argumentos)
    {
        if (argumentos.ValueKind == JsonValueKind.String)
        {
            var texto = argumentos.GetString();
            if (string.IsNullOrWhiteSpace(texto)) return Vacio();
            try
            {
                return JsonDocument.Parse(texto).RootElement.Clone();
            }
            catch (JsonException)
            {
                // Argumentos no válidos: se pasan vacíos y la validación del servicio responderá al modelo.
                return Vacio();
            }
        }
        return argumentos.ValueKind == JsonValueKind.Undefined ? Vacio() : argumentos.Clone();

        static JsonElement Vacio() => JsonDocument.Parse("{}").RootElement.Clone();
    }

    private static List<MensajeCf> ADeMensajes(SolicitudModelo solicitud)
    {
        var mensajes = new List<MensajeCf> { new("system", solicitud.Sistema) };

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
                        mensajes.Add(new MensajeCf("assistant", null)
                        {
                            LlamadasHerramienta = [new LlamadaCf(u.Id, "function", new FuncionCf(u.Nombre, u.Entrada.GetRawText()))],
                        });
                        break;
                    case BloqueResultadoHerramienta r:
                        mensajes.Add(new MensajeCf("tool", r.Contenido) { IdLlamada = r.IdUso });
                        break;
                    // El razonamiento del proveedor de Anthropic no tiene equivalente aquí y se omite.
                }
            }
        }
        return mensajes;
    }

    private static HerramientaCf AHerramienta(DefinicionHerramienta h) =>
        new("function", new FuncionDefinicionCf(h.Nombre, h.Descripcion, h.EsquemaEntrada));

    private sealed record PeticionCloudflare(
        [property: JsonPropertyName("messages")] List<MensajeCf> Mensajes,
        [property: JsonPropertyName("tools")] List<HerramientaCf> Herramientas,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed record MensajeCf(
        [property: JsonPropertyName("role")] string Rol,
        [property: JsonPropertyName("content")] string? Contenido)
    {
        /// <summary>Llamadas a herramientas del mensaje del asistente; se omiten en el JSON si no hay ninguna.</summary>
        [JsonPropertyName("tool_calls"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<LlamadaCf>? LlamadasHerramienta { get; init; }

        /// <summary>Identificador de la llamada a la que responde un mensaje de herramienta.</summary>
        [JsonPropertyName("tool_call_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? IdLlamada { get; init; }
    }

    private sealed record LlamadaCf(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Tipo,
        [property: JsonPropertyName("function")] FuncionCf Funcion);

    private sealed record FuncionCf(
        [property: JsonPropertyName("name")] string Nombre,
        [property: JsonPropertyName("arguments")] string Argumentos);

    private sealed record HerramientaCf(
        [property: JsonPropertyName("type")] string Tipo,
        [property: JsonPropertyName("function")] FuncionDefinicionCf Funcion);

    private sealed record FuncionDefinicionCf(
        [property: JsonPropertyName("name")] string Nombre,
        [property: JsonPropertyName("description")] string Descripcion,
        [property: JsonPropertyName("parameters")] JsonElement Parametros);

    private sealed class SobreCloudflare
    {
        /// <summary>Indica si Cloudflare completó la petición.</summary>
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        /// <summary>Cuerpo del resultado de la petición.</summary>
        [JsonPropertyName("result")]
        public ResultadoCloudflare? Resultado { get; set; }
    }

    private sealed class ResultadoCloudflare
    {
        /// <summary>Respuestas generadas por el modelo.</summary>
        [JsonPropertyName("choices")]
        public List<OpcionRecibida>? Opciones { get; set; }
    }

    private sealed class OpcionRecibida
    {
        /// <summary>Mensaje de la respuesta.</summary>
        [JsonPropertyName("message")]
        public MensajeRecibido Mensaje { get; set; } = new();
    }

    private sealed class MensajeRecibido
    {
        /// <summary>Texto del mensaje, si lo hay.</summary>
        [JsonPropertyName("content")]
        public string? Contenido { get; set; }

        /// <summary>Llamadas a herramientas que pide el modelo.</summary>
        [JsonPropertyName("tool_calls")]
        public List<LlamadaRecibida>? LlamadasHerramienta { get; set; }
    }

    private sealed class LlamadaRecibida
    {
        /// <summary>Identificador de la llamada.</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>Función a ejecutar y sus argumentos.</summary>
        [JsonPropertyName("function")]
        public FuncionRecibida Funcion { get; set; } = new();
    }

    private sealed class FuncionRecibida
    {
        /// <summary>Nombre de la función.</summary>
        [JsonPropertyName("name")]
        public string Nombre { get; set; } = "";

        /// <summary>Argumentos tal como los envía el modelo, en JSON.</summary>
        [JsonPropertyName("arguments")]
        public JsonElement Argumentos { get; set; }
    }
}
