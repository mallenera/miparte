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

    /// <summary>
    /// Dispositivos con avisos por miembro. Sin tope, una sola persona podría registrar cientos de endpoints y cada gasto del hogar
    /// provocaría otras tantas peticiones salientes del servidor.
    /// </summary>
    public const int MaxSuscripcionesPorMiembro = 10;

    /// <summary>
    /// Servicios push de los navegadores (Chrome y derivados, Firefox, Edge y Safari). El servidor hace una petición al endpoint en
    /// cada aviso, así que solo se aceptan estos destinos: con cualquier URL https, un usuario podría dirigir esas peticiones a
    /// máquinas internas o de terceros (SSRF). Cada entrada vale para el host exacto y sus subdominios.
    /// </summary>
    public static readonly IReadOnlyList<string> HostsPermitidos =
    [
        "fcm.googleapis.com",
        "updates.push.services.mozilla.com",
        "notify.windows.com",
        "push.apple.com",
    ];

    private const string SinMiembro = "No perteneces a este hogar.";

    /// <summary>
    /// Registra la suscripción de este dispositivo para el miembro activo del usuario en el hogar. Si el mismo endpoint estaba
    /// registrado (quizá con otro miembro), la sustituye. 400 si el endpoint no es una URL https de un servicio push conocido o las
    /// claves faltan; 403 si el usuario no es miembro activo del hogar; 409 si ya tiene <see cref="MaxSuscripcionesPorMiembro"/>
    /// dispositivos.
    /// </summary>
    public async Task<Resultado<bool>> SuscribirAsync(Guid userId, Guid hogarId, DatosSuscripcionPush datos, CancellationToken ct)
    {
        var yo = await miembros.MiembroActivoDeUsuarioAsync(userId, ct);
        if (yo is null || yo.HogarId != hogarId) return Resultado<bool>.Prohibido(SinMiembro);

        var endpoint = datos.Endpoint?.Trim() ?? "";
        if (!EsEndpointValido(endpoint))
            return Resultado<bool>.Invalido("El endpoint de la suscripción debe ser una URL https del servicio de avisos de un navegador.");
        var p256dh = datos.P256dh?.Trim() ?? "";
        var auth = datos.Auth?.Trim() ?? "";
        if (!EsClaveValida(p256dh) || !EsClaveValida(auth))
            return Resultado<bool>.Invalido("Faltan las claves de la suscripción.");
        // El mismo endpoint se sustituye, así que no cuenta como dispositivo nuevo.
        if (await repositorio.ContarOtrasDeMiembroAsync(yo.Id, endpoint, ct) >= MaxSuscripcionesPorMiembro)
            return Resultado<bool>.Conflicto(
                $"Ya tienes avisos activados en {MaxSuscripcionesPorMiembro} dispositivos. Desactívalos en alguno antes de añadir otro.");

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

    /// <summary>
    /// Indica si el endpoint es una URL absoluta https de tamaño razonable, en el puerto por defecto, sin credenciales y de uno de los
    /// <see cref="HostsPermitidos"/>.
    /// </summary>
    /// <param name="endpoint">Endpoint recibido.</param>
    public static bool EsEndpointValido(string endpoint)
        => endpoint.Length is > 0 and <= MaxLongitudEndpoint
           && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
           && uri.IsDefaultPort && uri.UserInfo.Length == 0
           && HostsPermitidos.Any(h => uri.IdnHost.Equals(h, StringComparison.OrdinalIgnoreCase)
                                       || uri.IdnHost.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

    /// <summary>Indica si una clave de la suscripción es no vacía y de tamaño razonable.</summary>
    private static bool EsClaveValida(string clave) => clave.Length is > 0 and <= MaxLongitudClave;
}
