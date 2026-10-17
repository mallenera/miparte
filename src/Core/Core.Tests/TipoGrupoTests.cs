using MiParte.Contracts;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

public class TipoGrupoTests
{
    [Fact]
    public void TryParse_SinTexto_EsHogarPorDefecto()
    {
        Assert.True(TextoTipoGrupo.TryParse(null, out var sinTexto));
        Assert.Equal(TipoGrupo.Hogar, sinTexto);

        Assert.True(TextoTipoGrupo.TryParse("   ", out var enBlanco));
        Assert.Equal(TipoGrupo.Hogar, enBlanco);
    }

    [Theory]
    [InlineData(TipoGrupo.Hogar, "hogar")]
    [InlineData(TipoGrupo.PisoCompartido, "piso_compartido")]
    [InlineData(TipoGrupo.Viaje, "viaje")]
    [InlineData(TipoGrupo.Evento, "evento")]
    [InlineData(TipoGrupo.Otro, "otro")]
    public void ATexto_DevuelveElTextoDelContrato_Y_TryParse_LoDeshace(TipoGrupo tipo, string texto)
    {
        Assert.Equal(texto, TextoTipoGrupo.ATexto(tipo));
        Assert.True(TextoTipoGrupo.TryParse(texto, out var ida));
        Assert.Equal(tipo, ida);
    }

    [Fact]
    public void TryParse_IgnoraMayusculasYEspacios_YRechazaDesconocidos()
    {
        Assert.True(TextoTipoGrupo.TryParse(" Piso_Compartido ", out var tipo));
        Assert.Equal(TipoGrupo.PisoCompartido, tipo);

        Assert.False(TextoTipoGrupo.TryParse("condominio", out _));
        Assert.False(TextoTipoGrupo.TryParse("PisoCompartido", out _));
    }

    [Fact]
    public void ConstantesDelContrato_CoincidenConElTextoDelDominio()
    {
        Assert.Equal(TiposGrupo.Hogar, TextoTipoGrupo.ATexto(TipoGrupo.Hogar));
        Assert.Equal(TiposGrupo.PisoCompartido, TextoTipoGrupo.ATexto(TipoGrupo.PisoCompartido));
        Assert.Equal(TiposGrupo.Viaje, TextoTipoGrupo.ATexto(TipoGrupo.Viaje));
        Assert.Equal(TiposGrupo.Evento, TextoTipoGrupo.ATexto(TipoGrupo.Evento));
        Assert.Equal(TiposGrupo.Otro, TextoTipoGrupo.ATexto(TipoGrupo.Otro));
    }

    [Fact]
    public void HogarResumen_SinTipo_ValeHogar_Y_SeDeserializaSinEl()
    {
        var resumen = new HogarResumen(Guid.Empty, "Casa");
        Assert.Equal(TiposGrupo.Hogar, resumen.Tipo);

        // Un cliente anterior no envía el campo: el contrato sigue siendo válido.
        var json = System.Text.Json.JsonSerializer.Deserialize<HogarResumen>(
            """{"id":"00000000-0000-0000-0000-000000000000","nombre":"Casa","cuentaComunActiva":false,"ahorroActivo":false}""",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(TiposGrupo.Hogar, json!.Tipo);
    }

    [Fact]
    public void Hogar_SembrarCategorias_ConservaLasSeisDeSiempre()
    {
        var categorias = SemillaHogar.CategoriasDe(TipoGrupo.Hogar);

        Assert.Equal(
            new[] { "Hipoteca/Alquiler", "Alimentación", "Suministros", "Gastos varios de casa", "Hijo", "Ocio personal" },
            categorias.Select(c => c.Categoria).ToArray());
        Assert.Equal(SemillaHogar.PerfilIndividual, categorias.Single(c => c.Categoria == "Ocio personal").Perfil);
    }

    [Theory]
    [InlineData(TipoGrupo.PisoCompartido, "Alquiler", "Suministros", "Compra común", "Internet y móvil", "Limpieza y mantenimiento", "Otros")]
    [InlineData(TipoGrupo.Viaje, "Transporte", "Alojamiento", "Comida", "Ocio", "Compras", "Otros")]
    [InlineData(TipoGrupo.Evento, "Proveedores", "Regalos colectivos", "Comida y bebida", "Otros")]
    [InlineData(TipoGrupo.Otro, "General")]
    public void CadaTipo_SiembraSusCategoriasConPerfilPorPartes(TipoGrupo tipo, params string[] esperadas)
    {
        var categorias = SemillaHogar.CategoriasDe(tipo);

        Assert.Equal(esperadas, categorias.Select(c => c.Categoria).ToArray());
        Assert.All(categorias, c => Assert.Equal(SemillaHogar.PerfilPartes, c.Perfil));
    }
}
