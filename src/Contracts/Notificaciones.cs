namespace MiParte.Contracts;

/// <summary>Suscripción del navegador a los avisos push (datos de <c>PushManager.subscribe</c>). El endpoint debe ser https.</summary>
/// <param name="Endpoint">URL del servicio push del navegador.</param>
/// <param name="P256dh">Clave pública del cliente (base64url).</param>
/// <param name="Auth">Secreto de autenticación del cliente (base64url).</param>
public record SuscripcionPushRequest(string Endpoint, string P256dh, string Auth);

/// <summary>Baja de la suscripción de este dispositivo, identificada por su endpoint.</summary>
/// <param name="Endpoint">Endpoint de la suscripción que se quita.</param>
public record BajaSuscripcionPushRequest(string Endpoint);

/// <summary>Clave pública VAPID con la que el navegador firma la suscripción (<c>applicationServerKey</c>).</summary>
/// <param name="ClavePublica">Clave pública VAPID en base64url.</param>
public record ClavePublicaPushResponse(string ClavePublica);
