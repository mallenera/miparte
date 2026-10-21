using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using MiParte.Contracts;

namespace MiParte.Web.Api;

/// <summary>
/// Cliente tipado de Assistant.Api: el chat (completo o en streaming) y la confirmación de un gasto propuesto. Usa el
/// mismo manejador que Core.Api, que añade <c>Authorization</c> y <c>X-Hogar-Id</c>; no reintenta (cada pregunta cuesta
/// tokens de pago, y una confirmación guarda un gasto).
/// </summary>
public sealed class AsistenteApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    /// <summary>Crea el cliente.</summary>
    /// <param name="http">Cliente HTTP con la URL base de Assistant.Api.</param>
    public AsistenteApiClient(HttpClient http) => _http = http;

    /// <summary>Envía la conversación (terminada en una pregunta) y devuelve la respuesta del asistente.</summary>
    /// <param name="mensajes">Historial reciente en orden cronológico.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <exception cref="ApiException">El asistente no está disponible o rechazó la petición; el mensaje es apto para el usuario.</exception>
    public Task<ChatResponse> PreguntarAsync(IReadOnlyList<MensajeChatDto> mensajes, CancellationToken ct = default)
        => EnviarAsync<ChatResponse>("api/chat", new ChatRequest(mensajes), ct);

    /// <summary>
    /// Como <see cref="PreguntarAsync"/>, pero la respuesta llega en vivo: <paramref name="alTexto"/> recibe cada trozo de
    /// texto en cuanto el servidor lo envía. Devuelve la respuesta completa al terminar.
    /// </summary>
    /// <param name="mensajes">Historial reciente en orden cronológico.</param>
    /// <param name="alTexto">Recibe cada trozo de texto.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <exception cref="ApiException">Fallo antes de empezar o un evento de error del servidor.</exception>
    public async Task<ChatResponse> PreguntarEnDirectoAsync(
        IReadOnlyList<MensajeChatDto> mensajes, Func<string, Task> alTexto, CancellationToken ct = default)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "api/chat/stream") { Content = JsonContent.Create(new ChatRequest(mensajes)) };
        // Sin esto el navegador espera a tener toda la respuesta antes de entregarla al código.
        peticion.SetBrowserResponseStreamingEnabled(true);

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(HttpStatusCode.ServiceUnavailable, "No se pudo conectar con el asistente. Comprueba tu conexión.");
        }

        using (respuesta)
        {
            if (!respuesta.IsSuccessStatusCode) throw new ApiException(respuesta.StatusCode, await LeerErrorAsync(respuesta, ct));
            if (respuesta.Content.Headers.ContentType?.MediaType != "text/event-stream")
                throw new ApiException(HttpStatusCode.BadGateway, "El asistente no ha respondido en modo de streaming.");

            await using var flujo = await respuesta.Content.ReadAsStreamAsync(ct);
            ChatResponse? final = null;
            await foreach (var (evento, datos) in LectorSse.LeerAsync(flujo, ct))
            {
                switch (evento)
                {
                    case "texto":
                        await alTexto(Deserializar<TextoEvento>(datos)?.Texto ?? "");
                        break;
                    case "fin":
                        final = Deserializar<ChatResponse>(datos);
                        break;
                    case "error":
                        throw new ApiException(HttpStatusCode.ServiceUnavailable, Deserializar<ErrorEvento>(datos)?.Error ?? "El asistente no ha podido responder.");
                }
            }

            return final ?? throw new ApiException(HttpStatusCode.BadGateway, "El asistente cortó la respuesta.");
        }
    }

    /// <summary>
    /// Confirma un borrador: el servidor guarda el gasto con el reparto y la categoría predeterminados del hogar.
    /// Es la única llamada que escribe desde el asistente.
    /// </summary>
    /// <param name="peticion">Datos que confirma la persona, con el miembro que paga.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <exception cref="ApiException">El gasto no se ha guardado; el mensaje es apto para el usuario.</exception>
    public Task<GastoResponse> ConfirmarGastoAsync(ConfirmarGastoRequest peticion, CancellationToken ct = default)
        => EnviarAsync<GastoResponse>("api/chat/confirmar-gasto", peticion, ct);

    private async Task<T> EnviarAsync<T>(string ruta, object cuerpo, CancellationToken ct)
    {
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.PostAsJsonAsync(ruta, cuerpo, ct);
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
                return await respuesta.Content.ReadFromJsonAsync<T>(ct)
                       ?? throw new ApiException(HttpStatusCode.BadGateway, "Respuesta vacía del asistente.");
            }
            catch (Exception e) when (e is JsonException or NotSupportedException)
            {
                throw new ApiException(HttpStatusCode.BadGateway,
                    "El asistente no ha respondido con datos válidos. Comprueba que Api:AssistantUrl apunta a Assistant.Api.");
            }
        }
    }

    private static T? Deserializar<T>(string datos)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(datos, Json);
        }
        catch (JsonException)
        {
            throw new ApiException(HttpStatusCode.BadGateway, "El asistente envió un evento que no entiendo.");
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
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            // Sin cuerpo JSON (o cuerpo de streaming): se usa el mensaje por código.
        }

        return respuesta.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Tu sesión ha caducado. Vuelve a iniciar sesión.",
            HttpStatusCode.TooManyRequests => "Demasiadas preguntas seguidas. Espera un momento.",
            HttpStatusCode.ServiceUnavailable => "El asistente no está disponible ahora mismo.",
            _ => $"El asistente respondió con un error ({(int)respuesta.StatusCode}).",
        };
    }

    /// <summary>Datos del evento <c>texto</c> del streaming.</summary>
    /// <param name="Texto">Trozo de respuesta.</param>
    public sealed record TextoEvento(string Texto);

    /// <summary>Datos del evento <c>error</c> del streaming.</summary>
    /// <param name="Error">Mensaje para la persona.</param>
    public sealed record ErrorEvento(string Error);
}
