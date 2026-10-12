---
paths:
  - "supabase/**"
  - "src/Core/Core.Infrastructure/**"
---
# Base de datos: SQL manda, EF solo mapea

- El esquema y la RLS viven en SQL: `supabase/migrations/*.sql` es la línea base congelada (no añadas ahí) y los cambios nuevos van en `src/Database/MiParte.Database/Scripts/`. **No generes migraciones EF.** Cambio de esquema = script nuevo + ajuste del mapeo en `MiParteDbContext` + actualizar `docs/modelo-de-datos.md`.
- Nombre de script: `AAAAMMDDHHMMSS_descripcion_en_snake_case.sql`, posterior al último. **Nunca edites un script ya aplicado**; crea otro. Deben ser **idempotentes** (`if not exists`, `create or replace`, `drop ... if exists`; patrones en `Scripts/README.md`): `migrar` los aplica una vez, pero el comando `script` y las recuperaciones a mano los reejecutan. CI aplica todo con `migrar --con-auth-stub`.
- Toda tabla de datos lleva `hogar_id`, claves foráneas compuestas `(hogar_id, id)`, RLS habilitada con política por pertenencia al hogar y `check` de integridad (importes > 0, `numeric(12,2)`).
- Toda entidad EF con `HogarId` necesita su `HasQueryFilter` en `MiParteDbContext`, además de la RLS: el rol de conexión puede saltarse la RLS.
- Enums como texto con conversión explícita; `UseSnakeCaseNamingConvention`; tablas fijadas con `ToTable`.
- Funciones `SECURITY DEFINER`: `set search_path = ''` y nombres cualificados.
- Prueba contra Postgres real con `MIPARTE_TEST_DB`; no confíes solo en los tests que se saltan.
