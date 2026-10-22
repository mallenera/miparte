using System.Net.Mail;

namespace MiParte.Core.Application.Contacto;

/// <summary>Mensaje del formulario de contacto tal como llega de la API.</summary>
/// <param name="Motivo">Clave del motivo: <c>duda</c>, <c>mejora</c> u <c>otro</c>.</param>
/// <param name="Email">Correo de quien escribe, para poder contestarle.</param>
/// <param name="Mensaje">Texto del mensaje.</param>
/// <param name="Nombre">Nombre de quien escribe; opcional.</param>
/// <param name="Trampa">Campo oculto del formulario: una persona lo deja vacío, un bot suele rellenarlo.</param>
public sealed record MensajeContacto(string? Motivo, string? Email, string? Mensaje, string? Nombre, string? Trampa);

/// <summary>
/// Caso de uso del formulario de contacto: valida el mensaje, compone el correo y lo entrega al <see cref="IEnviadorCorreo"/>. No
/// guarda nada en la base de datos.
/// </summary>
public sealed class ServicioContacto(IEnviadorCorreo enviador)
{
    /// <summary>Longitud mínima del mensaje: menos no da para entender qué se pide.</summary>
    public const int MinLongitudMensaje = 10;

    /// <summary>Longitud máxima del mensaje.</summary>
    public const int MaxLongitudMensaje = 4000;

    /// <summary>Longitud máxima del nombre.</summary>
    public const int MaxLongitudNombre = 100;

    /// <summary>Longitud máxima de una dirección de correo (RFC 5321).</summary>
    public const int MaxLongitudEmail = 254;

    /// <summary>Motivos admitidos con el asunto del correo de cada uno. El asunto sale de aquí y nunca del texto recibido.</summary>
    public static readonly IReadOnlyDictionary<string, string> Asuntos = new Dictionary<string, string>
    {
        ["duda"] = "Mi parte, tu parte: duda",
        ["mejora"] = "Mi parte, tu parte: propuesta de mejora",
        ["otro"] = "Mi parte, tu parte: contacto",
    };

    /// <summary>
    /// Envía el mensaje al buzón de contacto. 400 si el motivo, el correo o el mensaje no son válidos; 503 si no hay envío configurado
    /// o el proveedor falla. Con el campo trampa relleno responde como si se hubiera enviado, para no enseñar al bot qué lo delata.
    /// </summary>
    public async Task<Resultado<bool>> EnviarAsync(MensajeContacto datos, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(datos.Trampa)) return Resultado<bool>.Ok(true);
        if (!enviador.Disponible) return NoDisponible();

        if (datos.Motivo is null || !Asuntos.TryGetValue(datos.Motivo, out var asunto))
            return Resultado<bool>.Invalido("El motivo no es válido.");
        var email = datos.Email?.Trim() ?? "";
        if (!EsEmailValido(email)) return Resultado<bool>.Invalido("Escribe un correo válido para poder contestarte.");
        var mensaje = datos.Mensaje?.Trim() ?? "";
        if (mensaje.Length < MinLongitudMensaje)
            return Resultado<bool>.Invalido($"El mensaje debe tener al menos {MinLongitudMensaje} caracteres.");
        if (mensaje.Length > MaxLongitudMensaje)
            return Resultado<bool>.Invalido($"El mensaje no puede superar los {MaxLongitudMensaje} caracteres.");
        // Sin saltos de línea: el nombre va en una línea del cuerpo y no debe poder simular otras.
        var nombre = string.Join(' ', (datos.Nombre ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (nombre.Length > MaxLongitudNombre)
            return Resultado<bool>.Invalido($"El nombre no puede superar los {MaxLongitudNombre} caracteres.");

        var texto = $"""
            Motivo: {asunto}
            Nombre: {(nombre.Length > 0 ? nombre : "(sin nombre)")}
            Correo: {email}

            {mensaje}
            """;
        return await enviador.EnviarAsync(new CorreoContacto(asunto, texto, email), ct)
            ? Resultado<bool>.Ok(true)
            : NoDisponible();
    }

    /// <summary>
    /// Indica si el texto es una única dirección de correo con dominio. <see cref="MailAddress"/> también acepta formatos con
    /// nombre («Ana &lt;a@b.es&gt;») o listas; se exige que la dirección sea el texto completo.
    /// </summary>
    /// <param name="email">Correo ya sin espacios alrededor.</param>
    public static bool EsEmailValido(string email)
        => email.Length is > 0 and <= MaxLongitudEmail
           && MailAddress.TryCreate(email, out var direccion)
           && direccion.Address == email
           && direccion.Host.Contains('.');

    private static Resultado<bool> NoDisponible()
        => Resultado<bool>.Fallo(new(TipoError.NoDisponible,
            "No se ha podido enviar el mensaje ahora mismo. Inténtalo más tarde o escríbenos directamente por correo."));
}
