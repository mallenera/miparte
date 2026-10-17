using MiParte.Core.Application;
using MiParte.Core.Application.Miembros;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>Casos de uso de miembros e invitaciones probados sin HTTP ni base de datos, con un repositorio en memoria.</summary>
public class ServicioMiembrosTests
{
    private static readonly Guid Hogar = Guid.NewGuid();

    private readonly Guid _userAdmin = Guid.NewGuid(), _userNormal = Guid.NewGuid();
    private readonly Miembro _admin, _normal, _aCargo;
    private readonly RepositorioFalso _repo = new();
    private readonly Reloj _reloj = new(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));

    public ServicioMiembrosTests()
    {
        _admin = new Miembro { Id = Guid.NewGuid(), HogarId = Hogar, Nombre = "Ana", Rol = RolMiembro.Admin, UserId = _userAdmin };
        _normal = new Miembro { Id = Guid.NewGuid(), HogarId = Hogar, Nombre = "Marcos", Rol = RolMiembro.Miembro, UserId = _userNormal };
        _aCargo = new Miembro { Id = Guid.NewGuid(), HogarId = Hogar, Nombre = "Lucía", Tipo = TipoMiembro.ACargo, ResponsableId = _admin.Id };
        _repo.Miembros.AddRange([_admin, _normal, _aCargo]);
    }

    private ServicioMiembros Servicio => new(_repo, _reloj);

    /// <summary>Añade al hogar un adulto sin cuenta (sin usuario vinculado).</summary>
    private Miembro Sin(string nombre)
    {
        var m = new Miembro { Id = Guid.NewGuid(), HogarId = Hogar, Nombre = nombre, Tipo = TipoMiembro.Adulto };
        _repo.Miembros.Add(m);
        return m;
    }

    private static CambiosMiembro Cambios(
        string? nombre = null, bool? activo = null, string? rol = null, IReadOnlyList<string>? permisos = null, Guid? responsable = null)
        => new(nombre, activo, responsable, rol, permisos);

    [Fact]
    public async Task Crear_SinPermiso_EsProhibido()
    {
        var r = await Servicio.CrearAsync(_userNormal, Hogar, new("Pepe", "adulto", null), default);

        Assert.Equal(TipoError.Prohibido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Crear_ACargoSinResponsable_EsInvalido()
    {
        var r = await Servicio.CrearAsync(_userAdmin, Hogar, new("Nico", "a_cargo", null), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Crear_AdultoConResponsable_EsInvalido()
    {
        var r = await Servicio.CrearAsync(_userAdmin, Hogar, new("Pepe", "adulto", _admin.Id), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Crear_ACargoConResponsableAdulto_LoDaDeAlta()
    {
        var r = await Servicio.CrearAsync(_userAdmin, Hogar, new("  Nico ", "a_cargo", _admin.Id), default);

        Assert.Null(r.Error);
        Assert.Equal("Nico", r.Valor!.Nombre);
        Assert.Equal(_admin.Id, r.Valor.ResponsableId);
    }

    [Fact]
    public async Task Actualizar_RenombrarseUnoMismo_NoExigePermiso()
    {
        var r = await Servicio.ActualizarAsync(_userNormal, _normal.Id, Cambios(nombre: "Marc"), default);

        Assert.Null(r.Error);
        Assert.Equal("Marc", _normal.Nombre);
    }

    [Fact]
    public async Task Actualizar_AOtroSinPermiso_EsProhibido()
    {
        var r = await Servicio.ActualizarAsync(_userNormal, _admin.Id, Cambios(nombre: "Jefa"), default);

        Assert.Equal(TipoError.Prohibido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Actualizar_DesactivarAlUnicoAdmin_EsConflicto()
    {
        var r = await Servicio.ActualizarAsync(_userAdmin, _admin.Id, Cambios(activo: false), default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.True(_admin.Activo);
    }

    [Fact]
    public async Task Actualizar_DegradarAlUnicoAdmin_EsConflicto()
    {
        var r = await Servicio.ActualizarAsync(_userAdmin, _admin.Id, Cambios(rol: "miembro"), default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal(RolMiembro.Admin, _admin.Rol);
    }

    [Fact]
    public async Task Actualizar_DegradarAUnAdminHabiendoOtro_Funciona()
    {
        _normal.Rol = RolMiembro.Admin;

        var r = await Servicio.ActualizarAsync(_userAdmin, _admin.Id, Cambios(rol: "miembro"), default);

        Assert.Null(r.Error);
        Assert.Equal(RolMiembro.Miembro, _admin.Rol);
    }

    [Fact]
    public async Task Actualizar_QuitarPermisosGestionarAlUnicoQuePuede_EsConflicto()
    {
        var r = await Servicio.ActualizarAsync(_userAdmin, _admin.Id, Cambios(permisos: [CatalogoPermisos.GastosCrear]), default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task Actualizar_PermisoInexistente_EsInvalido()
    {
        var r = await Servicio.ActualizarAsync(_userAdmin, _normal.Id, Cambios(permisos: ["no.existe"]), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Actualizar_PermisosPropiosAUnACargo_EsConflicto()
    {
        var r = await Servicio.ActualizarAsync(_userAdmin, _aCargo.Id, Cambios(permisos: [CatalogoPermisos.GastosCrear]), default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task Desactivar_ResponsableDeACargoActivos_EsConflicto()
    {
        _normal.Rol = RolMiembro.Admin; // hay otro admin: el bloqueo es el de los miembros a cargo

        var r = await Servicio.DesactivarAsync(_userNormal, _admin.Id, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Contains("responsable", r.Error.Mensaje);
    }

    [Fact]
    public async Task Actualizar_Inexistente_EsNoEncontrado()
    {
        var r = await Servicio.ActualizarAsync(_userAdmin, Guid.NewGuid(), Cambios(nombre: "X"), default);

        Assert.Equal(TipoError.NoEncontrado, r.Error!.Tipo);
    }

    [Fact]
    public async Task Actualizar_SiLaReglaFalla_NoGuardaNada()
    {
        await Servicio.ActualizarAsync(_userAdmin, _admin.Id, Cambios(activo: false), default);

        Assert.Equal(0, _repo.Guardados);
    }

    [Fact]
    public async Task CrearInvitacion_DevuelveTokenEnClaroYGuardaSoloElHash()
    {
        var r = await Servicio.CrearInvitacionAsync(_userAdmin, Hogar, null, default);

        Assert.Null(r.Error);
        var inv = Assert.Single(_repo.Invitaciones);
        Assert.Equal(TokenInvitacion.Hash(r.Valor!.Token), inv.TokenHash);
        Assert.NotEqual(r.Valor.Token, inv.TokenHash);
        Assert.Equal(_reloj.GetUtcNow() + TokenInvitacion.Vigencia, inv.CaducaEn);
    }

    [Fact]
    public async Task CrearInvitacion_ParaMiembroYaVinculado_EsConflicto()
    {
        var r = await Servicio.CrearInvitacionAsync(_userAdmin, Hogar, _normal.Id, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task Aceptar_ConTokenDesconocido_EsNoEncontrado()
    {
        var r = await Servicio.AceptarInvitacionAsync(Guid.NewGuid(), "nope", "Nuevo", default);

        Assert.Equal(TipoError.NoEncontrado, r.Error!.Tipo);
    }

    [Fact]
    public async Task Aceptar_Caducada_EsConflicto()
    {
        var token = (await Servicio.CrearInvitacionAsync(_userAdmin, Hogar, null, default)).Valor!.Token;
        _reloj.Avanzar(TokenInvitacion.Vigencia + TimeSpan.FromSeconds(1));

        var r = await Servicio.AceptarInvitacionAsync(Guid.NewGuid(), token, "Nuevo", default);

        Assert.Equal("La invitación ha caducado.", r.Error!.Mensaje);
    }

    [Fact]
    public async Task Actualizar_RolAdminAUnAdultoSinCuenta_EsInvalido()
    {
        var sinCuenta = Sin("Pepe");

        var r = await Servicio.ActualizarAsync(_userAdmin, sinCuenta.Id, Cambios(rol: "admin"), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Equal(RolMiembro.Miembro, sinCuenta.Rol);
    }

    [Fact]
    public async Task Actualizar_AdminSinCuentaHeredado_NoCuentaComoAdminVinculado()
    {
        // Dato anterior a la regla: un adulto sin cuenta con rol admin no es un administrador vinculado. Marcos sí puede
        // gestionar permisos, así que el bloqueo que se comprueba es el del administrador.
        Sin("Pepe").Rol = RolMiembro.Admin;
        _normal.Permisos = [CatalogoPermisos.PermisosGestionar];

        var r = await Servicio.ActualizarAsync(_userAdmin, _admin.Id, Cambios(rol: "miembro"), default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal("El hogar debe conservar al menos un administrador.", r.Error.Mensaje);
        Assert.Equal(RolMiembro.Admin, _admin.Rol);
    }

    [Fact]
    public async Task CrearInvitacion_ParaAdultoSinCuenta_SinMiembrosGestionar_EsProhibido()
    {
        var sinCuenta = Sin("Pepe");
        _normal.Permisos = [CatalogoPermisos.InvitacionesCrear];

        var r = await Servicio.CrearInvitacionAsync(_userNormal, Hogar, sinCuenta.Id, default);

        Assert.Equal(TipoError.Prohibido, r.Error!.Tipo);
        Assert.Empty(_repo.Invitaciones);
    }

    [Fact]
    public async Task CrearInvitacion_ParaACargo_EsConflicto()
    {
        var r = await Servicio.CrearInvitacionAsync(_userAdmin, Hogar, _aCargo.Id, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task Vincular_AdultoSinCuenta_QuedaLigadoAlUsuarioQueAcepta()
    {
        var sinCuenta = Sin("Pepe");
        var token = (await Servicio.CrearInvitacionAsync(_userAdmin, Hogar, sinCuenta.Id, default)).Valor!.Token;
        var usuario = Guid.NewGuid();

        var r = await Servicio.AceptarInvitacionAsync(usuario, token, null, default);

        Assert.Null(r.Error);
        Assert.Equal(usuario, sinCuenta.UserId);
        Assert.NotNull(_repo.Invitaciones.Single().UsadaEn);
    }

    [Fact]
    public async Task Vincular_UsuarioQueYaEstaEnElHogar_EsConflicto()
    {
        var sinCuenta = Sin("Pepe");
        var token = (await Servicio.CrearInvitacionAsync(_userAdmin, Hogar, sinCuenta.Id, default)).Valor!.Token;

        var r = await Servicio.AceptarInvitacionAsync(_userNormal, token, null, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Null(sinCuenta.UserId);
    }

    [Fact]
    public async Task Vincular_MiembroYaVinculado_RechazaLaSegundaInvitacion()
    {
        var sinCuenta = Sin("Pepe");
        var primera = (await Servicio.CrearInvitacionAsync(_userAdmin, Hogar, sinCuenta.Id, default)).Valor!.Token;
        var segunda = (await Servicio.CrearInvitacionAsync(_userAdmin, Hogar, sinCuenta.Id, default)).Valor!.Token;
        Assert.Null((await Servicio.AceptarInvitacionAsync(Guid.NewGuid(), primera, null, default)).Error);

        var r = await Servicio.AceptarInvitacionAsync(Guid.NewGuid(), segunda, null, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal("El miembro de la invitación ya está vinculado a un usuario.", r.Error.Mensaje);
    }

    [Fact]
    public async Task Aceptar_InvitacionAntiguaDeACargo_EsConflicto()
    {
        // Una invitación creada antes de la regla no puede vincular a un miembro a cargo.
        const string token = "antigua";
        _repo.Invitaciones.Add(new InvitacionHogar
        {
            Id = Guid.NewGuid(), HogarId = Hogar, MiembroId = _aCargo.Id, TokenHash = TokenInvitacion.Hash(token),
            CaducaEn = _reloj.GetUtcNow() + TokenInvitacion.Vigencia,
        });

        var r = await Servicio.AceptarInvitacionAsync(Guid.NewGuid(), token, null, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Null(_aCargo.UserId);
    }

    [Fact]
    public async Task Aceptar_SinNombreYSinMiembro_EsInvalido()
    {
        var token = (await Servicio.CrearInvitacionAsync(_userAdmin, Hogar, null, default)).Valor!.Token;

        var r = await Servicio.AceptarInvitacionAsync(Guid.NewGuid(), token, "  ", default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Aceptar_CreaUnAdultoNuevoYMarcaLaInvitacionComoUsada()
    {
        var token = (await Servicio.CrearInvitacionAsync(_userAdmin, Hogar, null, default)).Valor!.Token;
        var usuario = Guid.NewGuid();

        var r = await Servicio.AceptarInvitacionAsync(usuario, token, "Nuevo", default);

        Assert.Null(r.Error);
        Assert.Contains(_repo.Miembros, m => m.UserId == usuario && m.Nombre == "Nuevo" && m.Tipo == TipoMiembro.Adulto);
        Assert.NotNull(_repo.Invitaciones.Single().UsadaEn);

        var otraVez = await Servicio.AceptarInvitacionAsync(Guid.NewGuid(), token, "Otro", default);
        Assert.Equal("La invitación ya fue utilizada.", otraVez.Error!.Mensaje);
    }

    [Fact]
    public async Task Aceptar_SiUnaPeticionConcurrenteSeAdelanta_EsConflicto()
    {
        var token = (await Servicio.CrearInvitacionAsync(_userAdmin, Hogar, null, default)).Valor!.Token;
        _repo.ResultadoConsumo = ResultadoConsumo.InvitacionNoDisponible;

        var r = await Servicio.AceptarInvitacionAsync(Guid.NewGuid(), token, "Nuevo", default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    /// <summary>Reloj manual para probar la caducidad sin depender de la hora real.</summary>
    private sealed class Reloj(DateTimeOffset ahora) : TimeProvider
    {
        private DateTimeOffset _ahora = ahora;

        public void Avanzar(TimeSpan t) => _ahora += t;

        public override DateTimeOffset GetUtcNow() => _ahora;
    }

    /// <summary>Repositorio en memoria: cada colección es lo que «hay» en la base de datos.</summary>
    private sealed class RepositorioFalso : IRepositorioMiembros
    {
        public List<Miembro> Miembros { get; } = [];
        public List<InvitacionHogar> Invitaciones { get; } = [];
        public int Guardados { get; private set; }
        public ResultadoConsumo ResultadoConsumo { get; set; } = ResultadoConsumo.Consumida;

        public Task<IReadOnlyList<Miembro>> ListarAsync(bool incluirInactivos, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Miembro>>(Miembros.Where(m => incluirInactivos || m.Activo).OrderBy(m => m.Nombre).ToList());

        public Task<Miembro?> MiembroActivoDeUsuarioAsync(Guid userId, CancellationToken ct)
            => Task.FromResult(Miembros.FirstOrDefault(m => m.UserId == userId && m.Activo));

        public Task<Miembro?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(Miembros.FirstOrDefault(m => m.Id == id));

        public Task AnadirAsync(Miembro miembro, CancellationToken ct)
        {
            Miembros.Add(miembro);
            return Task.CompletedTask;
        }

        public Task<bool> HayOtroAdminActivoAsync(Guid excluirId, CancellationToken ct)
            => Task.FromResult(Miembros.Any(x => x.Id != excluirId && x.Rol == RolMiembro.Admin && x.Activo && x.UserId != null));

        public Task<IReadOnlyList<Miembro>> OtrosConCuentaActivosAsync(Guid excluirId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Miembro>>(Miembros.Where(x => x.Id != excluirId && x.Activo && x.UserId != null).ToList());

        public Task<bool> TieneACargoActivosAsync(Guid responsableId, CancellationToken ct)
            => Task.FromResult(Miembros.Any(x => x.ResponsableId == responsableId && x.Activo));

        public Task GuardarAsync(CancellationToken ct)
        {
            Guardados++;
            return Task.CompletedTask;
        }

        public Task<Resultado<T>> EjecutarExclusivoAsync<T>(Guid hogarId, Func<Task<Resultado<T>>> operacion, CancellationToken ct)
            => operacion();

        public Task GuardarInvitacionAsync(InvitacionHogar invitacion, CancellationToken ct)
        {
            Invitaciones.Add(invitacion);
            return Task.CompletedTask;
        }

        public Task<InvitacionHogar?> InvitacionPorHashAsync(string hash, CancellationToken ct)
            => Task.FromResult(Invitaciones.FirstOrDefault(i => i.TokenHash == hash));

        public Task<bool> UsuarioEnHogarAsync(Guid hogarId, Guid userId, CancellationToken ct)
            => Task.FromResult(Miembros.Any(m => m.HogarId == hogarId && m.UserId == userId));

        public Task<Miembro?> MiembroDeHogarAsync(Guid hogarId, Guid miembroId, CancellationToken ct)
            => Task.FromResult(Miembros.FirstOrDefault(m => m.HogarId == hogarId && m.Id == miembroId));

        public Task<ResultadoConsumo> ConsumirInvitacionAsync(
            InvitacionHogar invitacion, Guid userId, Miembro? nuevo, DateTimeOffset ahora, CancellationToken ct)
        {
            if (ResultadoConsumo != ResultadoConsumo.Consumida) return Task.FromResult(ResultadoConsumo);
            invitacion.UsadaEn = ahora;
            invitacion.UsadaPor = userId;
            if (nuevo is not null) Miembros.Add(nuevo);
            else Miembros.First(m => m.Id == invitacion.MiembroId).UserId = userId;
            return Task.FromResult(ResultadoConsumo.Consumida);
        }

        public Task<Hogar> HogarAsync(Guid id, CancellationToken ct) => Task.FromResult(new Hogar { Id = id, Nombre = "Casa" });
    }
}
