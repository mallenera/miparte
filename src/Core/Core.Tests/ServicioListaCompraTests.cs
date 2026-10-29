using MiParte.Core.Application;
using MiParte.Core.Application.ListaCompra;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>Lista de la compra probada sin HTTP ni base de datos: reglas, activación, productos y enlaces QR.</summary>
public class ServicioListaCompraTests
{
    private static readonly Guid Hogar = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();

    private readonly RepositorioListaFalso _repo = new();
    private readonly RelojFalso _reloj = new(new DateTimeOffset(2026, 10, 28, 10, 0, 0, TimeSpan.Zero));

    private ServicioListaCompra Servicio => new(_repo, _reloj);

    public ServicioListaCompraTests()
    {
        _repo.Activas.Add(Hogar);
    }

    [Theory]
    [InlineData(null, "El producto necesita un nombre.")]
    [InlineData("   ", "El producto necesita un nombre.")]
    public void ValidarNombre_RechazaVacio(string? nombre, string esperado)
        => Assert.Equal(esperado, ReglasListaCompra.ValidarNombre(nombre));

    [Fact]
    public void ValidarNombre_RechazaMasDeCienCaracteres()
        => Assert.NotNull(ReglasListaCompra.ValidarNombre(new string('a', ReglasListaCompra.MaxNombre + 1)));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(999, true)]
    [InlineData(1000, false)]
    public void ValidarCantidad_SoloAceptaEnteros1a999(int cantidad, bool valida)
        => Assert.Equal(valida, ReglasListaCompra.ValidarCantidad(cantidad) is null);

    [Fact]
    public void Vigente_NoAdmiteUnEnlaceRevocadoNiCaducado()
    {
        var ahora = _reloj.GetUtcNow();
        Assert.False(ReglasListaCompra.Vigente(false, null, ahora));
        Assert.False(ReglasListaCompra.Vigente(true, ahora, ahora));
        Assert.True(ReglasListaCompra.Vigente(true, ahora.AddMinutes(1), ahora));
    }

    [Fact]
    public async Task Anadir_ConLaListaDesactivada_EsConflicto()
    {
        _repo.Activas.Clear();

        var r = await Servicio.AnadirAsync(Hogar, "Leche", 1, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task Anadir_SinCantidad_UsaUno_YRecortaElNombre()
    {
        var r = await Servicio.AnadirAsync(Hogar, "  Pan  ", 1, default);

        Assert.Null(r.Error);
        Assert.Equal("Pan", r.Valor!.Nombre);
        Assert.Equal(1, r.Valor.Cantidad);
        Assert.False(r.Valor.Comprado);
    }

    [Fact]
    public async Task Anadir_CantidadInvalida_EsInvalido()
    {
        var r = await Servicio.AnadirAsync(Hogar, "Pan", 0, default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Marcar_UnProductoDeOtroHogar_NoEncuentra()
    {
        var otro = new ProductoListaCompra { Id = Guid.NewGuid(), HogarId = Guid.NewGuid(), Nombre = "Huevos", Cantidad = 1 };
        _repo.Productos.Add(otro);

        var r = await Servicio.MarcarAsync(Hogar, otro.Id, true, default);

        Assert.Equal(TipoError.NoEncontrado, r.Error!.Tipo);
        Assert.False(otro.Comprado);
    }

    [Fact]
    public async Task CrearEnlace_ConLaListaDesactivada_EsConflicto()
    {
        _repo.Activas.Clear();

        var r = await Servicio.CrearEnlaceAsync(Hogar, Ana, null, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task CrearEnlace_SinCaducidad_CaduceAlosSieteDias()
    {
        var r = await Servicio.CrearEnlaceAsync(Hogar, Ana, null, default);

        Assert.Null(r.Error);
        Assert.Equal(_reloj.GetUtcNow() + ReglasListaCompra.VigenciaEnlace, r.Valor!.CaducaEn);
        // Solo se guarda el hash del token, nunca el token en claro.
        Assert.DoesNotContain(r.Valor.Token, _repo.Enlaces.Select(e => e.TokenHash));
    }

    [Fact]
    public async Task Publica_ConTokenDesconocido_NoEncuentra()
    {
        var r = await Servicio.PublicaAsync("token-que-no-existe", default);

        Assert.Equal(TipoError.NoEncontrado, r.Error!.Tipo);
    }

    [Fact]
    public async Task Publica_ConEnlaceRevocado_EsConflicto()
    {
        var emitido = (await Servicio.CrearEnlaceAsync(Hogar, Ana, null, default)).Valor!;
        await Servicio.RevocarEnlaceAsync(Hogar, emitido.Id, default);

        var r = await Servicio.PublicaAsync(emitido.Token, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task Publica_ConEnlaceCaducado_EsConflicto()
    {
        var emitido = (await Servicio.CrearEnlaceAsync(Hogar, Ana, null, default)).Valor!;
        _reloj.Avanzar(ReglasListaCompra.VigenciaEnlace + TimeSpan.FromMinutes(1));

        var r = await Servicio.PublicaAsync(emitido.Token, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task AnadirPorEnlace_AñadeAlHogarDelEnlace_YNoVeOtrosProductos()
    {
        var emitido = (await Servicio.CrearEnlaceAsync(Hogar, Ana, null, default)).Valor!;
        _repo.Productos.Add(new ProductoListaCompra { Id = Guid.NewGuid(), HogarId = Guid.NewGuid(), Nombre = "Ajeno", Cantidad = 1 });

        var anadido = await Servicio.AnadirPublicoAsync(emitido.Token, "Tomates", 3, default);
        var lista = await Servicio.PublicaAsync(emitido.Token, default);

        Assert.Equal(Hogar, anadido.Valor!.HogarId);
        var producto = Assert.Single(lista.Valor!.Productos);
        Assert.Equal("Tomates", producto.Nombre);
    }

    [Fact]
    public async Task MarcarPorEnlace_UnProductoAjeno_NoEncuentra()
    {
        var emitido = (await Servicio.CrearEnlaceAsync(Hogar, Ana, null, default)).Valor!;
        var ajeno = new ProductoListaCompra { Id = Guid.NewGuid(), HogarId = Guid.NewGuid(), Nombre = "Ajeno", Cantidad = 1 };
        _repo.Productos.Add(ajeno);

        var r = await Servicio.MarcarPublicoAsync(emitido.Token, ajeno.Id, true, default);

        Assert.Equal(TipoError.NoEncontrado, r.Error!.Tipo);
    }

    [Fact]
    public async Task MarcarPorEnlace_DeUnProductoDelHogar_LoMarca()
    {
        var emitido = (await Servicio.CrearEnlaceAsync(Hogar, Ana, null, default)).Valor!;
        var anadido = (await Servicio.AnadirPublicoAsync(emitido.Token, "Agua", 1, default)).Valor!;

        var r = await Servicio.MarcarPublicoAsync(emitido.Token, anadido.Id, true, default);

        Assert.True(r.Valor!.Comprado);
    }

    [Fact]
    public async Task Activar_YDesactivar_NoBorranProductos_SoloBloqueanLosCambios()
    {
        await Servicio.AnadirAsync(Hogar, "Pan", 1, default);

        await Servicio.ActivarAsync(Hogar, false, default);

        Assert.False(await Servicio.ActivaAsync(Hogar, default));
        Assert.Single(await Servicio.ListarAsync(Hogar, default));
        Assert.Equal(TipoError.Conflicto, (await Servicio.AnadirAsync(Hogar, "Leche", 1, default)).Error!.Tipo);

        await Servicio.ActivarAsync(Hogar, true, default);
        Assert.Null((await Servicio.AnadirAsync(Hogar, "Leche", 1, default)).Error);
    }

    [Fact]
    public async Task Anadir_AdmiteUnNombreDeCienCaracteres_YUnaCantidadDe999()
    {
        var r = await Servicio.AnadirAsync(Hogar, new string('a', 100), 999, default);

        Assert.Null(r.Error);
        Assert.Equal(999, r.Valor!.Cantidad);
    }

    [Fact]
    public async Task Marcar_YDesmarcar_VuelveAlEstadoAnterior()
    {
        var p = (await Servicio.AnadirAsync(Hogar, "Pan", 1, default)).Valor!;

        Assert.True((await Servicio.MarcarAsync(Hogar, p.Id, true, default)).Valor!.Comprado);
        Assert.False((await Servicio.MarcarAsync(Hogar, p.Id, false, default)).Valor!.Comprado);
    }

    [Fact]
    public async Task Eliminar_BorraElProducto_YUnoQueNoExisteNoSeEncuentra()
    {
        var p = (await Servicio.AnadirAsync(Hogar, "Pan", 1, default)).Valor!;

        Assert.Null((await Servicio.EliminarAsync(Hogar, p.Id, default)).Error);
        Assert.Equal(TipoError.NoEncontrado, (await Servicio.EliminarAsync(Hogar, p.Id, default)).Error!.Tipo);
        Assert.Empty(await Servicio.ListarAsync(Hogar, default));
    }

    [Fact]
    public async Task Eliminar_UnProductoDeOtroHogar_NoLoBorra()
    {
        var ajeno = new ProductoListaCompra { Id = Guid.NewGuid(), HogarId = Guid.NewGuid(), Nombre = "Ajeno", Cantidad = 1 };
        _repo.Productos.Add(ajeno);

        var r = await Servicio.EliminarAsync(Hogar, ajeno.Id, default);

        Assert.Equal(TipoError.NoEncontrado, r.Error!.Tipo);
        Assert.Contains(ajeno, _repo.Productos);
    }

    [Fact]
    public async Task CrearEnlace_ConUnaCaducidadPasada_EsInvalido_YConUnaFutura_LaRespeta()
    {
        var pasada = await Servicio.CrearEnlaceAsync(Hogar, Ana, _reloj.GetUtcNow().AddMinutes(-1), default);
        var futura = _reloj.GetUtcNow().AddDays(2);
        var valida = await Servicio.CrearEnlaceAsync(Hogar, Ana, futura, default);

        Assert.Equal(TipoError.Invalido, pasada.Error!.Tipo);
        Assert.Equal(futura, valida.Valor!.CaducaEn);
        Assert.Single(_repo.Enlaces);
    }

    [Fact]
    public async Task CadaEnlaceTieneSuPropioToken_YRevocarUnoNoAfectaAlOtro()
    {
        var a = (await Servicio.CrearEnlaceAsync(Hogar, Ana, null, default)).Valor!;
        var b = (await Servicio.CrearEnlaceAsync(Hogar, Ana, null, default)).Valor!;

        await Servicio.RevocarEnlaceAsync(Hogar, a.Id, default);

        Assert.NotEqual(a.Token, b.Token);
        Assert.Equal(TipoError.Conflicto, (await Servicio.PublicaAsync(a.Token, default)).Error!.Tipo);
        Assert.Null((await Servicio.PublicaAsync(b.Token, default)).Error);
    }

    [Fact]
    public async Task RevocarUnEnlaceDeOtroHogar_NoLoEncuentra_YElEnlaceSigueVigente()
    {
        var otroHogar = Guid.NewGuid();
        _repo.Activas.Add(otroHogar);
        var ajeno = (await Servicio.CrearEnlaceAsync(otroHogar, Ana, null, default)).Valor!;

        var r = await Servicio.RevocarEnlaceAsync(Hogar, ajeno.Id, default);

        Assert.Equal(TipoError.NoEncontrado, r.Error!.Tipo);
        Assert.Null((await Servicio.PublicaAsync(ajeno.Token, default)).Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SinToken_ElAccesoPublicoNoEncuentraNada(string? token)
    {
        Assert.Equal(TipoError.NoEncontrado, (await Servicio.PublicaAsync(token, default)).Error!.Tipo);
        Assert.Equal(TipoError.NoEncontrado, (await Servicio.AnadirPublicoAsync(token, "Pan", 1, default)).Error!.Tipo);
        Assert.Equal(TipoError.NoEncontrado, (await Servicio.MarcarPublicoAsync(token, Guid.NewGuid(), true, default)).Error!.Tipo);
    }

    [Fact]
    public async Task ConLaListaDesactivada_ElEnlaceDejaDeServir_YVuelveAServirAlReactivarla()
    {
        var e = (await Servicio.CrearEnlaceAsync(Hogar, Ana, null, default)).Valor!;

        await Servicio.ActivarAsync(Hogar, false, default);
        Assert.Equal(TipoError.Conflicto, (await Servicio.PublicaAsync(e.Token, default)).Error!.Tipo);
        Assert.Equal(TipoError.Conflicto, (await Servicio.AnadirPublicoAsync(e.Token, "Pan", 1, default)).Error!.Tipo);

        await Servicio.ActivarAsync(Hogar, true, default);
        Assert.Null((await Servicio.PublicaAsync(e.Token, default)).Error);
    }

    [Fact]
    public async Task AnadirPorEnlace_ValidaIgualQueDentro()
    {
        var e = (await Servicio.CrearEnlaceAsync(Hogar, Ana, null, default)).Valor!;

        Assert.Equal(TipoError.Invalido, (await Servicio.AnadirPublicoAsync(e.Token, " ", 1, default)).Error!.Tipo);
        Assert.Equal(TipoError.Invalido, (await Servicio.AnadirPublicoAsync(e.Token, "Pan", 1000, default)).Error!.Tipo);
        Assert.Empty(_repo.Productos);
    }

    [Fact]
    public async Task ElEnlaceExpiraJustoEnSuCaducidad()
    {
        var e = (await Servicio.CrearEnlaceAsync(Hogar, Ana, _reloj.GetUtcNow().AddHours(1), default)).Valor!;

        _reloj.Avanzar(TimeSpan.FromMinutes(59));
        Assert.Null((await Servicio.PublicaAsync(e.Token, default)).Error);

        _reloj.Avanzar(TimeSpan.FromMinutes(1));
        Assert.Equal(TipoError.Conflicto, (await Servicio.PublicaAsync(e.Token, default)).Error!.Tipo);
    }

    [Theory]
    [InlineData(true, false, EstadoEnlaceLista.Vigente, "vigente")]
    [InlineData(true, true, EstadoEnlaceLista.Caducado, "caducado")]
    [InlineData(false, false, EstadoEnlaceLista.Revocado, "revocado")]
    [InlineData(false, true, EstadoEnlaceLista.Revocado, "revocado")] // revocar manda sobre caducar
    public void ElEstadoDeUnEnlace_DependeDeSiSeRevocoYDeSuCaducidad(bool activo, bool caducado, EstadoEnlaceLista esperado, string texto)
    {
        var ahora = _reloj.GetUtcNow();
        DateTimeOffset? caduca = caducado ? ahora.AddMinutes(-1) : ahora.AddMinutes(1);

        var estado = ReglasListaCompra.Estado(activo, caduca, ahora);

        Assert.Equal(esperado, estado);
        Assert.Equal(texto, ReglasListaCompra.TextoEstado(estado));
        Assert.Equal(esperado == EstadoEnlaceLista.Vigente, ReglasListaCompra.Vigente(activo, caduca, ahora));
    }

    [Fact]
    public async Task ListarEnlaces_DevuelveLosDelHogar_ConSuEstado_ElMasRecientePrimero_YSinToken()
    {
        var viejo = (await Servicio.CrearEnlaceAsync(Hogar, Ana, null, default)).Valor!;
        _reloj.Avanzar(TimeSpan.FromHours(1));
        var revocado = (await Servicio.CrearEnlaceAsync(Hogar, Ana, null, default)).Valor!;
        await Servicio.RevocarEnlaceAsync(Hogar, revocado.Id, default);
        _reloj.Avanzar(TimeSpan.FromHours(1));
        var reciente = (await Servicio.CrearEnlaceAsync(Hogar, Ana, null, default)).Valor!;
        var otroHogar = Guid.NewGuid();
        _repo.Activas.Add(otroHogar);
        await Servicio.CrearEnlaceAsync(otroHogar, Ana, null, default);
        _reloj.Avanzar(ReglasListaCompra.VigenciaEnlace - TimeSpan.FromHours(1)); // el primero ya pasó de 7 días; el reciente aún no

        var enlaces = await Servicio.ListarEnlacesAsync(Hogar, default);

        Assert.Equal([reciente.Id, revocado.Id, viejo.Id], enlaces.Select(e => e.Id));
        Assert.Equal([EstadoEnlaceLista.Vigente, EstadoEnlaceLista.Revocado, EstadoEnlaceLista.Caducado], enlaces.Select(e => e.Estado));
        Assert.All(enlaces, e => Assert.Equal(Ana, e.CreadoPor));
        // El resumen no tiene dónde llevar el token ni su hash.
        Assert.DoesNotContain(typeof(ResumenEnlaceLista).GetProperties(), p => p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListarEnlaces_FuncionaConLaListaDesactivada_YSinEnlacesDevuelveVacio()
    {
        Assert.Empty(await Servicio.ListarEnlacesAsync(Hogar, default));
        await Servicio.CrearEnlaceAsync(Hogar, Ana, null, default);
        await Servicio.ActivarAsync(Hogar, false, default);

        Assert.Single(await Servicio.ListarEnlacesAsync(Hogar, default));
    }

    /// <summary>Reloj que el test mueve a mano.</summary>
    private sealed class RelojFalso(DateTimeOffset ahora) : TimeProvider
    {
        private DateTimeOffset _ahora = ahora;

        public override DateTimeOffset GetUtcNow() => _ahora;

        public void Avanzar(TimeSpan tiempo) => _ahora += tiempo;
    }

    /// <summary>Repositorio en memoria de la lista de la compra.</summary>
    private sealed class RepositorioListaFalso : IRepositorioListaCompra
    {
        public HashSet<Guid> Activas { get; } = [];
        public List<ProductoListaCompra> Productos { get; } = [];
        public List<EnlaceListaCompra> Enlaces { get; } = [];

        public Task<bool> ActivaAsync(Guid hogarId, CancellationToken ct) => Task.FromResult(Activas.Contains(hogarId));

        public Task EstablecerActivaAsync(Guid hogarId, bool activa, CancellationToken ct)
        {
            if (activa) Activas.Add(hogarId);
            else Activas.Remove(hogarId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProductoListaCompra>> ProductosAsync(Guid hogarId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ProductoListaCompra>>(Productos.Where(p => p.HogarId == hogarId).ToList());

        public Task<ProductoListaCompra?> ProductoAsync(Guid hogarId, Guid id, CancellationToken ct)
            => Task.FromResult(Productos.FirstOrDefault(p => p.HogarId == hogarId && p.Id == id));

        public Task GuardarProductoAsync(ProductoListaCompra producto, CancellationToken ct)
        {
            Productos.Add(producto);
            return Task.CompletedTask;
        }

        public Task ActualizarProductoAsync(ProductoListaCompra producto, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> EliminarProductoAsync(Guid hogarId, Guid id, CancellationToken ct)
            => Task.FromResult(Productos.RemoveAll(p => p.HogarId == hogarId && p.Id == id) > 0);

        public Task GuardarEnlaceAsync(EnlaceListaCompra enlace, CancellationToken ct)
        {
            Enlaces.Add(enlace);
            return Task.CompletedTask;
        }

        public Task<EnlaceListaCompra?> EnlaceAsync(Guid hogarId, Guid id, CancellationToken ct)
            => Task.FromResult(Enlaces.FirstOrDefault(e => e.HogarId == hogarId && e.Id == id));

        public Task<IReadOnlyList<EnlaceListaCompra>> EnlacesAsync(Guid hogarId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<EnlaceListaCompra>>(
                Enlaces.Where(e => e.HogarId == hogarId).OrderByDescending(e => e.CreadoEn).ThenBy(e => e.Id).ToList());

        public Task ActualizarEnlaceAsync(EnlaceListaCompra enlace, CancellationToken ct) => Task.CompletedTask;

        public Task<EnlaceListaCompra?> EnlacePorHashAsync(string tokenHash, CancellationToken ct)
            => Task.FromResult(Enlaces.FirstOrDefault(e => e.TokenHash == tokenHash));
    }
}
