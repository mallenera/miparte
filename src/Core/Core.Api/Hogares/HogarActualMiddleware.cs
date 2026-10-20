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

    /// <summary>Clave de <c>HttpContext.Items</c> con el miembro (<c>Miembro</c>, solo lectura) del usuario en el hogar de la petición, o null si no hay.</summary>
    public const string ClaveMiembro = "miparte.miembro-actual";

    /// <summary>
    /// Fija <see cref="HogarActual"/> para la petición autenticada y continúa el pipeline. Responde 400 si la
    /// cabecera no es un GUID, 403 si el hogar no es del usuario y 409 si tiene varios hogares y no la envía.
    /// Se omite en endpoints marcados con <see cref="SinHogarActual"/> y en peticiones anónimas.
    /// </summary>
    public async Task InvokeAsync(HttpContext ctx)
    {
        var sub = ctx.User.FindFirst("sub")?.Value;
        var sinHogar = ctx.GetEndpoint()?.Metadata.GetMetadata<SinHogarActual>() is not null;

        // El autor lo necesita la auditoría también en los endpoints sin hogar (crear hogar, aceptar invitación).
        // GetService: sin persistencia configurada (p. ej. /health) no existe y no debe fallar.
        if (ctx.User.Identity?.IsAuthenticated == true && Guid.TryParse(sub, out var autor)
            && ctx.RequestServices.GetService<HogarActual>() is { } actual)
            actual.UsuarioId = autor;

        if (!sinHogar && ctx.User.Identity?.IsAuthenticated == true && Guid.TryParse(sub, out var userId))
        {
            // Se resuelven aquí y no en el constructor: sin base de datos configurada
            // (p. ej. /health) la petición no debe fallar.
            var hogarActual = ctx.RequestServices.GetRequiredService<HogarActual>();
            var db = ctx.RequestServices.GetRequiredService<MiParteDbContext>();

            // Un miembro por hogar (hay un único índice user_id + hogar_id). Con la caché no hace falta consultar en cada petición,
            // y las escrituras con permisos reutilizan la fila (ClaveMiembro) en vez de volver a leerla.
            var cache = ctx.RequestServices.GetService<CacheAcceso>();
            var miembros = cache is not null
                ? await cache.MiembrosActivosAsync(userId, db, ctx.RequestAborted)
                : await db.Miembros.IgnoreQueryFilters().AsNoTracking()
                    .Where(m => m.UserId == userId && m.Activo).ToListAsync(ctx.RequestAborted);
            var hogares = miembros.Select(m => m.HogarId).Distinct().ToList();

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

            if (hogarActual.HogarId is { } hogarElegido)
            {
                var yo = miembros.FirstOrDefault(m => m.HogarId == hogarElegido);
                ctx.Items[ClaveMiembro] = yo;
                hogarActual.MiembroId = yo?.Id;
            }
        }

        try
        {
            await next(ctx);
        }
        finally
        {
            InvalidarSiCambiaAcceso(ctx);
        }
    }

    /// <summary>
    /// Tras una escritura sobre miembros, invitaciones u hogares se olvida lo cacheado, aunque la petición haya fallado a medias:
    /// así un permiso retirado, una desactivación o un hogar nuevo se ven en la petición siguiente.
    /// </summary>
    private static void InvalidarSiCambiaAcceso(HttpContext ctx)
    {
        if (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method) || HttpMethods.IsOptions(ctx.Request.Method)) return;
        var ruta = ctx.Request.Path;
        if (!ruta.StartsWithSegments("/api/miembros") && !ruta.StartsWithSegments("/api/invitaciones") && !ruta.StartsWithSegments("/api/hogares")
            && !ruta.StartsWithSegments("/api/hogar") && !ruta.StartsWithSegments("/api/enlaces-grupo"))
            return;

        var cache = ctx.RequestServices.GetService<CacheAcceso>();
        if (cache is null) return;
        if (Guid.TryParse(ctx.User.FindFirst("sub")?.Value, out var usuario)) cache.InvalidarUsuario(usuario);
        if (ctx.RequestServices.GetService<HogarActual>()?.HogarId is { } hogar) cache.InvalidarHogar(hogar);
    }

    /// <summary>Escribe una respuesta JSON de error { error } con el código HTTP indicado.</summary>
    private static Task Responder(HttpContext ctx, int status, string mensaje)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { error = mensaje });
    }
}
