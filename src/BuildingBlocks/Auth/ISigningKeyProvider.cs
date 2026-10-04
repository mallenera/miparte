using Microsoft.IdentityModel.Tokens;

namespace MiParte.Auth;

/// <summary>Proveedor de claves de firma con las que validar los JWT.</summary>
public interface ISigningKeyProvider
{
    /// <summary>Devuelve las claves de firma disponibles.</summary>
    /// <param name="kid">Identificador de clave del token; si es null se devuelven todas.</param>
    /// <returns>Claves que coinciden con <paramref name="kid"/> (o todas si es null).</returns>
    IReadOnlyCollection<SecurityKey> ObtenerClaves(string? kid);
}
