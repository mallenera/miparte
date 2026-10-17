-- Gastos recurrentes como configuración del hogar (fase 3 de grupos): interruptor recurrentes_activas. Las filas existentes
-- quedan a true, así que los hogares que ya generaban sus recurrentes siguen igual. La estructura ya lo añade
-- (Estructura/01_Tablas/hogar.sql); este script lo deja explícito y aplicable por sí solo en bases ya migradas. No necesita
-- ir antes de la estructura ni rellenar datos. Idempotente.
alter table public.hogar add column if not exists recurrentes_activas boolean not null default true;
