namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>
/// Hogar al que pertenece la petición en curso. Si es null, las consultas no
/// devuelven ningún dato de negocio (falla cerrado).
/// </summary>
public interface IHogarActual
{
    /// <summary>Identificador del hogar de la petición, o null si aún no se ha resuelto.</summary>
    Guid? HogarId { get; }

    /// <summary>Usuario autenticado (claim sub) de la petición, o null si no hay; es el autor que recoge la auditoría.</summary>
    Guid? UsuarioId { get; }

    /// <summary>
    /// Miembro activo que hace la petición en el hogar actual, o null. Decide qué gastos personales ve: solo los de quien paga
    /// (ni siquiera un admin ve los de otros). Sin miembro, no se ve ninguno.
    /// </summary>
    Guid? MiembroId { get; }

    /// <summary>
    /// Origen de la petición para la auditoría: <see cref="MiParte.Core.Domain.Entidades.OrigenesAuditoria"/>.Asistente si la
    /// petición llega confirmada desde el chat (cabecera <c>X-Origen</c>); en otro caso, manual.
    /// </summary>
    string Origen { get; }
}

/// <summary>Implementación scoped de <see cref="IHogarActual"/>; la rellena el middleware al resolver el hogar.</summary>
public class HogarActual : IHogarActual
{
    /// <inheritdoc />
    public Guid? HogarId { get; set; }

    /// <inheritdoc />
    public Guid? UsuarioId { get; set; }

    /// <inheritdoc />
    public Guid? MiembroId { get; set; }

    /// <inheritdoc />
    public string Origen { get; set; } = MiParte.Core.Domain.Entidades.OrigenesAuditoria.Manual;
}
