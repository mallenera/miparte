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
    /// <summary>Tiempo máximo que se reutilizan las claves cacheadas antes de recargarlas.</summary>
    private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(10);
    /// <summary>Tiempo mínimo entre dos cargas del JWKS.</summary>
    private static readonly TimeSpan EsperaMinima = TimeSpan.FromMinutes(1);

    /// <summary>Protege la caché de claves y la fecha de última carga.</summary>
    private readonly object _lock = new();
    /// <summary>Claves de firma cacheadas del último JWKS descargado.</summary>
    private IReadOnlyList<SecurityKey> _claves = [];
    /// <summary>Momento del último intento de carga del JWKS (incluso si falló).</summary>
    private DateTimeOffset _ultimaCarga = DateTimeOffset.MinValue;

    /// <inheritdoc />
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

    /// <summary>Descarga el JWKS de Supabase y sustituye la caché; si falla, registra el error y conserva las claves previas.</summary>
    /// <param name="ahora">Instante que se anota como última carga.</param>
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
