namespace MiParte.Web.Autenticacion;

/// <summary>
/// Guarda el código de una invitación recibida por enlace mientras la persona inicia sesión o crea su cuenta. El enlace
/// lleva el código en el fragmento (<c>/unirse#token=...</c>), que el navegador no envía al servidor ni a terceros.
/// </summary>
public static class InvitacionPendiente
{
    private const string Clave = "miparte.invitacion";
    private const string Parametro = "token=";

    /// <summary>Ruta de la página que acepta la invitación.</summary>
    public const string Ruta = "unirse";

    /// <summary>Construye el enlace de invitación para un código; el código va en el fragmento, nunca en la query.</summary>
    /// <param name="urlBase">URL absoluta de la página <see cref="Ruta"/>.</param>
    /// <param name="token">Código de la invitación.</param>
    public static string ConstruirEnlace(string urlBase, string token) => $"{urlBase}#{Parametro}{Uri.EscapeDataString(token)}";

    /// <summary>Extrae el código del fragmento de una URL, o null si no lo trae.</summary>
    /// <param name="url">URL completa.</param>
    public static string? ExtraerToken(string url)
    {
        var i = url.IndexOf('#');
        if (i < 0) return null;
        foreach (var par in url[(i + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!par.StartsWith(Parametro, StringComparison.Ordinal)) continue;
            var valor = Uri.UnescapeDataString(par[Parametro.Length..]).Trim();
            return valor.Length == 0 ? null : valor;
        }
        return null;
    }

    /// <summary>Guarda el código hasta que la persona lo acepte.</summary>
    /// <param name="almacen">Almacén del navegador.</param>
    /// <param name="token">Código de la invitación.</param>
    public static Task GuardarAsync(IAlmacenLocal almacen, string token) => almacen.EscribirAsync(Clave, token);

    /// <summary>Lee el código guardado, o null.</summary>
    /// <param name="almacen">Almacén del navegador.</param>
    public static Task<string?> LeerAsync(IAlmacenLocal almacen) => almacen.LeerAsync(Clave);

    /// <summary>Descarta el código guardado.</summary>
    /// <param name="almacen">Almacén del navegador.</param>
    public static Task DescartarAsync(IAlmacenLocal almacen) => almacen.EliminarAsync(Clave);

    /// <summary>Destino tras iniciar sesión: la página de unirse si hay una invitación pendiente, la raíz si no.</summary>
    /// <param name="almacen">Almacén del navegador.</param>
    public static async Task<string> DestinoTrasAccesoAsync(IAlmacenLocal almacen) =>
        await LeerAsync(almacen) is null ? "" : Ruta;
}
