using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Miembros;

/// <summary>Datos de un miembro nuevo. <paramref name="Tipo"/> es «adulto» o «a_cargo».</summary>
public sealed record NuevoMiembro(string? Nombre, string? Tipo, Guid? ResponsableId);

/// <summary>Cambios de un miembro: null = sin cambios. <paramref name="Rol"/> es «admin» o «miembro».</summary>
public sealed record CambiosMiembro(string? Nombre, bool? Activo, Guid? ResponsableId, string? Rol, IReadOnlyList<string>? Permisos);

/// <summary>Invitación recién creada: el token en claro solo existe en este momento.</summary>
public sealed record InvitacionEmitida(Guid Id, string Token, DateTimeOffset CaducaEn);

/// <summary>Casos de uso de miembros e invitaciones, con las reglas de permisos y de «siempre queda un administrador».</summary>
public sealed class ServicioMiembros(IRepositorioMiembros repositorio, TimeProvider reloj)
{
    private const string TipoAdulto = "adulto";
    private const string TipoACargo = "a_cargo";

    /// <summary>Miembro activo del hogar actual vinculado al usuario, o null si no lo es.</summary>
    public Task<Miembro?> MiembroActivoAsync(Guid userId, CancellationToken ct)
        => repositorio.MiembroActivoDeUsuarioAsync(userId, ct);

    /// <summary>Miembros del hogar actual ordenados por nombre; los inactivos solo si se piden.</summary>
    public Task<IReadOnlyList<Miembro>> ListarAsync(bool incluirInactivos, CancellationToken ct)
        => repositorio.ListarAsync(incluirInactivos, ct);

    /// <summary>
    /// Añade un miembro «adulto» o «a_cargo» sin usuario vinculado (permiso <c>miembros.gestionar</c>). Un a_cargo exige un
    /// responsable adulto activo y un adulto no puede tenerlo.
    /// </summary>
    public async Task<Resultado<Miembro>> CrearAsync(Guid userId, Guid hogarId, NuevoMiembro datos, CancellationToken ct)
    {
        var yo = await repositorio.MiembroActivoDeUsuarioAsync(userId, ct);
        if (yo is null || !CatalogoPermisos.Efectivos(yo).Contains(CatalogoPermisos.MiembrosGestionar))
            return Resultado<Miembro>.Prohibido("No tienes permiso para añadir miembros.");

        var nombre = datos.Nombre?.Trim();
        if (string.IsNullOrEmpty(nombre)) return Resultado<Miembro>.Invalido("El nombre es obligatorio.");
        if (nombre.Length > ReglasMiembros.MaxLongitudNombre)
            return Resultado<Miembro>.Invalido($"El nombre admite como máximo {ReglasMiembros.MaxLongitudNombre} caracteres.");

        var tipo = datos.Tipo switch
        {
            TipoAdulto => TipoMiembro.Adulto,
            TipoACargo => TipoMiembro.ACargo,
            _ => (TipoMiembro?)null,
        };
        if (tipo is null) return Resultado<Miembro>.Invalido("El tipo debe ser 'adulto' o 'a_cargo'.");

        Guid? responsable = null;
        if (tipo == TipoMiembro.ACargo)
        {
            if (datos.ResponsableId is null) return Resultado<Miembro>.Invalido("Un miembro a cargo necesita un responsable adulto.");
            var r = await repositorio.ObtenerAsync(datos.ResponsableId.Value, ct);
            if (r is null || !r.Activo || r.Tipo != TipoMiembro.Adulto)
                return Resultado<Miembro>.Invalido("El responsable debe ser un adulto activo del hogar.");
            responsable = r.Id;
        }
        else if (datos.ResponsableId is not null)
        {
            return Resultado<Miembro>.Invalido("Solo un miembro a cargo tiene responsable.");
        }

        var nuevo = new Miembro { Id = Guid.NewGuid(), HogarId = hogarId, Nombre = nombre, Tipo = tipo.Value, ResponsableId = responsable };
        await repositorio.AnadirAsync(nuevo, ct);
        return Resultado<Miembro>.Ok(nuevo);
    }

    /// <summary>
    /// Cambia nombre, estado activo, rol, permisos o responsable. Sin <c>miembros.gestionar</c> solo se puede renombrar a uno
    /// mismo. Nunca deja al hogar sin administrador activo y vinculado, ni sin alguien que pueda cambiar permisos, ni desactiva a
    /// un responsable de miembros a cargo activos.
    /// </summary>
    public async Task<Resultado<Miembro>> ActualizarAsync(Guid userId, Guid id, CambiosMiembro cambios, CancellationToken ct)
    {
        var yo = await repositorio.MiembroActivoDeUsuarioAsync(userId, ct);
        if (yo is null) return Resultado<Miembro>.Prohibido("No eres miembro activo del hogar.");

        var m = await repositorio.ObtenerAsync(id, ct);
        if (m is null) return Resultado<Miembro>.NoEncontrado("El miembro no existe en este hogar.");

        var mios = CatalogoPermisos.Efectivos(yo);
        // Renombrarse a uno mismo lo puede cualquiera; tocar a otros, el estado o el responsable exige gestionar miembros.
        if (((cambios.Nombre is not null && m.Id != yo.Id) || cambios.Activo is not null || cambios.ResponsableId is not null)
            && !mios.Contains(CatalogoPermisos.MiembrosGestionar))
            return Resultado<Miembro>.Prohibido("No tienes permiso para modificar a otros miembros, su estado o su responsable.");
        if ((cambios.Rol is not null || cambios.Permisos is not null) && !mios.Contains(CatalogoPermisos.PermisosGestionar))
            return Resultado<Miembro>.Prohibido("No tienes permiso para cambiar el rol ni los permisos de los miembros.");

        string? nombre = null;
        if (cambios.Nombre is not null)
        {
            nombre = cambios.Nombre.Trim();
            if (nombre.Length == 0) return Resultado<Miembro>.Invalido("El nombre no puede estar vacío.");
            if (nombre.Length > ReglasMiembros.MaxLongitudNombre)
                return Resultado<Miembro>.Invalido($"El nombre admite como máximo {ReglasMiembros.MaxLongitudNombre} caracteres.");
        }

        RolMiembro? nuevoRol = null;
        if (cambios.Rol is not null)
        {
            nuevoRol = cambios.Rol switch
            {
                "admin" => RolMiembro.Admin,
                "miembro" => RolMiembro.Miembro,
                _ => null,
            };
            if (nuevoRol is null) return Resultado<Miembro>.Invalido("El rol debe ser 'admin' o 'miembro'.");
        }

        List<string>? nuevosPermisos = null;
        if (cambios.Permisos is not null)
        {
            var desconocido = cambios.Permisos.FirstOrDefault(p => !CatalogoPermisos.Existe(p));
            if (desconocido is not null) return Resultado<Miembro>.Invalido($"El permiso «{desconocido}» no existe.");
            nuevosPermisos = CatalogoPermisos.Todos.Select(p => p.Clave).Where(cambios.Permisos.Contains).ToList();
            if (m.Tipo != TipoMiembro.Adulto || m.UserId is null)
                return Resultado<Miembro>.Conflicto("Solo un adulto con cuenta tiene permisos propios.");
        }

        if (cambios.ResponsableId is not null)
        {
            if (m.Tipo != TipoMiembro.ACargo) return Resultado<Miembro>.Invalido("Solo un miembro a cargo tiene responsable.");
            var r = await repositorio.ObtenerAsync(cambios.ResponsableId.Value, ct);
            if (r is null || !r.Activo || r.Tipo != TipoMiembro.Adulto)
                return Resultado<Miembro>.Invalido("El responsable debe ser un adulto activo del hogar.");
        }

        var seActiva = cambios.Activo ?? m.Activo;
        var rolFinal = nuevoRol ?? m.Rol;

        // Las comprobaciones de «queda alguien» y el guardado van juntas y en exclusiva por hogar: sin eso, dos admins que se
        // degradan o desactivan a la vez ven cada uno al otro como admin activo, ambos guardan y el hogar se queda sin administrador.
        return await repositorio.EjecutarExclusivoAsync(m.HogarId, async () =>
        {
            // Al cambiar de rol sin indicar permisos propios, el miembro vuelve a la plantilla de su rol nuevo.
            var permisosFinales = nuevosPermisos ?? (nuevoRol is not null && nuevoRol != m.Rol ? null : m.Permisos);

            // Siempre debe quedar alguien activo, con cuenta, que pueda cambiar permisos: si no, nadie podría arreglarlo.
            var cambiaAcceso = cambios.Activo is not null || cambios.Rol is not null || cambios.Permisos is not null;
            if (cambiaAcceso && ReglasMiembros.PuedeGestionarPermisos(m, m.Rol, m.Permisos, m.Activo))
            {
                var sigue = ReglasMiembros.PuedeGestionarPermisos(m, rolFinal, permisosFinales, seActiva)
                    || (await repositorio.OtrosConCuentaActivosAsync(m.Id, ct))
                        .Any(x => CatalogoPermisos.Efectivos(x).Contains(CatalogoPermisos.PermisosGestionar));
                if (!sigue) return Resultado<Miembro>.Conflicto("El hogar debe conservar al menos un miembro que pueda cambiar los permisos.");
            }

            // No dejar al hogar sin administrador activo y vinculado.
            if (m.Rol == RolMiembro.Admin && m.Activo && m.UserId is not null && (!seActiva || rolFinal != RolMiembro.Admin)
                && !await repositorio.HayOtroAdminActivoAsync(m.Id, ct))
                return Resultado<Miembro>.Conflicto("El hogar debe conservar al menos un administrador.");

            // Un adulto no se desactiva mientras sea responsable de miembros a cargo activos.
            if (m.Activo && !seActiva && await repositorio.TieneACargoActivosAsync(m.Id, ct))
                return Resultado<Miembro>.Conflicto("Es responsable de miembros a cargo activos: reasígnalos antes.");

            if (nombre is not null) m.Nombre = nombre;
            m.Activo = seActiva;
            m.Rol = rolFinal;
            // Se asigna una lista nueva solo si cambia: la auditoría compara por referencia y no debe registrar un cambio vacío.
            if (!ReglasMiembros.MismosPermisos(permisosFinales, m.Permisos)) m.Permisos = permisosFinales;
            if (cambios.ResponsableId is not null) m.ResponsableId = cambios.ResponsableId;

            await repositorio.GuardarAsync(ct);
            return Resultado<Miembro>.Ok(m);
        }, ct);
    }

    /// <summary>Desactiva un miembro (borrado lógico): equivale a actualizarlo con <c>Activo = false</c>, con las mismas reglas.</summary>
    public Task<Resultado<Miembro>> DesactivarAsync(Guid userId, Guid id, CancellationToken ct)
        => ActualizarAsync(userId, id, new CambiosMiembro(null, false, null, null, null), ct);

    /// <summary>
    /// Crea una invitación (permiso <c>invitaciones.crear</c>), opcionalmente para vincular un miembro existente sin usuario.
    /// El token en claro solo se devuelve aquí; en base de datos queda su hash.
    /// </summary>
    public async Task<Resultado<InvitacionEmitida>> CrearInvitacionAsync(
        Guid userId, Guid hogarId, Guid? miembroId, CancellationToken ct)
    {
        var yo = await repositorio.MiembroActivoDeUsuarioAsync(userId, ct);
        if (yo is null || !CatalogoPermisos.Efectivos(yo).Contains(CatalogoPermisos.InvitacionesCrear))
            return Resultado<InvitacionEmitida>.Prohibido("No tienes permiso para crear invitaciones.");

        if (miembroId is { } destino)
        {
            var m = await repositorio.ObtenerAsync(destino, ct);
            if (m is null) return Resultado<InvitacionEmitida>.NoEncontrado("El miembro no existe en este hogar.");
            if (!m.Activo) return Resultado<InvitacionEmitida>.Conflicto("El miembro está desactivado.");
            if (m.UserId is not null) return Resultado<InvitacionEmitida>.Conflicto("El miembro ya está vinculado a un usuario.");
        }

        var token = TokenInvitacion.Generar();
        var ahora = reloj.GetUtcNow();
        var inv = new InvitacionHogar
        {
            Id = Guid.NewGuid(), HogarId = hogarId, MiembroId = miembroId, TokenHash = TokenInvitacion.Hash(token),
            CreadaPor = userId, CreadaEn = ahora, CaducaEn = ahora + TokenInvitacion.Vigencia,
        };
        await repositorio.GuardarInvitacionAsync(inv, ct);
        return Resultado<InvitacionEmitida>.Ok(new InvitacionEmitida(inv.Id, token, inv.CaducaEn));
    }

    /// <summary>
    /// Acepta una invitación (sin hogar actual): vincula al usuario a un miembro existente o crea uno nuevo con el nombre dado.
    /// Es inválida sin token o sin nombre; no encontrada si el token es desconocido; conflicto si está usada, caducada, el usuario ya
    /// está en el hogar o el miembro no está disponible. Devuelve el hogar al que se une.
    /// </summary>
    public async Task<Resultado<Hogar>> AceptarInvitacionAsync(Guid userId, string? token, string? nombreNuevo, CancellationToken ct)
    {
        token = token?.Trim();
        if (string.IsNullOrEmpty(token)) return Resultado<Hogar>.Invalido("El token es obligatorio.");

        var inv = await repositorio.InvitacionPorHashAsync(TokenInvitacion.Hash(token), ct);
        if (inv is null) return Resultado<Hogar>.NoEncontrado("Invitación no válida.");
        if (inv.UsadaEn is not null) return Resultado<Hogar>.Conflicto("La invitación ya fue utilizada.");
        if (inv.CaducaEn <= reloj.GetUtcNow()) return Resultado<Hogar>.Conflicto("La invitación ha caducado.");

        // Un usuario no puede figurar dos veces en el mismo hogar (activo o no).
        if (await repositorio.UsuarioEnHogarAsync(inv.HogarId, userId, ct)) return Resultado<Hogar>.Conflicto("Ya perteneces a este hogar.");

        Miembro? nuevo = null;
        if (inv.MiembroId is { } existente)
        {
            var m = await repositorio.MiembroDeHogarAsync(inv.HogarId, existente, ct);
            if (m is null || !m.Activo) return Resultado<Hogar>.Conflicto("El miembro de la invitación ya no está disponible.");
            if (m.UserId is not null) return Resultado<Hogar>.Conflicto("El miembro de la invitación ya está vinculado a un usuario.");
        }
        else
        {
            var nombre = nombreNuevo?.Trim();
            if (string.IsNullOrEmpty(nombre)) return Resultado<Hogar>.Invalido("Hay que indicar un nombre para unirse al hogar.");
            if (nombre.Length > ReglasMiembros.MaxLongitudNombre)
                return Resultado<Hogar>.Invalido($"El nombre admite como máximo {ReglasMiembros.MaxLongitudNombre} caracteres.");
            nuevo = new Miembro
            {
                Id = Guid.NewGuid(), HogarId = inv.HogarId, Nombre = nombre, Tipo = TipoMiembro.Adulto,
                UserId = userId, Rol = RolMiembro.Miembro,
            };
        }

        switch (await repositorio.ConsumirInvitacionAsync(inv, userId, nuevo, reloj.GetUtcNow(), ct))
        {
            case ResultadoConsumo.InvitacionNoDisponible:
                return Resultado<Hogar>.Conflicto("La invitación ya fue utilizada o ha caducado.");
            case ResultadoConsumo.MiembroNoDisponible:
                return Resultado<Hogar>.Conflicto("El miembro de la invitación ya no está disponible o ya está vinculado a un usuario.");
        }

        return Resultado<Hogar>.Ok(await repositorio.HogarAsync(inv.HogarId, ct));
    }
}
