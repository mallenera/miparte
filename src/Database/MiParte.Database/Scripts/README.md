# Scripts versionados

La estructura del esquema vive en `../Estructura/` y `migrar` la converge sola. Aquí van **solo los pasos que no se
pueden inferir de ella**: borrar o renombrar, cambiar tipos, rellenar o corregir datos, retirar objetos que ya no
existen. Cada script corre **una vez** (journal en `miparte_migraciones.scripts_aplicados`), por orden de nombre.

- `Antes/`: corren antes de aplicar la estructura (p. ej. renombrar una columna para que la estructura no cree otra
  vacía; borrar un objeto que estorba).
- raíz (`*.sql`): corren después (p. ej. rellenar con datos una columna nueva y borrar la antigua).
- Nombre: `AAAAMMDDHHMMSS_descripcion_en_snake_case.sql`, posterior al último.
- **Idempotentes**: sobre una base vacía o ya migrada deben poder ejecutarse sin error ni efecto doble. Patrones:
  - borrar: `drop column if exists`, `drop function if exists`, `drop table if exists`;
  - renombrar: `do $$ begin if exists (select 1 from information_schema.columns where table_name = '...' and column_name = 'vieja') then alter table ... rename column vieja to nueva; end if; end $$;`;
  - datos: `update ... where <condición que ya no se cumple tras aplicar>` o `insert ... on conflict do nothing`.
- Funciones `SECURITY DEFINER`: `set search_path = ''` y nombres cualificados.
- Etiqueta de dólar: usa `$$`. DbUp toma `$nombre$` como variable y falla («Variable nombre has no value defined»); además `$miparte_script$` la usa el comando `script`.
- Nunca edites un script ya aplicado en algún entorno; crea otro.

## Generados

`Scripts/Generados/` recibe la salida del comando `script` (un SQL idempotente con estructura y scripts). Está
ignorado por git y no se incrusta (solo se incrustan los `.sql` de `Antes/` y de esta carpeta), así que no se
reaplica como migración.
