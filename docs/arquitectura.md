# Arquitectura del código

Cómo se organiza `src/` y qué reglas de dependencia hay que respetar. Los objetivos son tres: que la lógica de negocio no se mezcle
con el acceso a datos ni con la presentación, que cada pieza se pueda probar sin levantar HTTP ni base de datos, y que equipos
distintos puedan tocar áreas distintas sin pisarse.

## Capas de Core

```
Core.Api  ──►  Core.Application  ──►  Core.Domain
   │                  ▲
   └──►  Core.Infrastructure ──┘   (implementa los puertos de Application)
```

| Proyecto | Qué contiene | Qué NO contiene |
|---|---|---|
| `Core.Domain` | Entidades, reglas puras y cálculos: reparto, liquidación, resumen mensual, cuenta común, ahorro, permisos, reglas de campo (importe, concepto, nombre, ciclos de categorías, último administrador). Sin dependencias. | EF Core, ASP.NET, HTTP, DTOs de `Contracts`, nada que lea reloj o base de datos. |
| `Core.Application` | Casos de uso (`Servicio*`), sus puertos (`IRepositorio*`) y `Resultado<T>`. Orquesta: valida, lee por el puerto, aplica reglas del dominio, guarda y devuelve un resultado o un error de aplicación. Solo depende de `Core.Domain`. | EF Core, ASP.NET, `IResult`, DTOs HTTP, cadenas SQL, excepciones de Npgsql. |
| `Core.Infrastructure` | `MiParteDbContext`, los `Repositorio*` que implementan los puertos, consultas agrupadas (`HistoricoCuentaComun`), traducción de errores de PostgreSQL (unicidad, `MP409`), bloqueo consultivo por hogar y auditoría. | Reglas de negocio. |
| `Core.Api` | Minimal API: lee claims y query string, llama al servicio y traduce el resultado a un código HTTP. Permisos con `RequierePermiso`. | Consultas EF, `DbContext` en endpoints, reglas de negocio. |
| `Core.Mapeo` | Mapeo entidad → DTO de `Contracts` (`Dtos`) y parseo de parámetros de consulta (`ConsultasApi`). Lo comparten `Core.Api` y el modo demo del front, para que respondan igual. | Reglas de negocio ni acceso a datos. |

Las excepciones permitidas a «Api no usa `DbContext`» son infraestructura transversal: `HogarActualMiddleware`, `CacheAcceso`,
`RequierePermisoExtensions`, y los endpoints de hogares y auditoría, que aún no están migrados (ver el estado al final).

### Flujo de una petición

1. El endpoint (`Core.Api/<Área>/*Endpoints.cs`) comprueba el hogar, parsea el formato HTTP y construye un comando o `Datos*`.
2. El `Servicio*` valida, consulta lo que necesita a través del puerto y aplica las reglas de `Core.Domain`.
3. Si algo falla devuelve `Resultado<T>.Invalido | Conflicto | NoEncontrado | Prohibido`; `ApiComun.Respuesta` lo traduce a 400, 409,
   404 o 403. Los datos extra (`pendiente`, `disponible`, `falta`) viajan en `ErrorAplicacion.Extra`.
4. Si va bien, el endpoint mapea la entidad a su DTO.

### Casos de uso actuales

| Área | Servicio | Puerto | Reglas en el dominio |
|---|---|---|---|
| Resumen, liquidación y pagos | `ServicioLiquidacion` | `IRepositorioLiquidacion` | `ResumenMes`, `LiquidacionMes`, `Liquidacion` |
| Gastos | `ServicioGastos` | `IRepositorioGastos` | `RepartoGasto`, `AhorroCuenta`, `ReglasMovimiento` |
| Cuenta común y ahorro | `ServicioCuentaComun` | `IRepositorioCuentaComun` | `CuentaComun`, `HolguraAhorro`, `ReglasCuentaComun` |
| Miembros e invitaciones | `ServicioMiembros` | `IRepositorioMiembros` | `ReglasMiembros`, `TokenInvitacion`, `CatalogoPermisos` |
| Recurrentes | `ServicioRecurrentes` | `IRepositorioRecurrentes` (+ `IRepositorioGastos`) | `GeneracionRecurrente` |
| Categorías | `ServicioCategorias` | `IRepositorioCategorias` (+ `IRepositorioGastos`) | `ReglasCategoria` |
| Perfiles de reparto | `ServicioPerfiles` | `IRepositorioPerfiles` | `ReglasPerfil`, `TextoModoReparto` |
| Cierre de mes | `ServicioCierres` | `IRepositorioCierres` | — |

## Cómo añadir o cambiar una regla

1. **¿Es un cálculo o una regla que no necesita leer nada?** Va en `Core.Domain` como función estática o método de un record, con
   test unitario. Recibe datos ya leídos y devuelve un resultado o un mensaje de error.
2. **¿Necesita leer o escribir?** Añade el método al puerto (`IRepositorio*`) en `Core.Application`, impleméntalo en
   `Core.Infrastructure` y usa el puerto desde el servicio. Nunca pases `IQueryable` ni `DbContext` al servicio.
3. **Errores de base de datos que son decisiones de negocio** (mes cerrado por trigger, unicidad): tradúcelos en el repositorio a un
   valor (`bool`, enum) y que el servicio decida el mensaje y el tipo de error.
4. **Endpoint nuevo**: declara su permiso con `.RequierePermiso(...)`, mapea DTO ↔ entidad y delega. Actualiza `docs/api.md`.
5. **Registro**: el servicio y el repositorio se registran por fábrica en `Program.cs` y `DependencyInjection.AddRepositorios` (sin
   `DbContext` no hay repositorio, y la validación de DI de `/health` no debe fallar).

## Pruebas

| Qué se prueba | Dónde | Cómo |
|---|---|---|
| Reglas y cálculos | `Core.Tests/*Tests.cs` (`RepartoTests`, `LiquidacionTests`…) | Puras, sin dependencias. |
| Casos de uso | `Core.Tests/Servicio*Tests.cs` | Con un repositorio en memoria (`RepositorioGastosFalso`, fakes anidados) y un reloj manual; sin HTTP ni BD. |
| Contrato HTTP y permisos | `Core.Tests/*` con `WebApplicationFactory` | InMemory o PostgreSQL real (`MIPARTE_TEST_DB`). |
| Esquema, RLS, triggers | `EsquemaPostgresTests`, `MiParte.Database.Tests` | Solo con PostgreSQL real. |

Una regla nueva se prueba primero en su capa (dominio o servicio); los tests HTTP comprueban solo el cableado y el código de estado.

## Desvíos y deuda conocida

- La rama `IsRelational()` del proveedor InMemory sigue existiendo, pero aislada en `RepositorioMiembros.ConsumirInvitacionAsync`
  y `EjecutarExclusivoAsync`.
- `MiParteDbContext` mapea enums a texto con su propia función; `TextoModoReparto` del dominio es el mismo vocabulario.
- El cierre de mes se aplica dos veces a propósito: el servicio comprueba y los triggers de la base de datos rechazan la carrera
  (`MP409`). Son dos fuentes que deben coincidir.
- Los literales `"adulto"`, `"a_cargo"`, `"admin"` y `"miembro"` aparecen en `ServicioMiembros` además de en `Contracts`.

## Estado de la migración

| Área | Estado |
|---|---|
| Liquidación, gastos, cuenta común y ahorro, miembros, recurrentes, categorías, perfiles, cierres | **Migradas** a servicio + puerto. |
| Hogares (crear con semilla, eliminar, `/api/yo`) y auditoría (lectura) | **Pendiente**: aún usan `DbContext` en el endpoint. |
| Front (`src/Web`) | **En curso**: lógica de presentación extraída, `ModeloGastos` como patrón de modelo de pantalla y modo demo sobre los casos de uso reales (ver sección Front). Pendiente: aplicar el patrón al resto de vistas. |

## Front

El front (Blazor WASM) sigue el mismo principio: el componente pinta y reacciona a eventos; el estado y la orquestación van en un
modelo de pantalla y las reglas de presentación en clases C# sin dependencias de Blazor, con tests unitarios que no renderizan
componentes.

| Pieza | Dónde | Qué resuelve |
|---|---|---|
| Modelo de pantalla | `src/Web/Modelos/ModeloGastos` | Estado de la pestaña, carga de datos de referencia, periodo y filtros, formulario, y las operaciones contra la API (guardar, eliminar, recargar con control de respuestas tardías). La vista lo crea con `CoreApiClient` y enlaza sus propiedades. |
| `EntradaImporte` | `src/Web/Reparto` | Interpretación del importe escrito en un formulario (coma o punto, máx. 2 decimales, cero opcional). Una sola copia para Gastos, Recurrentes y Cuenta común. |
| `ListaGastos` | `src/Web/Reparto` | Filtros del cliente (categoría con subcategorías, quién paga), orden, totales y perfiles ofrecidos. |
| `GraficoGastos` | `src/Web/Reparto` | Agrupación en «Otras categorías» y porcentajes del Dashboard. |
| `AnalisisGastos`, `ArbolCategorias`, `EdicionPerfil` | `src/Web/Reparto` | Cálculo del Dashboard, árbol de categorías y edición de perfiles. |

Regla: si un `@code` necesita un `Sum`, un `Where` por una regla de negocio, un parseo o varias llamadas encadenadas a la API, va a
un modelo o a una clase de `Reparto/` con su test. La vista solo conserva lo que depende del DOM (foco, `ElementReference`).

### Modo demo: el servidor real en el navegador

`ServidorDemo` no reimplementa Core.Api. Enruta cada petición del `CoreApiClient` al mismo `Servicio*` de `Core.Application` que usa la
API, sobre repositorios en memoria (`AlmacenDemo` + `RepositoriosEnMemoria`, solo guardan y leen listas), y convierte el resultado a
DTO con `Core.Mapeo`, igual que los endpoints. Las reglas, los mensajes y los códigos de la demo son por tanto los del servidor; al
cambiar una regla en `Core.Domain` o `Core.Application` la demo la hereda sin tocarla.

`Core.Mapeo` (proyecto `src/Core/Core.Mapeo`) contiene el mapeo entidad → DTO (`Dtos`) y el parseo de parámetros de consulta
(`ConsultasApi`) que comparten `Core.Api` y la demo.

### Dependencias del front

`Web` referencia `Contracts`, `Core.Domain`, `Core.Application` y `Core.Mapeo`: la demo ejecuta los casos de uso reales, así que
el front compila contra ellos. Es una decisión deliberada (demo fiel a cambio de acoplar el WASM al backend de reglas); si se quisiera
un front que solo dependa de `Contracts`, la demo tendría que volver a ser un servidor de datos fijos.

### Pendiente en el front

- Aplicar el patrón de modelo de pantalla al resto de vistas: `VistaRecurrentes`, `VistaCuentaComun`, `VistaResumen`,
  `VistaDashboard` y `VistaConfiguracionHogar`.
- `CatalogoPermisos` sigue en `Core.Domain` y el front lo usa directamente (claves de permiso y etiquetas).
