using Xunit;

namespace MiParte.Database.Tests;

/// <summary>Reparto del volcado de pg_dump en ficheros por objeto (no necesita base de datos).</summary>
public class ExportadorEstructuraTests
{
    private const string Volcado = """
        SET client_encoding = 'UTF8';

        --
        -- Name: gasto; Type: TABLE; Schema: public; Owner: -
        --

        CREATE TABLE public.gasto (id uuid NOT NULL);

        --
        -- Name: gasto gasto_pkey; Type: CONSTRAINT; Schema: public; Owner: -
        --

        ALTER TABLE ONLY public.gasto
            ADD CONSTRAINT gasto_pkey PRIMARY KEY (id);

        --
        -- Name: calcular(integer); Type: FUNCTION; Schema: public; Owner: -
        --

        CREATE FUNCTION public.calcular(x integer) RETURNS integer LANGUAGE sql AS $$ select 1 from public.gasto $$;

        --
        -- Name: gasto gasto_hogar_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
        --

        ALTER TABLE ONLY public.gasto
            ADD CONSTRAINT gasto_hogar_fk FOREIGN KEY (id) REFERENCES public.hogar(id);

        --
        -- Name: TABLE gasto; Type: ACL; Schema: public; Owner: -
        --

        GRANT SELECT ON TABLE public.gasto TO authenticated;

        --
        -- PostgreSQL database dump complete
        --
        """;

    [Fact]
    public void Dividir_AgrupaCadaObjetoConLoQueDependeDeEl()
    {
        var ficheros = ExportadorEstructura.Dividir(Volcado);

        Assert.Contains("SET client_encoding", ficheros["00_Esquema.sql"]);
        Assert.Contains("CREATE TABLE public.gasto", ficheros["01_Tablas/gasto.sql"]);
        Assert.Contains("gasto_pkey", ficheros["01_Tablas/gasto.sql"]);
        Assert.Contains("CREATE FUNCTION public.calcular", ficheros["02_Funciones/calcular.sql"]);
        // La clave foránea va aparte, a una carpeta posterior, y no en la de la función que aparece antes.
        Assert.Contains("gasto_hogar_fk", ficheros["04_ClavesForaneas/gasto.sql"]);
        Assert.DoesNotContain("gasto_hogar_fk", ficheros["02_Funciones/calcular.sql"]);
        Assert.DoesNotContain("PostgreSQL database dump complete", string.Concat(ficheros.Values));
    }

    [Fact]
    public void Dividir_RevocaAntesDeConcederPrivilegiosDeTabla()
    {
        var seguridad = ExportadorEstructura.Dividir(Volcado)["05_Seguridad/gasto.sql"];

        var revoke = seguridad.IndexOf("REVOKE ALL ON TABLE public.gasto FROM anon, authenticated", StringComparison.Ordinal);
        var grant = seguridad.IndexOf("GRANT SELECT ON TABLE public.gasto", StringComparison.Ordinal);
        Assert.True(revoke >= 0 && revoke < grant);
    }
}
