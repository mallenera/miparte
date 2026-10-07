using MiParte.Contracts;
using MiParte.Web.Reparto;

namespace MiParte.Web.Tests;

public class EdicionPerfilTests
{
    private static readonly Guid Ana = Guid.NewGuid(), Luis = Guid.NewGuid(), Marta = Guid.NewGuid();

    [Fact]
    public void Un_perfil_nuevo_empieza_por_partes_iguales()
    {
        var e = new EdicionPerfil([Ana, Luis]) { Nombre = "Mitad y mitad" };

        Assert.Equal(ModosReparto.Partes, e.Modo);
        Assert.Equal([1m, 1m], e.Valores.Values);
        Assert.Null(e.Error);
    }

    [Fact]
    public void Porcentaje_reparte_a_partes_iguales_y_el_ultimo_absorbe_el_resto()
    {
        var e = new EdicionPerfil([Ana, Luis, Marta]) { Nombre = "Tres" };

        e.CambiarModo(ModosReparto.Porcentaje);

        Assert.Equal(100m, e.Suma);
        Assert.Equal(33.33m, e.Valores[Ana]);
        Assert.Equal(33.34m, e.Valores[Marta]);
        Assert.Null(e.Error);
    }

    [Fact]
    public void Porcentaje_que_no_suma_100_da_error()
    {
        var e = new EdicionPerfil([Ana, Luis]) { Nombre = "60/40" };
        e.CambiarModo(ModosReparto.Porcentaje);
        e.Valores[Ana] = 60m;
        e.Valores[Luis] = 30m;

        Assert.Contains("sumar 100", e.Error);

        e.Valores[Luis] = 40m;
        Assert.Null(e.Error);
    }

    [Fact]
    public void Partes_necesita_alguna_mayor_que_cero_y_ninguna_negativa()
    {
        var e = new EdicionPerfil([Ana, Luis]) { Nombre = "Partes" };
        e.Valores[Ana] = 0m;
        e.Valores[Luis] = 0m;
        Assert.Contains("mayor que 0", e.Error);

        e.Valores[Luis] = -1m;
        Assert.Contains("negativos", e.Error);
    }

    [Fact]
    public void El_nombre_es_obligatorio_y_tiene_tope()
    {
        var e = new EdicionPerfil([Ana]) { Nombre = "  " };
        Assert.NotNull(e.Error);

        e.Nombre = new string('x', EdicionPerfil.MaxNombre + 1);
        Assert.Contains("100", e.Error);
    }

    [Fact]
    public void CuentaComun_e_individual_no_llevan_detalle()
    {
        var e = new EdicionPerfil([Ana, Luis]) { Nombre = "Cuenta común" };
        e.CambiarModo(ModosReparto.CuentaComun);

        Assert.Null(e.Error);
        Assert.Empty(e.ARequest().Detalle!);
        e.CambiarModo(ModosReparto.Individual);
        Assert.Empty(e.ARequest().Detalle!);
    }

    [Fact]
    public void Sin_adultos_los_modos_con_detalle_no_se_pueden_guardar()
    {
        var e = new EdicionPerfil([]) { Nombre = "Vacío" };

        Assert.Contains("adulto", e.Error);
    }

    [Fact]
    public void Desde_un_perfil_guardado_carga_los_valores_y_deja_a_cero_a_los_nuevos_adultos()
    {
        var guardado = new PerfilRepartoDto(Guid.NewGuid(), "60/40", "porcentaje",
            [new PerfilDetalleDto(Ana, 60m), new PerfilDetalleDto(Luis, 40m)]);

        var e = EdicionPerfil.Desde(guardado, [Ana, Luis, Marta]);

        Assert.Equal("60/40", e.Nombre);
        Assert.Equal(60m, e.Valores[Ana]);
        Assert.Equal(0m, e.Valores[Marta]);
        Assert.Null(e.Error); // 60 + 40 + 0 sigue sumando 100
    }

    [Fact]
    public void La_peticion_recorta_el_nombre_y_envia_un_valor_por_adulto()
    {
        var e = new EdicionPerfil([Ana, Luis]) { Nombre = "  Partes  " };
        e.Valores[Luis] = 2m;

        var req = e.ARequest();

        Assert.Equal("Partes", req.Nombre);
        Assert.Equal("partes", req.Modo);
        Assert.Equal([new PerfilDetalleDto(Ana, 1m), new PerfilDetalleDto(Luis, 2m)], req.Detalle);
    }
}
