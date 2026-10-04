using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MiParte.Auth;

public static class ServiceCollectionExtensions
{
    private static readonly string[] AlgoritmosAsimetricos =
        [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256];

    /// <summary>
    /// Autenticación con JWT de Supabase Auth. Falla cerrado: sin configuración
    /// ningún token es válido (pero la aplicación arranca).
    /// </summary>
    public static IServiceCollection AddSupabaseAuth(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<SupabaseAuthOptions>(config.GetSection(SupabaseAuthOptions.Seccion));
        services.AddHttpClient();
        services.TryAddSingleton<ISigningKeyProvider, JwksSigningKeyProvider>();

        services.AddAuthorization();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<SupabaseAuthOptions>, ISigningKeyProvider>((jwt, supa, claves) =>
            {
                var s = supa.Value;
                jwt.MapInboundClaims = false; // conserva "sub" tal cual
                var parametros = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = s.EmisorEfectivo,
                    ValidateAudience = true,
                    ValidAudience = s.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                if (!string.IsNullOrWhiteSpace(s.JwtSecret))
                {
                    parametros.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];
                    parametros.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(s.JwtSecret));
                }
                else
                {
                    parametros.ValidAlgorithms = AlgoritmosAsimetricos;
                    parametros.IssuerSigningKeyResolver = (_, _, kid, _) => claves.ObtenerClaves(kid);
                }

                jwt.TokenValidationParameters = parametros;
            });

        return services;
    }
}
