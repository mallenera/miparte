using MiParte.Contracts;

namespace MiParte.Web.Reparto;

/// <summary>Modos de reparto de un perfil (<c>PerfilRepartoDto.Modo</c>) y sus textos para la interfaz.</summary>
public static class ModosReparto
{
    /// <summary>Reparto por porcentaje fijo: el detalle debe sumar 100.</summary>
    public const string Porcentaje = "porcentaje";

    /// <summary>Reparto por partes: el detalle necesita alguna parte mayor que 0.</summary>
    public const string Partes = "partes";

    /// <summary>Reparto proporcional a los ingresos del mes; sin detalle.</summary>
    public const string Ingresos = "ingresos";

    /// <summary>Lo asume quien paga; sin detalle.</summary>
    public const string Individual = "individual";

    /// <summary>Modos en el orden en que se ofrecen.</summary>
    public static readonly IReadOnlyList<string> Todos = [Ingresos, Partes, Porcentaje, Individual];

    /// <summary>Texto del modo para mostrar al usuario.</summary>
    /// <param name="modo">Modo de la API.</param>
    public static string Texto(string modo) => modo switch
    {
        Ingresos => "Proporcional a ingresos",
        Partes => "Por partes",
        Porcentaje => "Porcentaje fijo",
        Individual => "Individual (quien paga)",
        _ => modo,
    };

    /// <summary>Indica si el modo lleva un valor por miembro.</summary>
    /// <param name="modo">Modo de la API.</param>
    public static bool LlevaDetalle(string modo) => modo is Porcentaje or Partes;
}

/// <summary>
/// Estado de edición de un perfil de reparto: nombre, modo y un valor por adulto activo. Aplica las mismas reglas que
/// la API (<c>docs/api.md</c> §3.4) para avisar antes de enviar.
/// </summary>
public sealed class EdicionPerfil
{
    /// <summary>Tolerancia de la suma de porcentajes, igual que en la API.</summary>
    public const decimal Tolerancia = 0.0001m;

    /// <summary>Máximo de caracteres del nombre.</summary>
    public const int MaxNombre = 100;

    private readonly List<Guid> _adultos;

    /// <summary>Nombre del perfil.</summary>
    public string Nombre { get; set; } = "";

    /// <summary>Modo de reparto.</summary>
    public string Modo { get; private set; } = ModosReparto.Partes;

    /// <summary>Valor por adulto activo (porcentaje o partes según el modo).</summary>
    public Dictionary<Guid, decimal> Valores { get; } = new();

    /// <summary>Crea la edición para los adultos activos indicados, con reparto a partes iguales (1 parte cada uno).</summary>
    /// <param name="adultos">Identificadores de los adultos activos del hogar.</param>
    public EdicionPerfil(IEnumerable<Guid> adultos)
    {
        _adultos = adultos.ToList();
        CambiarModo(ModosReparto.Partes);
    }

    /// <summary>Crea la edición a partir de un perfil existente; los adultos sin valor guardado quedan a 0.</summary>
    /// <param name="perfil">Perfil guardado.</param>
    /// <param name="adultos">Identificadores de los adultos activos del hogar.</param>
    public static EdicionPerfil Desde(PerfilRepartoDto perfil, IEnumerable<Guid> adultos)
    {
        var e = new EdicionPerfil(adultos) { Nombre = perfil.Nombre };
        e.Modo = perfil.Modo;
        e.Valores.Clear();
        foreach (var a in e._adultos) e.Valores[a] = perfil.Detalle.FirstOrDefault(d => d.MiembroId == a)?.Valor ?? 0m;
        return e;
    }

    /// <summary>Cambia de modo y reinicia los valores: partes a 1 cada uno; porcentaje con reparto igual que suma 100.</summary>
    /// <param name="modo">Nuevo modo.</param>
    public void CambiarModo(string modo)
    {
        Modo = modo;
        Valores.Clear();
        if (_adultos.Count == 0) return;
        if (modo == ModosReparto.Partes)
        {
            foreach (var a in _adultos) Valores[a] = 1m;
        }
        else if (modo == ModosReparto.Porcentaje)
        {
            var cada = Math.Round(100m / _adultos.Count, 2);
            foreach (var a in _adultos) Valores[a] = cada;
            Valores[_adultos[^1]] = 100m - cada * (_adultos.Count - 1);
        }
    }

    /// <summary>Suma de los valores de los adultos.</summary>
    public decimal Suma => Valores.Values.Sum();

    /// <summary>Mensaje de error de validación, o null si el perfil se puede guardar.</summary>
    public string? Error
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Nombre)) return "Indica el nombre del perfil.";
            if (Nombre.Trim().Length > MaxNombre) return $"El nombre admite como máximo {MaxNombre} caracteres.";
            if (!ModosReparto.LlevaDetalle(Modo)) return null;
            if (_adultos.Count == 0) return "Hace falta al menos un adulto activo en el hogar.";
            if (Valores.Values.Any(v => v < 0)) return "Los valores no pueden ser negativos.";
            if (Modo == ModosReparto.Porcentaje && Math.Abs(Suma - 100m) > Tolerancia)
                return $"Los porcentajes deben sumar 100 (ahora suman {Suma:0.##}).";
            if (Modo == ModosReparto.Partes && Suma <= 0) return "Alguna parte tiene que ser mayor que 0.";
            return null;
        }
    }

    /// <summary>Petición para la API; en los modos sin detalle no se envía ninguno.</summary>
    public GuardarPerfilRequest ARequest() => new(
        Nombre.Trim(), Modo,
        ModosReparto.LlevaDetalle(Modo)
            ? _adultos.Select(a => new PerfilDetalleDto(a, Valores.GetValueOrDefault(a))).ToList()
            : []);
}
