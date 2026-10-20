using MiParte.Core.Application.Miembros;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Notificaciones;

/// <summary>Datos de una suscripción tal como llegan de la API, ya en tipos del dominio.</summary>
/// <param name="Endpoint">URL https del servicio push.</param>
/// <param name="P256dh">Clave pública del cliente (base64url).</param>
/// <param name="Auth">Secreto de autenticación (base64url).</param>
public sealed record DatosSuscripcionPush(string Endpoint, string P256dh, string Auth);

/// <summary>
/// Casos de uso de los avisos push del navegador: cada miembro con cuenta activa decide en su dispositivo si los quiere. Suscribirse
/// y darse de baja no exigen permiso: son preferencias personales.
/// </summary>
public sealed class ServicioNotificaciones(IRepositorioNotificaciones repositorio, IRepositorioMiembros miembros, TimeProvider reloj)
{
    /// <summary>Longitud máxima de un endpoint; los de los navegadores son mucho más cortos.</summary>
    public const int MaxLongitudEndpoint = 2048;

    /// <summary>Longitud máxima de cada clave de la suscripción (la clave p256dh mide 87 caracteres).</summary>
    public const int MaxLongitudClave = 256;

    private const string SinMiembro = "No perteneces a este hogar.";

    /// <summary>
    /// Registra la suscripción de este dispositivo para el miembro activo del usuario en el hogar. Si el mismo endpoint estaba
    /// registrado (quizá con otro miembro), la sustituye. 400 si el endpoint no es https o las claves faltan; 403 si el usuario no
    /// es miembro activo del hogar.
    /// </summary>
    public async Task<Resultado<bool>> SuscribirAsync(Guid userId, Guid hogarId, DatosSuscripcionPush datos, CancellationToken ct)
    {
        var yo = await miembros.MiembroActivoDeUsuarioAsync(userId, ct);
        if (yo is null || yo.HogarId != hogarId) return Resultado<bool>.Prohibido(SinMiembro);

        var endpoint = datos.Endpoint?.Trim() ?? "";
        if (!EsEndpointValido(endpoint)) return Resultado<bool>.Invalido("El endpoint de la suscripción debe ser una URL https válida.");
        var p256dh = datos.P256dh?.Trim() ?? "";
        var auth = datos.Auth?.Trim() ?? "";
        if (!EsClaveValida(p256dh) || !EsClaveValida(auth))
            return Resultado<bool>.Invalido("Faltan las claves de la suscripción.");

        var nueva = new SuscripcionPush
        {
            Id = Guid.NewGuid(), HogarId = hogarId, MiembroId = yo.Id, Endpoint = endpoint, P256dh = p256dh, Auth = auth,
            CreadaEn = reloj.GetUtcNow(),
        };
        await repositorio.ReemplazarAsync(endpoint, nueva, ct);
        return Resultado<bool>.Ok(true);
    }

    /// <summary>
    /// Quita la suscripción de este dispositivo para el miembro activo del usuario. Idempotente: si no existía, también es correcto.
    /// 403 si el usuario no es miembro activo del hogar.
    /// </summary>
    public async Task<Resultado<bool>> DesuscribirAsync(Guid userId, string endpoint, CancellationToken ct)
    {
        var yo = await miembros.MiembroActivoDeUsuarioAsync(userId, ct);
        if (yo is null) return Resultado<bool>.Prohibido(SinMiembro);

        await repositorio.EliminarAsync(yo.Id, endpoint?.Trim() ?? "", ct);
        return Resultado<bool>.Ok(true);
    }

    /// <summary>Indica si el endpoint es una URL absoluta https de tamaño razonable.</summary>
    /// <param name="endpoint">Endpoint recibido.</param>
    public static bool EsEndpointValido(string endpoint)
        => endpoint.Length is > 0 and <= MaxLongitudEndpoint
           && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>Indica si una clave de la suscripción es no vacía y de tamaño razonable.</summary>
    private static bool EsClaveValida(string clave) => clave.Length is > 0 and <= MaxLongitudClave;
}
