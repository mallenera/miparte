# Cuenta demo y su restablecimiento

La demo tiene dos formas (ver la fila «Demo sin registrarse» de [README.md](README.md)): el **modo local** del front (sin servidor, no necesita nada de esto) y la **cuenta demo real** (`demo` / `demo`, el front traduce `demo` a `demo@miparte.example`), que vive en el proyecto Supabase y es la que esta guía deja lista.

| Fichero | Qué hace |
|---|---|
| `supabase/seed/demo.sql` | Define `public.restablecer_demo()` y la ejecuta. Borra y recrea el usuario de Auth, el hogar demo (id fijo `00000000-0000-4000-8000-000000000001`) desvincula la cuenta demo de los hogares ajenos a los que se haya unido (sin borrarlos), y vuelve a sembrar miembros, perfiles, categorías, recurrentes, gastos (fechados respecto a hoy), cuenta común y aportaciones. Es idempotente. |
| `supabase/seed/demo_cron.sql` | Activa `pg_cron` y programa `restablecer_demo()` cada día a las 04:00 UTC (tarea `restablecer-demo`). |
| `.github/workflows/restablecer-demo.yml` | Botón manual (y plan B sin `pg_cron`) que ejecuta `demo.sql` con el secreto `DEMO_DB_URL`. |

Es una **semilla manual, no una migración**: CI no la aplica. La función solo la puede llamar el propietario y `pg_cron`; no está expuesta por RPC a `anon` ni `authenticated`. Si cambias los datos, cámbialos también en `src/Web/Demo/ServidorDemo.Datos.cs`.

## Paso a paso (proyecto Supabase real)

Prerrequisito: las migraciones de `supabase/migrations` ya aplicadas (`supabase db push`), porque la semilla escribe en las tablas del hogar.

1. **Ejecutar la semilla una vez.** Supabase → SQL Editor → pega el contenido de `supabase/seed/demo.sql` → Run. O desde una terminal, con la cadena del **Session pooler** (puerto 5432, ver `.env.example`) en una variable de entorno, sin escribirla en ningún fichero del repo:
   ```bash
   psql "$DEMO_DB_URL" -v ON_ERROR_STOP=1 -f supabase/seed/demo.sql
   ```
2. **Comprobar.** En la web, usuario `demo` y contraseña `demo`: debe entrar al hogar «Casa de Ana y Marcos» con gastos del mes anterior y del actual.
3. **Programar el restablecimiento.** SQL Editor → pega `supabase/seed/demo_cron.sql` → Run (si prefieres, activa antes la extensión en Database → Extensions → `pg_cron`). Comprueba con:
   ```sql
   select jobid, jobname, schedule, active from cron.job where jobname = 'restablecer-demo';
   select status, return_message, start_time from cron.job_run_details order by start_time desc limit 5;
   ```
   Volver a ejecutarlo actualiza la tarea, no la duplica. Para cambiar la hora, edita la expresión cron del fichero y ejecútalo de nuevo; para quitarla: `select cron.unschedule('restablecer-demo');`.
4. **(Opcional) Botón manual desde GitHub.** Settings → Environments → New environment `demo` (puedes exigir revisores) → añade el secreto `DEMO_DB_URL` (Session pooler, puerto 5432, `sslmode=require`). Después, Actions → «Restablecer demo» → Run workflow. El secreto no debe estar nunca en el repo ni en los logs.

## Notas

- Restablecer recrea el usuario de Auth con el mismo id: se pierden sus tokens de renovación, así que quien esté dentro tendrá que volver a iniciar sesión cuando caduque su token de acceso, y verá los datos reiniciados. A las 04:00 UTC es lo menos molesto.
- `pg_cron` ejecuta la función como propietario de la base; el plan gratuito de Supabase lo incluye, pero un proyecto pausado por inactividad no corre tareas hasta que se reactive.
- Verificado en local contra PostgreSQL 18 (esquema de CI más un stub ampliado de `auth.users`/`auth.identities` y `pgcrypto`): doble ejecución estable, recuperación tras modificar o borrar datos del hogar demo, desvinculación de la cuenta de hogares ajenos (sin borrarlos; este último caso aún no repetido tras el último cambio), reparto cuadrando con cada importe y contraseña `demo` válida contra el hash. `pg_cron` y el workflow **no** se han podido probar sin el proyecto real.
