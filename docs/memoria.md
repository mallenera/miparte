# Memoria del proyecto: Mi parte, tu parte

Borrador de la memoria de fin de máster. Solo recoge lo que consta en el repositorio; lo que falta aparece marcado como **[PENDIENTE]**.

**[PENDIENTE]** Datos de presentación: título oficial del máster, autoría, tutor, fecha de entrega y formato exigido por el centro.

## 1. Resumen

«Mi parte, tu parte» es una aplicación web para llevar los gastos del hogar: quién paga, cómo se reparte cada gasto entre los miembros (individual, por porcentajes, por partes o a cuenta común), liquidación mensual con las transferencias mínimas para saldar las deudas, cuenta común con ahorro, y un chatbot con tool calling que consulta los datos del hogar en modo solo lectura.

## 2. Objetivo y alcance

El objetivo y el MVP están en `docs/diseno-y-decisiones.md` (secciones «Resumen y objetivo» y «Alcance funcional»). Las reglas de reparto están en la sección «Reglas de reparto de gastos» de ese mismo documento.

Los ingresos no se guardan: es una decisión de diseño (ver `supabase/migrations/20261007000000_perfiles_cuenta_comun_sin_ingresos.sql` y `CLAUDE.md`). El reparto se basa en perfiles, no en ingresos.

## 3. Arquitectura por capas

El sistema tiene cuatro piezas desplegables y un esquema de base de datos en SQL (fuente de verdad: `src/Database/MiParte.Database/Estructura/`).

| Pieza | Tecnología | Función |
|---|---|---|
| `src/Web` | Blazor WebAssembly (PWA) | Front. Modelos de pantalla en `Modelos/`, reglas de presentación en `Reparto/`. |
| `src/Core/Core.Api` | Minimal API .NET 10 | Traduce HTTP y delega en los servicios. Filtros transversales en `Seguridad/`. |
| `src/Core/Core.Application` | Biblioteca de .NET | Casos de uso `Servicio*`, puertos `IRepositorio*` y `Resultado<T>`. |
| `src/Core/Core.Domain` | Biblioteca de .NET sin dependencias | Entidades y reglas puras: reparto, liquidación, cuenta común, permisos. |
| `src/Core/Core.Infrastructure` | EF Core / Npgsql | `MiParteDbContext` (solo mapea) y repositorios que implementan los puertos. |
| `src/Core/Core.Mapeo` | Biblioteca de .NET | Mapeo entidad → DTO y parseo de consultas; lo comparten la API y el modo demo. |
| `src/Assistant/Assistant.Api` | Minimal API con tool calling | Chatbot de solo lectura; consulta Core.Api con el JWT de la persona. |
| `src/BuildingBlocks/Auth` | Biblioteca | Validación del JWT de Supabase Auth compartida por las APIs. |
| Base de datos | PostgreSQL (Supabase) | Esquema SQL y RLS, convergido por `MiParte.Database`. |

Reglas de dependencia entre capas de Core: `Core.Api` → `Core.Application` → `Core.Domain`; `Core.Infrastructure` implementa los puertos de `Core.Application`. Los endpoints no usan `DbContext` ni contienen reglas. El detalle, el flujo de una petición y la tabla de casos de uso están en `docs/arquitectura.md`.

Multitenencia en dos capas: RLS en PostgreSQL y un filtro global por `hogar_id` en `MiParteDbContext`.

## 4. Decisiones D1-D7

Estado tomado de la tabla de `docs/diseno-y-decisiones.md` (sección «Decisiones a tomar»). La columna de recomendación era la propuesta original del diseño.

| # | Decisión | Estado |
|---|---|---|
| D1 | Dónde alojar la app | **Decidida**: Render con Docker para Core.Api y Assistant.Api; Cloudflare Pages para el front. |
| D2 | Base de datos y login | **Decidida**: Supabase (PostgreSQL y Auth con correo). El acceso con Google queda oculto. |
| D3 | Reparto de los gastos del hijo | **La app lo soporta** (miembro a cargo con peso en un perfil). **[PENDIENTE]** El porcentaje concreto lo decide la pareja. |
| D4 | Reparto de la alimentación | **La app lo soporta** (el hijo puede contar como una parte). **[PENDIENTE]** Decidir si cuenta como parte. |
| D5 | Interfaz | **Superada**: Blazor WASM (PWA) + minimal API .NET + Supabase, en lugar de Streamlit. |
| D6 | Alcance de la IA | Chatbot hecho. Categorización automática **pendiente** (extra). |
| D7 | Modelo de lenguaje y dominio | **Decidido de momento**: Cloudflare Workers AI en el plan gratuito; Anthropic como alternativa de pago. Sin dominio propio: `miparte.pages.dev`. **[PENDIENTE]** Dominio propio, si se decide. |

## 5. Pruebas

Recuento de la última ejecución completa de `dotnet test` (ver `docs/plan-de-accion.md`, punto 23):

- Con PostgreSQL 18 real (`MIPARTE_TEST_DB`, esquema aplicado con `migrar --con-auth-stub`): 1187 correctas, 0 fallidas.
- Sin base de datos: 1136 correctas, 51 omitidas (las que requieren PostgreSQL), 0 fallidas.

| Qué se prueba | Dónde | Cómo |
|---|---|---|
| Reglas y cálculos | `src/Core/Core.Tests` | Puras, sin dependencias. |
| Casos de uso | `src/Core/Core.Tests/Servicio*Tests.cs` | Repositorio en memoria y reloj manual; sin HTTP ni BD. |
| Contrato HTTP y permisos | `src/Core/Core.Tests` | `WebApplicationFactory`, en memoria o PostgreSQL real. |
| Esquema, RLS y triggers | `EsquemaPostgresTests`, `src/Database/MiParte.Database.Tests` | Solo con PostgreSQL real. |
| Front: modelos, reparto y componentes | `src/Web.Tests` | xUnit y bUnit; `ApiFalsa` simula Core.Api. |
| Chatbot | `src/Assistant/Assistant.Tests` | Modelo falso. |

CI (`.github/workflows/ci.yml`) ejecuta siempre las pruebas con PostgreSQL 17, la misma versión mayor que Supabase.

**[PENDIENTE]** Pruebas de extremo a extremo en navegador real y con el proyecto Supabase de producción (ver `docs/plan-de-accion.md`, fase 1).

## 6. Líneas futuras (fase 4 del plan)

Estado según `docs/plan-de-accion.md`, fase 4:

- **Notificaciones push**: implementadas; pendiente de desplegar con claves VAPID reales y de cubrir cierre de liquidación y registro de pagos.
- **Recurrentes personales**: implementadas; pendiente de comprobar con una plantilla real.
- **Asistente**: streaming y chat en la cuenta demo, no iniciados.
- **Alta de gastos por chat con confirmación**: no iniciada.
- **Extras del diseño** (presupuestos con alertas, exportación a Excel o PDF, categorización automática): no iniciados.
- **Mejoras «posterior»** del diseño: no iniciadas (avisos por correo, reembolsos ligados a gastos, copia de seguridad, resumen anual, entre otras).

Pendientes de funciones ya hechas: periodo de viaje (sin fijar desde la API; la generación de recurrentes no respeta el periodo cerrado) y la marca v3b (revisión visual).

## 7. Limitaciones conocidas

Ver `docs/arquitectura.md`, sección «Desvíos y deuda conocida», y `docs/README.md`. Entre ellas: los literales de rol en `ServicioMiembros`, la doble comprobación del cierre de mes (servicio y trigger, a propósito) y el arranque en Render con plan gratuito, que duerme tras 15 minutos sin tráfico.

## 8. Reproducibilidad

Arranque local y despliegue: `README.md`, `docs/despliegue.md` y `docs/demo.md`.
