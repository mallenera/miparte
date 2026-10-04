# miparte
Mi Parte, Tu Parte: app web de gastos e ingresos del hogar con reparto por porcentaje, partes o ingresos, liquidación mensual y chatbot con IA. Proyecto fin de máster.

## Estructura
```
src/
├── Web/            Blazor WebAssembly (PWA), front independiente
├── Core/
│   ├── Core.Api/       API: hogares, gastos, liquidación
│   ├── Core.Domain/    lógica de reparto, sin dependencias
│   └── Core.Tests/     xUnit
├── Assistant/
│   └── Assistant.Api/  chatbot con tool calling
└── Contracts/      DTOs compartidos entre front y servicios
db/                 migraciones y políticas RLS (Supabase/PostgreSQL)
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
