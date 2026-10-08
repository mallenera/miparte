using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MiParte.Contracts;

namespace MiParte.Web.Api;

/// <summary>
/// Cliente tipado de Assistant.Api (<c>POST /api/chat</c>). Usa el mismo manejador que Core.Api, que añade
/// <c>Authorization</c> y <c>X-Hogar-Id</c>; no reintenta (cada pregunta cuesta tokens de pago).
/// </summary>
public sealed class AsistenteApiClient
{
    private readonly HttpClient _http;

    /// <summary>Crea el cliente.</summary>
    /// <param name="http">Cliente HTTP con la URL base de Assistant.Api.</param>
    public AsistenteApiClient(HttpClient http) => _http = http;

    /// <summary>Envía la conversación (terminada en una pregunta) y devuelve la respuesta del asistente.</summary>
    /// <param name="mensajes">Historial reciente en orden cronológico.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <exception cref="ApiException">El asistente no está disponible o rechazó la petición; el mensaje es apto para el usuario.</exception>
    public async Task<ChatResponse> PreguntarAsync(IReadOnlyList<MensajeChatDto> mensajes, CancellationToken ct = default)
    {
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.PostAsJsonAsync("api/chat", new ChatRequest(mensajes), ct);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(HttpStatusCode.ServiceUnavailable, "No se pudo conectar con el asistente. Comprueba tu conexión.");
        }

        using (respuesta)
        {
            if (!respuesta.IsSuccessStatusCode) throw new ApiException(respuesta.StatusCode, await LeerErrorAsync(respuesta, ct));
            try
            {
                return await respuesta.Content.ReadFromJsonAsync<ChatResponse>(ct)
                       ?? throw new ApiException(HttpStatusCode.BadGateway, "Respuesta vacía del asistente.");
            }
            catch (Exception e) when (e is JsonException or NotSupportedException)
            {
                throw new ApiException(HttpStatusCode.BadGateway,
                    "El asistente no ha respondido con datos válidos. Comprueba que Api:AssistantUrl apunta a Assistant.Api.");
            }
        }
    }

    private static async Task<string> LeerErrorAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (cuerpo.ValueKind == JsonValueKind.Object && cuerpo.TryGetProperty("error", out var e) && e.GetString() is { Length: > 0 } texto)
                return texto;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Sin cuerpo JSON: se usa el mensaje por código.
        }

        return respuesta.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Tu sesión ha caducado. Vuelve a iniciar sesión.",
            HttpStatusCode.TooManyRequests => "Demasiadas preguntas seguidas. Espera un momento.",
            HttpStatusCode.ServiceUnavailable => "El asistente no está disponible ahora mismo.",
            _ => $"El asistente respondió con un error ({(int)respuesta.StatusCode}).",
        };
    }
}
