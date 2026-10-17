using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>Reparto de un gasto entre los adultos activos según el perfil, y su aplicación sobre la entidad.</summary>
public static class RepartoGasto
{
    /// <summary>
    /// Calcula el reparto de un gasto entre los adultos activos según el modo del perfil.
    /// Devuelve null y un mensaje si no se puede repartir.
    /// </summary>
    public static IReadOnlyList<ParteAsumida>? Repartir(
        PerfilReparto perfil, IReadOnlyList<Miembro> adultos, decimal importe, Guid pagadoPor, out string? error)
    {
        error = null;
        if (perfil.Modo == ModoReparto.CuentaComun) return []; // lo asume la cuenta común: sin reparto entre personas
        if (adultos.Count == 0) { error = "El hogar no tiene adultos activos entre los que repartir."; return null; }

        var miembros = adultos.Select(a => new MiembroReparto(a.Id, perfil.Modo switch
        {
            ModoReparto.Porcentaje or ModoReparto.Partes
                => perfil.Detalles.Where(d => d.MiembroId == a.Id).Sum(d => d.Valor),
            _ => 0m,
        })).ToList();

        if (perfil.Modo is ModoReparto.Porcentaje or ModoReparto.Partes && adultos.Count > 1 && miembros.All(m => m.Valor <= 0))
        {
            error = "El perfil de reparto no asigna valor a ningún adulto activo.";
            return null;
        }

        try
        {
            var partes = RepartoMiembros.Repartir(importe, perfil.Modo, miembros, pagadoPor);
            if (partes.Sum(p => p.Importe) != importe) { error = "El reparto no suma el importe del gasto."; return null; }
            return partes;
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
}
