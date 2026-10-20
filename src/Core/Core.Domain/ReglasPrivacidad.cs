namespace MiParte.Core.Domain;

/// <summary>
/// Privacidad de los gastos personales: un gasto personal solo lo ve quien lo paga. Ni los admins ni los demás miembros lo ven,
/// y un miembro desactivado no tiene acceso al hogar, así que sus personales quedan guardados sin verse hasta que vuelva a activarse.
/// </summary>
public static class ReglasPrivacidad
{
    /// <summary>Indica si un gasto con esos datos puede verlo <paramref name="yo"/> (el miembro de la petición, o null).</summary>
    /// <param name="esPersonal">Si el gasto es personal.</param>
    /// <param name="pagadoPor">Quien paga el gasto: el único que ve un personal.</param>
    /// <param name="yo">Miembro que hace la consulta, o null si no hay miembro activo.</param>
    public static bool EsVisible(bool esPersonal, Guid? pagadoPor, Guid? yo)
        => !esPersonal || (yo is not null && pagadoPor == yo);
}
