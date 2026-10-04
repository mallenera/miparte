using Microsoft.EntityFrameworkCore;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Hogares;

/// <summary>
/// Determina el hogar de la petición a partir del usuario autenticado (claim "sub" →
/// miembro.user_id). Si el usuario pertenece a varios hogares debe indicar cuál con
/// la cabecera X-Hogar-Id. Nunca se confía en un hogar que el usuario no tenga.
/// </summary>
public class HogarActualMiddleware(RequestDelegate next)
{
    public const string Cabecera = "X-Hogar-Id";

    public async Task InvokeAsync(HttpContext ctx)
    {
        var sub = ctx.User.FindFirst("sub")?.Value;
        if (ctx.User.Identity?.IsAuthenticated == true && Guid.TryParse(sub, out var userId))
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

    private static Task Responder(HttpContext ctx, int status, string mensaje)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { error = mensaje });
    }
}
