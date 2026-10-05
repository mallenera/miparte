using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Infrastructure;

/// <summary>Registro de servicios de infraestructura en el contenedor de DI.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registra el hogar actual (scoped, que rellena el middleware) y el <see cref="MiParteDbContext"/>
    /// sobre PostgreSQL con nombres en snake_case, acorde al esquema SQL.
    /// </summary>
    public static IServiceCollection AddPersistencia(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<HogarActual>();
        services.AddScoped<IHogarActual>(sp => sp.GetRequiredService<HogarActual>());
        services.AddDbContext<MiParteDbContext>(o => o
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());
        return services;
    }
}
