using MiParte.Core.Application;
using MiParte.Core.Application.Gastos;
using MiParte.Core.Application.Notificaciones;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>Avisos del navegador probados sin HTTP ni base de datos: suscripciones y aviso de gasto nuevo.</summary>
public class ServicioNotificacionesTests
{
    private static readonly Guid Hogar = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 10, 20);
    private const string Endpoint = "https://fcm.googleapis.com/fcm/send/abc123";
    private const string P256dh = "BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM";
    private const string Auth = "tBHItJI5svbpez7KI4CCXg";

    private readonly Guid _userAna = Guid.NewGuid(), _userMarcos = Guid.NewGuid();
    private readonly Miembro _ana, _marcos;
    private readonly ServicioMiembrosTests.RepositorioFalso _miembros = new();
    private readonly RepositorioNotificacionesFalso _notificaciones = new();
    private readonly PublicadorFalso _publicador = new();
    private readonly RelojManual _reloj = new(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));

    public ServicioNotificacionesTests()
    {
        _ana = new Miembro { Id = Guid.NewGuid(), HogarId = Hogar, Nombre = "Ana", UserId = _userAna, Rol = RolMiembro.Admin };
        _marcos = new Miembro { Id = Guid.NewGuid(), HogarId = Hogar, Nombre = "Marcos", UserId = _userMarcos };
        _miembros.Miembros.AddRange([_ana, _marcos]);
    }

    private ServicioNotificaciones Servicio => new(_notificaciones, _miembros, _reloj);

    private static DatosSuscripcionPush Datos(string endpoint = Endpoint, string p256dh = P256dh, string auth = Auth)
        => new(endpoint, p256dh, auth);

    [Fact]
    public async Task Suscribir_GuardaLaSuscripcionDelMiembroActivo()
    {
        var r = await Servicio.SuscribirAsync(_userAna, Hogar, Datos(), default);

        Assert.Null(r.Error);
        var s = Assert.Single(_notificaciones.Suscripciones);
        Assert.Equal((_ana.Id, Hogar, Endpoint), (s.MiembroId, s.HogarId, s.Endpoint));
        Assert.Equal(_reloj.GetUtcNow(), s.CreadaEn);
    }

    [Fact]
    public async Task Suscribir_MismoEndpointSustituyeLaAnteriorAunqueCambieDeMiembro()
    {
        await Servicio.SuscribirAsync(_userAna, Hogar, Datos(), default);
        var r = await Servicio.SuscribirAsync(_userMarcos, Hogar, Datos(), default);

        Assert.Null(r.Error);
        var s = Assert.Single(_notificaciones.Suscripciones);
        Assert.Equal(_marcos.Id, s.MiembroId);
    }

    [Fact]
    public async Task Suscribir_UsuarioSinMiembroActivo_EsProhibido()
    {
        var r = await Servicio.SuscribirAsync(Guid.NewGuid(), Hogar, Datos(), default);

        Assert.Equal(TipoError.Prohibido, r.Error!.Tipo);
        Assert.Empty(_notificaciones.Suscripciones);
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("no-es-una-url")]
    [InlineData("")]
    public async Task Suscribir_EndpointQueNoEsHttps_EsInvalido(string endpoint)
    {
        var r = await Servicio.SuscribirAsync(_userAna, Hogar, Datos(endpoint: endpoint), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Empty(_notificaciones.Suscripciones);
    }

    [Theory]
    [InlineData("", Auth)]
    [InlineData(P256dh, "")]
    public async Task Suscribir_SinClaves_EsInvalido(string p256dh, string auth)
    {
        var r = await Servicio.SuscribirAsync(_userAna, Hogar, Datos(p256dh: p256dh, auth: auth), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Desuscribir_QuitaLaSuscripcionYEsIdempotente()
    {
        await Servicio.SuscribirAsync(_userAna, Hogar, Datos(), default);

        Assert.Null((await Servicio.DesuscribirAsync(_userAna, Endpoint, default)).Error);
        Assert.Null((await Servicio.DesuscribirAsync(_userAna, Endpoint, default)).Error);
        Assert.Empty(_notificaciones.Suscripciones);
    }

    [Fact]
    public async Task Gasto_Nuevo_AvisaSinElPagadorConConceptoEImporte()
    {
        var gastos = GastosConMiembros();
        var gastosServicio = new ServicioGastos(gastos.Repositorio, _publicador);

        var r = await gastosServicio.CrearAsync(Hogar, new DatosGasto(Hoy, 12m, gastos.Categoria, _ana.Id, gastos.Partes, "Café", false, false), default);

        Assert.Null(r.Error);
        var aviso = Assert.Single(_publicador.Publicadas);
        Assert.Equal((Hogar, _ana.Id, "/gastos"), (aviso.HogarId, aviso.ExcluirMiembroId, aviso.Ruta));
        Assert.Equal("Nuevo gasto", aviso.Titulo);
        Assert.Equal("Café · 12,00 €", aviso.Cuerpo);
    }

    [Fact]
    public async Task Gasto_Personal_NoAvisa()
    {
        var gastos = GastosConMiembros();
        var gastosServicio = new ServicioGastos(gastos.Repositorio, _publicador);

        var r = await gastosServicio.CrearAsync(Hogar, new DatosGasto(Hoy, 12m, gastos.Categoria, _ana.Id, gastos.Partes, "Gimnasio", false, true), default);

        Assert.Null(r.Error);
        Assert.Empty(_publicador.Publicadas);
    }

    /// <summary>Hogar en memoria con dos miembros, una categoría y un perfil por partes, como en <see cref="ServicioGastosTests"/>.</summary>
    private (RepositorioGastosFalso Repositorio, Guid Categoria, Guid Partes) GastosConMiembros()
    {
        var repo = new RepositorioGastosFalso();
        var categoria = Guid.NewGuid();
        var partes = new PerfilReparto { Id = Guid.NewGuid(), HogarId = Hogar, Nombre = "Partes", Modo = ModoReparto.Partes };
        partes.Detalles.Add(new PerfilRepartoDetalle { Id = Guid.NewGuid(), HogarId = Hogar, PerfilId = partes.Id, MiembroId = _ana.Id, Valor = 1m });
        partes.Detalles.Add(new PerfilRepartoDetalle { Id = Guid.NewGuid(), HogarId = Hogar, PerfilId = partes.Id, MiembroId = _marcos.Id, Valor = 1m });
        repo.Miembros.AddRange([_ana, _marcos]);
        repo.Categorias.Add(categoria);
        repo.Perfiles.Add(partes);
        return (repo, categoria, partes.Id);
    }

    /// <summary>Repositorio en memoria de las suscripciones push.</summary>
    private sealed class RepositorioNotificacionesFalso : IRepositorioNotificaciones
    {
        public List<SuscripcionPush> Suscripciones { get; } = [];

        public Task ReemplazarAsync(string endpoint, SuscripcionPush nueva, CancellationToken ct)
        {
            Suscripciones.RemoveAll(s => s.Endpoint == endpoint);
            Suscripciones.Add(nueva);
            return Task.CompletedTask;
        }

        public Task<bool> EliminarAsync(Guid miembroId, string endpoint, CancellationToken ct)
            => Task.FromResult(Suscripciones.RemoveAll(s => s.MiembroId == miembroId && s.Endpoint == endpoint) > 0);

        public Task<IReadOnlyList<SuscripcionPush>> DestinatariosAsync(Guid hogarId, Guid? excluirMiembroId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SuscripcionPush>>(
                Suscripciones.Where(s => s.HogarId == hogarId && s.MiembroId != excluirMiembroId).ToList());

        public Task EliminarCaducadaAsync(string endpoint, CancellationToken ct)
        {
            Suscripciones.RemoveAll(s => s.Endpoint == endpoint);
            return Task.CompletedTask;
        }
    }

    /// <summary>Publicador que guarda los avisos para comprobarlos.</summary>
    private sealed class PublicadorFalso : IPublicadorNotificaciones
    {
        public List<NotificacionPush> Publicadas { get; } = [];

        public void Publicar(NotificacionPush notificacion) => Publicadas.Add(notificacion);
    }

    private sealed class RelojManual(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }
}
