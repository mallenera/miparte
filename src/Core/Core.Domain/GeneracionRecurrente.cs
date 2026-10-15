using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>Reglas de los gastos recurrentes (plantillas mensuales) y de los gastos que generan; no dependen de persistencia.</summary>
public static class GeneracionRecurrente
{
    /// <summary>Último día del mes en el que se puede fijar una plantilla: todos los meses lo tienen.</summary>
    public const int MaxDiaMes = 28;

    /// <summary>Valida el día del mes de una plantilla (1 a 28); devuelve el mensaje de error o null.</summary>
    public static string? ValidarDiaMes(int diaMes)
        => diaMes is < 1 or > MaxDiaMes ? $"El día del mes debe estar entre 1 y {MaxDiaMes}." : null;

    /// <summary>
    /// Crea el gasto de un mes a partir de una plantilla, con su reparto calculado según el perfil. Devuelve null y el
    /// motivo en <paramref name="error"/> si no se puede repartir. El gasto queda a cargo de la cuenta común si el perfil lo es.
    /// </summary>
    public static Gasto? CrearGasto(
        GastoRecurrente plantilla, DateOnly inicioMes, PerfilReparto perfil, IReadOnlyList<Miembro> adultos, out string? error)
    {
        var partes = RepartoGasto.Repartir(perfil, adultos, plantilla.Importe, plantilla.PagadoPor, out error);
        if (partes is null) return null;

        var gasto = new Gasto
        {
            Id = Guid.NewGuid(), HogarId = plantilla.HogarId, Fecha = new DateOnly(inicioMes.Year, inicioMes.Month, plantilla.DiaMes),
            Importe = plantilla.Importe, CategoriaId = plantilla.CategoriaId, PagadoPor = plantilla.PagadoPor,
            PerfilRepartoId = plantilla.PerfilRepartoId, Concepto = plantilla.Concepto, GastoRecurrenteId = plantilla.Id,
            ACargoCuentaComun = perfil.Modo == ModoReparto.CuentaComun,
        };
        RepartoGasto.Aplicar(gasto, partes);
        return gasto;
    }
}
