using MiParte.Contracts;
using MiParte.Web.Hogares;
using MiParte.Web.Reparto;

namespace MiParte.Web.Tests;

public class ArbolCategoriasTests
{
    private static CategoriaDto Cat(string nombre, Guid? padre = null) => new(Guid.NewGuid(), nombre, padre, null);

    [Fact]
    public void Las_subcategorias_van_justo_despues_de_su_padre_y_ordenadas_por_nombre()
    {
        var comida = Cat("Comida");
        var casa = Cat("Casa");
        var super_ = Cat("Supermercado", comida.Id);
        var bar = Cat("Bares", comida.Id);

        var plano = ArbolCategorias.Aplanar([comida, super_, casa, bar]);

        Assert.Equal(["Casa", "Comida", "Bares", "Supermercado"], plano.Select(n => n.Categoria.Nombre));
        Assert.Equal([0, 0, 1, 1], plano.Select(n => n.Nivel));
    }

    [Fact]
    public void Una_categoria_con_padre_desconocido_se_trata_como_de_primer_nivel()
    {
        var huerfana = Cat("Huérfana", Guid.NewGuid());

        var plano = ArbolCategorias.Aplanar([huerfana]);

        Assert.Single(plano);
        Assert.Equal(0, plano[0].Nivel);
    }

    [Fact]
    public void Con_descendientes_incluye_todos_los_niveles_y_no_a_las_hermanas()
    {
        var a = Cat("A");
        var b = Cat("B", a.Id);
        var c = Cat("C", b.Id);
        var otra = Cat("Otra");

        var ids = ArbolCategorias.ConDescendientes([a, b, c, otra], a.Id);

        Assert.Equal(new HashSet<Guid> { a.Id, b.Id, c.Id }, ids);
    }

    [Fact]
    public void El_color_del_miembro_depende_del_orden_por_id_y_da_la_vuelta_a_la_paleta()
    {
        var miembros = Enumerable.Range(0, 10)
            .Select(i => new MiembroDto(Guid.NewGuid(), $"M{i}", "adulto", null, true, "miembro", false)).ToList();
        var ordenados = miembros.OrderBy(m => m.Id).ToList();

        Assert.Equal(0, ColorMiembro.De(ordenados[0].Id, miembros));
        Assert.Equal(7, ColorMiembro.De(ordenados[7].Id, miembros));
        Assert.Equal(0, ColorMiembro.De(ordenados[8].Id, miembros));
        Assert.Equal(ColorMiembro.Colores - 1, ColorMiembro.De(Guid.NewGuid(), miembros));
    }

    [Fact]
    public void La_ruta_une_los_ancestros_y_una_categoria_desconocida_sale_como_raya()
    {
        var comida = Cat("Comida");
        var super_ = Cat("Supermercado", comida.Id);
        var fruta = Cat("Fruta", super_.Id);

        Assert.Equal("Comida", ArbolCategorias.Ruta([comida, super_, fruta], comida.Id));
        Assert.Equal("Comida › Supermercado › Fruta", ArbolCategorias.Ruta([comida, super_, fruta], fruta.Id));
        Assert.Equal("—", ArbolCategorias.Ruta([comida], Guid.NewGuid()));
    }

    [Fact]
    public void La_ruta_no_se_cuelga_con_un_ciclo()
    {
        var a = new CategoriaDto(Guid.NewGuid(), "A", null, null);
        var b = new CategoriaDto(Guid.NewGuid(), "B", a.Id, null);
        a = a with { CategoriaPadreId = b.Id };

        Assert.Contains("B", ArbolCategorias.Ruta([a, b], b.Id));
    }

    [Fact]
    public void El_resumen_en_arbol_suma_cada_gasto_una_sola_vez_e_incluye_madres_sin_gasto()
    {
        var casa = Cat("Casa");
        var luz = Cat("Luz", casa.Id);
        var agua = Cat("Agua", casa.Id);
        var ocio = Cat("Ocio");
        var miembro = Guid.NewGuid();
        ResumenCategoriaDto R(CategoriaDto c, decimal t) => new(c.Id, c.Nombre, t, [new ImporteMiembroDto(miembro, t)]);

        var arbol = ArbolCategorias.ResumenEnArbol([casa, luz, agua, ocio], [R(luz, 30m), R(agua, 10m), R(ocio, 100m)]);

        Assert.Equal(["Ocio", "Casa"], arbol.Select(n => n.Nombre));
        var madre = arbol[1];
        Assert.Equal(0m, madre.Propio);
        Assert.Equal(40m, madre.Total);
        Assert.Equal(40m, Assert.Single(madre.PorMiembro).Importe);
        Assert.Equal(["Luz", "Agua"], madre.Hijos.Select(h => h.Nombre));
        Assert.Equal(140m, arbol.Sum(n => n.Total));
    }
}
