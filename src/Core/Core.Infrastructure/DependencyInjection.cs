using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Infrastructure;

public static class DependencyInjection
{
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
