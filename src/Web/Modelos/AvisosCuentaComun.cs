using MiParte.Contracts;

namespace MiParte.Web.Modelos;

/// <summary>
/// Lo que la cuenta común debe a una persona en cada fondo, neto de los adelantos de la cuenta. Negativo: es ella la que debe a la cuenta.
/// </summary>
public sealed record PersonaCuenta(Guid MiembroId, string Nombre, decimal Hogar, decimal Ahorro);

/// <summary>
/// Avisos de la cuenta común que se leen de un vistazo: si el ahorro o el efectivo no alcanzan para lo que la cuenta le debe a las
/// personas, y lo que falta por reembolsar a cada una. Las cifras vienen de la API; aquí solo se suman y se formatean.
/// </summary>
public static class AvisosCuentaComun
{
    /// <summary>Avisos del mes; vacío si el ahorro y el efectivo cubren lo que la cuenta debe.</summary>
    public static IReadOnlyList<string> Avisos(CuentaComunResponse estado)
    {
        var avisos = new List<string>();

        var debeAhorro = estado.Pendientes.Where(p => p.DesdeAhorro).Sum(p => p.Importe);
        var faltaAhorro = debeAhorro - estado.AhorroDisponible;
        if (faltaAhorro > 0)
        {
            avisos.Add(
                $"El ahorro no cubre lo que debe a las personas: faltan {Formato.Euros(faltaAhorro)} " +
                $"(debe {Formato.Euros(debeAhorro)} y quedan {Formato.Euros(estado.AhorroDisponible)} disponibles).");
        }

        // Los reembolsos salen del efectivo de Hogar aunque el gasto se pagara desde el ahorro: el efectivo ya los contiene.
        var debeAPersonas = estado.Pendientes.Where(p => p.Importe > 0).Sum(p => p.Importe);
        var faltaEfectivo = debeAPersonas - estado.Efectivo;
        if (faltaEfectivo > 0)
        {
            avisos.Add(
                $"El efectivo no alcanza para reembolsar: faltan {Formato.Euros(faltaEfectivo)} " +
                $"(hay que reembolsar {Formato.Euros(debeAPersonas)} y hay {Formato.Euros(estado.Efectivo)}).");
        }

        return avisos;
    }

    /// <summary>Lo pendiente con la cuenta de cada persona, por fondo, ordenado por nombre.</summary>
    public static IReadOnlyList<PersonaCuenta> PorPersona(IEnumerable<PendienteCuentaDto> pendientes) =>
        pendientes.GroupBy(p => (p.MiembroId, p.Nombre))
            .Select(g => new PersonaCuenta(
                g.Key.MiembroId, g.Key.Nombre,
                g.Where(p => !p.DesdeAhorro).Sum(p => p.Importe),
                g.Where(p => p.DesdeAhorro).Sum(p => p.Importe)))
            .Where(p => p.Hogar != 0 || p.Ahorro != 0)
            .OrderBy(p => p.Nombre)
            .ToList();

    /// <summary>Frases de una persona: «falta reembolsar…» si la cuenta le debe, «debe a la cuenta…» si lo debe ella.</summary>
    public static IReadOnlyList<string> Lineas(PersonaCuenta persona)
    {
        var lineas = new List<string>();
        if (persona.Hogar > 0) lineas.Add($"falta reembolsar {Formato.Euros(persona.Hogar)} de Hogar");
        if (persona.Ahorro > 0) lineas.Add($"falta reembolsar {Formato.Euros(persona.Ahorro)} del ahorro");
        if (persona.Hogar < 0) lineas.Add($"debe {Formato.Euros(-persona.Hogar)} a Hogar");
        if (persona.Ahorro < 0) lineas.Add($"debe {Formato.Euros(-persona.Ahorro)} al ahorro");
        return lineas;
    }
}
