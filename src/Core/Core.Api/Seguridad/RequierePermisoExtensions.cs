using Microsoft.EntityFrameworkCore;
using MiParte.Core.Domain;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Seguridad;

/// <summary>Exige un permiso del catálogo (<see cref="CatalogoPermisos"/>) a quien llama a un endpoint.</summary>
public static class RequierePermisoExtensions
{
    /// <summary>
    /// Responde 403 si el miembro activo de la petición no tiene el permiso (un admin los tiene todos). Sin hogar resuelto deja pasar:
    /// el propio endpoint responde el 409 de falta de hogar.
    /// </summary>
    /// <param name="builder">Endpoint o grupo al que se aplica.</param>
    /// <param name="clave">Clave del permiso exigido.</param>
    public static TBuilder RequierePermiso<TBuilder>(this TBuilder builder, string clave) where TBuilder : IEndpointConventionBuilder
    {
        var permiso = CatalogoPermisos.Todos.First(p => p.Clave == clave);
        return builder.AddEndpointFilter(async (contexto, siguiente) =>
        {
            var http = contexto.HttpContext;
            var hogar = http.RequestServices.GetRequiredService<IHogarActual>();
            if (hogar.HogarId is null || hogar.UsuarioId is not { } usuario) return await siguiente(contexto);

            var db = http.RequestServices.GetRequiredService<MiParteDbContext>();
            var yo = await db.Miembros.FirstOrDefaultAsync(m => m.UserId == usuario && m.Activo, http.RequestAborted);
            if (yo is not null && CatalogoPermisos.Efectivos(yo).Contains(clave)) return await siguiente(contexto);

            return Results.Json(
                new { error = $"No tienes permiso para esta acción ({permiso.Etiqueta.ToLowerInvariant()}). Pídeselo a un administrador del hogar." },
                statusCode: StatusCodes.Status403Forbidden);
        });
    }
}
