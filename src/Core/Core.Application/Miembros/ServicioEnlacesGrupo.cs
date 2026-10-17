using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Miembros;

/// <summary>Enlace de grupo recién creado: el token en claro solo existe en este momento.</summary>
/// <param name="Id">Identificador del enlace.</param>
/// <param name="Token">Token en claro, para compartir el enlace.</param>
/// <param name="CaducaEn">Caducidad, o null si no caduca.</param>
public sealed record EnlaceGrupoEmitido(Guid Id, string Token, DateTimeOffset? CaducaEn);

/// <summary>Enlace de grupo tal como se muestra al hogar: sin token, con su estado calculado.</summary>
/// <param name="Id">Identificador del enlace.</param>
/// <param name="CreadoPor">Miembro que lo creó.</param>
/// <param name="CreadoEn">Fecha de creación.</param>
/// <param name="CaducaEn">Caducidad, o null si no caduca.</param>
/// <param name="Usos">Uniones hechas con el enlace.</param>
/// <param name="Estado">Estado en el momento de la consulta.</param>
public sealed record ResumenEnlaceGrupo(
    Guid Id, Guid CreadoPor, DateTimeOffset CreadoEn, DateTimeOffset? CaducaEn, int Usos, EstadoEnlaceGrupo Estado);

/// <summary>
/// Casos de uso de los enlaces de grupo: un enlace reutilizable con el que cualquiera con el token se une al hogar, sin
/// invitación nominal. Crear, listar y revocar exigen <c>enlaces.gestionar</c>; unirse solo exige un token activo.
/// </summary>
public sealed class ServicioEnlacesGrupo(IRepositorioEnlacesGrupo enlaces, IRepositorioMiembros miembros, TimeProvider reloj)
{
    private const string SinPermiso = "No tienes permiso para gestionar los enlaces de grupo.";

    /// <summary>Miembro activo del usuario si tiene <c>enlaces.gestionar</c>; null en otro caso.</summary>
    private async Task<Miembro?> GestorAsync(Guid userId, CancellationToken ct)
    {
        var yo = await miembros.MiembroActivoDeUsuarioAsync(userId, ct);
        return yo is not null && CatalogoPermisos.Efectivos(yo).Contains(CatalogoPermisos.EnlacesGestionar) ? yo : null;
    }

    /// <summary>
    /// Crea un enlace de grupo (permiso <c>enlaces.gestionar</c>). <paramref name="caducaEn"/> es opcional: null no caduca. El token en
    /// claro solo se devuelve aquí; en base de datos queda su hash.
    /// </summary>
    public async Task<Resultado<EnlaceGrupoEmitido>> CrearAsync(Guid userId, Guid hogarId, DateTimeOffset? caducaEn, CancellationToken ct)
    {
        var yo = await GestorAsync(userId, ct);
        if (yo is null) return Resultado<EnlaceGrupoEmitido>.Prohibido(SinPermiso);

        var ahora = reloj.GetUtcNow();
        if (caducaEn is { } caduca && caduca <= ahora)
            return Resultado<EnlaceGrupoEmitido>.Invalido("La fecha de caducidad debe ser posterior a ahora.");

        var token = TokenInvitacion.Generar();
        var enlace = new EnlaceGrupo
        {
            Id = Guid.NewGuid(), HogarId = hogarId, TokenHash = TokenInvitacion.Hash(token), CreadoPor = yo.Id,
            CreadoEn = ahora, CaducaEn = caducaEn, Activo = true, Usos = 0,
        };
        await enlaces.AnadirAsync(enlace, ct);
        return Resultado<EnlaceGrupoEmitido>.Ok(new EnlaceGrupoEmitido(enlace.Id, token, caducaEn));
    }

    /// <summary>Lista los enlaces del hogar con su estado (permiso <c>enlaces.gestionar</c>). No devuelve tokens.</summary>
    public async Task<Resultado<IReadOnlyList<ResumenEnlaceGrupo>>> ListarAsync(Guid userId, CancellationToken ct)
    {
        if (await GestorAsync(userId, ct) is null) return Resultado<IReadOnlyList<ResumenEnlaceGrupo>>.Prohibido(SinPermiso);

        var ahora = reloj.GetUtcNow();
        var lista = (await enlaces.ListarAsync(ct))
            .Select(e => new ResumenEnlaceGrupo(e.Id, e.CreadoPor, e.CreadoEn, e.CaducaEn, e.Usos, ReglasEnlaceGrupo.Estado(e, ahora)))
            .ToList();
        return Resultado<IReadOnlyList<ResumenEnlaceGrupo>>.Ok(lista);
    }

    /// <summary>
    /// Revoca un enlace (permiso <c>enlaces.gestionar</c>): deja de admitir uniones, pero se conserva. Revocar uno ya revocado no
    /// cambia nada. 404 si no es de este hogar.
    /// </summary>
    public async Task<Resultado<EnlaceGrupo>> RevocarAsync(Guid userId, Guid id, CancellationToken ct)
    {
        if (await GestorAsync(userId, ct) is null) return Resultado<EnlaceGrupo>.Prohibido(SinPermiso);

        var enlace = await enlaces.ObtenerAsync(id, ct);
        if (enlace is null) return Resultado<EnlaceGrupo>.NoEncontrado("El enlace de grupo no existe en este hogar.");

        if (enlace.Activo)
        {
            enlace.Activo = false;
            await enlaces.GuardarAsync(ct);
        }
        return Resultado<EnlaceGrupo>.Ok(enlace);
    }

    /// <summary>
    /// Une al usuario (sin hogar actual) al hogar del enlace: como miembro nuevo con <paramref name="nombreNuevo"/>, o vinculado a
    /// <paramref name="miembroId"/> si es un adulto existente sin cuenta. No requiere permiso, pero el enlace debe estar activo y sin
    /// caducar. Cada unión cuenta en <c>usos</c>; el enlace sirve para más personas. 400 sin token o sin nombre; 404 token desconocido o
    /// miembro ajeno; 409 revocado, caducado, usuario ya en el hogar o miembro no disponible.
    /// </summary>
    public async Task<Resultado<Hogar>> UnirseAsync(Guid userId, string? token, string? nombreNuevo, Guid? miembroId, CancellationToken ct)
    {
        token = token?.Trim();
        if (string.IsNullOrEmpty(token)) return Resultado<Hogar>.Invalido("El token es obligatorio.");

        var ahora = reloj.GetUtcNow();
        var enlace = await enlaces.PorHashAsync(TokenInvitacion.Hash(token), ct);
        if (enlace is null) return Resultado<Hogar>.NoEncontrado("Enlace no válido.");
        switch (ReglasEnlaceGrupo.Estado(enlace, ahora))
        {
            case EstadoEnlaceGrupo.Revocado: return Resultado<Hogar>.Conflicto("El enlace de grupo ha sido revocado.");
            case EstadoEnlaceGrupo.Caducado: return Resultado<Hogar>.Conflicto("El enlace de grupo ha caducado.");
        }

        // Un usuario no puede figurar dos veces en el mismo hogar (activo o no).
        if (await miembros.UsuarioEnHogarAsync(enlace.HogarId, userId, ct)) return Resultado<Hogar>.Conflicto("Ya perteneces a este hogar.");

        Miembro? nuevo = null;
        Guid? vincular = null;
        if (miembroId is { } existente)
        {
            var m = await miembros.MiembroDeHogarAsync(enlace.HogarId, existente, ct);
            if (m is null) return Resultado<Hogar>.NoEncontrado("El miembro no existe en este hogar.");
            if (!m.Activo) return Resultado<Hogar>.Conflicto("El miembro está desactivado.");
            if (m.UserId is not null) return Resultado<Hogar>.Conflicto("El miembro ya está vinculado a un usuario.");
            if (m.Tipo != TipoMiembro.Adulto) return Resultado<Hogar>.Conflicto("Solo un adulto puede vincularse con su propia cuenta.");
            vincular = m.Id;
        }
        else
        {
            var nombre = nombreNuevo?.Trim();
            if (string.IsNullOrEmpty(nombre)) return Resultado<Hogar>.Invalido("Hay que indicar un nombre para unirse al hogar.");
            if (nombre.Length > ReglasMiembros.MaxLongitudNombre)
                return Resultado<Hogar>.Invalido($"El nombre admite como máximo {ReglasMiembros.MaxLongitudNombre} caracteres.");
            nuevo = new Miembro
            {
                Id = Guid.NewGuid(), HogarId = enlace.HogarId, Nombre = nombre, Tipo = TipoMiembro.Adulto,
                UserId = userId, Rol = RolMiembro.Miembro,
            };
        }

        switch (await enlaces.UsarAsync(enlace, userId, nuevo, vincular, ahora, ct))
        {
            case ResultadoUsoEnlace.EnlaceNoDisponible:
                return Resultado<Hogar>.Conflicto("El enlace de grupo ha dejado de estar disponible.");
            case ResultadoUsoEnlace.MiembroNoDisponible:
                return Resultado<Hogar>.Conflicto("El miembro ya no está disponible o ya está vinculado a un usuario.");
        }

        return Resultado<Hogar>.Ok(await miembros.HogarAsync(enlace.HogarId, ct));
    }
}
