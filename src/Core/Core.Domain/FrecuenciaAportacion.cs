namespace MiParte.Core.Domain;

/// <summary>
/// Con qué frecuencia aporta el hogar a la cuenta común. <see cref="Unica"/> es una sola aportación por persona para el grupo,
/// sin periodo; las demás son periódicas y cubren un número fijo de meses (ver <see cref="ReglasFrecuenciaAportacion"/>).
/// </summary>
public enum FrecuenciaAportacion
{
    /// <summary>Una sola aportación por persona, sin periodo (p. ej. para un viaje).</summary>
    Unica,

    /// <summary>Cada mes: el comportamiento de siempre, y el de todos los hogares existentes.</summary>
    Mensual,

    /// <summary>Cada dos meses.</summary>
    Bimestral,

    /// <summary>Cada tres meses.</summary>
    Trimestral,

    /// <summary>Cada seis meses.</summary>
    Semestral,

    /// <summary>Cada doce meses.</summary>
    Anual,
}

/// <summary>
/// Reglas de las frecuencias de aportación. Una aportación periódica cobra su importe una vez por periodo, en el primer mes
/// del periodo: bimestral desde 2026-01 cobra en enero y su periodo cubre enero y febrero; el siguiente cobro es en marzo.
/// Una aportación única cobra una vez, en su mes de inicio, y no tiene periodo.
/// </summary>
public static class ReglasFrecuenciaAportacion
{
    /// <summary>Meses que cubre cada cobro de una frecuencia periódica; 0 para la única, que no tiene periodo.</summary>
    /// <param name="frecuencia">Frecuencia de la aportación.</param>
    public static int MesesPorPeriodo(FrecuenciaAportacion frecuencia) => frecuencia switch
    {
        FrecuenciaAportacion.Unica => 0,
        FrecuenciaAportacion.Mensual => 1,
        FrecuenciaAportacion.Bimestral => 2,
        FrecuenciaAportacion.Trimestral => 3,
        FrecuenciaAportacion.Semestral => 6,
        FrecuenciaAportacion.Anual => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(frecuencia)),
    };

    /// <summary>Si la frecuencia tiene periodo: todas menos la única.</summary>
    /// <param name="frecuencia">Frecuencia de la aportación.</param>
    public static bool TienePeriodo(FrecuenciaAportacion frecuencia) => frecuencia != FrecuenciaAportacion.Unica;

    /// <summary>
    /// Periodo que cubre el mes <paramref name="mes"/> para una aportación que empieza en <paramref name="desde"/>, con los
    /// periodos contados desde ese mes. Es un rango de días completo (del primer al último día de sus meses). Null si la
    /// frecuencia es la única o si el mes es anterior al inicio.
    /// </summary>
    /// <param name="frecuencia">Frecuencia de la aportación.</param>
    /// <param name="desde">Mes en que empieza la aportación (se usa su día 1).</param>
    /// <param name="mes">Cualquier día del mes consultado.</param>
    public static Periodo? PeriodoCubierto(FrecuenciaAportacion frecuencia, DateOnly desde, DateOnly mes)
    {
        if (!TienePeriodo(frecuencia)) return null;
        var inicio = InicioMes(desde);
        var consultado = InicioMes(mes);
        if (consultado < inicio) return null;

        var n = MesesPorPeriodo(frecuencia);
        var meses = (consultado.Year - inicio.Year) * 12 + consultado.Month - inicio.Month;
        var comienzo = inicio.AddMonths(meses / n * n);
        return Periodo.Rango(comienzo, comienzo.AddMonths(n).AddDays(-1));
    }

    /// <summary>
    /// Si el mes <paramref name="mes"/> es un mes de cobro: el primero del periodo (periódica) o el mes de inicio (única).
    /// </summary>
    /// <param name="frecuencia">Frecuencia de la aportación.</param>
    /// <param name="desde">Mes en que empieza la aportación.</param>
    /// <param name="mes">Mes consultado.</param>
    public static bool EsMesDeCobro(FrecuenciaAportacion frecuencia, DateOnly desde, DateOnly mes)
    {
        if (!TienePeriodo(frecuencia)) return InicioMes(mes) == InicioMes(desde);
        return PeriodoCubierto(frecuencia, desde, mes) is { } periodo && periodo.Desde == InicioMes(mes);
    }

    /// <summary>Primer día del mes de <paramref name="fecha"/>.</summary>
    private static DateOnly InicioMes(DateOnly fecha) => new(fecha.Year, fecha.Month, 1);
}
