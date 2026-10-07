# miparte
Mi Parte, Tu Parte: app web de gastos e ingresos del hogar con reparto por porcentaje, partes o ingresos, liquidación mensual y chatbot con IA. Proyecto fin de máster.

## Estructura
```
src/
├── Web/            Blazor WebAssembly (PWA), front independiente
├── Core/
│   ├── Core.Api/       API: hogares, gastos, liquidación
│   ├── Core.Domain/    entidades y lógica de reparto, sin dependencias
│   ├── Core.Infrastructure/  EF Core (DbContext) sobre PostgreSQL
│   └── Core.Tests/     xUnit
├── Assistant/
│   └── Assistant.Api/  chatbot con tool calling
└── Contracts/      DTOs compartidos entre front y servicios
supabase/       migraciones y políticas RLS (Supabase/PostgreSQL)
docs/               decisiones y documentación
```

## Requisitos
.NET SDK 10.0 y, opcionalmente, Docker.

## Arranque
```
dotnet build
dotnet test
dotnet run --project src/Core/Core.Api         # GET /health
dotnet run --project src/Assistant/Assistant.Api
dotnet run --project src/Web
docker compose up --build                       # web :8080, core :5001, assistant :5002
```

## Base de datos
El esquema vive en `supabase/migrations` (SQL); EF Core solo lo mapea, no genera migraciones.
`Core.Api` lee la cadena de conexión de la variable de entorno `ConnectionStrings__Default`.

Tests contra PostgreSQL real (opcionales en local, siempre activos en CI):
```
psql -d miparte -v ON_ERROR_STOP=1 -f supabase/tests/auth_stub.sql || exit 1
for f in supabase/migrations/*.sql; do   # todas, en orden; se detiene en el primer error
  psql -d miparte -v ON_ERROR_STOP=1 -f "$f" || exit 1
done
MIPARTE_TEST_DB="Host=localhost;Database=miparte;Username=postgres;Password=..." dotnet test
```

## Autenticación
`Core.Api` valida el JWT de Supabase Auth (`MiParte.Auth`). Variables de entorno:

| Variable | Uso |
|---|---|
| `Supabase__Url` | URL del proyecto, p. ej. `https://xxxx.supabase.co`. Emisor esperado: `{Url}/auth/v1`; audiencia `authenticated`. |
| `Supabase__JwtSecret` | Solo si el proyecto aún usa el secreto HS256 legado. Si se define, solo se aceptan tokens HS256. Si no, solo asimétricos validados con el JWKS del proyecto. |
| `ConnectionStrings__Default` | Cadena de conexión a PostgreSQL. |

Sin configuración, ningún token es válido (la API arranca igualmente). El hogar de la petición se
deduce del usuario (`sub` → `miembro.user_id`); si pertenece a varios, se indica con la cabecera `X-Hogar-Id`.
`GET /api/yo` devuelve el usuario y el hogar resueltos.



## Configuración
Copia `.env.example` a `.env` (ignorado por git) y rellena los valores; `docker compose` lo lee de forma opcional
y pasa las variables a `core` y `assistant`. En local sin Docker, usa variables de entorno o `dotnet user-secrets`.

| Variable | Uso |
|---|---|
| `Supabase__Url` | URL del proyecto Supabase. |
| `Supabase__JwtSecret` | Opcional, solo secreto HS256 legado. |
| `ConnectionStrings__Default` | Cadena del pooler de Supabase (ver formato en `.env.example`). |
| `Cors__OrigenesPermitidos__0` | Origen del front permitido por CORS (aumentar el índice para más). |

### Configuración del front (`src/Web/wwwroot/appsettings.json`)
Blazor WASM lee este fichero en el navegador, así que **nunca** pongas secretos aquí. Rellena las claves
(vienen vacías en el repo):

| Clave | Valor |
|---|---|
| `Supabase:Url` | URL del proyecto Supabase. |
| `Supabase:AnonKey` | Anon key (clave pública) de Supabase: Project Settings > API. |
| `Api:CoreUrl` | URL base de Core.Api (p. ej. `http://localhost:5001`). |
| `Api:AssistantUrl` | URL base de Assistant.Api (p. ej. `http://localhost:5002`). |
