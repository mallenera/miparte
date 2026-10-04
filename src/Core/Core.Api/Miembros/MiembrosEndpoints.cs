using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Api.Hogares;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Miembros;

public static class MiembrosEndpoints
{
    private const int MaxLongitudNombre = 100;
    public static readonly TimeSpan VigenciaInvitacion = TimeSpan.FromDays(7);

    public static IEndpointRouteBuilder MapMiembros(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/miembros", ListarAsync).RequireAuthorization();
        app.MapPost("/api/miembros", CrearAsync).RequireAuthorization();
        app.MapPut("/api/miembros/{id:guid}", ActualizarAsync).RequireAuthorization();
        app.MapDelete("/api/miembros/{id:guid}", DesactivarAsync).RequireAuthorization();

        app.MapPost("/api/invitaciones", CrearInvitacionAsync).RequireAuthorization();
        app.MapPost("/api/invitaciones/aceptar", AceptarInvitacionAsync)
            .RequireAuthorization()
            .WithMetadata(new SinHogarActual());

        return app;
    }

    /// <summary>SHA-256 en hexadecimal minúscula del token en UTF-8 (igual que aceptar_invitacion en SQL).</summary>
    public static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static MiembroDto ADto(Miembro m) => new(
        m.Id, m.Nombre, m.Tipo == TipoMiembro.Adulto ? "adulto" : "a_cargo", m.ResponsableId, m.Activo,
        m.Rol == RolMiembro.Admin ? "admin" : "miembro", m.UserId is not null);

    private static bool TryUsuario(HttpContext ctx, out Guid userId)
        => Guid.TryParse(ctx.User.FindFirst("sub")?.Value, out userId);

    private static IResult Error(int status, string mensaje)
        => Results.Json(new { error = mensaje }, statusCode: status);

    private static Task<Miembro?> Yo(MiParteDbContext db, Guid userId, CancellationToken ct)
        => db.Miembros.FirstOrDefaultAsync(m => m.UserId == userId && m.Activo, ct);

    private static async Task<IResult> ListarAsync(
        HttpContext ctx, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        var miembros = await db.Miembros.Where(m => m.Activo).OrderBy(m => m.Nombre).ToListAsync(ct);
        return Results.Ok(miembros.Select(ADto).ToList());
    }

    private static async Task<IResult> CrearAsync(
        CrearMiembroRequest req, HttpContext ctx, [FromServices] MiParteDbContext db,
        [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        var yo = await Yo(db, userId, ct);
        if (yo is null || yo.Rol != RolMiembro.Admin) return Error(403, "Solo un administrador puede añadir miembros.");

        var nombre = req.Nombre?.Trim();
        if (string.IsNullOrEmpty(nombre)) return Error(400, "El nombre es obligatorio.");
        if (nombre.Length > MaxLongitudNombre)
            return Error(400, $"El nombre admite como máximo {MaxLongitudNombre} caracteres.");

        TipoMiembro tipo;
        switch (req.Tipo)
        {
            case "adulto": tipo = TipoMiembro.Adulto; break;
            case "a_cargo": tipo = TipoMiembro.ACargo; break;
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

    private static async Task<IResult> ActualizarAsync(
        Guid id, ActualizarMiembroRequest req, HttpContext ctx, [FromServices] MiParteDbContext db,
        CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        var yo = await Yo(db, userId, ct);
        if (yo is null) return Error(403, "No eres miembro activo del hogar.");

        var m = await db.Miembros.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return Error(404, "El miembro no existe en este hogar.");

        var esAdmin = yo.Rol == RolMiembro.Admin;
        if (!esAdmin)
        {
            // Un no-admin solo puede renombrarse a sí mismo.
            if (m.Id != yo.Id) return Error(403, "Solo un administrador puede modificar a otros miembros.");
            if (req.Activo is not null || req.ResponsableId is not null || req.Rol is not null)
                return Error(403, "Solo un administrador puede cambiar el estado, el rol o el responsable.");
        }

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

        if (req.ResponsableId is not null)
        {
            if (m.Tipo != TipoMiembro.ACargo) return Error(400, "Solo un miembro a cargo tiene responsable.");
            var r = await db.Miembros.FirstOrDefaultAsync(x => x.Id == req.ResponsableId, ct);
            if (r is null || !r.Activo || r.Tipo != TipoMiembro.Adulto)
                return Error(400, "El responsable debe ser un adulto activo del hogar.");
        }

        var seActiva = req.Activo ?? m.Activo;
        var rolFinal = nuevoRol ?? m.Rol;

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
        if (req.ResponsableId is not null) m.ResponsableId = req.ResponsableId;

        await db.SaveChangesAsync(ct);
        return Results.Ok(ADto(m));
    }

    /// <summary>Borrado lógico: equivale a PUT con activo=false (mismas reglas).</summary>
    private static Task<IResult> DesactivarAsync(
        Guid id, HttpContext ctx, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        return ActualizarAsync(id, new ActualizarMiembroRequest(Activo: false), ctx, db, ct);
    }

    private static async Task<IResult> CrearInvitacionAsync(
        CrearInvitacionRequest? req, HttpContext ctx, [FromServices] MiParteDbContext db,
        [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        var yo = await Yo(db, userId, ct);
        if (yo is null || yo.Rol != RolMiembro.Admin)
            return Error(403, "Solo un administrador puede crear invitaciones.");

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

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Replica en EF la lógica de la función SQL aceptar_invitacion.</summary>
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
            m.UserId = userId;
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
        return Results.Ok(new HogarResumen(h.Id, h.Nombre));
    }
}
