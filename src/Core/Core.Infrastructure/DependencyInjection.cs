using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Core.Application.CuentaComun;
using MiParte.Core.Application.Auditoria;
using MiParte.Core.Application.Categorias;
using MiParte.Core.Application.Cierres;
using MiParte.Core.Application.Gastos;
using MiParte.Core.Application.Hogares;
using MiParte.Core.Application.Liquidacion;
using MiParte.Core.Application.Miembros;
using MiParte.Core.Application.Notificaciones;
using MiParte.Core.Application.Periodos;
using MiParte.Core.Application.Perfiles;
using MiParte.Core.Application.Recurrentes;
using MiParte.Core.Infrastructure.Notificaciones;
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
        services.AddScoped<IRepositorioEnlacesGrupo>(sp => new RepositorioEnlacesGrupo(sp.GetRequiredService<MiParteDbContext>()));
        services.AddScoped<IRepositorioRecurrentes>(sp => new RepositorioRecurrentes(sp.GetRequiredService<MiParteDbContext>()));
        services.AddScoped<IRepositorioPerfiles>(sp => new RepositorioPerfiles(sp.GetRequiredService<MiParteDbContext>()));
        services.AddScoped<IRepositorioCategorias>(sp => new RepositorioCategorias(sp.GetRequiredService<MiParteDbContext>()));
        services.AddScoped<IRepositorioCierres>(sp => new RepositorioCierres(sp.GetRequiredService<MiParteDbContext>()));
        services.AddScoped<IRepositorioHogares>(sp => new RepositorioHogares(sp.GetRequiredService<MiParteDbContext>()));
        services.AddScoped<IRepositorioAuditoria>(sp => new RepositorioAuditoria(sp.GetRequiredService<MiParteDbContext>()));
        services.AddScoped<IRepositorioPeriodos>(sp => new RepositorioPeriodos(sp.GetRequiredService<MiParteDbContext>()));
        services.AddScoped<IRepositorioNotificaciones>(sp => new RepositorioNotificaciones(sp.GetRequiredService<MiParteDbContext>()));
        return services;
    }

    /// <summary>
    /// Registra cola de avisos push: un singleton que los casos de uso llenan con <see cref="IPublicadorNotificaciones"/> y que
    /// consume el servicio de envío de Core.Api. No necesita base de datos, así que va aparte de <see cref="AddRepositorios"/>.
    /// </summary>
    public static IServiceCollection AddColaNotificacionesPush(this IServiceCollection services)
    {
        services.AddSingleton<ColaNotificacionesPush>();
        services.AddSingleton<IPublicadorNotificaciones>(sp => sp.GetRequiredService<ColaNotificacionesPush>());
        return services;
    }
}
