namespace MiParte.Web.Demo;

/// <summary>
/// Constantes del <b>modo demo local</b> (botón «Probar la demo»): una sesión ficticia con datos en memoria que no toca
/// ningún servidor. No hay cuenta demo compartida: las cuentas de prueba se crean y se entregan aparte, sin publicarlas.
/// </summary>
public static class CuentaDemo
{
    /// <summary>Identificador de usuario de la sesión local de demo; ningún usuario real de Supabase puede tenerlo.</summary>
    public const string IdUsuarioLocal = "00000000-0000-0000-0000-00000000de30";

    /// <summary>Indica si la sesión pertenece al modo demo local.</summary>
    /// <param name="userId">Identificador de usuario de la sesión.</param>
    public static bool EsLocal(string? userId) => userId == IdUsuarioLocal;
}
