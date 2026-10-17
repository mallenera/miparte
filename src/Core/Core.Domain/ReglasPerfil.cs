using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>Valor asignado a un miembro dentro de un perfil de reparto (porcentaje o partes).</summary>
public sealed record ValorMiembro(Guid MiembroId, decimal Valor);

/// <summary>Reglas de los perfiles de reparto que no dependen de persistencia.</summary>
public static class ReglasPerfil
{
    /// <summary>Longitud máxima del nombre de un perfil.</summary>
    public const int MaxLongitudNombre = 100;

    /// <summary>Tolerancia admitida al comprobar que los porcentajes suman 100.</summary>
    public const decimal ToleranciaPorcentaje = 0.0001m;

    /// <summary>Normaliza y valida el nombre; devuelve el error o null y deja el nombre recortado en <paramref name="nombre"/>.</summary>
    public static string? ValidarNombre(string? texto, out string nombre)
    {
        nombre = texto?.Trim() ?? "";
        if (nombre.Length == 0) return "El nombre es obligatorio.";
        if (nombre.Length > MaxLongitudNombre) return $"El nombre admite como máximo {MaxLongitudNombre} caracteres.";
        return null;
    }

    /// <summary>
    /// Valida la forma del detalle según el modo: cuenta común e individual no llevan detalle; porcentaje y partes lo exigen,
    /// sin miembros repetidos. Devuelve el error o null.
    /// </summary>
    public static string? ValidarEstructura(ModoReparto modo, IReadOnlyList<ValorMiembro> detalle)
    {
        if (modo is ModoReparto.CuentaComun or ModoReparto.Individual)
            return detalle.Count > 0 ? $"El modo {TextoModoReparto.ATexto(modo)} no lleva detalle." : null;

        if (detalle.Count == 0) return "El detalle es obligatorio en este modo.";
        if (detalle.Select(d => d.MiembroId).Distinct().Count() != detalle.Count) return "Hay miembros repetidos en el detalle.";
        return null;
    }

    /// <summary>Valida los valores: no negativos; los porcentajes suman 100 y con partes alguna es mayor que 0. Devuelve el error o null.</summary>
    public static string? ValidarValores(ModoReparto modo, IReadOnlyList<ValorMiembro> detalle)
    {
        if (modo is ModoReparto.CuentaComun or ModoReparto.Individual) return null;
        if (detalle.Any(d => d.Valor < 0)) return "Los valores no pueden ser negativos.";
        if (modo == ModoReparto.Porcentaje)
            return Math.Abs(detalle.Sum(d => d.Valor) - 100m) > ToleranciaPorcentaje ? "Los porcentajes deben sumar 100." : null;
        return detalle.All(d => d.Valor == 0) ? "Alguna parte debe ser mayor que 0." : null;
    }
}
