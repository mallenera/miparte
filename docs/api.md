# API de Core.Api (referencia para el front Blazor WASM)

Referencia extraída del código de `src/Core/Core.Api` y de los DTOs de `src/Contracts`. Los DTOs se pueden reutilizar directamente desde el proyecto `Contracts` en el front.

Convenciones generales:

- JSON en camelCase (valores por defecto de minimal API). Fechas `DateOnly` como `"2026-10-04"`; `DateTimeOffset` en ISO 8601; Guid como cadena.
- Errores de validación: `{ "error": "mensaje en español" }` con el código HTTP indicado (400, 403, 404, 409). Algunos 404 y los 204 no llevan cuerpo.
- Errores no controlados: **500** `application/problem+json` (`ProblemDetails`, RFC 9457) `{ "type", "title": "Error interno del servidor.", "status": 500, "error": "Error interno del servidor.", "traceId" }` sin detalles internos (la traza queda en el log del servidor); conserva las cabeceras CORS. `error` repite el título para que el cliente lo lea igual que el resto de errores.
- Base URL local: `http://localhost:5001` (docker compose) o la de `launchSettings.json`.
- El chatbot tiene su propia API (`POST /api/chat`), documentada en [asistente.md](asistente.md).
- Además existe `GET /health` (sin autenticación): `{ "service": "core", "status": "ok" }`.

## 1. Autenticación, hogar actual y CORS

### Autenticación

Todos los endpoints `/api/*` exigen `Authorization: Bearer <access_token>`, el `access_token` de la sesión de Supabase Auth. Sin token válido: **401**. El `sub` del JWT identifica al usuario.

### Hogar actual: cabecera `X-Hogar-Id`

`HogarActualMiddleware` resuelve el hogar de cada petición autenticada a partir de `sub` → miembros activos con ese `user_id`:

| Situación | Resultado |
|---|---|
| Cabecera `X-Hogar-Id` con valor que no es un Guid | **400** `{ "error": ... }` |
| Cabecera con un hogar al que el usuario no pertenece (como miembro activo) | **403** |
| Sin cabecera y el usuario tiene 1 hogar | Se usa ese hogar |
| Sin cabecera y el usuario tiene varios hogares | **409** (hay que enviar la cabecera) |
| Sin cabecera y el usuario no tiene hogares | La petición continúa sin hogar (ver abajo) |

Si no hay hogar y el endpoint lo necesita, los de gastos, gastos recurrentes, resumen, liquidación y pagos responden **409** `{ "error": "No hay hogar seleccionado." }`. En el resto (miembros, invitaciones, categorías, perfiles) un usuario sin hogar no tiene datos accesibles; el front no debe llamarlos sin hogar.

**Endpoints `SinHogarActual`** (el middleware no resuelve hogar; no se exige ni se valida `X-Hogar-Id`):

- `GET /api/hogares`, `POST /api/hogares`, `GET /api/hogares/{id}`
- `GET /api/yo`
- `POST /api/invitaciones/aceptar`

El resto de rutas `/api/*` requieren hogar (cabecera o hogar único). Los datos están aislados por hogar (RLS y filtro global por `hogar_id`).

### CORS

Orígenes permitidos: variable de entorno `Cors__OrigenesPermitidos__0` (y `__1`, ...), equivalente a `Cors:OrigenesPermitidos` en configuración. Ejemplo: `Cors__OrigenesPermitidos__0=http://localhost:8080`. Sin configuración no se permite ningún origen cruzado. Cabeceras permitidas: `Authorization`, `Content-Type`, `X-Hogar-Id`. Métodos: GET, POST, PUT, PATCH, DELETE, OPTIONS. Las barras finales del origen se ignoran.

### Límites de peticiones (429)

Ventana deslizante de 1 minuto, **por usuario** (`sub` del JWT ya validado) y, sin sesión válida, **por IP**. Al superarla: **429** con `Retry-After` (segundos) y `{ "error": "Demasiadas peticiones..." }`. El front debe tratarlo como un error recuperable y esperar.

| Ámbito | Límite por defecto | Variable de entorno |
|---|---|---|
| Toda la API (salvo `/health`) | 120/min | `Limites__PeticionesPorMinuto` |
| Costosas: `POST /api/hogares`, `POST /api/invitaciones`, `POST /api/invitaciones/aceptar`, `POST /api/gastos-recurrentes/generar` | 10/min | `Limites__CostosasPorMinuto` |
| Tamaño del cuerpo de la petición (413 al superarlo) | 65536 bytes | `Limites__MaxCuerpoBytes` |

Detrás de un proxy inverso hay que configurar `ForwardedHeaders`; si no, todos los anónimos comparten la IP del proxy.

## 2. Flujo recomendado de arranque del front

1. Con sesión Supabase iniciada, llamar `GET /api/yo` (sin `X-Hogar-Id`).
2. Según `hogares.length`:
   - **0**: pantalla "Crea tu hogar o únete": `POST /api/hogares` (nombre del hogar y el tuyo) o pegar un token de invitación y `POST /api/invitaciones/aceptar`. Después repetir `GET /api/yo`.
   - **1**: seleccionarlo automáticamente (`hogarActual` viene relleno). Se puede omitir `X-Hogar-Id`, aunque conviene enviarlo siempre.
   - **N > 1**: mostrar selector, guardar la elección (p. ej. `localStorage`) y enviar `X-Hogar-Id` en todas las llamadas. Al arrancar, si la elección guardada ya no está en `hogares`, volver a pedir elegir. Con varios hogares y sin cabecera, `hogarActual` es `null`.
3. Una vez elegido el hogar, cargar `GET /api/miembros`, `/api/categorias`, `/api/perfiles` y el mes actual.
4. Un 409 en cualquier endpoint con hogar significa "falta `X-Hogar-Id`" (o no hay hogar): volver al paso 1.

## 3. Endpoints

Permisos: "miembro" = cualquier miembro activo del hogar (incluye admin). "admin" = rol `admin`. `{id}` siempre Guid. Los errores 401 (sin token) y los 400/403/409 de la cabecera `X-Hogar-Id` aplican a todo lo que no sea `SinHogarActual` y no se repiten en las tablas.

### 3.1 Hogares y usuario (SinHogarActual)

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/yo` | Usuario, sus hogares y hogar actual | (cabecera `X-Hogar-Id` opcional) | `YoResponse` 200 | 401 | autenticado |
| `GET /api/hogares` | Hogares del usuario (ordenados por nombre) | - | `HogarResumen[]` 200 | 401 | autenticado |
| `POST /api/hogares` | Crea hogar; el creador es admin y adulto; siembra perfiles y categorías | `CrearHogarRequest` | `HogarResumen` 201 | 400 nombres vacíos o > 100 caracteres; 409 máximo 10 hogares por usuario | autenticado |
| `GET /api/hogares/{id}` | Un hogar del usuario | - | `HogarResumen` 200 | 404 si no existe o no eres miembro | autenticado |

### 3.2 Miembros e invitaciones

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/miembros?incluirInactivos=` | Miembros activos del hogar, por nombre; con `incluirInactivos=true` también los desactivados (el historial los necesita para poner nombre a quien ya no está) | - | `MiembroDto[]` 200 | - | miembro |
| `POST /api/miembros` | Alta de persona sin cuenta (`tipo`: `adulto` o `a_cargo`) | `CrearMiembroRequest` | `MiembroDto` 201 | 400 nombre vacío/> 100, tipo inválido, `a_cargo` sin responsable adulto activo, adulto con responsable; 403 no admin | admin |
| `PUT /api/miembros/{id}` | Edita; campos `null` = sin cambios | `ActualizarMiembroRequest` | `MiembroDto` 200 | 400 nombre vacío, rol inválido, responsable no válido o miembro no `a_cargo`; 403; 404; 409 último admin vinculado, o responsable de miembros a cargo activos | admin; un miembro solo puede cambiar su propio `nombre` |
| `DELETE /api/miembros/{id}` | Borrado lógico (= PUT `activo=false`, mismas reglas) | - | `MiembroDto` 200 | igual que PUT | admin (un no admin recibe 403) |
| `POST /api/invitaciones` | Crea invitación. `miembroId` opcional: vincula a un miembro existente sin usuario; sin él, quien acepte entra como nuevo adulto | `CrearInvitacionRequest` (cuerpo opcional) | `InvitacionCreada` 201 | 403 no admin; 404 miembro; 409 miembro desactivado o ya vinculado | admin |
| `POST /api/invitaciones/aceptar` | Acepta con el token; devuelve el hogar al que se une (SinHogarActual) | `AceptarInvitacionRequest` | `HogarResumen` 200 | 400 token vacío, o nombre obligatorio si la invitación no apunta a un miembro (máx. 100); 404 token no válido; 409 usada, caducada, ya perteneces al hogar, miembro no disponible o ya vinculado | autenticado |

### 3.3 Categorías

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/categorias` | Lista (por nombre) | - | `CategoriaDto[]` 200 | - | miembro |
| `POST /api/categorias` | Crea (subcategorías vía `categoriaPadreId`). `aCargoCuentaComun` (opcional, false) marca que sus gastos van por defecto a cargo de la cuenta común: exige que el perfil sea el de cuenta común (si no se envía perfil, se asigna el de cuenta común del hogar). Un perfil por defecto de cuenta común implica el indicador (elegir solo ese perfil guarda la marca sin exigir la cuenta activada; pedir `aCargoCuentaComun` explícitamente sí la exige) | `GuardarCategoriaRequest` | `CategoriaDto` 201 | 400 nombre vacío/> 100, perfil o padre inexistente, `aCargoCuentaComun` con otro perfil o sin perfil de cuenta común en el hogar; 409 nombre duplicado en ese nivel o `aCargoCuentaComun` con la cuenta común sin activar | miembro |
| `PUT /api/categorias/{id}` | Reemplaza todos los campos (también `aCargoCuentaComun`: omitirlo lo deja en false, salvo que el perfil sea de cuenta común) | `GuardarCategoriaRequest` | `CategoriaDto` 200 | 400 (igual, más padre = ella misma o ciclo), 404, 409 duplicada o cuenta común sin activar al marcar una categoría que no lo estaba | miembro |
| `DELETE /api/categorias/{id}` | Elimina | - | 204 | 404; 409 con subcategorías o con gastos/recurrentes asociados | miembro |

### 3.4 Perfiles de reparto

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/perfiles` | Lista con detalle | - | `PerfilRepartoDto[]` 200 | - | miembro |
| `GET /api/perfiles/{id}` | Uno | - | `PerfilRepartoDto` 200 | 404 | miembro |
| `POST /api/perfiles` | Crea | `GuardarPerfilRequest` | `PerfilRepartoDto` 201 | 400 (ver reglas), 409 nombre duplicado | miembro |
| `PUT /api/perfiles/{id}` | Reemplaza nombre, modo y detalle | `GuardarPerfilRequest` | `PerfilRepartoDto` 200 | 400, 404, 409 | miembro |
| `DELETE /api/perfiles/{id}` | Elimina | - | 204 | 404; 409 en uso por categorías, gastos o recurrentes | miembro |

Validación de perfiles (400): nombre obligatorio (≤ 100); `modo` en `porcentaje|partes|cuenta_comun|individual`; `cuenta_comun` e `individual` no llevan detalle; `porcentaje` y `partes` exigen detalle sin miembros repetidos, solo adultos activos (los miembros a cargo no reparten), valores ≥ 0; `porcentaje` debe sumar 100 (tolerancia 0,0001); `partes` necesita alguna parte > 0.

### 3.5 Ingresos

Los ingresos del hogar **no se guardan** y no hay endpoints de ingresos. Los perfiles de reparto son cuatro modos: `individual` (100 % de quien paga), `porcentaje` (por porcentajes, suman 100), `partes` (por número de personas/partes) y `cuenta_comun`. Un gasto con perfil `cuenta_comun` lo asume la cuenta común: `GastoResponse.aCargoCuentaComun` es `true`, `repartos` va vacío, no genera deuda entre personas y no entra en la liquidación ni en el «pagado» del resumen (sí en `gastosTotales` y en el total por categoría). Su saldo, aportaciones y reembolsos están en §3.9.

### 3.6 Gastos

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/gastos?mes=YYYY-MM&categoriaId=&miembroId=&buscar=` | Lista con repartos; filtros opcionales y combinables. `miembroId` deja los gastos que paga ese miembro o en cuyo reparto asume un importe > 0; `buscar` es un texto contenido en el concepto (sin distinguir mayúsculas, máx. 200 caracteres; vacío = sin filtro) | - | `GastoResponse[]` 200 | 400 mes inválido o `buscar` demasiado largo; 409 | miembro |
| `GET /api/gastos/{id}` | Uno | - | `GastoResponse` 200 | 404; 409 | miembro |
| `POST /api/gastos` | Crea; el servidor calcula y guarda el reparto | `GastoRequest` | `GastoResponse` 201 | 400 importe, concepto, fecha, categoría o perfil inexistente, pagador no adulto activo, perfil sin valores para los adultos, sin adultos, `pagadoDesdeAhorro` con pagador; 409 sin hogar o ahorro disponible insuficiente (`{ error, disponible }`) | miembro |
| `PUT /api/gastos/{id}` | Edita y recalcula el reparto de este gasto | `GastoRequest` | `GastoResponse` 200 | 400, 404, 409 (también si el ahorro no cubre el gasto) | miembro |
| `DELETE /api/gastos/{id}` | Elimina gasto y reparto | - | 204 | 404; 409 | miembro |

### 3.7 Gastos recurrentes

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/gastos-recurrentes` | Lista (por `diaMes`) | - | `GastoRecurrenteResponse[]` 200 | 409 sin hogar | miembro |
| `GET /api/gastos-recurrentes/{id}` | Una plantilla | - | `GastoRecurrenteResponse` 200 | 404; 409 | miembro |
| `POST /api/gastos-recurrentes` | Crea plantilla | `GastoRecurrenteRequest` | `GastoRecurrenteResponse` 201 | 400 importe, `diaMes` fuera de 1-28, categoría o perfil inexistente, pagador no adulto activo; 409 | miembro |
| `PUT /api/gastos-recurrentes/{id}` | Edita (no toca gastos ya generados) | `GastoRecurrenteRequest` | `GastoRecurrenteResponse` 200 | 400, 404, 409 | miembro |
| `DELETE /api/gastos-recurrentes/{id}` | Borra | - | 204 | 404; 409 si ya tiene gastos generados (desactivar con PUT `activo=false`) | miembro |
| `POST /api/gastos-recurrentes/generar?mes=YYYY-MM` | Crea los gastos del mes de las plantillas activas sin gasto ese mes (idempotente, todo o nada). Sin cuerpo | - | `GenerarRecurrentesResponse` 200 | 400 mes inválido o plantilla no repartible; 409 sin hogar | miembro |

### 3.8 Resumen, liquidación y pagos

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/resumen?mes=YYYY-MM` | Gastos del mes, por miembro y por categoría, y (si el hogar tiene la cuenta común activada) el bloque `cuentaComun` con los saldos al final del mes. `mes` obligatorio | - | `ResumenMensualResponse` 200 | 400 mes inválido; 409 sin hogar | miembro |
| `GET /api/liquidacion?mes=YYYY-MM` | Saldos (ya descontando pagos), transferencias sugeridas y pagos registrados. `mes` obligatorio | - | `LiquidacionResponse` 200 | 400; 409 | miembro |
| `POST /api/pagos-liquidacion` | Registra un pago entre dos miembros del mes. El importe puede ser parcial o distinto del sugerido: cualquier valor > 0 (máx. 2 decimales) hasta la deuda pendiente del par; `GET /api/liquidacion` recalcula saldos y transferencias descontándolo, y se pueden registrar varios pagos hasta saldar | `CrearPagoLiquidacionRequest` | `PagoLiquidacionDto` 201 | 400 mes no es día 1, mismo miembro, importe (≤ 0 o más de 2 decimales), miembros fuera del hogar; 409 importe mayor que la deuda pendiente (`{ error, pendiente }`) o sin hogar | miembro |
| `DELETE /api/pagos-liquidacion/{id}` | Elimina un pago | - | 204 | 404; 409 sin hogar | miembro |

### 3.9 Cuenta común

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `PUT /api/cuenta-comun/activacion` | **Solo admin.** Activa o desactiva la cuenta común del hogar (`{ "activa": true }`). Desactivarla no borra datos: solo bloquea las escrituras de la cuenta y los gastos a su cargo (409) hasta reactivarla. Los hogares que ya tenían datos de cuenta común quedaron activados por la migración | `ActivarCuentaComunRequest` | `{ "activa": bool }` 200 | 403 no es admin; 409 sin hogar | admin |
| `GET /api/cuenta-comun?mes=YYYY-MM` | Estado al final del mes (incluye `activa` y `partes`, «su parte» por persona; se devuelve aunque la cuenta no esté activada): aportado (mes y acumulado), gastado, saldo, reembolsos pendientes por miembro, efectivo, ahorro (del mes, acumulado, ingresado aparte, retirado, gastado y disponible), aportaciones configuradas, y reembolsos, ingresos aparte y retiradas de ahorro del mes. `mes` obligatorio | - | `CuentaComunResponse` 200 | 400 mes inválido; 409 sin hogar | miembro |
| `PUT /api/cuenta-comun/aportaciones` | Fija lo que aporta un adulto desde un mes (`desde` = día 1); si ya había una de ese mes la sustituye. Importe 0 = deja de aportar. `ahorro` (opcional, 0 por defecto) es la parte del importe que se aparta: el resto queda para gastos | `FijarAportacionRequest` | `AportacionCuentaDto` 200 | 400 `desde` no es día 1, importe o ahorro negativos o con más de 2 decimales, ahorro mayor que el importe, miembro no adulto activo; 409 sin hogar o rebaja del ahorro que dejaría sin respaldo lo ya retirado o gastado desde el ahorro (`{ error, falta }`); 409 también con la cuenta común sin activar | miembro |
| `POST /api/cuenta-comun/reembolsos` | Registra un pago de la cuenta a quien adelantó gastos cargados a ella (`fecha` opcional, por defecto hoy UTC) | `CrearReembolsoRequest` | `ReembolsoCuentaDto` 201 | 400 importe o miembro fuera del hogar; 409 importe mayor que lo pendiente (`{ error, pendiente }`) o sin hogar | miembro |
| `DELETE /api/cuenta-comun/reembolsos/{id}` | Elimina un reembolso | - | 204 | 404; 409 sin hogar | miembro |
| `POST /api/cuenta-comun/depositos-ahorro` | Registra dinero que entra al ahorro fuera de la aportación mensual: ahorro inicial, lotería... (`fecha` opcional, por defecto hoy UTC) | `CrearDepositoAhorroRequest` | `DepositoAhorroDto` 201 | 400 importe o miembro fuera del hogar; 409 sin hogar | miembro |
| `DELETE /api/cuenta-comun/depositos-ahorro/{id}` | Elimina un ingreso de ahorro | - | 204 | 404; 409 sin hogar o si dejaría sin respaldo lo ya retirado o gastado desde el ahorro (`{ error, falta }`) | miembro |
| `POST /api/cuenta-comun/retiradas-ahorro` | Registra dinero que sale del ahorro (`fecha` opcional, por defecto hoy UTC) | `CrearRetiradaAhorroRequest` | `RetiradaAhorroDto` 201 | 400 importe o miembro fuera del hogar; 409 importe mayor que el ahorro disponible (`{ error, disponible }`) o sin hogar | miembro |
| `DELETE /api/cuenta-comun/retiradas-ahorro/{id}` | Elimina una retirada de ahorro | - | 204 | 404; 409 sin hogar | miembro |

El importe de un mes es el de la aportación con `desde` más reciente que no pase de ese mes. **Saldo** = aportado acumulado − ahorro acumulado − gastos cargados a la cuenta hasta el mes (negativo si no los cubre): el ahorro no cuenta para gastos. **Ahorro acumulado** = parte de ahorro de las aportaciones + ingresos aparte (depósitos) hasta el mes. **Ahorro disponible** = ahorro acumulado − retiradas − gastos pagados desde el ahorro hasta el mes; una retirada baja solo el ahorro, nunca el saldo ni el efectivo. Un gasto se paga desde el ahorro con `GastoRequest.pagadoDesdeAhorro = true`: exige `pagadoPor = null` y el perfil `cuenta_comun` (400 en otro caso), baja el ahorro disponible y no el saldo ni el efectivo, no deja pendiente ni deuda entre personas, y no puede superar el ahorro disponible (409). Una retirada tampoco puede superar lo ahorrado hasta el mes de su fecha menos lo ya retirado o gastado desde el ahorro (409); el ahorro disponible nunca queda en negativo: rebajar el ahorro de una aportación (`PUT /aportaciones`) o eliminar un ingreso se rechaza con 409 si dejaría sin respaldo lo ya retirado o gastado desde el ahorro (se comprueba en el último mes con retiradas o gastos desde el ahorro). **Pendiente** de un miembro = gastos de la cuenta que adelantó − reembolsos recibidos. **Efectivo** = saldo + pendientes, sin contar el ahorro (el dinero de gastos de la cuenta no baja hasta reembolsar un gasto que adelantó una persona). Un gasto también puede pagarlo **directamente la cuenta**: `GastoRequest.pagadoPor = null`, solo con perfil `cuenta_comun` (400 en otro caso); no genera pendiente y baja el efectivo. `GastoResponse.pagadoPor` es `null` en esos gastos. Ejemplo: aportan 600 + 400, Ana adelanta una hipoteca de 900 a cargo de la cuenta → saldo 100, pendiente de Ana 900, efectivo 1000; tras reembolsarle 400 → saldo 100, pendiente 500, efectivo 600. Con ahorro: si Ana aporta 600 y aparta 150, y Beto aporta 400 sin ahorro, el saldo es 850 y el ahorro disponible 150; retirar 100 lo deja en 50 y el saldo sigue en 850. Con un ingreso aparte de 1000 (ahorro inicial) el disponible es 1050 y pagar una fianza de 300 desde el ahorro lo deja en 750, con el saldo en 850.

### 3.10 Auditoría

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/auditoria?entidad=&entidadId=&limite=&hasta=&despuesDeId=` | Historial de cambios del hogar, del más reciente al más antiguo (empates por `id`). `limite` 1-200 (50 por defecto). Se pagina con el `cuando` y el `id` del último evento recibido, enviados como `hasta` (ISO 8601) y `despuesDeId`; con solo `hasta` se perderían los eventos de un mismo guardado, que comparten `cuando` | - | `EventoAuditoriaDto[]` 200 | 400 límite fuera de 1-200; 403 no admin; 409 sin hogar | admin |

Cada cambio de gasto, gasto recurrente, pago de liquidación, aportación, reembolso, ingreso y retirada de ahorro, perfil, categoría, miembro e invitación queda registrado **en la misma transacción** que el cambio (no puede haber cambio sin rastro). `accion`: `crear` (solo `despues`), `editar` (solo los campos que cambian, en `antes` y `despues`), `borrar` (solo `antes`), `vincular` (un usuario queda ligado a un miembro al aceptar una invitación) y `usar` (invitación aceptada). El reparto de un gasto (`repartos`) y el detalle de un perfil (`detalle`) van dentro de su evento. Crear un hogar es un único evento (no se detalla la semilla). Nunca se registran el token de una invitación ni su hash. `autor` es el nombre del miembro vinculado al usuario; `usuarioId` sigue siendo válido aunque ese miembro se desactive.

Total: 48 endpoints de negocio (4 hogares/yo, 6 miembros/invitaciones, 4 categorías, 5 perfiles, 5 gastos, 6 recurrentes, 4 resumen/liquidación/pagos, 8 cuenta común, 1 auditoría) más `GET /health`.

## 4. Ejemplos JSON

### Hogares

`GET /api/yo` con un solo hogar:

```json
{
  "userId": "7c1f6d3e-0b8a-4f55-9a53-1d2f6c1b9e10",
  "hogares": [ { "id": "a1b2c3d4-0000-4000-8000-000000000001", "nombre": "Casa Pérez" } ],
  "hogarActual": { "id": "a1b2c3d4-0000-4000-8000-000000000001", "nombre": "Casa Pérez" }
}
```

`POST /api/hogares`:

```json
{ "nombreHogar": "Casa Pérez", "nombreMiembro": "Ana" }
```
```json
{ "id": "a1b2c3d4-0000-4000-8000-000000000001", "nombre": "Casa Pérez" }
```

### Miembros

`POST /api/miembros` (un hijo a cargo):

```json
{ "nombre": "Leo", "tipo": "a_cargo", "responsableId": "b0000000-0000-4000-8000-0000000000aa" }
```
```json
{
  "id": "b0000000-0000-4000-8000-0000000000bb", "nombre": "Leo", "tipo": "a_cargo",
  "responsableId": "b0000000-0000-4000-8000-0000000000aa", "activo": true, "rol": "miembro", "vinculado": false, "esYo": false
}
```

`PUT /api/miembros/{id}` (solo renombra; el resto ausente o `null`):

```json
{ "nombre": "Leonardo" }
```

### Invitaciones

`POST /api/invitaciones` (sin cuerpo o `{}` para un nuevo adulto; con `miembroId` para vincular a uno existente):

```json
{ "miembroId": "b0000000-0000-4000-8000-0000000000cc" }
```
```json
{ "id": "c0000000-0000-4000-8000-000000000001", "token": "Qm9n...base64url...", "caducaEn": "2026-10-11T09:30:00+00:00" }
```

`POST /api/invitaciones/aceptar` (`nombre` solo si la invitación no apunta a un miembro):

```json
{ "token": "Qm9n...base64url...", "nombre": "Luis" }
```
```json
{ "id": "a1b2c3d4-0000-4000-8000-000000000001", "nombre": "Casa Pérez" }
```

### Categorías

`POST /api/categorias`:

```json
{ "nombre": "Supermercado", "categoriaPadreId": "d0000000-0000-4000-8000-000000000002", "perfilRepartoId": "e0000000-0000-4000-8000-000000000001" }
```
```json
{ "id": "d0000000-0000-4000-8000-000000000009", "nombre": "Supermercado", "categoriaPadreId": "d0000000-0000-4000-8000-000000000002", "perfilRepartoId": "e0000000-0000-4000-8000-000000000001", "aCargoCuentaComun": false }
```

Categoría a cargo de la cuenta común (con la cuenta activada; el servidor asigna el perfil de cuenta común si no se envía): `{ "nombre": "Comunidad", "categoriaPadreId": null, "perfilRepartoId": null, "aCargoCuentaComun": true }`.

### Perfiles

`POST /api/perfiles` (modo porcentaje; suma 100):

```json
{
  "nombre": "60/40",
  "modo": "porcentaje",
  "detalle": [
    { "miembroId": "b0000000-0000-4000-8000-0000000000aa", "valor": 60 },
    { "miembroId": "b0000000-0000-4000-8000-0000000000dd", "valor": 40 }
  ]
}
```
```json
{
  "id": "e0000000-0000-4000-8000-000000000007", "nombre": "60/40", "modo": "porcentaje",
  "detalle": [
    { "miembroId": "b0000000-0000-4000-8000-0000000000aa", "valor": 60 },
    { "miembroId": "b0000000-0000-4000-8000-0000000000dd", "valor": 40 }
  ]
}
```

### Gastos

`POST /api/gastos`:

```json
{
  "fecha": "2026-10-03", "importe": 100.00,
  "categoriaId": "d0000000-0000-4000-8000-000000000002",
  "pagadoPor": "b0000000-0000-4000-8000-0000000000aa",
  "perfilRepartoId": "e0000000-0000-4000-8000-000000000007",
  "concepto": "Compra semanal"
}
```
```json
{
  "id": "90000000-0000-4000-8000-000000000001", "fecha": "2026-10-03", "importe": 100.00,
  "categoriaId": "d0000000-0000-4000-8000-000000000002",
  "pagadoPor": "b0000000-0000-4000-8000-0000000000aa",
  "perfilRepartoId": "e0000000-0000-4000-8000-000000000007",
  "concepto": "Compra semanal", "gastoRecurrenteId": null, "aCargoCuentaComun": false,
  "repartos": [
    { "miembroId": "b0000000-0000-4000-8000-0000000000aa", "importeAsumido": 60.00 },
    { "miembroId": "b0000000-0000-4000-8000-0000000000dd", "importeAsumido": 40.00 }
  ]
}
```

### Gastos recurrentes

`POST /api/gastos-recurrentes`:

```json
{
  "importe": 850.00, "categoriaId": "d0000000-0000-4000-8000-000000000001",
  "pagadoPor": "b0000000-0000-4000-8000-0000000000aa",
  "perfilRepartoId": "e0000000-0000-4000-8000-000000000002",
  "diaMes": 5, "concepto": "Alquiler", "activo": true
}
```
```json
{
  "id": "80000000-0000-4000-8000-000000000001", "importe": 850.00,
  "categoriaId": "d0000000-0000-4000-8000-000000000001",
  "pagadoPor": "b0000000-0000-4000-8000-0000000000aa",
  "perfilRepartoId": "e0000000-0000-4000-8000-000000000002",
  "diaMes": 5, "concepto": "Alquiler", "activo": true
}
```

`POST /api/gastos-recurrentes/generar?mes=2026-10` (response):

```json
{ "mes": "2026-10", "creados": 2, "yaExistentes": 1 }
```

### Resumen

`GET /api/resumen?mes=2026-10`:

```json
{
  "mes": "2026-10", "gastosTotales": 1200.00,
  "miembros": [
    { "miembroId": "b0000000-0000-4000-8000-0000000000aa", "nombre": "Ana", "pagado": 1000.00, "asumido": 700.00, "debeCuentaComun": 0.00 },
    { "miembroId": "b0000000-0000-4000-8000-0000000000dd", "nombre": "Luis", "pagado": 200.00, "asumido": 500.00, "debeCuentaComun": 0.00 }
  ],
  "categorias": [
    {
      "categoriaId": "d0000000-0000-4000-8000-000000000001", "nombre": "Hipoteca/Alquiler", "total": 850.00,
      "porMiembro": [
        { "miembroId": "b0000000-0000-4000-8000-0000000000aa", "importe": 500.00 },
        { "miembroId": "b0000000-0000-4000-8000-0000000000dd", "importe": 350.00 }
      ]
    }
  ]
}
```

`cuentaComun` (solo con la cuenta activada, si no es `null`): `{ "aportadoMes": 500.00, "gastadoMes": 120.00, "saldo": 280.00, "efectivo": 400.00, "pendiente": 120.00, "ahorroMes": 100.00, "ahorroDisponible": 100.00 }`, con los saldos acumulados al final del mes (`aportadoMes`, `gastadoMes` y `ahorroMes` son del propio mes; `pendiente` es el total que la cuenta debe a quienes adelantaron gastos).

`pagado` incluye lo que el miembro adelantó para gastos de la cuenta común; `debeCuentaComun` es lo que la cuenta le debe aún (acumulado hasta el fin del mes, descontados los reembolsos).

### Liquidación

`GET /api/liquidacion?mes=2026-10` (saldo positivo = le deben, negativo = debe):

```json
{
  "mes": "2026-10",
  "saldos": [
    { "miembroId": "b0000000-0000-4000-8000-0000000000aa", "nombre": "Ana", "saldo": 300.00 },
    { "miembroId": "b0000000-0000-4000-8000-0000000000dd", "nombre": "Luis", "saldo": -300.00 }
  ],
  "transferencias": [
    { "de": "b0000000-0000-4000-8000-0000000000dd", "a": "b0000000-0000-4000-8000-0000000000aa", "importe": 300.00 }
  ],
  "pagos": []
}
```

### Pagos de liquidación

`POST /api/pagos-liquidacion` (`mes` = día 1; `fecha` opcional, por defecto hoy UTC):

```json
{
  "mes": "2026-10-01",
  "deMiembroId": "b0000000-0000-4000-8000-0000000000dd",
  "aMiembroId": "b0000000-0000-4000-8000-0000000000aa",
  "importe": 150.00, "fecha": "2026-10-20", "concepto": "Bizum"
}
```
```json
{
  "id": "70000000-0000-4000-8000-000000000001", "mes": "2026-10-01",
  "deMiembroId": "b0000000-0000-4000-8000-0000000000dd",
  "aMiembroId": "b0000000-0000-4000-8000-0000000000aa",
  "importe": 150.00, "fecha": "2026-10-20", "concepto": "Bizum"
}
```

**Activación por hogar.** `hogar.cuenta_comun_activa` (false por defecto; `GET /api/cuenta-comun` la devuelve como `activa`). Con la cuenta sin activar responden 409 (`"La cuenta común no está activada en este hogar…"`): `PUT /aportaciones`, `POST` de reembolsos, ingresos y retiradas de ahorro, `POST`/`PUT /api/gastos` que queden a cargo de la cuenta (perfil `cuenta_comun`), `POST /api/gastos-recurrentes/generar` si alguna plantilla pendiente usa ese perfil, y marcar una categoría con `aCargoCuentaComun`. Los `DELETE` y las consultas siguen funcionando. El front oculta el perfil de cuenta común y la opción de pagar con la cuenta mientras no esté activa.

**Su parte por persona** (`partes`, `PartePersonaDto[]`): para cada miembro con aportaciones o ingresos de ahorro suyos hasta el mes, `aportado` (para gastos, sin ahorro), `ahorrado` (parte de ahorro de sus aportaciones más sus ingresos aparte), `porcentajeGastos`/`porcentajeAhorro` (su peso sobre el total, 2 decimales), `parteSaldo` (saldo de gastos repartido en proporción a lo aportado), `parteAhorro` (ahorro disponible repartido en proporción a lo ahorrado) y `pendiente` (lo que la cuenta le debe por gastos que adelantó). El céntimo sobrante lo absorbe el último miembro (por id): las partes suman siempre el saldo y el ahorro disponible. Un saldo negativo se reparte igual (parte de cada uno en el descubierto). Un ingreso de ahorro se atribuye a quien lo registró. Ejemplo: Ana aporta 600 (200 de ahorro) y Beto 400 (sin ahorro) más 100 de ahorro ingresado por Beto; hay un gasto de 101. Saldo 699 → 349,50 cada uno (ambos pusieron 400 para gastos); ahorro disponible 300 → Ana 200 y Beto 100.

Error típico si se pasa de la deuda (409): `{ "error": "El importe supera la deuda pendiente entre ambos miembros (150.00).", "pendiente": 150.00 }`.

## 5. Reglas de negocio útiles para la UI

- **Hogar de un adulto**: el reparto funciona (el creador recibe el 100 %), pero no hay liquidación que mostrar: el único saldo es 0 y no hay transferencias. La UI puede ocultar la pantalla de liquidación con un solo adulto activo.
- **Importes**: mayores que 0, máximo 2 decimales y hasta 9.999.999.999,99. Más decimales dan 400. Conceptos de hasta 200 caracteres; nombres de hogar, miembro, categoría y perfil hasta 100.
- **Meses**: parámetro `mes` con formato estricto `YYYY-MM` (`2026-10`); otro formato da 400. En `POST /api/pagos-liquidacion` el campo `mes` es una fecha que debe ser día 1 (`2026-10-01`). Las respuestas devuelven `mes` como `YYYY-MM`, salvo `PagoLiquidacionDto.mes`, que es fecha.
- **Reparto guardado e inmutable**: cada gasto guarda sus `repartos` al crearse. Cambiar un perfil después no recalcula gastos antiguos. Solo `PUT /api/gastos/{id}` recalcula ese gasto. Editar una plantilla recurrente tampoco toca los gastos ya generados. El último miembro absorbe el céntimo sobrante.
- **Quién interviene**: pagador y reparto solo se asignan a adultos activos. Los miembros `a_cargo` necesitan un responsable adulto activo.
- **Recurrentes**: `diaMes` entre 1 y 28. `generar` es idempotente por mes y plantilla; una plantilla con gastos generados no se borra, se desactiva.
- **Semilla al crear hogar**: 4 perfiles (`Cuenta común`, `Por partes`, `Porcentaje fijo`, `Individual`; el creador queda con 1 parte en "Por partes" y 100 % en "Porcentaje fijo") y 6 categorías (Hipoteca/Alquiler, Alimentación, Suministros, Gastos varios de casa e Hijo con "Por partes"; Ocio personal con "Individual"). El creador es admin y adulto.
- **Quién soy**: `MiembroDto.esYo` es `true` en el miembro vinculado al usuario autenticado (en `GET /api/miembros`, `PUT` y `DELETE`; el alta siempre devuelve `false`). El front lo usa para saber si mostrar los controles de admin y cuál es "su" miembro.
- **Roles**: solo admin crea miembros e invitaciones, modifica a otros y lee el historial (`/api/auditoria`); el hogar siempre conserva al menos un admin activo y vinculado (409). Un adulto responsable de miembros a cargo activos no se puede desactivar (409). **Decisión**: todo lo demás (gastos, pagos, reembolsos, aportaciones, perfiles, categorías, recurrentes) lo puede hacer cualquier miembro, admin o no; la responsabilidad se cubre con la auditoría, no con permisos.
- **Invitaciones**: el `token` en claro solo se devuelve en la respuesta de `POST /api/invitaciones` (en base de datos solo se guarda su hash): mostrarlo o copiarlo en ese momento. Caduca a los 7 días (`caducaEn`) y es de un solo uso. Quien acepta no necesita hogar previo. Si la invitación apunta a un `miembroId`, ese miembro queda vinculado al usuario; si no, hay que enviar `nombre` y entra como adulto con rol `miembro`.
- **Eliminaciones**: categorías y perfiles en uso devuelven 409; los miembros se desactivan, no se borran.

## 6. Nota de cambio en /api/yo

El campo `hogarId` de `GET /api/yo` ya no existe: ahora es `hogarActual` (objeto `{ id, nombre }` o `null`), y la lista completa va en `hogares`. En el código del front hay que usar `hogarActual.id` donde antes se leía `hogarId`.
