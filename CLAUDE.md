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
dotnet run --project src/Assistant/Assistant.Api                 # chatbot; necesita ANTHROPIC_API_KEY en el entorno
dotnet run --project src/Web
dotnet run --project src/Database/MiParte.Database -- migrar --conexion "<cadena>"   # migrar; también: pendientes, script, marcar-aplicadas
npm ci --prefix tests/pwa && npx --prefix tests/pwa playwright install chromium   # una vez, antes de la prueba de la PWA
node tests/pwa/probar.mjs <publish>/wwwroot                      # PWA publicada como en Cloudflare Pages
docker compose up --build                                       # web :8080 (nginx sin privilegios), core :5001, assistant :5002
```

No hay linter configurado. CodeRabbit revisa los PR (`.coderabbit.yaml`; exige documentación XML en el código de producción).

### Tests contra PostgreSQL real

`EsquemaPostgresTests` (y otros que usen BD) usan `[SkippableFact]` y se saltan si no existe la variable `MIPARTE_TEST_DB`. En CI siempre se ejecutan (servicio postgres:17, la misma versión mayor que Supabase en `supabase/config.toml`). En local:

```bash
dotnet run --project src/Database/MiParte.Database -- migrar --con-auth-stub --conexion "Host=localhost;Database=miparte;Username=postgres;Password=..."
MIPARTE_TEST_DB="Host=localhost;Database=miparte;Username=postgres;Password=..." dotnet test
```

`supabase/tests/auth_stub.sql` simula el esquema `auth` de Supabase (y sus privilegios por defecto para `anon`/`authenticated`, necesarios para que los tests de permisos tengan algo que comprobar); lo aplica `migrar --con-auth-stub` antes de las migraciones (en Supabase real no se usa). El proyecto `MiParte.Database.Tests` crea y borra bases temporales a partir del servidor de `MIPARTE_TEST_DB`.

#### Montar un PostgreSQL local en Windows (una vez)

Sin Docker ni instalador con administrador, con scoop (instala la última, hoy la 18, en `~\scoop\apps\postgresql`; `scoop install postgresql@17` falla porque su autoupdate no encuentra el zip de EnterpriseDB (403); CI y Supabase usan la 17, pero para desarrollo basta la 18: los ficheros de `Estructura/` se aplican en 15+ y el exportador quita lo propio de la 17+). Su post-instalación ya crea un clúster con superusuario `postgres` sin contraseña y autenticación `trust` solo local. En una terminal nueva (para que vea el PATH):

```bash
scoop install postgresql
pg_ctl -D ~/scoop/apps/postgresql/current/data -l ~/pg.log start     # no arranca solo con el equipo
createdb -U postgres -h localhost miparte
```

Cada vez que haya migraciones nuevas (o al empezar de cero), basta `migrar` (aplica solo lo pendiente); para empezar de cero, recrea la base y vuelve a migrar. No edites scripts ya aplicados, crea otro. Para parar el servidor: `pg_ctl -D ~/scoop/apps/postgresql/current/data stop`. Si `pg_ctl` se cuelga en una tubería (`| tail`), no redirijas su salida: el servidor queda arrancado igualmente (comprueba el log).

```bash
export PGUSER=postgres PGHOST=localhost
psql -c "drop database if exists miparte" -c "create database miparte"
dotnet run --project src/Database/MiParte.Database -- migrar --con-auth-stub --conexion "Host=localhost;Database=miparte;Username=postgres"
MIPARTE_TEST_DB="Host=localhost;Database=miparte;Username=postgres" dotnet test
```

## Seguridad del front (CSP)

`src/Web/default.conf.template` (nginx) fija la CSP y demás cabeceras; la imagen las aplica con envsubst a partir de `SUPABASE_URL` y `CORE_URL` (en `docker compose` salen de `Supabase__Url` y `CoreUrl`), que deben coincidir con `Supabase:Url` y `Api:CoreUrl` del `appsettings.json` del front. Consecuencias al desarrollar: **no añadas scripts en línea** a `index.html` (ponlos en `wwwroot/js/`), **ni atributos `style=""`** (la CSP usa `style-src-attr 'none'`; usa clases de `app.css`, p. ej. `u-mt12` o `av c0..c7`), ni cargues JavaScript de otro origen, ni hables con otros hosts desde el front sin añadirlos a `connect-src`. Para comprobar un cambio con CSP real no hace falta Docker: publica el front (`dotnet publish src/Web`) y sírvelo con las cabeceras de la plantilla; la consola del navegador mostrará las violaciones.

## Arquitectura

- `src/Web`: Blazor WASM (PWA), front independiente. Lee `wwwroot/appsettings.json` (`Supabase:Url`, `Supabase:AnonKey`, `Api:CoreUrl`; nunca secretos). Los DTOs compartidos viven en `src/Contracts`. Ya tiene la base: login/registro contra Supabase Auth por REST (`Autenticacion/`, sesión en `localStorage`, renovación automática), `ManejadorCoreApi` (pone `Authorization` y `X-Hogar-Id`; un 401 descarta la sesión), `CoreApiClient` (usuario, hogares, miembros, invitaciones, categorías y perfiles), `EstadoHogar`/`ServicioArranque` (flujo de arranque de `docs/api.md` §2) y páginas Login, Registro, SinHogar, ElegirHogar, Home, Configuración del hogar, Categorías e Historial (entradas del menú del hogar; la configuración reúne miembros, invitaciones, perfiles de reparto, permisos y las casillas de cuenta común y ahorro), Categorías y Gastos (lista por mes con reparto, alta, edición y borrado), Cuenta común (saldo, aportaciones, reembolsos y deudas de las personas con la cuenta), Lista de la compra (`/lista-compra`, y `/compra/{token}` sin sesión desde un QR), Resumen (página `/`: resumen mensual y liquidación con registro de pagos) y Recurrentes (plantillas y generación del mes, disparada por el botón) y Dashboard (`/dashboard`: KPIs y gráficos SVG del gasto entre dos fechas con filtro cruzado; el cálculo está en `Reparto/AnalisisGastos.cs`). Las páginas que necesitan hogar se envuelven en `<RequiereHogar>` y delegan en un componente `Vista*` que carga sus datos; las protegidas llevan `[Authorize]`. Tests en `src/Web.Tests` (xUnit + bUnit; `ApiFalsa` simula Core.Api).
  - **Caché de lecturas**: `CacheLecturas` (singleton) guarda 60 s las listas de miembros, categorías, perfiles y cierres que `CoreApiClient` pide en cada pestaña; comparte la petición en curso, se separa por hogar y se invalida sola con cualquier escritura sobre esas rutas. Si añades un endpoint que modifica esas listas desde otra ruta, inclúyelo en `RutasEnCache`/`InvalidarTrasEscritura`. La pestaña Gastos lee si la cuenta común está activa de `EstadoHogar`, no de `/api/cuenta-comun`.
  - **Imagen de marca** (detalle en `docs/marca.md`): burdeos `#7A1F33` principal, naranja `#E8742A` acento, crema `#FBF5EF` fondo, tinta `#24161A`; Fraunces solo en logotipo y titulares grandes, Manrope en toda la interfaz; modo oscuro propio; el nombre en el logotipo va siempre en minúsculas. Los tokens están en `wwwroot/css/app.css` (no hay Bootstrap). Cada miembro tiene color + inicial (`--m0..--m7`, `Avatar` y `ColorMiembro`, derivado del orden por id porque la API no guarda color). Los colores de estado (ok/err/warn) nunca son burdeos ni naranja y siempre llevan texto o signo. Iconos PWA generados desde el símbolo del logo.
  - `MiembroDto.EsYo` (API) permite al front saber qué miembro es el usuario y si es admin.
- `src/Core`: `Core.Domain` (entidades y lógica sin dependencias: `Reparto.Dividir` redondea a 2 decimales y el último miembro absorbe el céntimo sobrante; `RepartoMiembros` reparte entre N miembros; `Liquidacion` calcula transferencias mínimas), `Core.Application` (casos de uso `Servicio*`, puertos `IRepositorio*` y `Resultado<T>`; solo depende de `Core.Domain`), `Core.Infrastructure` (EF Core/Npgsql: `MiParteDbContext` y los `Repositorio*` que implementan los puertos), `Core.Api` (minimal API: solo traduce HTTP y delega en los servicios), `Core.Tests` (xUnit). **Regla de capas**: las reglas de negocio van en `Core.Domain` o en un servicio de `Core.Application`; los endpoints no usan `MiParteDbContext` ni contienen reglas, y la API no conoce Npgsql. Detalle, cómo añadir una regla y estado de la migración en `docs/arquitectura.md`.
- `src/Assistant/Assistant.Api`: chatbot con tool calling (`POST /api/chat`, cinco funciones de solo lectura que consultan Core.Api con el JWT y el hogar de la persona; cliente del SDK de Anthropic detrás de `IClienteModelo`; clave solo en `ANTHROPIC_API_KEY`). Tests en `src/Assistant/Assistant.Tests` con modelo falso. Contrato y seguridad en `docs/asistente.md`; actualízalo al tocar el asistente.
- `src/BuildingBlocks/Auth` (`MiParte.Auth`): validación del JWT de Supabase compartida entre APIs.

### Core.Api: endpoints y reglas

Endpoints por carpeta en `src/Core/Core.Api`: `Hogares`, `Miembros`, `Reparto` (categorías y perfiles), `Gastos` (gastos, recurrentes, liquidación y cuenta común), `Cierres` (cierre de mes), `Auditoria` (historial) y `Contacto` (`POST /api/contacto`, anónimo con su propio límite: envía el formulario por la API HTTP de Resend, `Correo__*`) y `ListaCompra` (productos, activación y enlaces QR; sus rutas `/api/lista-compra/publica` también son anónimas); `Seguridad` reúne los filtros transversales (permisos, límite de peticiones, cabeceras y errores). La referencia completa para el front está en `docs/api.md`; **actualízala al cambiar un endpoint o DTO**. Reglas a recordar:

- Los endpoints que funcionan sin hogar seleccionado (hogares, `/api/yo`, aceptar invitación) llevan el metadato `SinHogarActual`; el resto exige hogar.
- Al crear un hogar se siembran 5 perfiles (incluido «Cuenta común (ahorro)», modo `cuenta_comun_ahorro`) y 6 categorías (`SemillaHogar`); el creador es admin y adulto.
- Roles y permisos: el rol (admin/miembro) solo fija la plantilla de permisos; cualquier adulto con cuenta puede tener la combinación que se le asigne (también un admin), incluidos gestionar miembros, invitaciones, funciones del hogar y permisos; siempre queda un admin activo y vinculado y alguien con `permisos.gestionar`. Permisos por miembro (`CatalogoPermisos` en Core.Domain, `miembro.permisos`): las escrituras llevan `.RequierePermiso(CatalogoPermisos.X)` (Core.Api/Seguridad) y responden 403 sin él; un endpoint de escritura nuevo debe declarar su permiso y, si es nuevo, añadirlo al catálogo, al check de la migración y a la matriz de docs/api.md. Pagador solo adultos activos. El reparto cuenta a los adultos y a los miembros `a_cargo` con peso; la parte de cada `a_cargo` la asume su responsable (`gasto_reparto_a_cargo` guarda el desglose). Los `a_cargo` necesitan un adulto responsable.
- Invitaciones: el token en claro solo se devuelve al crearla (en BD solo el hash); caducan a 7 días y son de un solo uso.
- Cada gasto guarda su reparto al crearse y no se recalcula salvo con `PUT` del gasto. Importes > 0 con máx. 2 decimales; meses con formato `YYYY-MM`.
- Recurrentes: `diaMes` 1-28; `POST /api/gastos-recurrentes/generar?mes=` es idempotente. No hay proceso en segundo plano: lo dispara el cliente.
- CORS: orígenes permitidos en `Cors__OrigenesPermitidos__N`.
- **Ingresos no se guardan** (decisión de diseño): la migración `20261007000000_perfiles_cuenta_comun_sin_ingresos.sql` elimina la tabla `ingreso`; los perfiles son individual, porcentajes, partes y cuenta común (`cuenta_comun`: el gasto no se reparte ni genera deuda; `gasto.a_cargo_cuenta_comun`). **Cuenta común** (`GET /api/cuenta-comun` y `GET /api/resumen` leen el histórico con `HistoricoCuentaComun`: una sola consulta `UNION ALL` ya agrupada y acotada al fin del mes; no añadas ahí columnas o constantes `Guid.Empty` literales, PostgreSQL no puede tipar la unión: usa una variable): aportaciones, saldo, reembolsos y ahorro (parte de cada aportación que se aparta, ingresos aparte, retiradas y gastos pagados desde el ahorro) implementados (`/api/cuenta-comun`, `docs/api.md` §3.9); cada hogar la activa (un admin, `PUT /api/cuenta-comun/activacion`) y una categoría puede ir «a cargo de la cuenta común».
- **Deuda con la cuenta común** (`MovimientoDeuda`/`DeudaMiembro` en `CuentaComun.cs`, tabla `deuda_cuenta`, `POST /api/cuenta-comun/deudas`): un **adelanto** (la cuenta paga por una persona) crea una deuda de ella por fondo (Hogar o ahorro); se salda con una **devolución** (ingreso) o una **compensación** (resta de lo que la cuenta le debe por reembolsos, sin mover dinero). La deuda no suma al efectivo y no entra en la liquidación entre personas. `Calcular`, `Extracto`, `ResumenMensual` y `MovimientosBanco` reciben los movimientos (parámetro opcional `deudas`) y deben seguir cuadrando entre sí: los tests `CuentaComunDeudaEscenariosTests` comprueban esa invariante. Si añades un cálculo que lea la cuenta, pásale las deudas. Las deudas **del ahorro** también cuentan en el ahorro disponible: `DatosAhorro.Gastado` y `UsosDeAhorroAsync` las incluyen (`AhorroCuenta.UsosPorDeudas`, con signo), y `HolguraAhorro` las usa; no las sumes aparte en un servicio o contarás dos veces. `DELETE /api/cuenta-comun/deudas/{id}` rechaza (409) lo que dejaría la deuda negativa o el ahorro sin respaldo.
- **Lista de la compra** (`ServicioListaCompra`, tablas `producto_compra` y `enlace_lista_compra`, `hogar.lista_compra_activa`, permiso `lista.compra`): la activa quien tiene `hogar.funciones`. Los enlaces QR llevan un token que caduca a los 7 días (en BD solo el hash) y se envía en la cabecera `X-Enlace-Lista`, permitida en CORS. **Las rutas públicas no tienen hogar en la petición**: el repositorio recibe el hogar de forma explícita y filtra con `IgnoreQueryFilters()`; cualquier método nuevo del puerto debe filtrar por ese `hogarId` y se prueba sin sesión en `ListaCompraHttpTests`. El QR lo genera el servidor con QRCoder (SVG sin `style` ni `script`, por la CSP) y el front refresca la lista cada 15 s. Si añades una consulta de hogares con una proyección de columnas (como `RepositorioHogares.ListarAsync`), incluye `ListaCompraActiva` o `/api/yo` no la devolverá.

### Base de datos: el esquema es SQL, no EF

El esquema y las políticas RLS viven en SQL y la **fuente de verdad es `src/Database/MiParte.Database/Estructura/`**: un fichero por tabla y por función en `01_Tablas`, `02_Funciones`, `03_Disparadores`, `04_ClavesForaneas` y `05_Seguridad` (formato `pg_dump`, ver su README). El proyecto `MiParte.Database` (DbUp) **converge** cualquier base con esa estructura al migrar: crea lo que falta, reemplaza funciones y políticas y añade columnas y restricciones ausentes, siempre de forma idempotente. Lo que no se puede inferir (borrar, renombrar, cambiar tipo, rellenar datos) va en scripts versionados que corren una vez, con journal en el esquema `miparte_migraciones`: `Scripts/Antes/` (antes de la estructura) y `Scripts/` (después), siempre idempotentes. Comandos: `migrar`, `pendientes`, `script` (un único SQL idempotente completo para pegar en Supabase o psql; sale a `Scripts/Generados/`, ignorado por git), `estructura` (vuelca una base real a `Estructura/` con `pg_dump`, para regenerar o canonicalizar tras tocar la base a mano) y `marcar-aplicadas`. `supabase/migrations/*.sql` es la historia anterior: ya no se aplica ni se amplía.
 EF Core **solo mapea** (no se generan migraciones EF; `MiParteDbContext` fija tablas con `ToTable`, `UseSnakeCaseNamingConvention`, y conversiones explícitas de enums a texto). Cambios de esquema = nuevo script en `Scripts/` + ajustar el mapeo en `MiParteDbContext`; 

### Multitenencia por hogar (dos capas)

1. RLS en Postgres (Supabase).
2. En el servicio, el rol de conexión puede saltarse la RLS, así que `MiParteDbContext` aplica un **global query filter por `hogar_id`** a todas las entidades (con `IHogarActual`). Toda entidad nueva con `HogarId` necesita su `HasQueryFilter`.

`HogarActualMiddleware` resuelve el hogar de cada petición autenticada: `sub` del JWT → `miembro.user_id` (activos). Esa lectura pasa por `CacheAcceso` (singleton, 30 s) y deja el miembro del hogar en `HttpContext.Items[ClaveMiembro]`, que `RequierePermiso` reutiliza sin consultar; el middleware invalida la caché tras cualquier escritura en `/api/miembros`, `/api/invitaciones` y `/api/hogar(es)`, así que un endpoint nuevo que cambie miembros desde otra ruta debe invalidarla también. Con varias instancias de la API, un permiso retirado tarda como mucho 30 s en surtir efecto en las demás. Un solo hogar se usa directamente; si hay varios exige la cabecera `X-Hogar-Id` (409 si falta, 403 si no pertenece, 400 si es inválida). Usa `IgnoreQueryFilters()` porque aún no hay hogar. `HogarActual` es scoped y se resuelve dentro del middleware para que `/health` funcione sin BD.

### Autenticación

`Core.Api` valida el JWT de Supabase Auth: emisor `{Supabase__Url}/auth/v1`, audiencia `authenticated`. Si se define `Supabase__JwtSecret` solo se aceptan HS256 (legado); si no, solo asimétricos vía JWKS del proyecto. Sin configuración ningún token es válido pero la API arranca. La cadena de conexión viene de `ConnectionStrings__Default` (variable de entorno, nunca en el repo); si falta no se registra la persistencia. Con Supabase usa el Session pooler (puerto 5432), no el Transaction pooler (6543), por las sentencias preparadas de Npgsql; en producción `SSL Mode=VerifyFull` con el CA del proyecto (ver `.env.example`). El `.env` (ignorado por git) lo lee `docker compose` pero no `dotnet run`.
