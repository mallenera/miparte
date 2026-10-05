# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Proyecto

"Mi Parte, Tu Parte": app web de gastos e ingresos del hogar con reparto (porcentaje, partes, ingresos), liquidación mensual y chatbot con IA. Proyecto fin de máster. Código, esquema SQL y comentarios están en español; mantén esa convención (nombres de dominio como `Hogar`, `Miembro`, `Gasto`, `PerfilReparto`).

## Comandos

.NET SDK 8.0 (solución `MiParte.sln`).

```bash
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~RepartoTests"          # un test/clase concreto
dotnet run --project src/Core/Core.Api                          # GET /health, GET /api/yo (no lee .env)
./scripts/arrancar-core.ps1                                     # Core.Api cargando antes el .env de la raíz
./scripts/probar-api.ps1 -SupabaseUrl https://xxxx.supabase.co -Email ...   # prueba de humo contra Supabase real
dotnet run --project src/Assistant/Assistant.Api
dotnet run --project src/Web
docker compose up --build                                       # web :8080, core :5001, assistant :5002
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

`supabase/tests/auth_stub.sql` simula el esquema `auth` de Supabase; hay que aplicarlo antes de las migraciones.

## Arquitectura

- `src/Web`: Blazor WASM (PWA), front independiente. Lee `wwwroot/appsettings.json` (`Supabase:Url`, `Supabase:AnonKey`, `Api:CoreUrl`; nunca secretos). Los DTOs compartidos viven en `src/Contracts`. Ya tiene la base: login/registro contra Supabase Auth por REST (`Autenticacion/`, sesión en `localStorage`, renovación automática), `ManejadorCoreApi` (pone `Authorization` y `X-Hogar-Id`; un 401 descarta la sesión), `CoreApiClient` (de momento `/api/yo`, crear hogar, aceptar invitación), `EstadoHogar`/`ServicioArranque` (flujo de arranque de `docs/api.md` §2) y páginas Login, Registro, SinHogar, ElegirHogar y Home. Las páginas que necesitan hogar se envuelven en `<RequiereHogar>`; las protegidas llevan `[Authorize]`. Faltan las pantallas de negocio (miembros, categorías, perfiles, ingresos, gastos, recurrentes, liquidación). Tests en `src/Web.Tests` (xUnit, sin bUnit aún).
- `src/Core`: `Core.Domain` (entidades y lógica sin dependencias: `Reparto.Dividir` redondea a 2 decimales y el último miembro absorbe el céntimo sobrante; `RepartoMiembros` reparte entre N miembros; `Liquidacion` calcula transferencias mínimas), `Core.Infrastructure` (EF Core/Npgsql), `Core.Api` (minimal API), `Core.Tests` (xUnit).
- `src/Assistant/Assistant.Api`: chatbot con tool calling (aún esqueleto: solo `/health`).
- `src/BuildingBlocks/Auth` (`MiParte.Auth`): validación del JWT de Supabase compartida entre APIs.

### Core.Api: endpoints y reglas

Endpoints por carpeta en `src/Core/Core.Api`: `Hogares`, `Miembros`, `Reparto` (categorías y perfiles), `Gastos` (gastos, ingresos, recurrentes y liquidación). La referencia completa para el front está en `docs/api.md`; **actualízala al cambiar un endpoint o DTO**. Reglas a recordar:

- Los endpoints que funcionan sin hogar seleccionado (hogares, `/api/yo`, aceptar invitación) llevan el metadato `SinHogarActual`; el resto exige hogar.
- Al crear un hogar se siembran 4 perfiles y 6 categorías (`SemillaHogar`); el creador es admin y adulto.
- Roles: solo admin gestiona miembros e invitaciones; siempre queda un admin activo y vinculado. Pagador, ingresos y reparto solo van a adultos activos; los miembros `a_cargo` necesitan un adulto responsable.
- Invitaciones: el token en claro solo se devuelve al crearla (en BD solo el hash); caducan a 7 días y son de un solo uso.
- Cada gasto guarda su reparto al crearse y no se recalcula salvo con `PUT` del gasto. Importes > 0 con máx. 2 decimales; meses con formato `YYYY-MM`.
- Recurrentes: `diaMes` 1-28; `POST /api/gastos-recurrentes/generar?mes=` es idempotente. No hay proceso en segundo plano: lo dispara el cliente.
- CORS: orígenes permitidos en `Cors__OrigenesPermitidos__N`.

### Base de datos: el esquema es SQL, no EF

El esquema y las políticas RLS viven en `supabase/migrations/*.sql` (esquema inicial, multihogar con invitaciones y pagos, recurrentes únicos). EF Core **solo mapea** (no se generan migraciones EF; `MiParteDbContext` fija tablas con `ToTable`, `UseSnakeCaseNamingConvention`, y conversiones explícitas de enums a texto). Cambios de esquema = nueva migración SQL + ajustar el mapeo en `MiParteDbContext`; CI aplica todas las migraciones en orden.

### Multitenencia por hogar (dos capas)

1. RLS en Postgres (Supabase).
2. En el servicio, el rol de conexión puede saltarse la RLS, así que `MiParteDbContext` aplica un **global query filter por `hogar_id`** a todas las entidades (con `IHogarActual`). Toda entidad nueva con `HogarId` necesita su `HasQueryFilter`.

`HogarActualMiddleware` resuelve el hogar de cada petición autenticada: `sub` del JWT → `miembro.user_id` (activos). Un solo hogar se usa directamente; si hay varios exige la cabecera `X-Hogar-Id` (409 si falta, 403 si no pertenece, 400 si es inválida). Usa `IgnoreQueryFilters()` porque aún no hay hogar. `HogarActual` es scoped y se resuelve dentro del middleware para que `/health` funcione sin BD.

### Autenticación

`Core.Api` valida el JWT de Supabase Auth: emisor `{Supabase__Url}/auth/v1`, audiencia `authenticated`. Si se define `Supabase__JwtSecret` solo se aceptan HS256 (legado); si no, solo asimétricos vía JWKS del proyecto. Sin configuración ningún token es válido pero la API arranca. La cadena de conexión viene de `ConnectionStrings__Default` (variable de entorno, nunca en el repo); si falta no se registra la persistencia. Con Supabase usa el Session pooler (puerto 5432), no el Transaction pooler (6543), por las sentencias preparadas de Npgsql; en producción `SSL Mode=VerifyFull` con el CA del proyecto (ver `.env.example`). El `.env` (ignorado por git) lo lee `docker compose` pero no `dotnet run`.
