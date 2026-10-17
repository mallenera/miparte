-- Tipo de grupo: segunda pasada para bases que ya registraron 20261017130000_hogar_tipo_datos.sql en su
-- journal (ese script no se vuelve a ejecutar). Corrige tipos NULL o no válidos y deja hogar.tipo NOT NULL.
-- Antes de la estructura y con guarda de columna. Idempotente.
do $$
begin
    if exists (select 1 from information_schema.columns
               where table_schema = 'public' and table_name = 'hogar' and column_name = 'tipo') then
        update public.hogar
           set tipo = 'hogar'
         where tipo is null or tipo not in ('hogar', 'piso_compartido', 'viaje', 'evento', 'otro');
        alter table public.hogar alter column tipo set not null;
    end if;
end $$;
