namespace MiParte.Core.Application.Contacto;

/// <summary>Correo ya compuesto que se envía al buzón de contacto del proyecto.</summary>
/// <param name="Asunto">Asunto del correo.</param>
/// <param name="Texto">Cuerpo en texto plano.</param>
/// <param name="ResponderA">Correo de quien escribe: al pulsar «Responder» se le contesta a esa persona.</param>
public sealed record CorreoContacto(string Asunto, string Texto, string ResponderA);

/// <summary>
/// Puerto de salida del correo de contacto. El remitente y el destino los fija la configuración de la implementación, nunca la
/// petición: así el endpoint público no puede usarse para mandar correo a terceros.
/// </summary>
public interface IEnviadorCorreo
{
    /// <summary>Indica si hay configuración para enviar; sin ella el formulario de contacto no está disponible.</summary>
    bool Disponible { get; }

    /// <summary>Envía el correo al buzón de contacto; devuelve false si el proveedor lo rechaza o no responde.</summary>
    /// <param name="correo">Correo compuesto.</param>
    /// <param name="ct">Token de cancelación.</param>
    Task<bool> EnviarAsync(CorreoContacto correo, CancellationToken ct);
}
