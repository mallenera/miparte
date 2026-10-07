---
paths:
  - "supabase/**"
  - "src/Core/Core.Infrastructure/**"
---
# Base de datos: SQL manda, EF solo mapea

- El esquema y la RLS viven en `supabase/migrations/*.sql`. **No generes migraciones EF.** Cambio de esquema = migración nueva + ajuste del mapeo en `MiParteDbContext` + actualizar `docs/modelo-de-datos.md`.
- Nombre de migración: `AAAAMMDDHHMMSS_descripcion_en_snake_case.sql`, posterior a la última. **Nunca edites una migración ya aplicada**; crea otra. CI las aplica todas en orden tras `supabase/tests/auth_stub.sql`.
- Toda tabla de datos lleva `hogar_id`, claves foráneas compuestas `(hogar_id, id)`, RLS habilitada con política por pertenencia al hogar y `check` de integridad (importes > 0, `numeric(12,2)`).
- Toda entidad EF con `HogarId` necesita su `HasQueryFilter` en `MiParteDbContext`, además de la RLS: el rol de conexión puede saltarse la RLS.
- Enums como texto con conversión explícita; `UseSnakeCaseNamingConvention`; tablas fijadas con `ToTable`.
- Funciones `SECURITY DEFINER`: `set search_path = ''` y nombres cualificados.
- Prueba contra Postgres real con `MIPARTE_TEST_DB`; no confíes solo en los tests que se saltan.
