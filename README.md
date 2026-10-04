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
