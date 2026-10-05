using MiParte.Contracts;
using MiParte.Web.Hogares;

namespace MiParte.Web.Tests;

public class EstadoHogarTests
{
    private static readonly HogarResumen Casa = new(Guid.NewGuid(), "Casa");
    private static readonly HogarResumen Playa = new(Guid.NewGuid(), "Playa");

    private static YoResponse Yo(params HogarResumen[] hogares) => new("u1", hogares, null);

    [Fact]
    public async Task Sin_hogares_pide_crear_o_unirse()
    {
        var estado = new EstadoHogar(new AlmacenMemoria());

        Assert.Equal(DestinoArranque.SinHogar, await estado.AplicarAsync(Yo()));
        Assert.Null(estado.HogarActual);
    }

    [Fact]
    public async Task Con_un_hogar_lo_selecciona_y_lo_recuerda()
    {
        var almacen = new AlmacenMemoria();
        var estado = new EstadoHogar(almacen);

        Assert.Equal(DestinoArranque.Listo, await estado.AplicarAsync(Yo(Casa)));
        Assert.Equal(Casa, estado.HogarActual);
        Assert.Equal(Casa.Id.ToString(), almacen.Datos["miparte.hogar"]);
    }

    [Fact]
    public async Task Con_varios_hogares_sin_eleccion_pide_elegir()
    {
        var estado = new EstadoHogar(new AlmacenMemoria());

        Assert.Equal(DestinoArranque.ElegirHogar, await estado.AplicarAsync(Yo(Casa, Playa)));
        Assert.Null(estado.HogarActual);
    }

    [Fact]
    public async Task Recupera_la_eleccion_guardada_si_sigue_siendo_valida()
    {
        var almacen = new AlmacenMemoria();
        almacen.Datos["miparte.hogar"] = Playa.Id.ToString();
        var estado = new EstadoHogar(almacen);

        Assert.Equal(DestinoArranque.Listo, await estado.AplicarAsync(Yo(Casa, Playa)));
        Assert.Equal(Playa, estado.HogarActual);
    }

    [Fact]
    public async Task Una_eleccion_guardada_que_ya_no_existe_vuelve_a_pedir_elegir()
    {
        var almacen = new AlmacenMemoria();
        almacen.Datos["miparte.hogar"] = Guid.NewGuid().ToString();
        var estado = new EstadoHogar(almacen);

        Assert.Equal(DestinoArranque.ElegirHogar, await estado.AplicarAsync(Yo(Casa, Playa)));
        Assert.False(almacen.Datos.ContainsKey("miparte.hogar"));
    }

    [Fact]
    public async Task Seleccionar_un_hogar_ajeno_falla()
    {
        var estado = new EstadoHogar(new AlmacenMemoria());
        await estado.AplicarAsync(Yo(Casa));

        await Assert.ThrowsAsync<ArgumentException>(() => estado.SeleccionarAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Limpiar_olvida_hogares_y_eleccion()
    {
        var almacen = new AlmacenMemoria();
        var estado = new EstadoHogar(almacen);
        await estado.AplicarAsync(Yo(Casa));

        await estado.LimpiarAsync();

        Assert.Null(estado.HogarActual);
        Assert.Empty(estado.Hogares);
        Assert.False(estado.Cargado);
        Assert.Empty(almacen.Datos);
    }
}
