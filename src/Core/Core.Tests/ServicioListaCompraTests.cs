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

        public Task ActualizarEnlaceAsync(EnlaceListaCompra enlace, CancellationToken ct) => Task.CompletedTask;

        public Task<EnlaceListaCompra?> EnlacePorHashAsync(string tokenHash, CancellationToken ct)
            => Task.FromResult(Enlaces.FirstOrDefault(e => e.TokenHash == tokenHash));
    }
}
