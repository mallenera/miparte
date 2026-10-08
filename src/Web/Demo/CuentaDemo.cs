namespace MiParte.Web.Demo;

/// <summary>
/// Constantes de las dos formas de probar la aplicación sin registrarse: el <b>modo demo local</b> (botón «Probar la demo»,
/// datos en memoria sin tocar ningún servidor) y la <b>cuenta demo real</b> (usuario <c>demo</c>, contraseña <c>demo</c>,
/// que entra por Supabase y Core.Api con un hogar de ejemplo sembrado por <c>supabase/seed/demo.sql</c>).
/// </summary>
public static class CuentaDemo
{
    /// <summary>Usuario que se escribe en el formulario de acceso para entrar con la cuenta demo real.</summary>
    public const string Usuario = "demo";

    /// <summary>Contraseña de la cuenta demo real. Es pública a propósito: la cuenta solo contiene datos de ejemplo.</summary>
    public const string Contrasena = "demo";

    /// <summary>Correo con el que existe la cuenta demo real en Supabase Auth (el dominio <c>.example</c> está reservado y no recibe correo).</summary>
    public const string Correo = "demo@miparte.example";

    /// <summary>Identificador de usuario de la sesión local de demo; ningún usuario real de Supabase puede tenerlo.</summary>
    public const string IdUsuarioLocal = "00000000-0000-0000-0000-00000000de30";

    /// <summary>Traduce el usuario escrito en el formulario: <c>demo</c> (sin distinguir mayúsculas) pasa a ser el correo de la cuenta demo real.</summary>
    /// <param name="usuario">Texto del campo de acceso.</param>
    public static string ACorreo(string usuario) =>
        string.Equals(usuario.Trim(), Usuario, StringComparison.OrdinalIgnoreCase) ? Correo : usuario.Trim();

    /// <summary>Indica si la sesión pertenece al modo demo local.</summary>
    /// <param name="userId">Identificador de usuario de la sesión.</param>
    public static bool EsLocal(string? userId) => userId == IdUsuarioLocal;
}
