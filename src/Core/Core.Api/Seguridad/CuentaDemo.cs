using System.Security.Claims;

namespace MiParte.Core.Api.Seguridad;

/// <summary>
/// La cuenta demo real (ver <c>docs/demo.md</c>) tiene la contraseña publicada a propósito, así que cualquiera actúa con ella. Lo que
/// hace dentro de su hogar se deshace con el restablecimiento diario; lo que deja rastro fuera de él (hogares nuevos, que el
/// restablecimiento no borra) o hace que el servidor llame a otros sistemas (suscripciones push) se le cierra con
/// <see cref="NoDisponibleEnDemo"/>.
/// </summary>
public static class CuentaDemo
{
    /// <summary>Correo de la cuenta demo en Supabase Auth; el mismo que <c>supabase/seed/demo.sql</c> y el front.</summary>
    public const string Correo = "demo@miparte.example";

    /// <summary>Indica si el token validado es de la cuenta demo (claim <c>email</c> del JWT de Supabase).</summary>
    /// <param name="usuario">Usuario autenticado de la petición.</param>
    public static bool Es(ClaimsPrincipal usuario)
        => string.Equals(usuario.FindFirst("email")?.Value, Correo, StringComparison.OrdinalIgnoreCase);

    /// <summary>Responde 403 a la cuenta demo en este endpoint; el resto de usuarios no nota nada.</summary>
    /// <param name="builder">Endpoint o grupo al que se aplica.</param>
    public static TBuilder NoDisponibleEnDemo<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.AddEndpointFilter(async (contexto, siguiente) => Es(contexto.HttpContext.User)
            ? Results.Json(
                new { error = "Esta acción no está disponible en la cuenta de demostración. Crea tu propia cuenta para usarla." },
                statusCode: StatusCodes.Status403Forbidden)
            : await siguiente(contexto));
}
