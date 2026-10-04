using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MiParte.Auth;

/// <summary>
/// Obtiene las claves públicas del JWKS de Supabase y las cachea. Si llega un kid
/// desconocido, refresca como máximo una vez por minuto (rotación de claves).
/// </summary>
public sealed class JwksSigningKeyProvider(
    IHttpClientFactory httpFactory,
    IOptions<SupabaseAuthOptions> options,
    ILogger<JwksSigningKeyProvider> logger) : ISigningKeyProvider
{
    private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan EsperaMinima = TimeSpan.FromMinutes(1);

    private readonly object _lock = new();
    private IReadOnlyList<SecurityKey> _claves = [];
    private DateTimeOffset _ultimaCarga = DateTimeOffset.MinValue;

    public IReadOnlyCollection<SecurityKey> ObtenerClaves(string? kid)
    {
        lock (_lock)
        {
            var ahora = DateTimeOffset.UtcNow;
            var caducada = ahora - _ultimaCarga > Vigencia;
            var kidDesconocido = kid is not null && _claves.All(k => k.KeyId != kid)
                                 && ahora - _ultimaCarga > EsperaMinima;
            if (_claves.Count == 0 && ahora - _ultimaCarga > EsperaMinima || caducada || kidDesconocido)
            {
                Cargar(ahora);
            }

            return kid is null ? _claves.ToList() : _claves.Where(k => k.KeyId == kid).ToList();
        }
    }

    private void Cargar(DateTimeOffset ahora)
    {
        _ultimaCarga = ahora; // también tras un fallo, para no martillear al servidor
        var url = options.Value.JwksUrl;
        if (url is null)
        {
            logger.LogWarning("Supabase:Url no configurada; no se pueden validar tokens.");
            return;
        }

        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var json = http.GetStringAsync(url).GetAwaiter().GetResult();
            var jwks = new JsonWebKeySet(json);
            _claves = jwks.GetSigningKeys().ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo descargar el JWKS de {Url}", url);
        }
    }
}
