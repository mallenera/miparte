using Xunit;

namespace MiParte.Database.Tests;

/// <summary>Reglas que hacen idempotente el SQL de Estructura/ (no necesitan base de datos).</summary>
public class TransformadorEstructuraTests
{
    [Fact]
    public void Tabla_SeCreaSiNoExisteYSeLeAnadenLasColumnasQueFalten()
    {
        var sql = "CREATE TABLE public.t (\n    id uuid NOT NULL,\n    nombre text DEFAULT 'x'::text NOT NULL,\n" +
                  "    CONSTRAINT t_nombre_check CHECK ((length(nombre) > 0))\n);";

        var r = TransformadorEstructura.HacerIdempotente(sql);

        Assert.Contains("CREATE TABLE IF NOT EXISTS public.t (", r);
        Assert.Contains("ALTER TABLE public.t ADD COLUMN IF NOT EXISTS id uuid NOT NULL;", r);
        Assert.Contains("ADD COLUMN IF NOT EXISTS nombre text DEFAULT 'x'::text NOT NULL;", r);
        Assert.Contains("conname = 't_nombre_check'", r);
        Assert.DoesNotContain("ADD COLUMN IF NOT EXISTS CONSTRAINT", r);
    }

    [Fact]
    public void FuncionesPoliticasIndicesYTriggers_SeReemplazan()
    {
        var sql = "CREATE FUNCTION public.f() RETURNS int AS $$ select 1 $$ LANGUAGE sql;\n" +
                  "CREATE POLICY p ON public.t USING (true);\n" +
                  "CREATE UNIQUE INDEX i ON public.t (a);\n" +
                  "CREATE TRIGGER tg BEFORE INSERT ON public.t FOR EACH ROW EXECUTE FUNCTION public.f();";

        var r = TransformadorEstructura.HacerIdempotente(sql);

        Assert.Contains("CREATE OR REPLACE FUNCTION public.f()", r);
        Assert.Contains("DROP POLICY IF EXISTS p ON public.t;\nCREATE POLICY p ON public.t", r);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS i ON", r);
        Assert.Contains("CREATE OR REPLACE TRIGGER tg", r);
    }

    [Fact]
    public void RestriccionAnadida_SeProtegePorNombre()
    {
        var sql = "ALTER TABLE ONLY public.t\n    ADD CONSTRAINT t_fk FOREIGN KEY (a) REFERENCES public.o(id);";

        var r = TransformadorEstructura.HacerIdempotente(sql);

        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint WHERE conname = 't_fk'", r);
        Assert.Contains("ADD CONSTRAINT t_fk FOREIGN KEY", r);
    }

    [Fact]
    public void YaIdempotente_NoSeTransformaDeNuevo()
    {
        var sql = "CREATE TABLE IF NOT EXISTS public.t (\n    id uuid\n);\n" +
                  "CREATE OR REPLACE FUNCTION public.f() RETURNS int AS $$ select 1 $$ LANGUAGE sql;";

        Assert.Equal(sql, TransformadorEstructura.HacerIdempotente(sql));
    }
}
