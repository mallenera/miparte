using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace MiParte.Assistant.Api.Modelo;

/// <summary>
/// Convierte una nota de voz en texto. Es un puerto aparte de <see cref="IClienteModelo"/> porque el modelo del chat no
/// acepta audio: la transcripción la hace un modelo de voz y el texto resultante entra después en el chat normal.
/// </summary>
public interface ITranscriptor
{
    /// <summary>Transcribe el audio (en español) y devuelve el texto tal cual lo entiende el modelo; vacío si no hay voz.</summary>
    /// <param name="audio">Contenido del fichero de audio, ya validado (tipo y tamaño).</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <exception cref="ModeloNoDisponibleException">El proveedor no responde, rechaza la petición o falta configuración.</exception>
    Task<string> TranscribirAsync(byte[] audio, CancellationToken ct);
}

/// <summary>
/// <see cref="ITranscriptor"/> sobre Cloudflare Workers AI (Whisper, <c>POST /accounts/{cuenta}/ai/run/{modelo}</c> con el
/// audio en base64). Usa la misma cuenta y el mismo token que <see cref="ClienteWorkersAi"/>, sea cual sea el proveedor del
/// chat. El audio no se guarda ni se registra: solo viaja en esta petición.
/// </summary>
/// <param name="fabrica">Fábrica de <see cref="HttpClient"/> (con el nombre <see cref="ClienteWorkersAi.NombreCliente"/>).</param>
/// <param name="opciones">Opciones del asistente (cuenta y modelo de voz).</param>
/// <param name="config">Configuración de la que se lee <c>CLOUDFLARE_API_TOKEN</c> (variables de entorno).</param>
public sealed class TranscriptorWorkersAi(IHttpClientFactory fabrica, IOptions<OpcionesAsistente> opciones, IConfiguration config)
    : ITranscriptor
{
    private static readonly Uri Base = new("https://api.cloudflare.com/client/v4/");

    /// <inheritdoc />
    public async Task<string> TranscribirAsync(byte[] audio, CancellationToken ct)
    {
        var o = opciones.Value;
        var token = config["CLOUDFLARE_API_TOKEN"];
        if (string.IsNullOrWhiteSpace(token))
            throw new ModeloNoDisponibleException("Falta la variable de entorno CLOUDFLARE_API_TOKEN.");
        if (string.IsNullOrWhiteSpace(o.CloudflareCuenta))
            throw new ModeloNoDisponibleException("Falta Asistente__CloudflareCuenta (identificador de cuenta de Cloudflare).");

        var ruta = $"accounts/{Uri.EscapeDataString(o.CloudflareCuenta)}/ai/run/{o.CloudflareModeloVoz}";
        using var peticion = new HttpRequestMessage(HttpMethod.Post, new Uri(Base, ruta))
        {
            // Idioma fijo: sin él Whisper adivina y con notas cortas a veces elige otro idioma.
            Content = JsonContent.Create(new PeticionWhisper(Convert.ToBase64String(audio), "transcribe", "es")),
        };
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await fabrica.CreateClient(ClienteWorkersAi.NombreCliente).SendAsync(peticion, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ModeloNoDisponibleException("No se pudo contactar con Cloudflare Workers AI (voz).", ex);
        }

        using (respuesta)
        {
            // Solo el código: el cuerpo podría repetir lo dicho en el audio.
            if (!respuesta.IsSuccessStatusCode)
                throw new ModeloNoDisponibleException($"Cloudflare Workers AI (voz) respondió {(int)respuesta.StatusCode}.");

            var sobre = await respuesta.Content.ReadFromJsonAsync<SobreWhisper>(ct)
                ?? throw new ModeloNoDisponibleException("Cloudflare Workers AI (voz) devolvió una respuesta vacía.");
            return sobre.Resultado?.Texto ?? "";
        }
    }

    private sealed record PeticionWhisper(
        [property: JsonPropertyName("audio")] string Audio,
        [property: JsonPropertyName("task")] string Tarea,
        [property: JsonPropertyName("language")] string Idioma);

    private sealed class SobreWhisper
    {
        /// <summary>Resultado de la transcripción.</summary>
        [JsonPropertyName("result")]
        public ResultadoWhisper? Resultado { get; set; }
    }

    private sealed class ResultadoWhisper
    {
        /// <summary>Transcripción completa.</summary>
        [JsonPropertyName("text")]
        public string? Texto { get; set; }
    }
}
