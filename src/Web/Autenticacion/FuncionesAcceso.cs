namespace MiParte.Web.Autenticacion;

/// <summary>
/// Interruptores de las formas de acceso. Están apagados por decisión de producto: el flujo de Google
/// (<see cref="ServicioSesion.UrlGoogleAsync"/> y <see cref="ServicioSesion.CompletarGoogleAsync"/>) se conserva
/// para reactivarlo cuando se revise, sin tocar la lógica de sesión.
/// </summary>
public static class FuncionesAcceso
{
    /// <summary>Muestra el botón «Acceder/Registrarse con Google» en Login y Registro.</summary>
    public static readonly bool GoogleActivo = false;
}
