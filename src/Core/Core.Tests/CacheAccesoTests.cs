using MiParte.Core.Api.Hogares;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>Política de la caché de acceso: vigencia, purga de entradas caducadas y lecturas anteriores a una invalidación.</summary>
public class CacheAccesoTests
{
    /// <summary>Reloj que solo avanza cuando la prueba lo manda (Core.Tests no referencia el paquete de relojes falsos).</summary>
    private sealed class RelojManual(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _ahora = inicio;
        public override DateTimeOffset GetUtcNow() => _ahora;
        public void Advance(TimeSpan t) => _ahora += t;
    }

    private readonly RelojManual _reloj = new(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));

    private static Miembro Miembro(Guid hogar, Guid usuario, string nombre = "Ana")
        => new() { Id = Guid.NewGuid(), HogarId = hogar, UserId = usuario, Nombre = nombre };

    [Fact]
    public async Task Reutiliza_la_lectura_dentro_de_la_vigencia_y_vuelve_a_leer_al_caducar()
    {
        var cache = new CacheAcceso(_reloj);
        var usuario = Guid.NewGuid();
        var lecturas = 0;
        Task<List<Miembro>> Leer() { lecturas++; return Task.FromResult(new List<Miembro> { Miembro(Guid.NewGuid(), usuario) }); }

        await cache.MiembrosActivosAsync(usuario, Leer);
        await cache.MiembrosActivosAsync(usuario, Leer);
        Assert.Equal(1, lecturas);

        _reloj.Advance(CacheAcceso.Vigencia + TimeSpan.FromSeconds(1));
        await cache.MiembrosActivosAsync(usuario, Leer);
        Assert.Equal(2, lecturas);
    }

    [Fact]
    public async Task Las_entradas_caducadas_de_otros_usuarios_se_quitan_de_memoria()
    {
        var cache = new CacheAcceso(_reloj);
        for (var i = 0; i < 50; i++)
        {
            var u = Guid.NewGuid();
            await cache.MiembrosActivosAsync(u, () => Task.FromResult(new List<Miembro> { Miembro(Guid.NewGuid(), u) }));
        }

        Assert.Equal(50, cache.Entradas);

        // Pasada la vigencia, la siguiente lectura guardada purga todo lo caducado: solo queda la nueva.
        _reloj.Advance(CacheAcceso.Vigencia + TimeSpan.FromSeconds(1));
        var nuevo = Guid.NewGuid();
        await cache.MiembrosActivosAsync(nuevo, () => Task.FromResult(new List<Miembro> { Miembro(Guid.NewGuid(), nuevo) }));

        Assert.Equal(1, cache.Entradas);
    }

    [Fact]
    public async Task Una_lectura_iniciada_antes_de_invalidar_no_vuelve_a_publicar_el_permiso_revocado()
    {
        var cache = new CacheAcceso(_reloj);
        var hogar = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        var lecturaLenta = new TaskCompletionSource<List<Miembro>>();
        var primera = cache.MiembrosActivosAsync(usuario, () => lecturaLenta.Task);

        // Mientras la lectura está en vuelo (con los datos de antes del cambio), otra petición revoca y la caché se invalida.
        cache.InvalidarHogar(hogar);
        lecturaLenta.SetResult([Miembro(hogar, usuario)]);
        await primera;

        // La petición que leyó sigue usando su resultado, pero no queda guardado para las siguientes.
        Assert.Equal(0, cache.Entradas);
        var lecturas = 0;
        await cache.MiembrosActivosAsync(usuario, () => { lecturas++; return Task.FromResult(new List<Miembro>()); });
        Assert.Equal(1, lecturas);
    }

    /// <summary>
    /// La comprobación de la generación y la escritura son atómicas respecto de la invalidación: si la lectura anotó la
    /// generación antes de invalidar, tras publicar e invalidar a la vez la caché queda siempre vacía (o se publicó antes y la
    /// invalidación la quitó, o se rechazó). Con una invalidación colándose entre la comprobación y la escritura, a veces quedaba la entrada.
    /// </summary>
    [Fact]
    public async Task Publicar_e_invalidar_a_la_vez_nunca_deja_una_entrada_anterior_al_cambio()
    {
        var hogar = Guid.NewGuid();
        for (var i = 0; i < 2000; i++)
        {
            var cache = new CacheAcceso(_reloj);
            var usuario = Guid.NewGuid();
            var generacion = cache.Generacion; // la lectura empezó antes de la invalidación
            var inicio = new ManualResetEventSlim(false);

            var publica = Task.Run(() => { inicio.Wait(); cache.Publicar(usuario, _reloj.GetUtcNow(), [Miembro(hogar, usuario)], generacion); });
            var invalida = Task.Run(() => { inicio.Wait(); cache.InvalidarHogar(hogar); cache.InvalidarUsuario(usuario); });
            inicio.Set();
            await Task.WhenAll(publica, invalida);

            Assert.Equal(0, cache.Entradas);
        }
    }
}
