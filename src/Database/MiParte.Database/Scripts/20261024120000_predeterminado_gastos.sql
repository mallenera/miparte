-- Casilla «Predeterminado para gastos» en perfiles de reparto y categorías (punto 18 del plan: el asistente la usa para proponer
-- el alta de un gasto desde el chat). La estructura ya lo añade (Estructura/01_Tablas/perfil_reparto.sql y categoria.sql); este script
-- lo deja explícito y aplicable por sí solo en bases ya migradas. Las filas existentes quedan sin marcar. Idempotente.
alter table public.perfil_reparto add column if not exists predeterminado_gastos boolean not null default false;
alter table public.categoria add column if not exists predeterminado_gastos boolean not null default false;
