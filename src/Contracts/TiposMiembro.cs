namespace MiParte.Contracts;

/// <summary>Valores de <c>MiembroDto.Tipo</c> y de <c>CrearMiembroRequest.Tipo</c>: evitan repetir los textos del contrato.</summary>
public static class TiposMiembro
{
    /// <summary>Persona que aporta, paga y puede tener cuenta.</summary>
    public const string Adulto = "adulto";

    /// <summary>Persona sin cuenta que depende de un adulto responsable (por ejemplo, un hijo).</summary>
    public const string ACargo = "a_cargo";
}
