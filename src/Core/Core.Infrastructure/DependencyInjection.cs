using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Core.Application.CuentaComun;
using MiParte.Core.Application.Gastos;
using MiParte.Core.Application.Liquidacion;
using MiParte.Core.Application.Miembros;
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

    /// <summary>
    /// Registra los repositorios (scoped). Van aparte de <see cref="AddPersistencia"/> porque dependen solo del
    /// <see cref="MiParteDbContext"/>, que los tests sustituyen por uno en memoria.
    /// </summary>
    public static IServiceCollection AddRepositorios(this IServiceCollection services)
    {
        // Con fábrica: sin cadena de conexión no hay DbContext y la validación de DI no debe impedir arrancar (/health).
        services.AddScoped<IRepositorioLiquidacion>(sp => new RepositorioLiquidacion(sp.GetRequiredService<MiParteDbContext>()));
        services.AddScoped<IRepositorioGastos>(sp => new RepositorioGastos(sp.GetRequiredService<MiParteDbContext>()));
        services.AddScoped<IRepositorioCuentaComun>(sp => new RepositorioCuentaComun(sp.GetRequiredService<MiParteDbContext>()));
        services.AddScoped<IRepositorioMiembros>(sp => new RepositorioMiembros(sp.GetRequiredService<MiParteDbContext>()));
        return services;
    }
}
