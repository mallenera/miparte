namespace MiParte.Core.Application;

/// <summary>Mensajes de error compartidos entre casos de uso y la capa HTTP.</summary>
public static class Mensajes
{
    /// <summary>Se intenta tocar un mes cerrado.</summary>
    public const string MesCerrado = "Ese mes está cerrado: quien tenga permiso para reabrir meses (un administrador) debe reabrirlo para cambiar sus gastos.";

    /// <summary>El hogar no ha activado la cuenta común.</summary>
    public const string CuentaInactiva = "La cuenta común no está activada en este hogar. Un administrador puede activarla en la configuración del hogar.";

    /// <summary>Quien intenta cerrar un mes no es miembro activo del hogar.</summary>
    public const string SoloMiembroCierra = "Solo un miembro activo del hogar puede cerrar un mes.";

    /// <summary>El hogar no ha activado el ahorro.</summary>
    public const string AhorroInactivo = "El ahorro no está activado en este hogar. Un administrador puede activarlo en la configuración del hogar.";
}
