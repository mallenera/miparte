using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Api.Hogares;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Auditoria;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Miembros;

/// <summary>
/// Endpoints de miembros del hogar actual (listar, añadir, actualizar, desactivar) y de invitaciones
/// (crear y aceptar). Los datos se filtran por hogar; las operaciones de gestión exigen rol admin.
/// </summary>
public static class MiembrosEndpoints
{
    /// <summary>Longitud máxima del nombre de un miembro.</summary>
    private const int MaxLongitudNombre = 100;

    /// <summary>Tiempo durante el que una invitación puede aceptarse desde su creación (7 días).</summary>
    public static readonly TimeSpan VigenciaInvitacion = TimeSpan.FromDays(7);

    /// <summary>
    /// Registra GET/POST /api/miembros, PUT/DELETE /api/miembros/{id}, POST /api/invitaciones y
    /// POST /api/invitaciones/aceptar (este último sin hogar actual). Todos requieren autenticación.
    /// </summary>
    public static IEndpointRouteBuilder MapMiembros(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/miembros", ListarAsync).RequireAuthorization();
        app.MapPost("/api/miembros", CrearAsync).RequireAuthorization();
        app.MapPut("/api/miembros/{id:guid}", ActualizarAsync).RequireAuthorization();
        app.MapDelete("/api/miembros/{id:guid}", DesactivarAsync).RequireAuthorization();

        app.MapPost("/api/invitaciones", CrearInvitacionAsync)
            .RequireAuthorization()
            .RequireRateLimiting(LimitacionPeticiones.Costosa);
        app.MapPost("/api/invitaciones/aceptar", AceptarInvitacionAsync)
            .RequireAuthorization()
            .RequireRateLimiting(LimitacionPeticiones.Costosa)
            .WithMetadata(new SinHogarActual());

        return app;
    }

    /// <summary>SHA-256 en hexadecimal minúscula del token en UTF-8 (igual que aceptar_invitacion en SQL).</summary>
    public static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>Convierte un miembro en su DTO: tipo "adulto"/"a_cargo", rol "admin"/"miembro", si está vinculado a un usuario y si es el usuario autenticado (<paramref name="usuarioActual"/>).</summary>
    private static MiembroDto ADto(Miembro m, Guid? usuarioActual = null) => new(
        m.Id, m.Nombre, m.Tipo == TipoMiembro.Adulto ? TiposMiembro.Adulto : TiposMiembro.ACargo, m.ResponsableId, m.Activo,
        m.Rol == RolMiembro.Admin ? "admin" : "miembro", m.UserId is not null,
        usuarioActual is not null && m.UserId == usuarioActual,
        CatalogoPermisos.Todos.Select(p => p.Clave).Where(CatalogoPermisos.Efectivos(m).Contains).ToList());

    /// <summary>Obtiene el id del usuario autenticado del claim "sub"; false si falta o no es un GUID.</summary>
    private static bool TryUsuario(HttpContext ctx, out Guid userId)
        => Guid.TryParse(ctx.User.FindFirst("sub")?.Value, out userId);

    /// <summary>Respuesta JSON de error { error } con el código HTTP indicado.</summary>
    private static IResult Error(int status, string mensaje)
        => Results.Json(new { error = mensaje }, statusCode: status);

    /// <summary>Miembro activo del hogar actual vinculado al usuario autenticado, o null si no lo es.</summary>
    private static Task<Miembro?> Yo(MiParteDbContext db, Guid userId, CancellationToken ct)
        => db.Miembros.FirstOrDefaultAsync(m => m.UserId == userId && m.Activo, ct);

    /// <summary>
    /// GET /api/miembros: miembros activos del hogar actual ordenados por nombre; con <c>incluirInactivos=true</c> también
    /// los desactivados (para poner nombre a quien aparece en el historial). Cualquier miembro puede consultarlo.
    /// </summary>
    private static async Task<IResult> ListarAsync(
        HttpContext ctx, [FromServices] MiParteDbContext db, CancellationToken ct, bool incluirInactivos = false)
    {
        var miembros = await db.Miembros.Where(m => incluirInactivos || m.Activo).OrderBy(m => m.Nombre).ToListAsync(ct);
        var usuario = TryUsuario(ctx, out var userId) ? userId : (Guid?)null;
        return Results.Ok(miembros.Select(m => ADto(m, usuario)).ToList());
    }

    /// <summary>
    /// POST /api/miembros (permiso <c>miembros.gestionar</c>): añade un miembro "adulto" o "a_cargo" sin usuario vinculado.
    /// Un a_cargo exige un responsable adulto activo y un adulto no puede tenerlo. 403 sin el permiso,
    /// 400 por datos inválidos, 201 con el miembro creado.
    /// </summary>
    private static async Task<IResult> CrearAsync(
        CrearMiembroRequest req, HttpContext ctx, [FromServices] MiParteDbContext db,
        [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        var yo = await Yo(db, userId, ct);
        if (yo is null || !CatalogoPermisos.Efectivos(yo).Contains(CatalogoPermisos.MiembrosGestionar))
            return Error(403, "No tienes permiso para añadir miembros.");

        var nombre = req.Nombre?.Trim();
        if (string.IsNullOrEmpty(nombre)) return Error(400, "El nombre es obligatorio.");
        if (nombre.Length > MaxLongitudNombre)
            return Error(400, $"El nombre admite como máximo {MaxLongitudNombre} caracteres.");

        TipoMiembro tipo;
        switch (req.Tipo)
        {
            case TiposMiembro.Adulto: tipo = TipoMiembro.Adulto; break;
            case TiposMiembro.ACargo: tipo = TipoMiembro.ACargo; break;
            default: return Error(400, "El tipo debe ser 'adulto' o 'a_cargo'.");
        }

        Guid? responsable = null;
        if (tipo == TipoMiembro.ACargo)
        {
            if (req.ResponsableId is null) return Error(400, "Un miembro a cargo necesita un responsable adulto.");
            var r = await db.Miembros.FirstOrDefaultAsync(m => m.Id == req.ResponsableId, ct);
            if (r is null || !r.Activo || r.Tipo != TipoMiembro.Adulto)
                return Error(400, "El responsable debe ser un adulto activo del hogar.");
            responsable = r.Id;
        }
        else if (req.ResponsableId is not null)
        {
            return Error(400, "Solo un miembro a cargo tiene responsable.");
        }

        var nuevo = new Miembro
        {
            Id = Guid.NewGuid(),
            HogarId = hogar.HogarId!.Value,
            Nombre = nombre,
            Tipo = tipo,
            ResponsableId = responsable,
        };
        db.Miembros.Add(nuevo);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/miembros/{nuevo.Id}", ADto(nuevo));
    }

    /// <summary>
    /// PUT /api/miembros/{id}: cambia nombre, estado activo, rol, permisos o responsable. Sin <c>miembros.gestionar</c> solo se puede
    /// renombrarse a sí mismo (403 si intenta más). 404 si no existe; 400 por datos inválidos; 409 si
    /// dejaría al hogar sin administrador activo y vinculado, o si desactiva a un responsable de miembros
    /// a cargo activos.
    /// </summary>
    private static async Task<IResult> ActualizarAsync(
        Guid id, ActualizarMiembroRequest req, HttpContext ctx, [FromServices] MiParteDbContext db,
        CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        var yo = await Yo(db, userId, ct);
        if (yo is null) return Error(403, "No eres miembro activo del hogar.");

        var m = await db.Miembros.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return Error(404, "El miembro no existe en este hogar.");

        var mios = CatalogoPermisos.Efectivos(yo);
        // Renombrarse a uno mismo lo puede cualquiera; tocar a otros, el estado o el responsable exige gestionar miembros.
        if (((req.Nombre is not null && m.Id != yo.Id) || req.Activo is not null || req.ResponsableId is not null)
            && !mios.Contains(CatalogoPermisos.MiembrosGestionar))
            return Error(403, "No tienes permiso para modificar a otros miembros, su estado o su responsable.");
        if ((req.Rol is not null || req.Permisos is not null) && !mios.Contains(CatalogoPermisos.PermisosGestionar))
            return Error(403, "No tienes permiso para cambiar el rol ni los permisos de los miembros.");

        string? nombre = null;
        if (req.Nombre is not null)
        {
            nombre = req.Nombre.Trim();
            if (nombre.Length == 0) return Error(400, "El nombre no puede estar vacío.");
            if (nombre.Length > MaxLongitudNombre)
                return Error(400, $"El nombre admite como máximo {MaxLongitudNombre} caracteres.");
        }

        RolMiembro? nuevoRol = null;
        if (req.Rol is not null)
        {
            nuevoRol = req.Rol switch
            {
                "admin" => RolMiembro.Admin,
                "miembro" => RolMiembro.Miembro,
                _ => null,
            };
            if (nuevoRol is null) return Error(400, "El rol debe ser 'admin' o 'miembro'.");
        }

        List<string>? nuevosPermisos = null;
        if (req.Permisos is not null)
        {
            var desconocido = req.Permisos.FirstOrDefault(p => !CatalogoPermisos.Existe(p));
            if (desconocido is not null) return Error(400, $"El permiso «{desconocido}» no existe.");
            nuevosPermisos = CatalogoPermisos.Todos.Select(p => p.Clave).Where(req.Permisos.Contains).ToList();
            if (m.Tipo != TipoMiembro.Adulto || m.UserId is null)
                return Error(409, "Solo un adulto con cuenta tiene permisos propios.");
        }

        if (req.ResponsableId is not null)
        {
            if (m.Tipo != TipoMiembro.ACargo) return Error(400, "Solo un miembro a cargo tiene responsable.");
            var r = await db.Miembros.FirstOrDefaultAsync(x => x.Id == req.ResponsableId, ct);
            if (r is null || !r.Activo || r.Tipo != TipoMiembro.Adulto)
                return Error(400, "El responsable debe ser un adulto activo del hogar.");
        }

        var seActiva = req.Activo ?? m.Activo;
        var rolFinal = nuevoRol ?? m.Rol;

        // Serializa por hogar la comprobación de "último admin" y el guardado: sin esto, dos admins que se
        // degradan o desactivan a la vez ven cada uno al otro como admin activo, ambos guardan y el hogar
        // se queda sin administrador. El bloqueo consultivo transaccional de Postgres se libera al
        // confirmar o revertir; la segunda petición espera y su AnyAsync ya ve el cambio de la primera
        // (READ COMMITTED). InMemory no admite transacciones ni SQL crudo, así que allí no se aplica.
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (tx is not null)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"select pg_advisory_xact_lock(hashtext({m.HogarId.ToString()}))", ct);

        // Al cambiar de rol sin indicar permisos propios, el miembro vuelve a la plantilla de su rol nuevo.
        var permisosFinales = nuevosPermisos ?? (nuevoRol is not null && nuevoRol != m.Rol ? null : m.Permisos);

        // Siempre debe quedar alguien activo, con cuenta, que pueda cambiar permisos: si no, nadie podría arreglarlo.
        var cambiaAcceso = req.Activo is not null || req.Rol is not null || req.Permisos is not null;
        if (cambiaAcceso && PuedeGestionarPermisos(m, m.Rol, m.Permisos, m.Activo))
        {
            var sigue = PuedeGestionarPermisos(m, rolFinal, permisosFinales, seActiva)
                || (await db.Miembros.Where(x => x.Id != m.Id && x.Activo && x.UserId != null).ToListAsync(ct))
                    .Any(x => CatalogoPermisos.Efectivos(x).Contains(CatalogoPermisos.PermisosGestionar));
            if (!sigue) return Error(409, "El hogar debe conservar al menos un miembro que pueda cambiar los permisos.");
        }

        // No dejar al hogar sin administrador activo y vinculado.
        if (m.Rol == RolMiembro.Admin && m.Activo && m.UserId is not null
            && (!seActiva || rolFinal != RolMiembro.Admin))
        {
            var hayOtro = await db.Miembros.AnyAsync(x =>
                x.Id != m.Id && x.Rol == RolMiembro.Admin && x.Activo && x.UserId != null, ct);
            if (!hayOtro) return Error(409, "El hogar debe conservar al menos un administrador.");
        }

        // Un adulto no se desactiva mientras sea responsable de miembros a cargo activos.
        if (m.Activo && !seActiva && await db.Miembros.AnyAsync(x => x.ResponsableId == m.Id && x.Activo, ct))
            return Error(409, "Es responsable de miembros a cargo activos: reasígnalos antes.");

        if (nombre is not null) m.Nombre = nombre;
        m.Activo = seActiva;
        m.Rol = rolFinal;
        // Se asigna una lista nueva solo si cambia: la auditoría compara por referencia y no debe registrar un cambio vacío.
        if (!MismosPermisos(permisosFinales, m.Permisos)) m.Permisos = permisosFinales;
        if (req.ResponsableId is not null) m.ResponsableId = req.ResponsableId;

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return Results.Ok(ADto(m, userId));
    }

    /// <summary>Si un miembro con el rol, los permisos propios y el estado dados podría cambiar permisos: activo, adulto con cuenta y con el permiso efectivo.</summary>
    private static bool PuedeGestionarPermisos(Miembro m, RolMiembro rol, List<string>? permisos, bool activo)
        => activo && CatalogoPermisos.Efectivos(new Miembro { Tipo = m.Tipo, UserId = m.UserId, Rol = rol, Permisos = permisos })
            .Contains(CatalogoPermisos.PermisosGestionar);

    /// <summary>Si dos listas propias de permisos son iguales como conjuntos (null = sin lista propia).</summary>
    private static bool MismosPermisos(List<string>? a, List<string>? b)
        => a is null || b is null ? a is null && b is null : a.ToHashSet().SetEquals(b);

    /// <summary>DELETE /api/miembros/{id}: borrado lógico, equivale a PUT con activo=false (mismas reglas y códigos).</summary>
    private static Task<IResult> DesactivarAsync(
        Guid id, HttpContext ctx, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        return ActualizarAsync(id, new ActualizarMiembroRequest(Activo: false), ctx, db, ct);
    }

    /// <summary>
    /// POST /api/invitaciones (permiso <c>invitaciones.crear</c>): crea una invitación, opcionalmente para vincular un miembro
    /// existente (404 si no existe; 409 si está desactivado o ya vinculado). El token se devuelve en claro
    /// solo en esta respuesta (201); en base de datos se guarda únicamente su hash SHA-256.
    /// </summary>
    private static async Task<IResult> CrearInvitacionAsync(
        CrearInvitacionRequest? req, HttpContext ctx, [FromServices] MiParteDbContext db,
        [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        var yo = await Yo(db, userId, ct);
        if (yo is null || !CatalogoPermisos.Efectivos(yo).Contains(CatalogoPermisos.InvitacionesCrear))
            return Error(403, "No tienes permiso para crear invitaciones.");

        if (req?.MiembroId is Guid destino)
        {
            var m = await db.Miembros.FirstOrDefaultAsync(x => x.Id == destino, ct);
            if (m is null) return Error(404, "El miembro no existe en este hogar.");
            if (!m.Activo) return Error(409, "El miembro está desactivado.");
            if (m.UserId is not null) return Error(409, "El miembro ya está vinculado a un usuario.");
        }

        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var ahora = DateTimeOffset.UtcNow;
        var inv = new InvitacionHogar
        {
            Id = Guid.NewGuid(),
            HogarId = hogar.HogarId!.Value,
            MiembroId = req?.MiembroId,
            TokenHash = HashToken(token),
            CreadaPor = userId,
            CreadaEn = ahora,
            CaducaEn = ahora + VigenciaInvitacion,
        };
        db.Invitaciones.Add(inv);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/invitaciones/{inv.Id}", new InvitacionCreada(inv.Id, token, inv.CaducaEn));
    }

    /// <summary>Codifica bytes en Base64 apto para URL (sin relleno, '-' y '_' en lugar de '+' y '/').</summary>
    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// POST /api/invitaciones/aceptar (sin hogar actual): replica en EF la función SQL aceptar_invitacion.
    /// Busca la invitación por hash del token y vincula al usuario a un miembro existente o crea uno nuevo
    /// con el nombre dado. 400 sin token/nombre; 404 token desconocido; 409 si está usada, caducada, el
    /// usuario ya está en el hogar o el miembro no está disponible. Devuelve el resumen del hogar.
    /// </summary>
    private static async Task<IResult> AceptarInvitacionAsync(
        AceptarInvitacionRequest req, HttpContext ctx, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();

        var token = req.Token?.Trim();
        if (string.IsNullOrEmpty(token)) return Error(400, "El token es obligatorio.");

        var hash = HashToken(token);
        var inv = await db.Invitaciones.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (inv is null) return Error(404, "Invitación no válida.");
        if (inv.UsadaEn is not null) return Error(409, "La invitación ya fue utilizada.");
        if (inv.CaducaEn <= DateTimeOffset.UtcNow) return Error(409, "La invitación ha caducado.");

        // Un usuario no puede figurar dos veces en el mismo hogar (activo o no).
        if (await db.Miembros.IgnoreQueryFilters().AnyAsync(m => m.HogarId == inv.HogarId && m.UserId == userId, ct))
            return Error(409, "Ya perteneces a este hogar.");

        if (inv.MiembroId is not null)
        {
            var m = await db.Miembros.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.HogarId == inv.HogarId && x.Id == inv.MiembroId, ct);
            if (m is null || !m.Activo) return Error(409, "El miembro de la invitación ya no está disponible.");
            if (m.UserId is not null) return Error(409, "El miembro de la invitación ya está vinculado a un usuario.");
            // En la rama relacional el vínculo se hace más abajo con un UPDATE condicional; con InMemory,
            // por entity tracking.
            if (!db.Database.IsRelational()) m.UserId = userId;
        }
        else
        {
            var nombre = req.Nombre?.Trim();
            if (string.IsNullOrEmpty(nombre)) return Error(400, "Hay que indicar un nombre para unirse al hogar.");
            if (nombre.Length > MaxLongitudNombre)
                return Error(400, $"El nombre admite como máximo {MaxLongitudNombre} caracteres.");
            db.Miembros.Add(new Miembro
            {
                Id = Guid.NewGuid(),
                HogarId = inv.HogarId,
                Nombre = nombre,
                Tipo = TipoMiembro.Adulto,
                UserId = userId,
                Rol = RolMiembro.Miembro,
            });
        }

        var ahora = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational())
        {
            // Postgres: el UPDATE condicional (UsadaEn IS NULL y no caducada) es atómico, así que de dos
            // aceptaciones simultáneas del mismo token solo una afecta una fila (equivale al
            // "for update" de aceptar_invitacion en SQL). La otra recibe 409 y se revierte todo.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var marcadas = await db.Invitaciones.IgnoreQueryFilters()
                .Where(i => i.Id == inv.Id && i.UsadaEn == null && i.CaducaEn > ahora)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.UsadaEn, ahora)
                    .SetProperty(i => i.UsadaPor, userId), ct);
            if (marcadas == 0) return Error(409, "La invitación ya fue utilizada o ha caducado.");

            if (inv.MiembroId is Guid destino)
            {
                // Dos invitaciones distintas pueden apuntar al mismo miembro: la lectura previa no bloquea,
                // así que el vínculo se hace condicional y atómico (user_id IS NULL y activo). Si otra
                // aceptación se adelantó afecta 0 filas: 409 y se revierte todo, incluida la invitación.
                var vinculados = await db.Miembros.IgnoreQueryFilters()
                    .Where(x => x.HogarId == inv.HogarId && x.Id == destino && x.UserId == null && x.Activo)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, userId), ct);
                if (vinculados == 0)
                    return Error(409, "El miembro de la invitación ya no está disponible o ya está vinculado a un usuario.");
                db.Auditoria.Add(RegistroAuditoria.Manual(
                    inv.HogarId, userId, RegistroAuditoria.Vincular, "miembro", destino, despues: new { userId }));
            }

            // Los UPDATE condicionales no los ve el ChangeTracker: el rastro se añade a mano (el miembro nuevo sí se registra solo).
            db.Auditoria.Add(RegistroAuditoria.Manual(
                inv.HogarId, userId, RegistroAuditoria.Usar, "invitacion", inv.Id, despues: new { usadaPor = userId, usadaEn = ahora }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        else
        {
            // Proveedor InMemory (tests): no admite ExecuteUpdate ni transacciones, se marca con el
            // entity tracking y una sola SaveChanges. En Postgres la garantía viene del UPDATE condicional.
            inv.UsadaEn = ahora;
            inv.UsadaPor = userId;
            await db.SaveChangesAsync(ct);
        }

        var h = await db.Hogares.IgnoreQueryFilters().FirstAsync(x => x.Id == inv.HogarId, ct);
        return Results.Ok(new HogarResumen(h.Id, h.Nombre, h.CuentaComunActiva, h.AhorroActivo));
    }
}
