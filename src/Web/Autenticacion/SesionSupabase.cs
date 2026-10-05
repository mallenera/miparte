namespace MiParte.Web.Autenticacion;

/// <summary>Sesión de Supabase Auth del usuario.</summary>
/// <param name="AccessToken">JWT que se envía a las APIs.</param>
/// <param name="RefreshToken">Token para renovar la sesión.</param>
/// <param name="ExpiraEn">Instante en que caduca el <paramref name="AccessToken"/>.</param>
/// <param name="UserId">Identificador del usuario (claim <c>sub</c>).</param>
/// <param name="Email">Correo del usuario, si consta.</param>
public sealed record SesionSupabase(string AccessToken, string RefreshToken, DateTimeOffset ExpiraEn, string UserId, string? Email)
{
    /// <summary>Indica si el token ha caducado o caducará dentro del margen indicado.</summary>
    /// <param name="ahora">Instante actual.</param>
    /// <param name="margen">Antelación con la que se considera caducado.</param>
    public bool CaducaPronto(DateTimeOffset ahora, TimeSpan margen) => ahora >= ExpiraEn - margen;
}
