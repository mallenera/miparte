# Estructura: estado deseado del esquema

Esta carpeta es la **fuente de verdad** del esquema `public` (tablas, funciones, disparadores, claves foráneas, RLS
y privilegios). `migrar` la aplica entera, en orden, sobre cualquier base y la deja igual que ella:

| Carpeta | Contenido | Cómo converge |
|---|---|---|
| `00_Esquema.sql` | preámbulo, esquema, permisos por defecto | `IF NOT EXISTS` |
| `01_Tablas/` | tabla, PK/UNIQUE/CHECK, índices | `CREATE TABLE IF NOT EXISTS` + `ADD COLUMN IF NOT EXISTS` por columna; restricciones y índices por nombre |
| `02_Funciones/` | función y su `EXECUTE` | `CREATE OR REPLACE` |
| `03_Disparadores/` | triggers por tabla | `CREATE OR REPLACE TRIGGER` |
| `04_ClavesForaneas/` | FK por tabla | añadidas si falta una con ese nombre |
| `05_Seguridad/` | RLS, políticas y privilegios por tabla | `DROP POLICY IF EXISTS` + `CREATE`; `REVOKE` + `GRANT` |

## Cómo cambiar el esquema
1. Edita el fichero de la tabla o función (o crea uno nuevo en la carpeta que toque). Mantén el formato de
   `pg_dump`: una columna por línea, `CREATE TABLE public.x (`, `ALTER TABLE ONLY public.x` + `ADD CONSTRAINT ...`.
2. `dotnet run --project src/Database/MiParte.Database -- migrar --con-auth-stub --conexion "..."`.
3. Ajusta `MiParteDbContext` y `docs/modelo-de-datos.md`.

## Lo que NO infiere `migrar`
Borrar o renombrar columnas, tablas o funciones, cambiar el tipo de una columna, **cambiar la definición de una
restricción o un índice que conserva su nombre**, y rellenar datos. Eso va en un script versionado idempotente
(`../Scripts/Antes` antes de la estructura, `../Scripts` después). Una columna `NOT NULL` nueva en una tabla con
datos necesita `DEFAULT` o un script `Antes` que la añada y rellene.

## Regenerar desde una base
`estructura` vuelca una base real a esta carpeta (requiere `pg_dump`) y la normaliza (sin `MAINTAIN`, sin
`SET transaction_timeout`, sin tokens aleatorios). Útil si has tocado una base a mano o para comprobar que el diff
es el esperado; la carpeta se regenera entera.
