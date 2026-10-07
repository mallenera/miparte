-- Elimina miembro.porcentaje_proporcional: la creó una versión anterior de la migración de perfiles sin
-- ingresos (ya sustituida) y solo existe en la base remota. Con if exists es inocua en bases nuevas.

alter table public.miembro drop column if exists porcentaje_proporcional;
