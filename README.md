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
.NET SDK 8.0 y, opcionalmente, Docker.

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
psql -d miparte -f supabase/tests/auth_stub.sql -f supabase/migrations/20261004000000_esquema_inicial.sql
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


