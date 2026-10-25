using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>Parte de un miembro a cargo en un gasto: cuenta en el reparto, pero la asume su responsable.</summary>
/// <param name="MiembroId">Miembro a cargo que tiene la parte.</param>
/// <param name="ResponsableId">Adulto que asume esa parte en su total.</param>
/// <param name="Importe">Importe de la parte del miembro a cargo.</param>
public sealed record ParteACargo(Guid MiembroId, Guid ResponsableId, decimal Importe);

/// <summary>
/// Resultado del reparto con dependientes: lo que asume cada adulto (ya incluye la parte de sus dependientes) y el
/// desglose de esas partes, para mostrarlas como «Lorien (a cargo de Nuria)».
/// </summary>
/// <param name="PorAdulto">Importe que asume cada adulto activo.</param>
/// <param name="ACargo">Partes de los miembros a cargo con importe mayor que cero.</param>
public sealed record ResultadoReparto(IReadOnlyList<ParteAsumida> PorAdulto, IReadOnlyList<ParteACargo> ACargo);

/// <summary>Reparto de un gasto entre los adultos activos (y sus dependientes) según el perfil, y su aplicación sobre la entidad.</summary>
public static class RepartoGasto
{
    /// <summary>
    /// Calcula el reparto de un gasto entre los adultos activos según el modo del perfil.
    /// Devuelve null y un mensaje si no se puede repartir.
    /// </summary>
    public static IReadOnlyList<ParteAsumida>? Repartir(
        PerfilReparto perfil, IReadOnlyList<Miembro> adultos, decimal importe, Guid pagadoPor, out string? error)
    {
        var resultado = RepartirConDependientes(perfil, adultos, importe, pagadoPor, out error);
        return resultado?.PorAdulto;
    }

    /// <summary>
    /// Reparte un gasto entre todos los miembros activos (adultos y a cargo). La parte de cada miembro a cargo se suma a la
    /// de su responsable: el responsable asume ese importe y el desglose queda en <see cref="ResultadoReparto.ACargo"/>.
    /// Devuelve null y un mensaje si no se puede repartir.
    /// </summary>
    public static ResultadoReparto? RepartirConDependientes(
        PerfilReparto perfil, IReadOnlyList<Miembro> miembros, decimal importe, Guid pagadoPor, out string? error)
    {
        error = null;
        // Lo asume la cuenta común (o su ahorro): sin reparto entre personas.
        if (ModosDeCuenta.EsDeCuentaComun(perfil.Modo)) return new ResultadoReparto([], []);

        var adultos = miembros.Where(m => m.Tipo == TipoMiembro.Adulto).ToList();
        if (adultos.Count == 0) { error = "El hogar no tiene adultos activos entre los que repartir."; return null; }

        var idsAdultos = adultos.Select(a => a.Id).ToHashSet();
        if (miembros.Where(m => m.Tipo == TipoMiembro.ACargo).Any(m => m.ResponsableId is not { } r || !idsAdultos.Contains(r)))
        {
            error = "Un miembro a cargo necesita un responsable adulto activo.";
            return null;
        }

        var valores = miembros.Select(m => new MiembroReparto(m.Id, perfil.Modo switch
        {
            ModoReparto.Porcentaje or ModoReparto.Partes
                => perfil.Detalles.Where(d => d.MiembroId == m.Id).Sum(d => d.Valor),
            _ => 0m,
        })).ToList();

        if (perfil.Modo is ModoReparto.Porcentaje or ModoReparto.Partes && miembros.Count > 1 && valores.All(m => m.Valor <= 0))
        {
            error = "El perfil de reparto no asigna valor a ningún miembro activo.";
            return null;
        }

        try
        {
            var partes = RepartoMiembros.Repartir(importe, perfil.Modo, valores, pagadoPor);
            if (partes.Sum(p => p.Importe) != importe) { error = "El reparto no suma el importe del gasto."; return null; }

            var porAdulto = adultos.ToDictionary(a => a.Id, _ => 0m);
            var aCargo = new List<ParteACargo>();
            foreach (var parte in partes)
            {
                var miembro = miembros.First(m => m.Id == parte.MiembroId);
                if (miembro.Tipo == TipoMiembro.Adulto)
                {
                    porAdulto[miembro.Id] += parte.Importe;
                    continue;
                }
                var responsable = miembro.ResponsableId!.Value;
                porAdulto[responsable] += parte.Importe;
                if (parte.Importe > 0) aCargo.Add(new ParteACargo(miembro.Id, responsable, parte.Importe));
            }

            return new ResultadoReparto(porAdulto.Select(kv => new ParteAsumida(kv.Key, kv.Value)).ToList(), aCargo);
        }
        catch (ArgumentException e)
        {
            error = e.Message;
            return null;
        }
    }

    /// <summary>Aplica el reparto al gasto: actualiza filas existentes, borra las que sobran y añade las nuevas.</summary>
    public static void Aplicar(Gasto gasto, IReadOnlyList<ParteAsumida> partes)
    {
        foreach (var r in gasto.Repartos.Where(r => partes.All(p => p.MiembroId != r.MiembroId)).ToList())
            gasto.Repartos.Remove(r);
        foreach (var p in partes)
        {
            var existente = gasto.Repartos.FirstOrDefault(r => r.MiembroId == p.MiembroId);
            if (existente is not null) existente.ImporteAsumido = p.Importe;
            else gasto.Repartos.Add(new GastoReparto
            {
                GastoId = gasto.Id, MiembroId = p.MiembroId, HogarId = gasto.HogarId, ImporteAsumido = p.Importe,
            });
        }
    }

    /// <summary>Aplica un reparto con dependientes: el de cada adulto y el desglose de las partes a cargo.</summary>
    public static void Aplicar(Gasto gasto, ResultadoReparto resultado)
    {
        Aplicar(gasto, resultado.PorAdulto);
        gasto.RepartosACargo.Clear();
        foreach (var p in resultado.ACargo)
        {
            gasto.RepartosACargo.Add(new GastoRepartoACargo
            {
                GastoId = gasto.Id, MiembroId = p.MiembroId, ResponsableId = p.ResponsableId, HogarId = gasto.HogarId, ImporteAsumido = p.Importe,
            });
        }
    }
}
