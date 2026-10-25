using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>Texto con el que un modo de reparto viaja por la API y se guarda en base de datos.</summary>
public static class TextoModoReparto
{
    /// <summary>Convierte el modo al texto: porcentaje, partes, cuenta_comun, cuenta_comun_ahorro o individual.</summary>
    public static string ATexto(ModoReparto m) => m switch
    {
        ModoReparto.Porcentaje => "porcentaje",
        ModoReparto.Partes => "partes",
        ModoReparto.CuentaComun => "cuenta_comun",
        ModoReparto.CuentaComunAhorro => "cuenta_comun_ahorro",
        _ => "individual",
    };

    /// <summary>Interpreta el texto del modo (sin distinguir mayúsculas ni espacios laterales); false si no es válido.</summary>
    public static bool TryParse(string? texto, out ModoReparto modo)
    {
        switch (texto?.Trim().ToLowerInvariant())
        {
            case "porcentaje": modo = ModoReparto.Porcentaje; return true;
            case "partes": modo = ModoReparto.Partes; return true;
            case "cuenta_comun": modo = ModoReparto.CuentaComun; return true;
            case "cuenta_comun_ahorro": modo = ModoReparto.CuentaComunAhorro; return true;
            case "individual": modo = ModoReparto.Individual; return true;
            default: modo = default; return false;
        }
    }
}

/// <summary>Qué modos de reparto van a cargo de la cuenta común.</summary>
public static class ModosDeCuenta
{
    /// <summary>
    /// Si el modo carga el gasto a la cuenta común (de su saldo de gastos o de su ahorro): sin reparto entre personas, sin deuda y
    /// fuera de la liquidación.
    /// </summary>
    public static bool EsDeCuentaComun(ModoReparto modo) => modo is ModoReparto.CuentaComun or ModoReparto.CuentaComunAhorro;
}
