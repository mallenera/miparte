# Scripts nuevos del esquema

Aquí van los cambios de esquema **nuevos**, posteriores a la línea base (`supabase/migrations`, congelada).
Se incrustan en el ensamblado y `migrar` aplica cada uno una sola vez, por orden de nombre.

- Nombre: `AAAAMMDDHHMMSS_descripcion_en_snake_case.sql`, posterior al último.
- **Idempotentes**: cada script debe poder ejecutarse dos veces sin error ni efecto doble (así también vale
  pegarlo a mano o recuperarse de una ejecución a medias). Patrones:
  - tablas, índices y esquemas: `create table if not exists`, `create index if not exists`;
  - columnas: `alter table ... add column if not exists`, `drop column if exists`;
  - funciones y vistas: `create or replace`;
  - restricciones, políticas y tipos (no admiten `if not exists`): `drop policy if exists` + `create policy`,
    o `do $$ begin if not exists (select 1 from pg_constraint where conname = '...') then ... end if; end $$;`;
  - datos: `insert ... on conflict do nothing` o `update` con `where` acotado.
- Funciones `SECURITY DEFINER`: `set search_path = ''` y nombres cualificados.
- No uses `$miparte_script$` como etiqueta de dólar (la usa el comando `script`).
- Nunca edites un script ya aplicado en algún entorno; crea otro.
