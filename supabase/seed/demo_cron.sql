-- Restablece la cuenta demo cada día a las 04:00 UTC llamando a public.restablecer_demo() (definida en demo.sql).
-- Requiere haber ejecutado antes demo.sql. cron.schedule con el mismo nombre actualiza la tarea, así que es repetible.
-- Para quitarla: select cron.unschedule('restablecer-demo');

create extension if not exists pg_cron with schema pg_catalog;

select cron.schedule('restablecer-demo', '0 4 * * *', 'select public.restablecer_demo()');
