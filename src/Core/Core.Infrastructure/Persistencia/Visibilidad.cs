using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>
/// Consultas de lectura que aplican la privacidad de los gastos personales (<see cref="ReglasPrivacidad"/>). Se usan en las
/// lecturas que devuelven datos a la API, no como filtro global: la generación de recurrentes, los cierres y la liquidación
/// necesitan ver todo el hogar para no duplicar ni descuadrar nada.
/// </summary>
public static class Visibilidad
{
    /// <summary>Gastos que <paramref name="yo"/> puede ver.</summary>
    public static IQueryable<Gasto> GastosVisiblesPara(this IQueryable<Gasto> q, Guid? yo)
        => q.Where(g => !g.EsPersonal || (yo != null && g.PagadoPor == yo));

    /// <summary>Plantillas recurrentes que <paramref name="yo"/> puede ver.</summary>
    public static IQueryable<GastoRecurrente> RecurrentesVisiblesPara(this IQueryable<GastoRecurrente> q, Guid? yo)
        => q.Where(r => !r.EsPersonal || (yo != null && r.PagadoPor == yo));

    /// <summary>Eventos de auditoría que <paramref name="yo"/> puede ver: los de gastos personales solo para quien paga.</summary>
    public static IQueryable<EventoAuditoria> AuditoriaVisiblePara(this IQueryable<EventoAuditoria> q, Guid? yo)
        => q.Where(e => e.PersonalDe == null || (yo != null && e.PersonalDe == yo));
}
