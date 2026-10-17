-- Tipo de grupo: los hogares con un tipo no válido pasan a 'hogar'. Va en Antes/ porque la estructura añade
-- hogar_tipo_check, que fallaría si quedara alguna fila con un valor no válido. Si la columna aún no existe
-- (base nueva), no hay nada que corregir. No sobrescribe los tipos válidos. Idempotente.
do $$
begin
    if exists (select 1 from information_schema.columns
               where table_schema = 'public' and table_name = 'hogar' and column_name = 'tipo') then
        update public.hogar
           set tipo = 'hogar'
         where tipo not in ('hogar', 'piso_compartido', 'viaje', 'evento', 'otro');
    end if;
end $$;
