using System.Text.Json;
using MiParte.Contracts;
using MiParte.Core.Application;
using MiParte.Core.Application.Miembros;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>Enlaces de grupo reutilizables probados sin HTTP ni base de datos, con repositorios en memoria.</summary>
public class ServicioEnlacesGrupoTests
{
    private static readonly Guid HogarPrueba = Guid.NewGuid();
    private static readonly DateTimeOffset Inicio = new(2026, 10, 17, 10, 0, 0, TimeSpan.Zero);

    private readonly Guid _userAdmin = Guid.NewGuid(), _userSinPermiso = Guid.NewGuid(), _userNuevo = Guid.NewGuid();
    private readonly Miembro _admin, _sinPermiso, _aCargo, _sinCuenta;
    private readonly ServicioMiembrosTests.RepositorioFalso _miembros = new();
    private readonly RepositorioEnlacesFalso _enlaces;
    private readonly RelojManual _reloj = new(Inicio);

    public ServicioEnlacesGrupoTests()
    {
        _admin = new Miembro { Id = Guid.NewGuid(), HogarId = HogarPrueba, Nombre = "Ana", Rol = RolMiembro.Admin, UserId = _userAdmin };
        // Lista propia vacía: no tiene ningún permiso, ni siquiera los de por defecto.
        _sinPermiso = new Miembro { Id = Guid.NewGuid(), HogarId = HogarPrueba, Nombre = "Marcos", UserId = _userSinPermiso, Permisos = [] };
        _aCargo = new Miembro { Id = Guid.NewGuid(), HogarId = HogarPrueba, Nombre = "Lucía", Tipo = TipoMiembro.ACargo, ResponsableId = _admin.Id };
        _sinCuenta = new Miembro { Id = Guid.NewGuid(), HogarId = HogarPrueba, Nombre = "Pepe", Tipo = TipoMiembro.Adulto };
        _miembros.Miembros.AddRange([_admin, _sinPermiso, _aCargo, _sinCuenta]);
        _enlaces = new RepositorioEnlacesFalso(_miembros);
    }

    private ServicioEnlacesGrupo Servicio => new(_enlaces, _miembros, _reloj);

    /// <summary>Añade directamente un enlace al repositorio en memoria, como si ya existiera en la base de datos.</summary>
    private EnlaceGrupo AnadirEnlace(string token, DateTimeOffset? caduca = null, bool activo = true)
    {
        var e = new EnlaceGrupo
        {
            Id = Guid.NewGuid(), HogarId = HogarPrueba, TokenHash = TokenInvitacion.Hash(token), CreadoPor = _admin.Id,
            CreadoEn = Inicio, CaducaEn = caduca, Activo = activo,
        };
        _enlaces.Enlaces.Add(e);
        return e;
    }

    [Fact]
    public void Estado_RevocadoTienePrioridadSobreCaducado()
    {
        var enlace = new EnlaceGrupo { Activo = false, CaducaEn = Inicio.AddHours(-1) };

        Assert.Equal(EstadoEnlaceGrupo.Revocado, ReglasEnlaceGrupo.Estado(enlace, Inicio));
    }

    [Fact]
    public void Estado_SinCaducidadNuncaCaduca()
    {
        var enlace = new EnlaceGrupo { Activo = true, CaducaEn = null };

        Assert.Equal(EstadoEnlaceGrupo.Activo, ReglasEnlaceGrupo.Estado(enlace, Inicio.AddYears(50)));
    }

    [Fact]
    public async Task Crear_SinPermiso_EsProhibido()
    {
        var r = await Servicio.CrearAsync(_userSinPermiso, HogarPrueba, null, default);

        Assert.Equal(TipoError.Prohibido, r.Error!.Tipo);
        Assert.Empty(_enlaces.Enlaces);
    }

    [Fact]
    public async Task Crear_CaducidadYaPasada_EsInvalido()
    {
        var r = await Servicio.CrearAsync(_userAdmin, HogarPrueba, Inicio.AddMinutes(-1), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Crear_DevuelveElTokenUnaVezYGuardaSoloSuHash()
    {
        var r = await Servicio.CrearAsync(_userAdmin, HogarPrueba, null, default);

        Assert.Null(r.Error);
        var emitido = r.Valor!;
        var guardado = Assert.Single(_enlaces.Enlaces);
        Assert.False(string.IsNullOrEmpty(emitido.Token));
        Assert.Equal(TokenInvitacion.Hash(emitido.Token), guardado.TokenHash);
        Assert.NotEqual(emitido.Token, guardado.TokenHash);
        Assert.Equal(_admin.Id, guardado.CreadoPor);
        Assert.Null(guardado.CaducaEn);
        Assert.Null(emitido.CaducaEn);
    }

    [Fact]
    public async Task Listar_NoDevuelveTokensYCalculaElEstadoDeCadaEnlace()
    {
        AnadirEnlace("activo");
        AnadirEnlace("revocado", activo: false);
        AnadirEnlace("caduca", caduca: Inicio.AddHours(1));
        _reloj.Avanzar(TimeSpan.FromHours(2));

        var r = await Servicio.ListarAsync(_userAdmin, default);

        var estados = r.Valor!.Select(e => e.Estado).ToList();
        Assert.Equal(3, estados.Count);
        Assert.Contains(EstadoEnlaceGrupo.Activo, estados);
        Assert.Contains(EstadoEnlaceGrupo.Revocado, estados);
        Assert.Contains(EstadoEnlaceGrupo.Caducado, estados);
    }

    [Fact]
    public async Task Listar_SinPermiso_EsProhibido()
    {
        var r = await Servicio.ListarAsync(_userSinPermiso, default);

        Assert.Equal(TipoError.Prohibido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Revocar_DesactivaElEnlaceYRepetirloNoCambiaNada()
    {
        var enlace = AnadirEnlace("tok");

        var primera = await Servicio.RevocarAsync(_userAdmin, enlace.Id, default);
        var segunda = await Servicio.RevocarAsync(_userAdmin, enlace.Id, default);

        Assert.Null(primera.Error);
        Assert.Null(segunda.Error);
        Assert.False(enlace.Activo);
        Assert.Equal(1, _enlaces.Guardados);
    }

    [Fact]
    public async Task Revocar_IdInexistenteEnEsteHogar_EsNoEncontrado()
    {
        var r = await Servicio.RevocarAsync(_userAdmin, Guid.NewGuid(), default);

        Assert.Equal(TipoError.NoEncontrado, r.Error!.Tipo);
    }

    [Fact]
    public async Task Revocar_SinPermiso_EsProhibidoYNoRevoca()
    {
        var enlace = AnadirEnlace("tok");

        var r = await Servicio.RevocarAsync(_userSinPermiso, enlace.Id, default);

        Assert.Equal(TipoError.Prohibido, r.Error!.Tipo);
        Assert.True(enlace.Activo);
    }

    [Fact]
    public async Task Unirse_TokenValido_DaDeAltaUnAdultoConSuNombreYCuentaElUso()
    {
        var enlace = AnadirEnlace("tok");

        var r = await Servicio.UnirseAsync(_userNuevo, "tok", "  Laura ", default);

        Assert.Null(r.Error);
        Assert.Equal(HogarPrueba, r.Valor!.Id);
        var nuevo = Assert.Single(_miembros.Miembros, m => m.UserId == _userNuevo);
        Assert.Equal("Laura", nuevo.Nombre);
        Assert.Equal(TipoMiembro.Adulto, nuevo.Tipo);
        Assert.Equal(RolMiembro.Miembro, nuevo.Rol);
        Assert.Equal(1, enlace.Usos);
    }

    [Fact]
    public async Task Unirse_UsoRepetido_EsPermitidoYCadaUsuarioEntra()
    {
        var enlace = AnadirEnlace("tok");
        var otro = Guid.NewGuid();

        var primera = await Servicio.UnirseAsync(_userNuevo, "tok", "Laura", default);
        var segunda = await Servicio.UnirseAsync(otro, "tok", "Iván", default);

        Assert.Null(primera.Error);
        Assert.Null(segunda.Error);
        Assert.Equal(2, enlace.Usos);
        Assert.Equal(2, _miembros.Miembros.Count(m => m.UserId is not null && m.UserId != _userAdmin && m.UserId != _userSinPermiso));
    }

    [Fact]
    public async Task Unirse_EnlaceRevocado_EsConflicto()
    {
        AnadirEnlace("tok", activo: false);

        var r = await Servicio.UnirseAsync(_userNuevo, "tok", "Laura", default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.DoesNotContain(_miembros.Miembros, m => m.UserId == _userNuevo);
    }

    [Fact]
    public async Task Unirse_EnlaceCaducado_EsConflicto()
    {
        AnadirEnlace("tok", caduca: Inicio.AddHours(1));
        _reloj.Avanzar(TimeSpan.FromHours(2));

        var r = await Servicio.UnirseAsync(_userNuevo, "tok", "Laura", default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task Unirse_EnlaceSinCaducidad_SigueValidoMuchoTiempoDespues()
    {
        AnadirEnlace("tok", caduca: null);
        _reloj.Avanzar(TimeSpan.FromDays(3650));

        var r = await Servicio.UnirseAsync(_userNuevo, "tok", "Laura", default);

        Assert.Null(r.Error);
    }

    [Fact]
    public async Task Unirse_TokenDesconocido_EsNoEncontrado()
    {
        var r = await Servicio.UnirseAsync(_userNuevo, "no-existe", "Laura", default);

        Assert.Equal(TipoError.NoEncontrado, r.Error!.Tipo);
    }

    [Fact]
    public async Task Unirse_TokenVacio_EsInvalido()
    {
        var r = await Servicio.UnirseAsync(_userNuevo, "   ", "Laura", default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Unirse_SinNombreYSinMiembro_EsInvalido()
    {
        AnadirEnlace("tok");

        var r = await Servicio.UnirseAsync(_userNuevo, "tok", "  ", default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Unirse_UsuarioYaEnElHogar_EsConflicto()
    {
        AnadirEnlace("tok");

        var r = await Servicio.UnirseAsync(_userAdmin, "tok", "Ana otra vez", default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task Unirse_ConMiembroIdEnLaPeticion_SeIgnoraYCreaUnMiembroNuevo()
    {
        // Un cliente malicioso envía el id de un adulto sin cuenta: el enlace no puede vincularlo a su propia cuenta.
        AnadirEnlace("tok");
        var antes = _miembros.Miembros.Count;
        var cuerpo = $"{{\"token\":\"tok\",\"nombre\":\"Laura\",\"miembroId\":\"{_sinCuenta.Id}\"}}";
        var peticion = JsonSerializer.Deserialize<UnirseEnlaceGrupoRequest>(cuerpo, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var r = await Servicio.UnirseAsync(_userNuevo, peticion.Token, peticion.Nombre, default);

        Assert.Null(r.Error);
        Assert.Null(_sinCuenta.UserId);
        Assert.Equal(antes + 1, _miembros.Miembros.Count);
        Assert.Contains(_miembros.Miembros, m => m.UserId == _userNuevo && m.Nombre == "Laura" && m.Id != _sinCuenta.Id);
    }

    [Fact]
    public async Task Unirse_SiOtraPeticionSeAdelantaAlUso_EsConflicto()
    {
        AnadirEnlace("tok");
        _enlaces.ResultadoUso = ResultadoUsoEnlace.EnlaceNoDisponible;

        var r = await Servicio.UnirseAsync(_userNuevo, "tok", "Laura", default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    /// <summary>Reloj que avanza solo cuando el test lo pide.</summary>
    private sealed class RelojManual(DateTimeOffset ahora) : TimeProvider
    {
        private DateTimeOffset _ahora = ahora;

        public void Avanzar(TimeSpan t) => _ahora += t;

        public override DateTimeOffset GetUtcNow() => _ahora;
    }

    /// <summary>Repositorio de enlaces en memoria; las uniones tocan el repositorio de miembros compartido con el test.</summary>
    private sealed class RepositorioEnlacesFalso(ServicioMiembrosTests.RepositorioFalso miembros) : IRepositorioEnlacesGrupo
    {
        public List<EnlaceGrupo> Enlaces { get; } = [];
        public int Guardados { get; private set; }
        public ResultadoUsoEnlace ResultadoUso { get; set; } = ResultadoUsoEnlace.Usado;

        public Task<IReadOnlyList<EnlaceGrupo>> ListarAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<EnlaceGrupo>>(Enlaces.OrderByDescending(e => e.CreadoEn).ToList());

        public Task<EnlaceGrupo?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(Enlaces.FirstOrDefault(e => e.Id == id));

        public Task AnadirAsync(EnlaceGrupo enlace, CancellationToken ct)
        {
            Enlaces.Add(enlace);
            return Task.CompletedTask;
        }

        public Task GuardarAsync(CancellationToken ct)
        {
            Guardados++;
            return Task.CompletedTask;
        }

        public Task<EnlaceGrupo?> PorHashAsync(string hash, CancellationToken ct)
            => Task.FromResult(Enlaces.FirstOrDefault(e => e.TokenHash == hash));

        public Task<ResultadoUsoEnlace> UsarAsync(EnlaceGrupo enlace, Guid userId, Miembro nuevo, DateTimeOffset ahora, CancellationToken ct)
        {
            if (ResultadoUso != ResultadoUsoEnlace.Usado) return Task.FromResult(ResultadoUso);
            enlace.Usos++;
            miembros.Miembros.Add(nuevo);
            return Task.FromResult(ResultadoUsoEnlace.Usado);
        }
    }
}
