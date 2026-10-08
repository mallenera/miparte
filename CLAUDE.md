# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Proyecto

"Mi Parte, Tu Parte": app web de gastos e ingresos del hogar con reparto (individual, porcentajes, partes, cuenta común), liquidación mensual y chatbot con IA. Proyecto fin de máster. Código, esquema SQL y comentarios están en español; mantén esa convención (nombres de dominio como `Hogar`, `Miembro`, `Gasto`, `PerfilReparto`).

## Documentación y reglas

Toda la documentación de producto está en `docs/`; empieza por `docs/README.md` (índice, jerarquía de fuentes y **tabla de estado diseño frente a código**):

- `docs/diseno-y-decisiones.md`: objetivo, MVP, reglas de reparto, cuenta común, chatbot, decisiones D1-D7, plan y riesgos.
- `docs/modelo-de-datos.md`: tablas, relaciones e invariantes (la fuente de verdad sigue siendo `supabase/migrations`).
- `docs/marca.md`: logotipo, paleta y tipografía. `docs/referencia/` guarda la maqueta interactiva y la hoja de identidad originales.
- `docs/api.md`: contrato HTTP de Core.Api.

Si el código y el diseño chocan, manda el código; si te apartas del diseño a propósito, refleja el cambio en `docs/README.md`. Las reglas por área están en `.claude/rules/` (idioma y documentación, dominio de reparto, base de datos, Core.Api, front y marca, tests); varias se cargan solo al tocar las rutas que indican.

## Comandos

.NET SDK 10.0 (solución `MiParte.sln`).

```bash
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~RepartoTests"          # un test/clase concreto
dotnet run --project src/Core/Core.Api                          # GET /health, GET /api/yo (no lee .env)
./scripts/arrancar-core.ps1                                     # Core.Api cargando antes el .env de la raíz
./scripts/probar-api.ps1 -SupabaseUrl https://xxxx.supabase.co -Email ...   # prueba de humo contra Supabase real
dotnet run --project src/Assistant/Assistant.Api
dotnet run --project src/Web
docker compose up --build                                       # web :8080 (nginx sin privilegios), core :5001, assistant :5002
```

No hay linter configurado. CodeRabbit revisa los PR (`.coderabbit.yaml`; exige documentación XML en el código de producción).

### Tests contra PostgreSQL real

`EsquemaPostgresTests` (y otros que usen BD) usan `[SkippableFact]` y se saltan si no existe la variable `MIPARTE_TEST_DB`. En CI siempre se ejecutan (servicio postgres:16). En local:

```bash
psql -d miparte -v ON_ERROR_STOP=1 -f supabase/tests/auth_stub.sql || exit 1
for f in supabase/migrations/*.sql; do
  psql -d miparte -v ON_ERROR_STOP=1 -f "$f" || exit 1
done
MIPARTE_TEST_DB="Host=localhost;Database=miparte;Username=postgres;Password=..." dotnet test
```

`supabase/tests/auth_stub.sql` simula el esquema `auth` de Supabase (y sus privilegios por defecto para `anon`/`authenticated`, necesarios para que los tests de permisos tengan algo que comprobar); hay que aplicarlo antes de las migraciones.

#### Montar un PostgreSQL local en Windows (una vez)

Sin Docker ni instalador con administrador, con scoop (instala la 18 en `~\scoop\apps\postgresql`; CI usa la 16, el SQL es estándar). Su post-instalación ya crea un clúster con superusuario `postgres` sin contraseña y autenticación `trust` solo local. En una terminal nueva (para que vea el PATH):

```bash
scoop install postgresql
pg_ctl -D ~/scoop/apps/postgresql/current/data -l ~/pg.log start     # no arranca solo con el equipo
createdb -U postgres -h localhost miparte
```

Cada vez que haya migraciones nuevas (o al empezar de cero), recrea la base y aplica stub y migraciones en orden (los comandos de arriba, con `PGUSER=postgres PGHOST=localhost`); no edites migraciones ya aplicadas, crea otra. Para parar el servidor: `pg_ctl -D ~/scoop/apps/postgresql/current/data stop`. Si `pg_ctl` se cuelga en una tubería (`| tail`), no redirijas su salida: el servidor queda arrancado igualmente (comprueba el log).

```bash
psql -U postgres -h localhost -c "drop database if exists miparte" -c "create database miparte"
MIPARTE_TEST_DB="Host=localhost;Database=miparte;Username=postgres" dotnet test
```

## Seguridad del front (CSP)

`src/Web/default.conf.template` (nginx) fija la CSP y demás cabeceras; la imagen las aplica con envsubst a partir de `SUPABASE_URL` y `CORE_URL` (en `docker compose` salen de `Supabase__Url` y `CoreUrl`), que deben coincidir con `Supabase:Url` y `Api:CoreUrl` del `appsettings.json` del front. Consecuencias al desarrollar: **no añadas scripts en línea** a `index.html` (ponlos en `wwwroot/js/`), ni cargues JavaScript de otro origen, ni hables con otros hosts desde el front sin añadirlos a `connect-src`. Para comprobar un cambio con CSP real no hace falta Docker: publica el front (`dotnet publish src/Web`) y sírvelo con las cabeceras de la plantilla; la consola del navegador mostrará las violaciones.

## Arquitectura

- `src/Web`: Blazor WASM (PWA), front independiente. Lee `wwwroot/appsettings.json` (`Supabase:Url`, `Supabase:AnonKey`, `Api:CoreUrl`; nunca secretos). Los DTOs compartidos viven en `src/Contracts`. Ya tiene la base: login/registro contra Supabase Auth por REST (`Autenticacion/`, sesión en `localStorage`, renovación automática), `ManejadorCoreApi` (pone `Authorization` y `X-Hogar-Id`; un 401 descarta la sesión), `CoreApiClient` (usuario, hogares, miembros, invitaciones, categorías y perfiles), `EstadoHogar`/`ServicioArranque` (flujo de arranque de `docs/api.md` §2) y páginas Login, Registro, SinHogar, ElegirHogar, Home, Hogar (miembros, invitaciones, perfiles de reparto), Categorías y Gastos (lista por mes con reparto, alta, edición y borrado), Cuenta común (saldo, aportaciones y reembolsos), Resumen (página `/`: resumen mensual y liquidación con registro de pagos) y Recurrentes (plantillas y generación del mes, disparada por el botón). Las páginas que necesitan hogar se envuelven en `<RequiereHogar>` y delegan en un componente `Vista*` que carga sus datos; las protegidas llevan `[Authorize]`. Tests en `src/Web.Tests` (xUnit + bUnit; `ApiFalsa` simula Core.Api).
  - **Imagen de marca** (detalle en `docs/marca.md`): burdeos `#7A1F33` principal, naranja `#E8742A` acento, crema `#FBF5EF` fondo, tinta `#24161A`; Fraunces solo en logotipo y titulares grandes, Manrope en toda la interfaz; modo oscuro propio; el nombre en el logotipo va siempre en minúsculas. Los tokens están en `wwwroot/css/app.css` (no hay Bootstrap). Cada miembro tiene color + inicial (`--m0..--m7`, `Avatar` y `ColorMiembro`, derivado del orden por id porque la API no guarda color). Los colores de estado (ok/err/warn) nunca son burdeos ni naranja y siempre llevan texto o signo. Iconos PWA generados desde el símbolo del logo.
  - `MiembroDto.EsYo` (API) permite al front saber qué miembro es el usuario y si es admin.
- `src/Core`: `Core.Domain` (entidades y lógica sin dependencias: `Reparto.Dividir` redondea a 2 decimales y el último miembro absorbe el céntimo sobrante; `RepartoMiembros` reparte entre N miembros; `Liquidacion` calcula transferencias mínimas), `Core.Infrastructure` (EF Core/Npgsql), `Core.Api` (minimal API), `Core.Tests` (xUnit).
- `src/Assistant/Assistant.Api`: chatbot con tool calling (aún esqueleto: solo `/health`).
- `src/BuildingBlocks/Auth` (`MiParte.Auth`): validación del JWT de Supabase compartida entre APIs.

### Core.Api: endpoints y reglas

Endpoints por carpeta en `src/Core/Core.Api`: `Hogares`, `Miembros`, `Reparto` (categorías y perfiles), `Gastos` (gastos, recurrentes y liquidación). La referencia completa para el front está en `docs/api.md`; **actualízala al cambiar un endpoint o DTO**. Reglas a recordar:

- Los endpoints que funcionan sin hogar seleccionado (hogares, `/api/yo`, aceptar invitación) llevan el metadato `SinHogarActual`; el resto exige hogar.
- Al crear un hogar se siembran 4 perfiles y 6 categorías (`SemillaHogar`); el creador es admin y adulto.
- Roles: solo admin gestiona miembros e invitaciones; siempre queda un admin activo y vinculado. Pagador y reparto solo van a adultos activos; los miembros `a_cargo` necesitan un adulto responsable.
- Invitaciones: el token en claro solo se devuelve al crearla (en BD solo el hash); caducan a 7 días y son de un solo uso.
- Cada gasto guarda su reparto al crearse y no se recalcula salvo con `PUT` del gasto. Importes > 0 con máx. 2 decimales; meses con formato `YYYY-MM`.
- Recurrentes: `diaMes` 1-28; `POST /api/gastos-recurrentes/generar?mes=` es idempotente. No hay proceso en segundo plano: lo dispara el cliente.
- CORS: orígenes permitidos en `Cors__OrigenesPermitidos__N`.
- **Ingresos no se guardan** (decisión de diseño): la migración `20261007000000_perfiles_cuenta_comun_sin_ingresos.sql` elimina la tabla `ingreso`; los perfiles son individual, porcentajes, partes y cuenta común (`cuenta_comun`: el gasto no se reparte ni genera deuda; `gasto.a_cargo_cuenta_comun`). **Cuenta común**: aportaciones, saldo y reembolsos implementados (`/api/cuenta-comun`, `docs/api.md` §3.9); falta activarla por hogar.

### Base de datos: el esquema es SQL, no EF

El esquema y las políticas RLS viven en `supabase/migrations/*.sql` (esquema inicial, multihogar con invitaciones y pagos, recurrentes únicos, perfiles de cuenta común sin ingresos). EF Core **solo mapea** (no se generan migraciones EF; `MiParteDbContext` fija tablas con `ToTable`, `UseSnakeCaseNamingConvention`, y conversiones explícitas de enums a texto). Cambios de esquema = nueva migración SQL + ajustar el mapeo en `MiParteDbContext`; CI aplica todas las migraciones en orden.

### Multitenencia por hogar (dos capas)

1. RLS en Postgres (Supabase).
2. En el servicio, el rol de conexión puede saltarse la RLS, así que `MiParteDbContext` aplica un **global query filter por `hogar_id`** a todas las entidades (con `IHogarActual`). Toda entidad nueva con `HogarId` necesita su `HasQueryFilter`.

`HogarActualMiddleware` resuelve el hogar de cada petición autenticada: `sub` del JWT → `miembro.user_id` (activos). Un solo hogar se usa directamente; si hay varios exige la cabecera `X-Hogar-Id` (409 si falta, 403 si no pertenece, 400 si es inválida). Usa `IgnoreQueryFilters()` porque aún no hay hogar. `HogarActual` es scoped y se resuelve dentro del middleware para que `/health` funcione sin BD.

### Autenticación

`Core.Api` valida el JWT de Supabase Auth: emisor `{Supabase__Url}/auth/v1`, audiencia `authenticated`. Si se define `Supabase__JwtSecret` solo se aceptan HS256 (legado); si no, solo asimétricos vía JWKS del proyecto. Sin configuración ningún token es válido pero la API arranca. La cadena de conexión viene de `ConnectionStrings__Default` (variable de entorno, nunca en el repo); si falta no se registra la persistencia. Con Supabase usa el Session pooler (puerto 5432), no el Transaction pooler (6543), por las sentencias preparadas de Npgsql; en producción `SSL Mode=VerifyFull` con el CA del proyecto (ver `.env.example`). El `.env` (ignorado por git) lo lee `docker compose` pero no `dotnet run`.
