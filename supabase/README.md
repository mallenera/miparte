# Supabase: migraciones con la CLI

Esquema y RLS en `migrations/*.sql` (orden por nombre). `tests/auth_stub.sql` solo lo usa el CI con Postgres puro; con `supabase start` el esquema `auth` es el real.

## Instalar la CLI (Windows)

```powershell
scoop bucket add supabase https://github.com/supabase/scoop-bucket.git
scoop install supabase
supabase --version
```

Alternativa: `npm i -g supabase` no está soportada; usa scoop, el binario de GitHub Releases o `npx supabase`.

## Enlazar con tu proyecto y aplicar migraciones

Desde la raíz del repo:

```bash
supabase login                                  # abre el navegador; guarda el token en tu equipo
supabase link --project-ref <project-ref>       # pide la contraseña de la base de datos
supabase db push --dry-run                      # muestra qué migraciones se aplicarían
supabase db push                                # aplica las pendientes (inicial + multihogar)
supabase migration list                         # local vs remoto
```

`<project-ref>` es el identificador de la URL del proyecto (`https://<project-ref>.supabase.co`). No guardes la contraseña ni el token en el repo.

## Nuevas migraciones

```bash
supabase migration new nombre_del_cambio        # crea supabase/migrations/<timestamp>_nombre_del_cambio.sql
```

Escribe el SQL, ajusta el mapeo en `MiParteDbContext` y ejecuta `supabase db push`. Las migraciones aplicadas no se editan: se crea otra.

## Entorno local (opcional, requiere Docker)

```bash
supabase start        # Postgres + Auth + Studio locales (usa config.toml)
supabase db reset     # recrea la base local aplicando todas las migraciones
supabase stop
```

## Autenticación

`config.toml` solo se aplica al proyecto remoto con `supabase config push`; los proveedores y las Redirect URLs del proyecto real se configuran en el panel (Authentication → Providers / URL Configuration). Google: define `SUPABASE_AUTH_GOOGLE_CLIENT_ID` y `SUPABASE_AUTH_GOOGLE_SECRET` en tu entorno y pon `enabled = true`.

## Cuenta demo (semilla)

`seed/demo.sql` crea el usuario `demo` (correo `demo@miparte.example`, contraseña `demo`) con un hogar de ejemplo y dos meses de gastos. No es una migración: CI no la aplica; se ejecuta a mano en el SQL Editor del proyecto (o `psql -f supabase/seed/demo.sql`) y es idempotente, así que volver a ejecutarla **restablece** la demo. La contraseña es pública a propósito; la cuenta solo ve el hogar demo.
