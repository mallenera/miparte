namespace MiParte.Core.Domain.Entidades;

/// <summary>
/// Dispositivo de un miembro que aceptó los avisos del navegador (tabla <c>suscripcion_push</c>). El endpoint y las claves son
/// secretos de cada suscripción: la API nunca los devuelve.
/// </summary>
public class SuscripcionPush
{
    /// <summary>Identificador de la suscripción.</summary>
    public Guid Id { get; set; }
    /// <summary>Hogar al que pertenece.</summary>
    public Guid HogarId { get; set; }
    /// <summary>Miembro que recibe los avisos en este dispositivo.</summary>
    public Guid MiembroId { get; set; }
    /// <summary>URL del servicio push del navegador; es única en todo el sistema.</summary>
    public string Endpoint { get; set; } = "";
    /// <summary>Clave pública del cliente (p256dh, base64url).</summary>
    public string P256dh { get; set; } = "";
    /// <summary>Secreto de autenticación del cliente (auth, base64url).</summary>
    public string Auth { get; set; } = "";
    /// <summary>Fecha de alta.</summary>
    public DateTimeOffset CreadaEn { get; set; }
}
