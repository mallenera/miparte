namespace MiParte.Core.Api.Notificaciones;

/// <summary>
/// Claves VAPID de los avisos del navegador (sección <c>Notificaciones</c>; variables <c>Notificaciones__VapidPublica</c>,
/// <c>Notificaciones__VapidPrivada</c> y <c>Notificaciones__Contacto</c>). La privada es secreta: solo por entorno, nunca en el repo.
/// </summary>
public sealed class OpcionesPush
{
    /// <summary>Nombre de la sección de configuración.</summary>
    public const string Seccion = "Notificaciones";

    /// <summary>Clave pública VAPID en base64url; el navegador la usa para suscribirse.</summary>
    public string VapidPublica { get; set; } = "";

    /// <summary>Clave privada VAPID en base64url; firma los envíos.</summary>
    public string VapidPrivada { get; set; } = "";

    /// <summary>Contacto del servidor de avisos (<c>mailto:</c> o <c>https:</c>), que los servicios push pueden usar.</summary>
    public string Contacto { get; set; } = "";

    /// <summary>Indica si hay claves completas; sin ellas no se envía nada y la API no ofrece clave pública.</summary>
    public bool Configurado
        => !string.IsNullOrWhiteSpace(VapidPublica) && !string.IsNullOrWhiteSpace(VapidPrivada) && !string.IsNullOrWhiteSpace(Contacto);
}
