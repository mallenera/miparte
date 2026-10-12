# Supabase: configuración y entorno local

> **El esquema ya no se migra con la CLI de Supabase.** La fuente de verdad es `src/Database/MiParte.Database/Estructura/` y se aplica con el proyecto `MiParte.Database` (`migrar`, `pendientes`, `script`), que converge cualquier base con esa estructura. `migrations/*.sql` es la historia anterior: no se amplía ni se aplica a ningún proyecto remoto. **No ejecutes `supabase db push`**: aplicaría historia ya cubierta por `Estructura/` y desalinearía el control de versiones del esquema. Esta carpeta solo documenta la configuración de Auth, el entorno local y la semilla demo.

`tests/auth_stub.sql` solo lo usa el CI con Postgres puro (`migrar --con-auth-stub`); con `supabase start` el esquema `auth` es el real.

## Instalar la CLI (Windows)

```powershell
scoop bucket add supabase https://github.com/supabase/scoop-bucket.git
scoop install supabase
supabase --version
```

Alternativa: `npm i -g supabase` no está soportada; usa scoop, el binario de GitHub Releases o `npx supabase`.

## Enlazar con tu proyecto (solo configuración)

Desde la raíz del repo:

```bash
supabase login                                  # abre el navegador; guarda el token en tu equipo
supabase link --project-ref <project-ref>       # pide la contraseña de la base de datos
supabase config push                            # aplica config.toml (Auth) al proyecto remoto
```

`<project-ref>` es el identificador de la URL del proyecto (`https://<project-ref>.supabase.co`). No guardes la contraseña ni el token en el repo.

## Cambios de esquema

Edita `src/Database/MiParte.Database/Estructura/` (la tabla, función o política afectada; ver su README), ajusta el mapeo en `MiParteDbContext` y aplica con:

```bash
dotnet run --project src/Database/MiParte.Database -- pendientes --conexion "<session pooler, puerto 5432>"
dotnet run --project src/Database/MiParte.Database -- migrar --conexion "<session pooler, puerto 5432>"
dotnet run --project src/Database/MiParte.Database -- script supabase-completo.sql   # alternativa: pegar en el editor SQL
```

Si el cambio no se puede inferir de la estructura (borrar o renombrar, cambiar tipo, rellenar datos) añade además un script versionado idempotente en `Scripts/Antes` o `Scripts` (ver su README).

## Entorno local (opcional, requiere Docker)

```bash
supabase start        # Postgres + Auth + Studio locales (usa config.toml)
dotnet run --project src/Database/MiParte.Database -- migrar --conexion "Host=127.0.0.1;Port=54322;Database=postgres;Username=postgres;Password=postgres"
supabase stop
```

`supabase db reset` aplicaría las migraciones históricas de `migrations/`, no la estructura vigente: usa `migrar` sobre la base local.

## Autenticación

`config.toml` solo se aplica al proyecto remoto con `supabase config push`; los proveedores y las Redirect URLs del proyecto real se configuran en el panel (Authentication → Providers / URL Configuration). Google: define `SUPABASE_AUTH_GOOGLE_CLIENT_ID` y `SUPABASE_AUTH_GOOGLE_SECRET` en tu entorno y pon `enabled = true`.

## Cuenta demo (semilla)

`seed/demo.sql` crea el usuario `demo` (correo `demo@miparte.example`, contraseña `demo`) con un hogar de ejemplo y dos meses de gastos. No es una migración: CI no la aplica; se ejecuta a mano en el SQL Editor del proyecto (o `psql -f supabase/seed/demo.sql`, o `semilla-demo` del proyecto de base de datos) y es idempotente, así que volver a ejecutarla **restablece** la demo. La contraseña es pública a propósito; la cuenta solo ve el hogar demo.
