-- Tipo de grupo: los hogares existentes pasan a 'hogar' si su tipo no es válido. La columna ya tiene
-- 'hogar' por defecto (20261017120000_hogar_tipo.sql), así que en una base recién migrada no cambia ninguna fila.
-- No sobrescribe los tipos válidos (piso_compartido, viaje, evento, otro). Idempotente.
update public.hogar
   set tipo = 'hogar'
 where tipo not in ('hogar', 'piso_compartido', 'viaje', 'evento', 'otro');
