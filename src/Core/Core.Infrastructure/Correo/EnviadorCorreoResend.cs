using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MiParte.Core.Application.Contacto;

namespace MiParte.Core.Infrastructure.Correo;

/// <summary>
/// Envía el correo de contacto con <c>POST https://api.resend.com/emails</c>. El remitente y el destino salen de
/// <see cref="OpcionesCorreo"/>; de la petición solo llegan el asunto, el texto y la dirección de respuesta.
/// </summary>
/// <param name="http">Cliente HTTP con la URL base de la API de Resend.</param>
/// <param name="opciones">Configuración del envío.</param>
/// <param name="log">Registro: deja el código de un rechazo, nunca la clave ni el contenido del mensaje.</param>
public sealed class EnviadorCorreoResend(HttpClient http, OpcionesCorreo opciones, ILogger<EnviadorCorreoResend> log) : IEnviadorCorreo
{
    /// <summary>URL base de la API de Resend.</summary>
    public static readonly Uri UrlBase = new("https://api.resend.com/");

    /// <inheritdoc />
    public bool Disponible => opciones.Configurado;

    /// <inheritdoc />
    public async Task<bool> EnviarAsync(CorreoContacto correo, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new CuerpoResend(opciones.Remitente, [opciones.Destino], correo.Asunto, correo.Texto, correo.ResponderA)),
        };
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opciones.ResendApiKey);
        // Resend rechaza las peticiones sin User-Agent.
        peticion.Headers.UserAgent.ParseAdd("miparte-core/1.0");

        try
        {
            using var respuesta = await http.SendAsync(peticion, ct);
            if (respuesta.IsSuccessStatusCode) return true;
            log.LogWarning("Resend rechazó el correo de contacto: {Codigo}.", (int)respuesta.StatusCode);
            return false;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("No se pudo conectar con Resend: {Tipo}.", e.GetType().Name);
            return false;
        }
    }

    /// <summary>Cuerpo de <c>POST /emails</c> con los nombres de campo de la API de Resend.</summary>
    private sealed record CuerpoResend(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("reply_to")] string ReplyTo);
}
