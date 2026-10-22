namespace MiParte.Core.Infrastructure.Correo;

/// <summary>
/// Envío del correo de contacto por la API HTTP de Resend (sección <c>Correo</c>; variables <c>Correo__ResendApiKey</c>,
/// <c>Correo__Remitente</c> y <c>Correo__Destino</c>). Se usa HTTP y no SMTP porque el plan gratuito de Render bloquea los puertos
/// SMTP de salida. La clave es secreta: solo por entorno, nunca en el repo.
/// </summary>
public sealed class OpcionesCorreo
{
    /// <summary>Nombre de la sección de configuración.</summary>
    public const string Seccion = "Correo";

    /// <summary>Clave de la API de Resend (<c>re_...</c>).</summary>
    public string ResendApiKey { get; set; } = "";

    /// <summary>
    /// Remitente, con un dominio verificado en Resend o <c>onboarding@resend.dev</c> (este solo entrega al correo de la propia
    /// cuenta de Resend). Admite «Nombre &lt;correo&gt;».
    /// </summary>
    public string Remitente { get; set; } = "Mi parte, tu parte <onboarding@resend.dev>";

    /// <summary>Buzón que recibe los mensajes del formulario de contacto.</summary>
    public string Destino { get; set; } = "";

    /// <summary>Indica si hay clave y destino; sin ellos el formulario responde 503 y el front ofrece escribir por correo.</summary>
    public bool Configurado
        => !string.IsNullOrWhiteSpace(ResendApiKey) && !string.IsNullOrWhiteSpace(Remitente) && !string.IsNullOrWhiteSpace(Destino);
}
