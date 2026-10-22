using MiParte.Contracts;
using MiParte.Web.Reparto;

namespace MiParte.Web.Tests;

/// <summary>CSV de movimientos de la cuenta común para Excel en español.</summary>
public class CsvMovimientosTests
{
    private static readonly MiembroDto Ana = ApiFalsa.Miembro("Ana");

    private static string Generar(params MovimientoCuentaComunDto[] movimientos)
        => CsvMovimientos.Generar(movimientos, m => $"Desc {m.Tipo}", [Ana]);

    private static string[] Lineas(string csv) => csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Lleva_BOM_cabecera_y_una_linea_por_movimiento_en_orden_cronologico()
    {
        var csv = Generar(
            new("retirada_ahorro", Guid.NewGuid(), new DateOnly(2026, 10, 9), -60m, Ana.Id, null, null, 0m, 480m),
            new("aportacion", null, new DateOnly(2026, 10, 1), 500m, Ana.Id, null, null, 100m, 540m));

        Assert.StartsWith("\uFEFF" + CsvMovimientos.Cabecera, csv);
        var lineas = Lineas(csv);
        Assert.Equal(3, lineas.Length);
        Assert.Equal("01/10/2026;Aportación;Desc aportacion;Ana;;500,00;100,00;540,00", lineas[1]);
        Assert.Equal("09/10/2026;Retirada de ahorro;Desc retirada_ahorro;Ana;;-60,00;;480,00", lineas[2]);
    }

    [Fact]
    public void Importes_con_coma_decimal_y_sin_separador_de_miles()
    {
        var csv = Generar(new MovimientoCuentaComunDto("ingreso", Guid.NewGuid(), new DateOnly(2026, 10, 3), 1234.5m, null, null, null, 0m, 12345.67m));

        Assert.EndsWith(";1234,50;;12345,67", Lineas(csv)[1]);
    }

    [Theory]
    [InlineData("Cena; amigas", "\"Cena; amigas\"")]
    [InlineData("Dijo \"hola\"", "\"Dijo \"\"hola\"\"\"")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+34 600", "'+34 600")]
    [InlineData("Normal", "Normal")]
    public void El_concepto_se_escapa_y_no_se_ejecuta_como_formula(string concepto, string esperado)
    {
        var csv = Generar(new MovimientoCuentaComunDto("ingreso", Guid.NewGuid(), new DateOnly(2026, 10, 3), 10m, null, null, concepto, 0m, 10m));

        Assert.Contains($";{esperado};10,00;", csv);
    }

    [Fact]
    public void El_nombre_del_fichero_lleva_el_periodo()
        => Assert.Equal("movimientos-cuenta-comun_2026-10-01_2026-10-31.csv",
            CsvMovimientos.NombreFichero(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));
}
