namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>
/// Hogar al que pertenece la petición en curso. Si es null, las consultas no
/// devuelven ningún dato de negocio (falla cerrado).
/// </summary>
public interface IHogarActual
{
    Guid? HogarId { get; }
}

public class HogarActual : IHogarActual
{
    public Guid? HogarId { get; set; }
}
