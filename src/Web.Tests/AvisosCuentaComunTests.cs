using MiParte.Contracts;
using MiParte.Web.Modelos;

namespace MiParte.Web.Tests;

/// <summary>Avisos de la cuenta común y lo que falta por reembolsar a cada persona, probados sin renderizar componentes.</summary>
public class AvisosCuentaComunTests
{
    private static readonly Guid Ana = Guid.NewGuid(), Luis = Guid.NewGuid();

    private static CuentaComunResponse Estado(decimal efectivo, decimal ahorroDisponible, params PendienteCuentaDto[] pendientes) =>
        new("2026-10", 0m, 0m, 0m, 0m, pendientes, efectivo, [], [], AhorroDisponible: ahorroDisponible);

    [Fact]
    public void SinAvisos_CuandoElAhorroYElEfectivoCubrenLoQueDebe()
    {
        var estado = Estado(201.34m, 80.94m, new PendienteCuentaDto(Ana, "Ana", 50m, true));

        Assert.Empty(AvisosCuentaComun.Avisos(estado));
    }

    [Fact]
    public void AvisaDelAhorroQueNoCubre_YDiceCuantoFalta()
    {
        var estado = Estado(201.34m, 80.94m, new PendienteCuentaDto(Ana, "Ana", 112.95m, true));

        var aviso = Assert.Single(AvisosCuentaComun.Avisos(estado));
        Assert.Contains("El ahorro no cubre", aviso);
        Assert.Contains("32,01", aviso);
    }

    [Fact]
    public void AvisaDelEfectivoQueNoAlcanzaParaReembolsar()
    {
        var estado = Estado(40m, 500m, new PendienteCuentaDto(Ana, "Ana", 60m, false));

        var aviso = Assert.Single(AvisosCuentaComun.Avisos(estado));
        Assert.Contains("El efectivo no alcanza", aviso);
        Assert.Contains("20,00", aviso);
    }

    [Fact]
    public void UnaPersonaQueDebeALaCuenta_RebajaLoQueHayQueReembolsar()
    {
        // La cuenta debe 50 a Ana y Luis debe 30 a Hogar: hay que reembolsar 20 netos. Con 10 en efectivo faltan 10.
        var estado = Estado(10m, 0m, new PendienteCuentaDto(Ana, "Ana", 50m), new PendienteCuentaDto(Luis, "Luis", -30m));

        var aviso = Assert.Single(AvisosCuentaComun.Avisos(estado));
        Assert.Contains("faltan 10,00", aviso);
        Assert.Contains("hay que reembolsar 20,00", aviso);
    }

    [Fact]
    public void UnaPersonaQueDebeALaCuenta_NoAvisaSiElEfectivoCubreElNeto()
    {
        // La cuenta debe 50 a Ana y Luis debe 30: 20 netos, y hay 20 en efectivo.
        var estado = Estado(20m, 0m, new PendienteCuentaDto(Ana, "Ana", 50m), new PendienteCuentaDto(Luis, "Luis", -30m));

        Assert.Empty(AvisosCuentaComun.Avisos(estado));
    }

    [Fact]
    public void PorPersona_AgrupaLosDosFondosYDejaSoloLasQueTienenAlgoPendiente()
    {
        var personas = AvisosCuentaComun.PorPersona(
        [
            new PendienteCuentaDto(Luis, "Luis", -10m),
            new PendienteCuentaDto(Ana, "Ana", 30m),
            new PendienteCuentaDto(Ana, "Ana", 12m, true),
            new PendienteCuentaDto(Luis, "Luis", 0m, true),
        ]);

        Assert.Equal([new PersonaCuenta(Ana, "Ana", 30m, 12m), new PersonaCuenta(Luis, "Luis", -10m, 0m)], personas);
    }

    [Fact]
    public void Lineas_DistinguenLoQueSeReembolsaDeLoQueDebeLaPersona()
    {
        var lineas = AvisosCuentaComun.Lineas(new PersonaCuenta(Luis, "Luis", -10m, 25m));

        Assert.Equal(
            [$"falta reembolsar {Formato.Euros(25m)} del ahorro", $"debe {Formato.Euros(10m)} a Hogar"], lineas);
    }
}
