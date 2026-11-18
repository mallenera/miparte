---
paths:
  - "supabase/**"
  - "src/Core/Core.Infrastructure/**"
---
# Base de datos: SQL manda, EF solo mapea

- La fuente de verdad del esquema y la RLS es `src/Database/MiParte.Database/Estructura/` (estado deseado, un fichero por tabla o función; edítalo a mano respetando el formato `pg_dump`: una columna por línea, `CREATE TABLE public.x (`, `ALTER TABLE ONLY public.x ADD CONSTRAINT ...`). `supabase/migrations/*.sql` es historia y **no se toca ni se aplica**. **No generes migraciones EF.** Cambio de esquema = editar `Estructura/` + ajuste del mapeo en `MiParteDbContext` + actualizar `docs/modelo-de-datos.md`; `migrar` lo converge sobre cualquier base.
- Los cambios que `migrar` no puede inferir de `Estructura/` (borrar o renombrar columnas, cambiar tipos, rellenar datos, borrar objetos que ya no existen) van en un script versionado `AAAAMMDDHHMMSS_descripcion_en_snake_case.sql`: `Scripts/Antes/` si debe correr antes de la estructura, `Scripts/` si después. Corren una vez (journal) y deben ser **idempotentes**; patrones en `Scripts/README.md`. **Nunca edites un script ya aplicado**; crea otro. CI aplica todo con `migrar --con-auth-stub`.
- Compatibilidad: los ficheros de `Estructura/` se aplican en PostgreSQL 15+ (sin `MAINTAIN` ni `SET transaction_timeout`; el exportador los quita). Una columna `NOT NULL` nueva sobre una tabla con datos necesita `DEFAULT` o un script `Antes` que la rellene.

- Toda tabla de datos lleva `hogar_id`, claves foráneas compuestas `(hogar_id, id)`, RLS habilitada con política por pertenencia al hogar y `check` de integridad (importes > 0, `numeric(12,2)`).
- Toda entidad EF con `HogarId` necesita su `HasQueryFilter` en `MiParteDbContext`, además de la RLS: el rol de conexión `miparte_api` ve todas las filas (política `api_servicio`).
- Toda tabla nueva lleva en `05_Seguridad` la política `api_servicio` (`TO miparte_api USING (true) WITH CHECK (true)`) y `GRANT SELECT,INSERT,DELETE,UPDATE ... TO miparte_api` (menos si no debe modificarse, como `auditoria`). Nunca des a `miparte_api` DDL, `TRUNCATE`, acceso a `auth` ni pertenencia a otro rol; `EsquemaPostgresTests.RolDeLaApi_SoloLeeYEscribeFilasDePublic` lo vigila.
- Enums como texto con conversión explícita; `UseSnakeCaseNamingConvention`; tablas fijadas con `ToTable`.
- Funciones `SECURITY DEFINER`: `set search_path = ''` y nombres cualificados.
- Prueba contra Postgres real con `MIPARTE_TEST_DB`; no confíes solo en los tests que se saltan.
