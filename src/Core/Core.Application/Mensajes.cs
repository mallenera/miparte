namespace MiParte.Core.Application;

/// <summary>Mensajes de error compartidos entre casos de uso y la capa HTTP.</summary>
public static class Mensajes
{
    /// <summary>Se intenta tocar un mes cerrado.</summary>
    public const string MesCerrado = "Ese mes está cerrado: quien tenga permiso para reabrir meses (un administrador) debe reabrirlo para cambiar sus gastos.";

    /// <summary>El hogar tiene desactivados los gastos recurrentes.</summary>
    public const string RecurrentesInactivas = "Las recurrentes no están activadas en este hogar.";

    /// <summary>El hogar no ha activado la cuenta común.</summary>
    public const string CuentaInactiva = "La cuenta común no está activada en este hogar. Un administrador puede activarla en la configuración del hogar.";

    /// <summary>Quien intenta cerrar un mes no es miembro activo del hogar.</summary>
    public const string SoloMiembroCierra = "Solo un miembro activo del hogar puede cerrar un mes.";

    /// <summary>El hogar no ha activado el ahorro.</summary>
    public const string AhorroInactivo = "El ahorro no está activado en este hogar. Un administrador puede activarlo en la configuración del hogar.";

    /// <summary>Se intenta cerrar un ciclo en un hogar que liquida por meses.</summary>
    public const string HogarNoEsCiclos = "El hogar liquida por meses. Un administrador puede pasarlo a liquidación por ciclos en la configuración del hogar.";

    /// <summary>Se intenta cerrar un ciclo sin gastos pendientes de liquidar.</summary>
    public const string SinGastosPendientes = "No hay gastos sin liquidar";

    /// <summary>Se intenta registrar un pago de liquidación en un hogar que liquida por ciclos.</summary>
    public const string PagosEnCiclos = "En la liquidación por ciclos no se registran pagos: el grupo salda sus cuentas por su cuenta y después cierra la liquidación.";

    /// <summary>Otro cierre de ciclo se ha completado a la vez; hay que volver a mirar los gastos pendientes.</summary>
    public const string CierreConcurrente = "Otra persona acaba de cerrar la liquidación. Revisa los gastos pendientes y vuelve a intentarlo.";

    /// <summary>Se intenta crear, editar o borrar un gasto dentro del periodo propio ya cerrado del hogar.</summary>
    public const string PeriodoCerrado = "El periodo está cerrado.";

    /// <summary>Se intenta cerrar un periodo que ya estaba cerrado.</summary>
    public const string PeriodoYaCerrado = "El periodo ya está cerrado.";

    /// <summary>Se intenta reabrir un periodo que no estaba cerrado.</summary>
    public const string PeriodoNoCerrado = "El periodo no estaba cerrado.";
}
