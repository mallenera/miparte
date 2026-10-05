namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>
/// Hogar al que pertenece la petición en curso. Si es null, las consultas no
/// devuelven ningún dato de negocio (falla cerrado).
/// </summary>
public interface IHogarActual
{
    /// <summary>Identificador del hogar de la petición, o null si aún no se ha resuelto.</summary>
    Guid? HogarId { get; }
}

/// <summary>Implementación scoped de <see cref="IHogarActual"/>; la rellena el middleware al resolver el hogar.</summary>
public class HogarActual : IHogarActual
{
    /// <inheritdoc />
    public Guid? HogarId { get; set; }
}
