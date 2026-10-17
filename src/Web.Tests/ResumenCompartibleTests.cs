using MiParte.Contracts;
using MiParte.Web.Reparto;

namespace MiParte.Web.Tests;

/// <summary>Texto del resumen compartible: se prueba como función pura, sin renderizar nada.</summary>
public class ResumenCompartibleTests
{
    private static readonly DateOnly Octubre = new(2026, 10, 15);
    private readonly Guid _ana = Guid.NewGuid(), _luis = Guid.NewGuid();

    private static MiembroDto Miembro(Guid id, string nombre) => ApiFalsa.Miembro(nombre, id: id);

    [Fact]
    public void Con_transferencias_pone_titulo_con_hogar_y_mes_y_una_linea_por_pago()
    {
        var liquidacion = new LiquidacionResponse("2026-10",
            [new SaldoMiembroDto(_ana, "Ana", 42.5m), new SaldoMiembroDto(_luis, "Luis", -42.5m)],
            [new TransferenciaDto(_luis, _ana, 42.5m)],
            []);

        var texto = ResumenCompartible.Generar("Piso", Octubre, liquidacion, []);

        Assert.Equal("Resumen de Piso · octubre 2026\n\nLuis debe a Ana 42,50 €", texto);
        Assert.DoesNotContain(ResumenCompartible.TodoCuadrado, texto);
    }

    [Fact]
    public void Sin_transferencias_pone_todo_cuadrado()
    {
        var liquidacion = new LiquidacionResponse("2026-10",
            [new SaldoMiembroDto(_ana, "Ana", 0m), new SaldoMiembroDto(_luis, "Luis", 0m)],
            [],
            []);

        var texto = ResumenCompartible.Generar("Piso", Octubre, liquidacion, []);

        Assert.EndsWith("Todo cuadrado", texto);
        Assert.DoesNotContain("debe a", texto);
    }

    [Fact]
    public void Los_importes_llevan_coma_decimal_miles_y_simbolo_euro_con_espacio_normal()
    {
        var liquidacion = new LiquidacionResponse("2026-10",
            [new SaldoMiembroDto(_ana, "Ana", 1234.5m), new SaldoMiembroDto(_luis, "Luis", -1234.5m)],
            [new TransferenciaDto(_luis, _ana, 1234.5m), new TransferenciaDto(_ana, _luis, 3m)],
            []);

        var texto = ResumenCompartible.Generar("Piso", Octubre, liquidacion, []);

        Assert.Contains("Luis debe a Ana 1.234,50 €", texto);
        Assert.Contains("Ana debe a Luis 3,00 €", texto);
        Assert.DoesNotContain(' ', texto);
    }

    [Fact]
    public void Nombra_con_los_miembros_del_hogar_si_la_transferencia_no_tiene_saldo()
    {
        var liquidacion = new LiquidacionResponse("2026-10", [], [new TransferenciaDto(_luis, _ana, 10m)], []);

        var texto = ResumenCompartible.Generar("Piso", Octubre, liquidacion, [Miembro(_ana, "Ana"), Miembro(_luis, "Luis")]);

        Assert.Contains("Luis debe a Ana 10,00 €", texto);
    }

    [Fact]
    public void No_muestra_identificadores_tecnicos()
    {
        var liquidacion = new LiquidacionResponse("2026-10",
            [new SaldoMiembroDto(_ana, "Ana", 5m), new SaldoMiembroDto(_luis, "Luis", -5m)],
            [new TransferenciaDto(_luis, _ana, 5m)],
            []);

        var texto = ResumenCompartible.Generar("Piso", Octubre, liquidacion, []);

        Assert.DoesNotContain(_ana.ToString(), texto);
        Assert.DoesNotContain(_luis.ToString(), texto);
    }
}
