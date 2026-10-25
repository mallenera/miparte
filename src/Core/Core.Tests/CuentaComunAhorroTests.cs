using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>Perfil «Cuenta común (ahorro)»: funciona como el de cuenta común, pero lo gastado sale del ahorro.</summary>
public class CuentaComunAhorroTests
{
    private static readonly Guid Ana = Guid.Parse("b0000000-0000-4000-8000-000000000001");
    private static readonly Guid Beto = Guid.Parse("b0000000-0000-4000-8000-000000000002");
    private static readonly DateOnly Septiembre = new(2026, 9, 1);

    private static Miembro Adulto(Guid id) => new() { Id = id, Nombre = id.ToString(), Tipo = TipoMiembro.Adulto };

    [Fact]
    public void El_modo_viaja_como_cuenta_comun_ahorro_y_es_de_cuenta_comun()
    {
        Assert.Equal("cuenta_comun_ahorro", TextoModoReparto.ATexto(ModoReparto.CuentaComunAhorro));
        Assert.True(TextoModoReparto.TryParse(" Cuenta_Comun_Ahorro ", out var modo));
        Assert.Equal(ModoReparto.CuentaComunAhorro, modo);
        Assert.True(ModosDeCuenta.EsDeCuentaComun(ModoReparto.CuentaComunAhorro));
        Assert.True(ModosDeCuenta.EsDeCuentaComun(ModoReparto.CuentaComun));
        Assert.False(ModosDeCuenta.EsDeCuentaComun(ModoReparto.Partes));
    }

    [Fact]
    public void No_lleva_detalle_ni_se_reparte_entre_personas()
    {
        Assert.Null(ReglasPerfil.ValidarEstructura(ModoReparto.CuentaComunAhorro, []));
        Assert.NotNull(ReglasPerfil.ValidarEstructura(ModoReparto.CuentaComunAhorro, [new ValorMiembro(Ana, 1m)]));

        var perfil = new PerfilReparto { Nombre = "Ahorro", Modo = ModoReparto.CuentaComunAhorro };
        var r = RepartoGasto.RepartirConDependientes(perfil, [Adulto(Ana), Adulto(Beto)], 900m, Ana, out var error);
        Assert.Null(error);
        Assert.Empty(r!.PorAdulto);
        Assert.Throws<ArgumentException>(() =>
            RepartoMiembros.Repartir(900m, ModoReparto.CuentaComunAhorro, [new(Ana, 0m), new(Beto, 0m)], Ana));
    }

    [Fact]
    public void Una_plantilla_con_el_perfil_genera_un_gasto_del_ahorro_adelantado_por_quien_paga()
    {
        var perfil = new PerfilReparto { Id = Guid.NewGuid(), Nombre = "Ahorro", Modo = ModoReparto.CuentaComunAhorro };
        var plantilla = new GastoRecurrente
        {
            Id = Guid.NewGuid(), Importe = 80m, CategoriaId = Guid.NewGuid(), PagadoPor = Ana, PerfilRepartoId = perfil.Id, DiaMes = 5,
        };

        var g = GeneracionRecurrente.CrearGasto(plantilla, Septiembre, perfil, [Adulto(Ana), Adulto(Beto)], out var error);

        Assert.Null(error);
        Assert.True(g!.ACargoCuentaComun);
        Assert.True(g.PagadoDesdeAhorro);
        Assert.Equal(Ana, g.PagadoPor);
        Assert.Empty(g.Repartos);

        // Si la plantilla es personal, el perfil no cuenta: ni cuenta común ni ahorro.
        plantilla.EsPersonal = true;
        var personal = GeneracionRecurrente.CrearGasto(plantilla, Septiembre, perfil, [Adulto(Ana), Adulto(Beto)], out _);
        Assert.False(personal!.ACargoCuentaComun);
        Assert.False(personal.PagadoDesdeAhorro);
    }

    [Fact]
    public void Un_gasto_del_ahorro_adelantado_baja_el_ahorro_y_deja_pendiente_sin_tocar_lo_gastado_de_la_cuenta()
    {
        // Ana aporta 500 y aparta 100; adelanta 60 del ahorro y la cuenta paga 30 de gastos.
        var aportaciones = new[] { new AportacionVigente(Ana, Septiembre, 500m, 100m) };
        var gastos = new[]
        {
            new GastoDeCuenta(Ana, Septiembre.AddDays(9), 60m, DesdeAhorro: true),
            new GastoDeCuenta(null, Septiembre.AddDays(10), 30m),
        };

        var e = CuentaComun.Calcular(Septiembre, aportaciones, gastos, []);

        Assert.Equal((30m, 60m), (e.Gastado, e.AhorroGastado));
        Assert.Equal((370m, 40m), (e.Saldo, e.AhorroDisponible));
        Assert.Equal(60m, Assert.Single(e.Pendientes).Importe);
        Assert.Equal(430m, e.Efectivo); // 370 de saldo más los 60 que la cuenta debe a Ana

        // Al reembolsárselo baja el efectivo y el pendiente; el ahorro y lo gastado no cambian.
        var tras = CuentaComun.Calcular(Septiembre, aportaciones, gastos, [new ReembolsoDeCuenta(Ana, Septiembre.AddDays(20), 60m)]);
        Assert.Empty(tras.Pendientes);
        Assert.Equal((370m, 40m, 370m), (tras.Saldo, tras.AhorroDisponible, tras.Efectivo));
    }
}
