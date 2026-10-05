using Microsoft.EntityFrameworkCore;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Hogares;

/// <summary>
/// Determina el hogar de la petición a partir del usuario autenticado (claim "sub" →
/// miembro.user_id). Si el usuario pertenece a varios hogares debe indicar cuál con
/// la cabecera X-Hogar-Id. Nunca se confía en un hogar que el usuario no tenga.
/// </summary>
/// <param name="next">Siguiente delegado del pipeline HTTP.</param>
public class HogarActualMiddleware(RequestDelegate next)
{
    /// <summary>Nombre de la cabecera HTTP con la que el cliente elige hogar cuando pertenece a varios.</summary>
    public const string Cabecera = "X-Hogar-Id";

    /// <summary>
    /// Fija <see cref="HogarActual"/> para la petición autenticada y continúa el pipeline. Responde 400 si la
    /// cabecera no es un GUID, 403 si el hogar no es del usuario y 409 si tiene varios hogares y no la envía.
    /// Se omite en endpoints marcados con <see cref="SinHogarActual"/> y en peticiones anónimas.
    /// </summary>
    public async Task InvokeAsync(HttpContext ctx)
    {
        var sub = ctx.User.FindFirst("sub")?.Value;
        var sinHogar = ctx.GetEndpoint()?.Metadata.GetMetadata<SinHogarActual>() is not null;
        if (!sinHogar && ctx.User.Identity?.IsAuthenticated == true && Guid.TryParse(sub, out var userId))
        {
            // Se resuelven aquí y no en el constructor: sin base de datos configurada
            // (p. ej. /health) la petición no debe fallar.
            var hogarActual = ctx.RequestServices.GetRequiredService<HogarActual>();
            var db = ctx.RequestServices.GetRequiredService<MiParteDbContext>();

            var hogares = await db.Miembros
                .IgnoreQueryFilters() // aquí aún no hay hogar: es justo lo que se resuelve
                .Where(m => m.UserId == userId && m.Activo)
                .Select(m => m.HogarId)
                .Distinct()
                .ToListAsync(ctx.RequestAborted);

            if (ctx.Request.Headers.TryGetValue(Cabecera, out var valor))
            {
                if (!Guid.TryParse(valor.ToString(), out var pedido))
                {
                    await Responder(ctx, StatusCodes.Status400BadRequest, $"{Cabecera} no es un identificador válido.");
                    return;
                }
                if (!hogares.Contains(pedido))
                {
                    await Responder(ctx, StatusCodes.Status403Forbidden, "No perteneces a ese hogar.");
                    return;
                }
                hogarActual.HogarId = pedido;
            }
            else if (hogares.Count == 1)
            {
                hogarActual.HogarId = hogares[0];
            }
            else if (hogares.Count > 1)
            {
                await Responder(ctx, StatusCodes.Status409Conflict, $"Perteneces a varios hogares: indica {Cabecera}.");
                return;
            }
        }

        await next(ctx);
    }

    /// <summary>Escribe una respuesta JSON de error { error } con el código HTTP indicado.</summary>
    private static Task Responder(HttpContext ctx, int status, string mensaje)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { error = mensaje });
    }
}
