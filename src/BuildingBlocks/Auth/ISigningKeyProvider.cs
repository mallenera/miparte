using Microsoft.IdentityModel.Tokens;

namespace MiParte.Auth;

public interface ISigningKeyProvider
{
    IReadOnlyCollection<SecurityKey> ObtenerClaves(string? kid);
}
