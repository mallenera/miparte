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

Todos los endpoints `/api/*` salvo `POST /api/contacto` (§3.13) exigen `Authorization: Bearer <access_token>`, el `access_token` de la sesión de Supabase Auth. Sin token válido: **401**. El `sub` del JWT identifica al usuario.

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
- `POST /api/contacto` (además, anónimo)

El resto de rutas `/api/*` requieren hogar (cabecera o hogar único). Los datos están aislados por hogar (RLS y filtro global por `hogar_id`).

### CORS

Orígenes permitidos: variable de entorno `Cors__OrigenesPermitidos__0` (y `__1`, ...), equivalente a `Cors:OrigenesPermitidos` en configuración. Ejemplo: `Cors__OrigenesPermitidos__0=http://localhost:8080`. Sin configuración no se permite ningún origen cruzado. Cabeceras permitidas: `Authorization`, `Content-Type`, `X-Hogar-Id` y `X-Enlace-Lista` (el token de los enlaces QR de la lista de la compra, §3.14). Métodos: GET, POST, PUT, PATCH, DELETE, OPTIONS. Las barras finales del origen se ignoran.

### Límites de peticiones (429)

Ventana deslizante de 1 minuto, **por usuario** (`sub` del JWT ya validado) y, sin sesión válida, **por IP**. Al superarla: **429** con `Retry-After` (segundos) y `{ "error": "Demasiadas peticiones..." }`. El front lo trata como un error recuperable: en las lecturas (GET) espera lo que indica `Retry-After` (con tope de 20 s) y reintenta hasta 2 veces mostrando un aviso; si sigue limitado, muestra el mensaje de la API. Las escrituras no se reintentan.

| Ámbito | Límite por defecto | Variable de entorno |
|---|---|---|
| Toda la API (salvo `/health`) | 120/min | `Limites__PeticionesPorMinuto` |
| Costosas: `POST /api/hogares`, `POST /api/invitaciones`, `POST /api/invitaciones/aceptar`, `POST /api/gastos-recurrentes/generar` | 10/min | `Limites__CostosasPorMinuto` |
| Escrituras públicas de la lista de la compra: `POST /api/lista-compra/publica/productos` y `PUT /api/lista-compra/publica/productos/{id}` (comparten el tope de las costosas; sin sesión cuentan por IP) | 10/min | `Limites__CostosasPorMinuto` |
| Formulario de contacto: `POST /api/contacto` (ventana de 1 hora; `Retry-After` indica 60 s, el front no reintenta) | 5/hora | `Limites__ContactoPorHora` |
| Tamaño del cuerpo de la petición (413 al superarlo) | 65536 bytes | `Limites__MaxCuerpoBytes` |

Detrás de un proxy inverso hay que configurar `ForwardedHeaders`; si no, todos los anónimos comparten la IP del proxy.

## 2. Flujo recomendado de arranque del front

1. Con sesión Supabase iniciada, llamar `GET /api/yo` (sin `X-Hogar-Id`).
2. Según `hogares.length`:
   - **0**: pantalla "Crea tu hogar o únete": `POST /api/hogares` (nombre del hogar y el tuyo) o pegar un token de invitación y `POST /api/invitaciones/aceptar`. Después repetir `GET /api/yo`.
   - **1**: seleccionarlo automáticamente (`hogarActual` viene relleno). Se puede omitir `X-Hogar-Id`, aunque conviene enviarlo siempre.
   - **N > 1**: mostrar selector, guardar la elección (p. ej. `localStorage`) y enviar `X-Hogar-Id` en todas las llamadas. Al arrancar, si la elección guardada ya no está en `hogares`, volver a pedir elegir. Con varios hogares y sin cabecera, `hogarActual` es `null`.
3. Una vez elegido el hogar, cargar `GET /api/miembros`, `/api/categorias`, `/api/perfiles` y el mes actual.
4. Un 409 de resolución del hogar (falta `X-Hogar-Id` o no hay hogar) obliga a volver al paso 1. Los 409 de negocio, como el de mes cerrado, se tratan según el endpoint: muestran su `error`, no cambian de hogar.

## 3. Endpoints

Permisos: "miembro" = cualquier miembro activo del hogar (incluye admin). "admin" = rol `admin`. "permiso `x`" = el miembro lo tiene entre sus permisos efectivos (un admin sin lista propia los tiene todos; ver «Permisos por miembro» en las notas). `{id}` siempre Guid. Los errores 401 (sin token) y los 400/403/409 de la cabecera `X-Hogar-Id` aplican a todo lo que no sea `SinHogarActual` y no se repiten en las tablas.

### 3.1 Hogares y usuario (SinHogarActual)

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/yo` | Usuario, sus hogares y hogar actual | (cabecera `X-Hogar-Id` opcional) | `YoResponse` 200 | 401 | autenticado |
| `GET /api/hogares` | Hogares del usuario (ordenados por nombre) | - | `HogarResumen[]` 200 | 401 | autenticado |
| `POST /api/hogares` | Crea hogar; el creador es admin y adulto; siembra perfiles y categorías. `tipo` opcional (`hogar` por defecto; ver «Tipo de grupo» en §4) | `CrearHogarRequest` | `HogarResumen` 201 | 400 nombres vacíos o > 100 caracteres, o `tipo` no válido; 409 máximo 10 hogares por usuario | autenticado |
| `DELETE /api/hogar?nombre=` | **Irreversible.** Elimina el hogar actual (cabecera `X-Hogar-Id`) y, en cascada, todos sus datos: miembros, gastos y recurrentes, cuenta común y ahorro, cierres de mes, categorías, perfiles, invitaciones y el historial. `nombre` debe ser el nombre exacto del hogar (distingue mayúsculas) | - | 204 | 400 nombre ausente o distinto; 403 sin el permiso `hogar.eliminar`; 409 sin hogar | permiso `hogar.eliminar` |
| `GET /api/hogares/{id}` | Un hogar del usuario | - | `HogarResumen` 200 | 404 si no existe o no eres miembro | autenticado |

### 3.2 Miembros e invitaciones

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/miembros?incluirInactivos=` | Miembros activos del hogar, por nombre; con `incluirInactivos=true` también los desactivados (el historial los necesita para poner nombre a quien ya no está) | - | `MiembroDto[]` 200 | - | miembro |
| `POST /api/miembros` | Alta de persona sin cuenta (`tipo`: `adulto` o `a_cargo`) | `CrearMiembroRequest` | `MiembroDto` 201 | 400 nombre vacío/> 100, tipo inválido, `a_cargo` sin responsable adulto activo, adulto con responsable; 403 sin el permiso `miembros.gestionar` | permiso `miembros.gestionar` |
| `PUT /api/miembros/{id}` | Edita; campos `null` = sin cambios; `permisos` fija la lista completa de permisos (ver «Permisos por miembro») | `ActualizarMiembroRequest` | `MiembroDto` 200 | 400 nombre vacío, rol inválido, rol `admin` para alguien sin cuenta, responsable no válido o miembro no `a_cargo`, o `permisos` con una clave desconocida; 403; 404; 409 último admin vinculado, responsable de miembros a cargo activos, o `permisos` de alguien sin cuenta, o si nadie más podría cambiar permisos | `miembros.gestionar` para nombre, estado y responsable de otros; `permisos.gestionar` para `rol` y `permisos`; cualquiera puede cambiar su propio `nombre` |
| `DELETE /api/miembros/{id}` | Borrado lógico (= PUT `activo=false`, mismas reglas) | - | `MiembroDto` 200 | igual que PUT | permiso `miembros.gestionar` |
| `POST /api/invitaciones` | Crea invitación. `miembroId` opcional: vincula a un **adulto sin cuenta** del hogar; sin él, quien acepte entra como nuevo adulto. Es la única vía para vincular un miembro a un usuario (no hay endpoint que reciba un id de usuario) | `CrearInvitacionRequest` (cuerpo opcional) | `InvitacionCreada` 201 | 403 sin el permiso `invitaciones.crear`, o con `miembroId` sin `miembros.gestionar`; 404 miembro; 409 miembro desactivado, ya vinculado o que no es adulto (`a_cargo`) | permiso `invitaciones.crear`; con `miembroId`, además `miembros.gestionar` |
| `POST /api/invitaciones/aceptar` | Acepta con el token; devuelve el hogar al que se une (SinHogarActual) | `AceptarInvitacionRequest` | `HogarResumen` 200 | 400 token vacío, o nombre obligatorio si la invitación no apunta a un miembro (máx. 100); 404 token no válido; 409 usada, caducada, ya perteneces al hogar, miembro no disponible o ya vinculado | autenticado |
| `GET /api/enlaces-grupo` | Lista los enlaces de grupo del hogar (activos, revocados y caducados), sin token | - | `EnlaceGrupoDto[]` 200 | 403 sin el permiso; 409 sin hogar | permiso `enlaces.gestionar` |
| `POST /api/enlaces-grupo` | Crea un enlace reutilizable. `caducaEn` opcional (sin él, no caduca). El token en claro solo se devuelve aquí | `CrearEnlaceGrupoRequest` (cuerpo opcional) | `EnlaceGrupoCreado` 201 | 400 `caducaEn` ya pasada; 403 sin el permiso; 409 sin hogar | permiso `enlaces.gestionar` |
| `DELETE /api/enlaces-grupo/{id}` | Revoca el enlace: deja de admitir uniones, pero se conserva (`activo` = false). Revocar uno ya revocado no hace nada | - | 204 | 403 sin el permiso; 404 no es de este hogar; 409 sin hogar | permiso `enlaces.gestionar` |
| `POST /api/enlaces-grupo/unirse` | Une al usuario al hogar del enlace (SinHogarActual) como adulto nuevo con `nombre`. No vincula miembros existentes (un `miembroId` en el cuerpo se ignora) | `UnirseEnlaceGrupoRequest` | `HogarResumen` 200 | 400 token vacío o nombre obligatorio (máx. 100); 404 token no válido; 409 enlace revocado o caducado, o ya perteneces al hogar | autenticado (sin permiso especial) |

### 3.3 Categorías

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/categorias` | Lista (por nombre) | - | `CategoriaDto[]` 200 | - | miembro |
| `POST /api/categorias` | Crea (subcategorías vía `categoriaPadreId`). `aCargoCuentaComun` (opcional, false) marca que sus gastos van por defecto a cargo de la cuenta común: exige que el perfil sea el de cuenta común (si no se envía perfil, se asigna el de cuenta común del hogar). Un perfil por defecto de cuenta común implica el indicador (elegir solo ese perfil guarda la marca sin exigir la cuenta activada; pedir `aCargoCuentaComun` explícitamente sí la exige) | `GuardarCategoriaRequest` | `CategoriaDto` 201 | 400 nombre vacío/> 100, perfil o padre inexistente, `aCargoCuentaComun` con otro perfil o sin perfil de cuenta común en el hogar; 409 nombre duplicado en ese nivel o `aCargoCuentaComun` con la cuenta común sin activar | permiso `categorias.gestionar` |
| `PUT /api/categorias/{id}` | Reemplaza todos los campos (también `aCargoCuentaComun`: omitirlo lo deja en false, salvo que el perfil sea de cuenta común) | `GuardarCategoriaRequest` | `CategoriaDto` 200 | 400 (igual, más padre = ella misma o ciclo), 404, 409 duplicada o cuenta común sin activar al marcar una categoría que no lo estaba | permiso `categorias.gestionar` |
| `DELETE /api/categorias/{id}?moverA={id}` | Elimina. Si tiene gastos o recurrentes hay que indicar `moverA`: pasan a esa categoría en el mismo guardado | - | 204 | 400 destino inexistente o ella misma; 404; 409 con subcategorías, o con gastos sin `moverA` (el cuerpo lleva `enUso: true`) | permiso `categorias.gestionar` |

### 3.4 Perfiles de reparto

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/perfiles` | Lista con detalle | - | `PerfilRepartoDto[]` 200 | - | miembro |
| `GET /api/perfiles/{id}` | Uno | - | `PerfilRepartoDto` 200 | 404 | miembro |
| `POST /api/perfiles` | Crea | `GuardarPerfilRequest` | `PerfilRepartoDto` 201 | 400 (ver reglas), 409 nombre duplicado | permiso `perfiles.gestionar` |
| `PUT /api/perfiles/{id}` | Reemplaza nombre, modo y detalle | `GuardarPerfilRequest` | `PerfilRepartoDto` 200 | 400, 404, 409 | permiso `perfiles.gestionar` |
| `DELETE /api/perfiles/{id}?moverA={id}` | Elimina. Si está en uso hay que indicar `moverA`: sus categorías, gastos y recurrentes pasan a ese perfil (debe ser de cuenta común si el borrado lo es, y no serlo si no) | - | 204 | 400 destino inexistente, el mismo o de otra naturaleza; 404; 409 en uso sin `moverA` (el cuerpo lleva `enUso: true`) | permiso `perfiles.gestionar` |

`predeterminadoGastos` (opcional, `false` por defecto; en `PerfilRepartoDto`, `GuardarPerfilRequest` y en las categorías) marca la casilla «Predeterminado para gastos». Es un booleano sin más reglas en la API **como máximo uno por hogar** en cada caso: un segundo perfil o una segunda categoría marcados dan 409 con un mensaje que lo dice (el índice único parcial de la base lo garantiza). Ninguno marcado no es error; el asistente avisa de que falta el predeterminado. El asistente la usa para proponer el alta de un gasto desde el chat (`docs/asistente.md`); los endpoints de negocio no la leen. `PUT` la reemplaza junto con el resto de campos, así que el cliente debe enviarla también al editar.

Validación de perfiles (400): nombre obligatorio (≤ 100); `modo` en `porcentaje|partes|cuenta_comun|cuenta_comun_ahorro|individual`; `cuenta_comun`, `cuenta_comun_ahorro` e `individual` no llevan detalle; `porcentaje` y `partes` exigen detalle sin miembros repetidos, adultos activos o miembros a cargo con responsable adulto activo, valores ≥ 0; `porcentaje` debe sumar 100 (tolerancia 0,0001); `partes` necesita alguna parte > 0.

### 3.5 Ingresos

Los ingresos personales del hogar **no se guardan**. Los ingresos externos a la cuenta común sí se registran, mediante los endpoints de §3.9 (`/api/cuenta-comun/ingresos`). Los perfiles de reparto son cinco modos: `individual` (100 % de quien paga), `porcentaje` (por porcentajes, suman 100), `partes` (por número de personas/partes), `cuenta_comun` y `cuenta_comun_ahorro` («Cuenta común (ahorro)»: funciona igual que `cuenta_comun`, pero el gasto se paga desde el ahorro; ver §3.9). Un gasto con perfil `cuenta_comun` lo asume la cuenta común: `GastoResponse.aCargoCuentaComun` es `true`, `repartos` va vacío, no genera deuda entre personas y no entra en la liquidación ni en el «pagado» del resumen (sí en `gastosTotales` y en el total por categoría). Su saldo, aportaciones y reembolsos están en §3.9.

### 3.6 Gastos

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/gastos?mes=YYYY-MM&desde=YYYY-MM-DD&hasta=YYYY-MM-DD&categoriaId=&miembroId=&buscar=` | Lista con repartos. **Hay que indicar `mes`, `desde` o `hasta` (al menos uno)**: sin ninguno se devolvería todo el histórico y responde 400. El resto de filtros son opcionales y combinables. `desde` y `hasta` acotan por fecha del gasto, ambos días incluidos (cada uno puede ir solo); el dashboard los usa para pedir un periodo cualquiera; `miembroId` deja los gastos que paga ese miembro o en cuyo reparto asume un importe > 0; `buscar` es un texto contenido en el concepto (sin distinguir mayúsculas, máx. 200 caracteres; vacío = sin filtro) | - | `GastoResponse[]` 200 | 400 sin `mes`, `desde` ni `hasta`, mes inválido, `desde`/`hasta` con formato distinto de YYYY-MM-DD o `desde` posterior a `hasta`, o `buscar` demasiado largo; 409 | miembro |
| `GET /api/gastos/{id}` | Uno | - | `GastoResponse` 200 | 404; 409 | miembro |
| `POST /api/gastos` | Crea; el servidor calcula y guarda el reparto | `GastoRequest` | `GastoResponse` 201 | 400 importe, concepto, fecha, categoría o perfil inexistente, pagador no adulto activo, perfil sin valores para los adultos, sin adultos, `pagadoDesdeAhorro` con un perfil que no es de cuenta común; 409 sin hogar o ahorro disponible insuficiente (`{ error, disponible }`) | permiso `gastos.crear` |
| `PUT /api/gastos/{id}` | Edita y recalcula el reparto de este gasto | `GastoRequest` | `GastoResponse` 200 | 400, 404, 409 (también si el ahorro no cubre el gasto, o si el gasto ya forma parte de una liquidación por ciclos cerrada: «Este gasto ya forma parte de una liquidación cerrada…») | permiso `gastos.editar` |
| `DELETE /api/gastos/{id}` | Elimina gasto y reparto | - | 204 | 404; 409 (mes cerrado, o gasto ya liquidado por un ciclo cerrado) | permiso `gastos.borrar` |

**Miembros a cargo.** Un miembro a cargo (`tipo` = `a_cargo`) puede tener peso en un perfil (`porcentaje` o `partes`). Su parte cuenta en el reparto, pero la asume su responsable: `repartos` lista un importe por adulto que ya incluye esas partes, y `repartosACargo` (`{ miembroId, responsableId, importeAsumido }`) desglosa cada parte a cargo para mostrarla como «Lorien (a cargo de Nuria)». Un miembro a cargo sin responsable adulto activo, o un perfil de cuenta común, da 400 o no genera desglose. Los gastos guardados no cambian al cambiar el peso o el responsable del miembro a cargo.

**Gastos personales**: con `GastoRequest.personal = true` el gasto es solo de `pagadoPor` (obligatorio, adulto activo; 400 sin pagador o con `pagadoDesdeAhorro`). El servidor guarda un único reparto del 100 % para el pagador, ignora el modo del perfil (que debe existir igualmente) y nunca lo marca `aCargoCuentaComun`. `GastoResponse.personal` lo devuelve. No genera deuda: queda fuera de `/api/liquidacion` y de `gastosTotales`, `pagado`, `asumido` y totales por categoría del resumen, que lo informa aparte (§3.8). Se ve en `GET /api/gastos` como cualquier otro gasto y cualquier miembro del hogar puede verlo y editarlo; al editarlo con `personal = false` vuelve a ser un gasto del hogar y se reparte según el perfil.

### 3.7 Gastos recurrentes

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/gastos-recurrentes` | Lista (por `diaMes`) | - | `GastoRecurrenteResponse[]` 200 | 409 sin hogar | miembro |
| `GET /api/gastos-recurrentes/{id}` | Una plantilla | - | `GastoRecurrenteResponse` 200 | 404; 409 | miembro |
| `POST /api/gastos-recurrentes` | Crea plantilla. Con `personal: true` cada gasto generado es personal: lo asume íntegro quien paga, sin reparto ni cuenta común, y queda fuera de la liquidación | `GastoRecurrenteRequest` | `GastoRecurrenteResponse` 201 | 400 importe, `diaMes` fuera de 1-28, categoría o perfil inexistente, pagador no adulto activo; 409 sin hogar o con las recurrentes desactivadas en el hogar | permiso `recurrentes.gestionar` |
| `PUT /api/gastos-recurrentes/{id}` | Edita (no toca gastos ya generados) | `GastoRecurrenteRequest` | `GastoRecurrenteResponse` 200 | 400, 404, 409 | permiso `recurrentes.gestionar` |
| `DELETE /api/gastos-recurrentes/{id}` | Borra | - | 204 | 404; 409 si ya tiene gastos generados (desactivar con PUT `activo=false`) | permiso `recurrentes.gestionar` |
| `PUT /api/hogar/recurrentes` | **Permiso `hogar.funciones`.** Activa o desactiva los gastos recurrentes del hogar (`{ "activas": true }`). Desactivarlos no borra plantillas: quedan pausadas. Con ellos desactivados, `POST /api/gastos-recurrentes` y `POST /api/gastos-recurrentes/generar` responden 409 (`"Las recurrentes no están activadas en este hogar."`); las consultas, la edición y el borrado siguen funcionando | `ActivarRecurrentesRequest` | `{ "activas": bool }` 200 | 403 sin el permiso; 409 sin hogar | permiso `hogar.funciones` |
| `POST /api/gastos-recurrentes/generar?mes=YYYY-MM` | Crea los gastos del mes de las plantillas activas sin gasto ese mes (idempotente, todo o nada). Sin cuerpo | - | `GenerarRecurrentesResponse` 200 | 400 mes inválido o plantilla no repartible; 409 sin hogar o con las recurrentes desactivadas en el hogar | permiso `recurrentes.gestionar` |

### 3.8 Resumen, liquidación y pagos

Los gastos personales (§3.6) no entran en la liquidación ni en `gastosTotales`, `miembros[].pagado`, `miembros[].asumido` ni `categorias`; el resumen los da aparte en `gastosPersonales` (total del mes) y `miembros[].personal` (por quien los pagó).

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/resumen?mes=YYYY-MM` | Gastos del mes, por miembro y por categoría, y (si el hogar tiene la cuenta común activada) el bloque `cuentaComun` con los saldos al final del mes. `mes` obligatorio | - | `ResumenMensualResponse` 200 | 400 mes inválido; 409 sin hogar | miembro |
| `GET /api/liquidacion?mes=YYYY-MM` | Saldos (ya descontando pagos), transferencias sugeridas y pagos registrados. `mes` obligatorio | - | `LiquidacionResponse` 200 | 400; 409 | miembro |
| `GET /api/resumen-liquidacion?mes=YYYY-MM` | El resumen y la liquidación del mes en una sola respuesta: `{ resumen, liquidacion }`, con exactamente lo que devuelven `/api/resumen` y `/api/liquidacion`. Es lo que usa la pestaña Resumen (lee una vez los gastos y los miembros del mes en lugar de dos). `mes` obligatorio | - | `ResumenYLiquidacionResponse` 200 | 400; 409 | miembro |
| `POST /api/pagos-liquidacion` | Registra un pago entre dos miembros del mes (409 en modo `ciclos`, ver más abajo). El importe puede ser parcial o distinto del sugerido: cualquier valor > 0 (máx. 2 decimales) hasta la deuda pendiente del par; `GET /api/liquidacion` recalcula saldos y transferencias descontándolo, y se pueden registrar varios pagos hasta saldar | `CrearPagoLiquidacionRequest` | `PagoLiquidacionDto` 201 | 400 mes no es día 1, mismo miembro, importe (≤ 0 o más de 2 decimales), miembros fuera del hogar; 409 importe mayor que la deuda pendiente (`{ error, pendiente }`) o sin hogar | permiso `pagos.registrar` |
| `DELETE /api/pagos-liquidacion/{id}` | Elimina un pago | - | 204 | 404; 409 sin hogar | permiso `pagos.registrar` |
| `PUT /api/hogar/liquidacion` | **Permiso `hogar.funciones`.** Cambia el modo de liquidación del hogar: `{ "modo": "mensual" \| "ciclos" }` (`mensual` por defecto; los hogares existentes quedan en `mensual`). No altera gastos ni ciclos ya cerrados. Pasar a `mensual` es **409** si el hogar tiene algún ciclo cerrado («No se puede volver a liquidación mensual mientras haya ciclos cerrados…»): esos gastos ya están liquidados y el cálculo mensual no los contaría. Pasar a `ciclos` siempre se permite. Queda en el historial como `liquidacionModo` | `ModoLiquidacionRequest` | `{ "modo": "..." }` 200 | 400 modo inválido; 403 sin el permiso; 409 sin hogar o pasar a `mensual` con ciclos cerrados | permiso `hogar.funciones` |
| `POST /api/liquidacion/ciclos` | Cierra el **ciclo actual** (solo con `liquidacionModo` = `ciclos`): liquida los gastos pendientes —los que no tienen ciclo— y guarda el ciclo con su `total` (suma de sus importes) y `desdeGastoFecha` / `hastaFecha` (fechas extremas de esos gastos). Los marca como liquidados; **no bloquea los gastos**, que siguen consultables. El siguiente ciclo empieza con el primer gasto que se registre después. Sin cuerpo | - | `CicloLiquidacionDto` 201 | 409 sin hogar; hogar en modo `mensual`; «No hay gastos sin liquidar» si no hay pendientes; «Otra persona acaba de cerrar la liquidación…» si otro cierre se hace a la vez (todo o nada) | permiso `pagos.registrar` |
| `GET /api/liquidacion/ciclos` | Ciclos cerrados del hogar, del más reciente al más antiguo: `numero`, `cerradoEn`, `cerradoPor` (miembro), `autor` (su nombre), `desdeGastoFecha`, `hastaFecha`, `total` | - | `CicloLiquidacionDto[]` 200 | 409 sin hogar | miembro |

**Liquidación por ciclos.** Con `liquidacionModo` = `ciclos`, la liquidación no es mensual: `GET /api/liquidacion?mes=` (y el bloque `liquidacion` de `/api/resumen-liquidacion`) devuelve los saldos y transferencias de **los gastos pendientes desde el último cierre**, sin pagos (el parámetro `mes` se valida pero no filtra). Como los pagos se hacen fuera de la app, `POST /api/pagos-liquidacion` responde **409** con `"En la liquidación por ciclos no se registran pagos…"`. Cerrar un ciclo sin gastos pendientes responde 409, y cerrarlo en modo mensual también. `GET /api/resumen` no cambia: sigue siendo el resumen del mes. Cambiar el modo a `ciclos` con gastos anteriores aún sin ciclo hace que entren en el primer cierre. Un gasto ya liquidado por un ciclo cerrado no se edita ni se borra (409), y el hogar no vuelve a `mensual` mientras tenga ciclos cerrados (409).

### 3.9 Cuenta común

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `PUT /api/hogar/aportaciones` | **Permiso `hogar.funciones`.** Fija con qué frecuencia aportan los miembros a la cuenta común (`{ "frecuencia": "unica" \| "mensual" \| "bimestral" \| "trimestral" \| "semestral" \| "anual" }`). Por defecto `mensual`, como en los hogares existentes. Cada aportación hereda la frecuencia del hogar: una periódica cobra su importe una vez por periodo, en el primer mes (bimestral desde enero cobra en enero y cubre enero y febrero); una `unica` cobra una vez, en el mes de su aportación, y no tiene periodo. Cambiarla recalcula también los cobros pasados | `FijarFrecuenciaAportacionRequest` | `FrecuenciaAportacionResponse` 200 | 400 frecuencia vacía o desconocida; 403 sin el permiso; 409 sin hogar o si con la nueva frecuencia el ahorro ya retirado o gastado quedaría sin respaldo (`{ error, falta }`) | permiso `hogar.funciones` |
| `PUT /api/cuenta-comun/activacion` | **Permiso `hogar.funciones`.** Activa o desactiva la cuenta común del hogar (`{ "activa": true }`). Desactivarla no borra datos: solo bloquea las escrituras de la cuenta y los gastos a su cargo (409) hasta reactivarla. Los hogares que ya tenían datos de cuenta común quedaron activados por la migración | `ActivarCuentaComunRequest` | `{ "activa": bool }` 200 | 403 sin el permiso; 409 sin hogar | permiso `hogar.funciones` |
| `GET /api/cuenta-comun?mes=YYYY-MM` | Estado al final del mes (incluye `activa` y `partes`, «su parte» por persona; se devuelve aunque la cuenta no esté activada): aportado (mes y acumulado), gastado, saldo, reembolsos pendientes por miembro y fondo (`pendientes[].desdeAhorro`), lo que cada persona debe a la cuenta (`deudas`, `DeudaCuentaDto`; no suma al efectivo) y los movimientos de deuda del mes uno a uno (`movimientosDeuda`, `MovimientoDeudaCuentaDto` con su `id`, para mostrarlos y borrarlos), efectivo, ahorro (del mes, acumulado, ingresado aparte, retirado, gastado y disponible), aportaciones configuradas, y reembolsos, ingresos aparte y retiradas de ahorro del mes. Incluye `extracto` (`ExtractoCuentaComunDto`: efectivo al inicio y al final del mes con sus movimientos, como un extracto bancario; el inicio coincide con el final del mes anterior), `saldoInicial` (`{ fecha, importe }` o null) y `resumen` (`ResumenCuentaMesDto`: filas `hogar`, `ahorro` y `total`, cada una con `inicial` (disponible del mes anterior), `ingresos` (aportaciones del mes a la fila), `ingresosSueltos` (ingresos aparte del mes: a Hogar, o depósitos de ahorro), `gastos` (lo que sale de la fila; en ahorro, incluye las retiradas y los gastos desde el ahorro) y `reembolsos` (lo devuelto a quien adelantó gastos, solo hogar) y `disponible`, que pasa al mes siguiente, y `pendiente` (lo que la cuenta aún debe al cierre por los adelantos de esa fila: Hogar, o Ahorro). El `disponible` de hogar coincide con `efectivo` y el de ahorro con `ahorroDisponible`). `mes` obligatorio | - | `CuentaComunResponse` 200 | 400 mes inválido; 409 sin hogar | miembro |
| `GET /api/cuenta-comun/movimientos?desde=YYYY-MM-DD&hasta=YYYY-MM-DD` | Extracto de la cuenta línea a línea entre dos fechas (ambas incluidas), para cuadrarlo con el banco. Supone el ahorro en la misma cuenta que el dinero de gastos: un único saldo. Líneas (`tipo`): `saldo_inicial`, `aportacion` (una por persona y mes de cobro, fechada el día 1, importe completo con su parte `ahorro`), `ingreso`, `deposito_ahorro` (entradas, importe positivo); `gasto` y `gasto_ahorro` (solo los que paga la cuenta directamente, `pagadoPor` nulo), `reembolso`, `retirada_ahorro` (salidas, importe negativo; desde `Scripts/20261025210000_retiradas_ahorro_a_gastos.sql` solo quedan retiradas de meses cerrados). `gasto_ahorro_adelantado`: gasto del ahorro que adelantó una persona (`miembroId`), con `importe` 0 porque no mueve el banco y `ahorro` negativo (lo que pasa del ahorro al efectivo hasta reembolsarlo); `saldo` no cambia. Los gastos de la cuenta común que adelanta una persona no salen: el banco solo ve su reembolso. Cada línea lleva `saldo` tras ella, acumulado desde el primer movimiento; `saldoAnterior` es el del día previo a `desde`. El saldo final se reparte en `efectivoFinal` (mismo cálculo que `efectivo` de `GET /api/cuenta-comun`: con `hasta` en el último día del mes coinciden) y `ahorroFinal` (el `ahorroDisponible`). Orden cronológico; en un mismo día, primero las entradas. Se devuelve aunque la cuenta no esté activada | - | `MovimientosCuentaComunResponse` 200 | 400 falta una fecha, formato inválido o `desde` posterior a `hasta`; 409 sin hogar | miembro |
| `PUT /api/cuenta-comun/saldo-inicial` | Fija (o sustituye) el dinero que había en la cuenta para gastos a una fecha, p. ej. el saldo del banco al empezar a usar la app. Cuenta como un ingreso de esa fecha: los meses anteriores no lo ven. Solo afecta al saldo y al efectivo de gastos, no al ahorro | `FijarSaldoInicialRequest` (`{ fecha, importe }`) | `SaldoInicialDto` 200 | 400 importe negativo, con más de 2 decimales o demasiado grande; 409 sin hogar o cuenta común sin activar | permiso `cuenta.movimientos` |
| `DELETE /api/cuenta-comun/saldo-inicial` | Quita el saldo inicial | - | 204 | 404 si no hay saldo inicial; 409 sin hogar | permiso `cuenta.movimientos` |
| `PUT /api/cuenta-comun/ahorro/activacion` | **Permiso `hogar.funciones`.** Activa o desactiva el ahorro del hogar (`{ "activo": true }`). Desactivarlo no borra datos: solo bloquea las escrituras del ahorro (409). Exige la cuenta común activada | `ActivarAhorroRequest` | `{ "activo": bool }` 200 | 403 sin el permiso; 409 sin hogar o cuenta común sin activar | permiso `hogar.funciones` |
| `PUT /api/cuenta-comun/aportaciones` | Fija lo que aporta un adulto desde un mes (`desde` = día 1); si ya había una de ese mes la sustituye. Importe 0 = deja de aportar. `ahorro` (opcional, 0 por defecto) es la parte del importe que se aparta: el resto queda para gastos | `FijarAportacionRequest` | `AportacionCuentaDto` 200 | 400 `desde` no es día 1, importe o ahorro negativos o con más de 2 decimales, ahorro mayor que el importe, miembro no adulto activo; 409 sin hogar o rebaja del ahorro que dejaría sin respaldo lo ya retirado o gastado desde el ahorro (`{ error, falta }`); 409 también con la cuenta común sin activar | permiso `cuenta.movimientos` |
| `POST /api/cuenta-comun/reembolsos` | Registra un pago de la cuenta a quien adelantó gastos cargados a ella (`fecha` opcional, por defecto hoy UTC). `desdeAhorro` (opcional, `false` por defecto) indica de qué fondo sale: `true` paga los adelantos hechos desde el ahorro, `false` los de Hogar. Un reembolso es de un solo fondo y se compara con lo pendiente de ese fondo; la respuesta (`ReembolsoCuentaDto`) incluye `desdeAhorro`. Los reembolsos anteriores a este cambio son de Hogar | `CrearReembolsoRequest` | `ReembolsoCuentaDto` 201 | 400 importe o miembro fuera del hogar; 409 importe mayor que lo pendiente (`{ error, pendiente }`) o sin hogar | permiso `cuenta.movimientos` |
| `DELETE /api/cuenta-comun/reembolsos/{id}` | Elimina un reembolso | - | 204 | 404; 409 sin hogar | permiso `cuenta.movimientos` |
| `POST /api/cuenta-comun/deudas` | Registra un movimiento de la deuda de una persona con la cuenta común. `tipo`: `adelanto` (la cuenta paga por ella: baja el efectivo, o el ahorro si `desdeAhorro`, y ella le debe ese importe), `devolucion` (ella ingresa para pagar: entra en la cuenta, o en el ahorro) o `compensacion` (la cuenta resta lo que le debe por reembolsos pendientes; no mueve dinero). `fecha` opcional, por defecto hoy UTC. Una devolución o compensación no puede superar lo que la persona debe (409 con `{ error, debe }`); una compensación no puede superar lo pendiente de reembolsarle (409 con `{ error, pendiente }`); un adelanto del ahorro no puede superar el ahorro disponible (409). La deuda aparece en `CuentaComunResponse.deudas` (`DeudaCuentaDto`: `miembroId`, `nombre`, `importe`, `desdeAhorro`) y en el extracto como `adelanto_persona`, `adelanto_persona_ahorro`, `devolucion_deuda`, `devolucion_deuda_ahorro` y `compensacion_deuda` | `CrearDeudaCuentaRequest` | `MovimientoDeudaCuentaDto` 201 | 400 tipo, importe o miembro inválidos; 409 cuenta o ahorro inactivos, sin hogar o superando lo que debe o lo pendiente | permiso `cuenta.movimientos` |
| `DELETE /api/cuenta-comun/deudas/{id}` | Borra un movimiento de deuda (el `id` sale de `movimientosDeuda` o del extracto). Un **adelanto** al que ya se han aplicado devoluciones o compensaciones no se puede borrar si la deuda quedaría negativa (409 con `{ error, falta }`); una **devolución o compensación del ahorro** tampoco si ese dinero ya se ha usado y el ahorro quedaría sin respaldo (409 con `{ error, falta }`). Borrar una devolución o compensación de Hogar siempre se puede: la deuda y lo pendiente de reembolso vuelven | - | 204 | 404 no existe en el hogar; 409 sin hogar o por las reglas anteriores | permiso `cuenta.movimientos` |
| `GET /api/cuenta-comun/ingresos` | Lista los ingresos a la cuenta común fuera de aportaciones y ahorro, del más reciente al más antiguo | - | `IngresoCuentaComunDto[]` 200 | 409 sin hogar | cualquier miembro del hogar |
| `POST /api/cuenta-comun/ingresos` | Registra dinero que entra a la cuenta común (p. ej. lo que pagan las amigas de una cena). Suma al saldo y al efectivo de la cuenta desde su fecha (y al extracto del mes, `extracto.ingresos`); no cambia el ahorro ni los reembolsos pendientes (`fecha` opcional, por defecto hoy UTC) | `CrearIngresoCuentaComunRequest` | `IngresoCuentaComunDto` 201 | 400 importe o miembro fuera del hogar; 409 cuenta inactiva o sin hogar | permiso `cuenta.movimientos` |
| `DELETE /api/cuenta-comun/ingresos/{id}` | Elimina un ingreso a la cuenta común | - | 204 | 404; 409 sin hogar | permiso `cuenta.movimientos` |
| `POST /api/cuenta-comun/depositos-ahorro` | Registra dinero que entra al ahorro fuera de la aportación mensual: ahorro inicial, lotería... (`fecha` opcional, por defecto hoy UTC) | `CrearDepositoAhorroRequest` | `DepositoAhorroDto` 201 | 400 importe o miembro fuera del hogar; 409 sin hogar | permiso `ahorro.movimientos` |
| `DELETE /api/cuenta-comun/depositos-ahorro/{id}` | Elimina un ingreso de ahorro | - | 204 | 404; 409 sin hogar o si dejaría sin respaldo lo ya retirado o gastado desde el ahorro (`{ error, falta }`) | permiso `ahorro.movimientos` |
| `POST /api/cuenta-comun/retiradas-ahorro` | **Sin uso desde la web** (lo que se saca del ahorro se anota como gasto con el perfil `cuenta_comun_ahorro`); se mantiene por compatibilidad. Registra dinero que sale del ahorro (`fecha` opcional, por defecto hoy UTC) | `CrearRetiradaAhorroRequest` | `RetiradaAhorroDto` 201 | 400 importe o miembro fuera del hogar; 409 importe mayor que el ahorro disponible (`{ error, disponible }`) o sin hogar | permiso `ahorro.movimientos` |
| `DELETE /api/cuenta-comun/retiradas-ahorro/{id}` | Elimina una retirada de ahorro | - | 204 | 404; 409 sin hogar | permiso `ahorro.movimientos` |

El importe de un mes es el de la aportación con `desde` más reciente que no pase de ese mes. **Saldo** = aportado acumulado − ahorro acumulado − gastos cargados a la cuenta hasta el mes (negativo si no los cubre): el ahorro no cuenta para gastos. **Ahorro acumulado** = parte de ahorro de las aportaciones + ingresos aparte (depósitos) hasta el mes. **Ahorro disponible** = ahorro acumulado − retiradas − gastos pagados desde el ahorro hasta el mes; una retirada baja solo el ahorro, nunca el saldo ni el efectivo. Un gasto se paga desde el ahorro con el perfil `cuenta_comun_ahorro` (el servidor marca `pagadoDesdeAhorro` aunque no se envíe) o con `GastoRequest.pagadoDesdeAhorro = true` y un perfil de cuenta común (400 con otro perfil): baja el ahorro disponible y no el saldo de gastos (`gastado` y `ahorroGastado` van por separado), no genera deuda entre personas, y no puede superar el ahorro disponible (409). Como en la cuenta común, puede pagarlo el propio ahorro (`pagadoPor = null`, baja el efectivo total) o adelantarlo un adulto (`pagadoPor` = ese adulto): entonces queda pendiente de reembolso para esa persona y el extracto lo muestra como «gastos del ahorro adelantados» (el ahorro baja y el efectivo sube lo mismo hasta reembolsarlo). Las plantillas recurrentes con ese perfil generan gastos del ahorro adelantados por su pagador; `POST /api/gastos-recurrentes/generar` responde 409 si el ahorro no está activado o no cubre la suma de los gastos del ahorro del mes (`{ error, disponible }`). Una retirada tampoco puede superar lo ahorrado hasta el mes de su fecha menos lo ya retirado o gastado desde el ahorro (409); el ahorro disponible nunca queda en negativo: rebajar el ahorro de una aportación (`PUT /aportaciones`) o eliminar un ingreso se rechaza con 409 si dejaría sin respaldo lo ya retirado o gastado desde el ahorro (se comprueba en el último mes con retiradas o gastos desde el ahorro). **Pendiente** de un miembro = gastos de la cuenta que adelantó − reembolsos recibidos, **por fondo**: el de Hogar son los adelantos pagados del saldo menos los reembolsos de Hogar, y el del ahorro, los adelantos pagados desde el ahorro menos sus reembolsos (`PendienteCuentaDto.desdeAhorro`). Un miembro puede tener los dos a la vez; cada reembolso paga de uno solo. El efectivo y el total de la cuenta suman los dos fondos. **Efectivo** = saldo + pendientes, sin contar el ahorro (el dinero de gastos de la cuenta no baja hasta reembolsar un gasto que adelantó una persona). Un gasto también puede pagarlo **directamente la cuenta**: `GastoRequest.pagadoPor = null`, solo con un perfil `cuenta_comun` o `cuenta_comun_ahorro` (400 en otro caso); no genera pendiente y baja el efectivo. `GastoResponse.pagadoPor` es `null` en esos gastos. Ejemplo: aportan 600 + 400, Ana adelanta una hipoteca de 900 a cargo de la cuenta → saldo 100, pendiente de Ana 900, efectivo 1000; tras reembolsarle 400 → saldo 100, pendiente 500, efectivo 600. Con ahorro: si Ana aporta 600 y aparta 150, y Beto aporta 400 sin ahorro, el saldo es 850 y el ahorro disponible 150; retirar 100 lo deja en 50 y el saldo sigue en 850. Con un ingreso aparte de 1000 (ahorro inicial) el disponible es 1050 y pagar una fianza de 300 desde el ahorro lo deja en 750, con el saldo en 850.

### 3.10 Cierre de mes

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/cierres-mes` | Meses cerrados del hogar, del más reciente al más antiguo | - | `MesCerradoDto[]` 200 | 409 sin hogar | miembro |
| `POST /api/cierres-mes` | Cierra un mes (`mes` = `YYYY-MM`) | `CerrarMesRequest` | `MesCerradoDto` 201 | 400 mes inválido o que aún no ha terminado; 403 sin el permiso `mes.cerrar`; 409 ya cerrado o sin hogar | permiso `mes.cerrar` |
| `DELETE /api/cierres-mes/{mes}` | Reabre un mes cerrado (`mes` = `YYYY-MM`) | - | 204 | 400 mes inválido; 403 sin el permiso `mes.reabrir`; 404 no estaba cerrado; 409 sin hogar | permiso `mes.reabrir` |
| `POST /api/hogar/cerrar-periodo` | **Cierra el periodo propio del hogar** (viaje o evento: un periodo de rango o abierto, nunca un mes). Un periodo abierto termina hoy; uno de rango conserva su fin. Queda congelado (`periodo_cerrado_en`): sus gastos no se pueden crear, editar ni borrar. Genera la liquidación final: los gastos pendientes se marcan como un ciclo de liquidación (`ciclo_liquidacion`) en cualquier modo, y si no quedan pendientes no se crea ciclo | - | `CierrePeriodoResponse` 200 `{ periodo, cerradoEn, ciclo }` (`ciclo` null si no había pendientes) | 400 el hogar no tiene periodo propio o aún no ha empezado; 403 sin el permiso o sin ser miembro; 409 sin hogar, si el periodo ya está cerrado o si otro cierre de liquidación se completa a la vez | permiso `mes.cerrar` |
| `POST /api/hogar/reabrir-periodo` | Reabre el periodo cerrado; sus gastos vuelven a poder cambiarse. El periodo queda como rango con la fecha de fin que tuvo al cerrarse | - | 204 | 403 sin el permiso; 404 no estaba cerrado; 409 sin hogar | permiso `hogar.funciones` |

Un mes cerrado congela sus **gastos** y, con ellos, su reparto, su resumen y su liquidación (saldos y transferencias sugeridas): `POST /api/gastos` con fecha en ese mes, `PUT /api/gastos/{id}` (si la fecha actual **o** la nueva cae en un mes cerrado, es decir, tampoco se saca un gasto de él ni se mete uno), `DELETE /api/gastos/{id}` y `POST /api/gastos-recurrentes/generar?mes=` devuelven 409 `{ "error": "Ese mes está cerrado: ..." }`. El 409 también se da si el mes se cierra *durante* la petición: la base de datos serializa el cierre y las escrituras de gasto del mismo hogar y mes y rechaza la escritura tardía (el gasto no se guarda). Los pagos de liquidación (`/api/pagos-liquidacion`) siguen permitidos porque saldan lo congelado sin cambiarlo. Solo se cierra un mes ya terminado (el mes en curso y los futuros dan 400). Cierra quien tenga el permiso `mes.cerrar` (por defecto, cualquier miembro); reabrir es posible en cualquier momento, pero solo con el permiso `mes.reabrir` (por defecto, solo un admin), y queda en el historial. Cerrar y reabrir quedan en la auditoría con la entidad `mes_cerrado`.

### 3.11 Auditoría

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/auditoria?entidad=&entidadId=&limite=&hasta=&despuesDeId=` | Historial de cambios del hogar, del más reciente al más antiguo (empates por `id`). `limite` 1-200 (50 por defecto). Se pagina con el `cuando` y el `id` del último evento recibido, enviados como `hasta` (ISO 8601) y `despuesDeId`; con solo `hasta` se perderían los eventos de un mismo guardado, que comparten `cuando` | - | `EventoAuditoriaDto[]` 200 | 400 límite fuera de 1-200; 403 sin el permiso `historial.ver`; 409 sin hogar | permiso `historial.ver` |

Cada cambio de gasto, gasto recurrente, pago de liquidación, aportación, reembolso, deuda con la cuenta, ingreso y retirada de ahorro, perfil, categoría, miembro, invitación, enlace de grupo, producto de la lista de la compra (alta, borrado y cambios de nombre o cantidad; marcarlo como comprado no se registra) y enlace QR de la lista queda registrado **en la misma transacción** que el cambio (no puede haber cambio sin rastro). `accion`: `crear` (solo `despues`), `editar` (solo los campos que cambian, en `antes` y `despues`), `borrar` (solo `antes`), `vincular` (un usuario queda ligado a un miembro al aceptar una invitación) y `usar` (invitación aceptada). El reparto de un gasto (`repartos`) y el detalle de un perfil (`detalle`) van dentro de su evento. Crear un hogar es un único evento (no se detalla la semilla). Nunca se registran el token de una invitación ni su hash. `autor` es el nombre del miembro vinculado al usuario; `usuarioId` sigue siendo válido aunque ese miembro se desactive.

**Origen del cambio (`origen`).** Cada `EventoAuditoriaDto` lleva `origen`: `manual` (por defecto, lo que hace la persona desde la aplicación) o `asistente` (un gasto que propuso el chat y confirmó la persona). Assistant.Api lo marca enviando la cabecera `X-Origen: asistente` en `POST /api/gastos` al confirmar un borrador (`POST /api/chat/confirmar-gasto`, `docs/asistente.md`). El autor (`usuarioId`, `autor`) sigue siendo quien confirma. Es una **etiqueta** que envía el cliente con el JWT de la persona, no una prueba de origen: cualquier otro valor de `X-Origen` cuenta como `manual`. Columna `auditoria.origen` (`manual` o `asistente`), script `Scripts/20261024130000_predeterminado_unico_y_origen_auditoria.sql`.

### 3.12 Avisos del navegador

Avisos push a los miembros del hogar cuando alguien anota un gasto nuevo que no es personal. Cada miembro activa los avisos en su propio dispositivo; no hace falta permiso, es una preferencia personal. La suscripción la guarda el miembro activo del usuario en el hogar actual.

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/notificaciones/clave-publica` | Clave pública VAPID con la que el navegador se suscribe | - | `ClavePublicaPushResponse` 200 | 401 sin token; 404 si el servidor no tiene las claves configuradas | - |
| `POST /api/notificaciones/suscripcion` | Registra la suscripción de este dispositivo. Si el mismo endpoint estaba registrado (quizá con otro miembro), lo sustituye | `SuscripcionPushRequest` | 204 | 400 endpoint que no es https o claves vacías; 403 no eres miembro activo; 409 sin hogar | - |
| `DELETE /api/notificaciones/suscripcion` | Quita la suscripción de este dispositivo. Idempotente: 204 también si ya no existía | `BajaSuscripcionPushRequest` | 204 | 400 sin endpoint; 403 no eres miembro activo; 409 sin hogar | - |

Además del gasto nuevo, avisan: el cierre de un ciclo y el de un periodo propio (sin el miembro que lo cierra; el periodo incluye el total de su liquidación final si la hubo) y un pago registrado (a todo el hogar, con quién paga y quién recibe). Un gasto nuevo no personal avisa a las suscripciones del hogar salvo a la del pagador (`PagadoPor`). El título dice de dónde sale: quién lo realiza («Nuevo gasto realizado por Ana»), «Nuevo gasto de la cuenta común» o «Nuevo gasto pagado desde el ahorro». Un gasto de la cuenta común o del ahorro lo reciben todos los suscritos, también quien lo paga. El cuerpo es «importe · categoría · descripción», y la descripción solo aparece si la hay. El aviso sale en segundo plano: la respuesta de `POST /api/gastos` no espera al servicio push, y si la cola interna se llena o faltan las claves, el aviso se descarta sin fallar el gasto. Una suscripción que el servicio push da por caducada (404 o 410) se borra. El contenido del aviso es JSON `{ "titulo", "cuerpo", "ruta" }`. Si cambian las claves VAPID, las suscripciones existentes dejan de servir y hay que volver a activar los avisos.

### 3.13 Contacto

Formulario de la página pública `/contacto`. Es el único endpoint anónimo además de `/health`: no exige token ni hogar (si llega un token válido, se usa solo para el límite de peticiones). Core.Api envía el mensaje por correo al buzón del proyecto con la API HTTP de Resend (no SMTP: el plan gratuito de Render bloquea esos puertos). El remitente y el destino los fija la configuración (`Correo__Remitente`, `Correo__Destino`), nunca la petición; el correo de quien escribe va como `reply_to`. No se guarda nada en la base de datos.

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `POST /api/contacto` | Envía un mensaje al buzón de contacto. El asunto sale del motivo: `duda`, `mejora` u `otro` | `ContactoRequest` | 204 | 400 motivo desconocido, correo no válido, mensaje de menos de 10 o más de 4000 caracteres o nombre de más de 100; 429 más de 5 por hora; 503 sin `Correo__ResendApiKey` o `Correo__Destino`, o si Resend rechaza el envío | anónimo |

`ContactoRequest`: `{ "motivo": "duda", "email": "ana@ejemplo.es", "mensaje": "...", "nombre": "Ana", "web": null }`. `nombre` es opcional. `web` es un campo trampa que el formulario deja oculto y vacío: si llega relleno, la API responde 204 sin enviar nada. Con 503 el front ofrece escribir directamente a `mipartetuparte@gmail.com`.

Total: 69 endpoints de negocio (5 hogares/yo, 10 miembros/invitaciones/enlaces de grupo, 4 categorías, 5 perfiles, 5 gastos, 6 recurrentes, 8 resumen/liquidación/pagos/ciclos, 16 cuenta común, 5 cierre de mes, 1 auditoría, 3 avisos del navegador, 1 contacto) más `GET /health`.

### 3.14 Lista de la compra

Lista de productos del hogar, con su función propia (`hogar.lista_compra_activa`, por defecto desactivada; la activa un miembro con `hogar.funciones`). Un producto tiene nombre (1-100 caracteres), cantidad (entero de 1 a 999, por defecto 1) y el check de comprado; no se convierte en gastos. Los productos y los enlaces no se borran al desactivar la lista: solo quedan bloqueados los cambios (409).

Los enlaces QR son para quien no tiene cuenta: `POST /api/lista-compra/enlaces` devuelve un token en claro, que no vuelve a verse, y la URL pública es `/compra/{token}` del front. El token solo permite leer, añadir y marcar productos de ese hogar, y caduca a los 7 días; revocarlo lo invalida. En BD solo se guarda su hash (`enlace_lista_compra.token_hash`). Las rutas públicas no piden sesión y llevan el token en la cabecera `X-Enlace-Lista` (está permitida en CORS).

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/lista-compra` | Activación y productos del hogar (pendientes primero) | - | `ListaCompraResponse` 200 | 409 sin hogar | miembro |
| `PUT /api/lista-compra/activacion` | Activa o desactiva la lista del hogar | `ActivarListaCompraRequest` | `{ activa }` 200 | 409 sin hogar | `hogar.funciones` |
| `POST /api/lista-compra/productos` | Añade un producto | `CrearProductoCompraRequest` (`nombre`, `cantidad` opcional) | `ProductoCompraDto` 201 | 400 nombre o cantidad inválidos; 409 lista desactivada o sin hogar | `lista.compra` |
| `PUT /api/lista-compra/productos/{id}` | Marca o desmarca como comprado | `MarcarProductoCompraRequest` | `ProductoCompraDto` 200 | 404 no es del hogar; 409 sin hogar | `lista.compra` |
| `DELETE /api/lista-compra/productos/{id}` | Borra un producto | - | 204 | 404; 409 sin hogar | `lista.compra` |
| `GET /api/lista-compra/enlaces` | Enlaces QR del hogar, del más reciente al más antiguo, con su estado (`vigente`, `revocado` o `caducado`) y quién los creó. **No lleva el token**: solo se devuelve al crear el enlace; para uno perdido hay que revocarlo y crear otro. Funciona con la lista desactivada | - | `EnlaceListaDto[]` 200 | 409 sin hogar | miembro |
| `POST /api/lista-compra/enlaces` | Crea un enlace QR (caduca a los 7 días). El token va solo aquí | - | `EnlaceListaCreado` 201 | 409 lista desactivada o sin hogar | `lista.compra` |
| `DELETE /api/lista-compra/enlaces/{id}` | Revoca un enlace | - | 204 | 404; 409 sin hogar | `lista.compra` |
| `POST /api/lista-compra/qr` | Genera el SVG de un QR con el texto indicado (1-500 caracteres) | `QrListaRequest` | `QrListaResponse` 200 | 400 texto inválido; 409 sin hogar | sesión |
| `GET /api/lista-compra/publica` | Lista del enlace QR (sin sesión, cabecera `X-Enlace-Lista`) | - | `ListaCompraResponse` 200 | 404 token desconocido; 409 enlace revocado o caducado, o lista desactivada | enlace QR |
| `POST /api/lista-compra/publica/productos` | Añade un producto con el enlace QR | `CrearProductoCompraRequest` | `ProductoCompraDto` 201 | 400; 404; 409 | enlace QR (limitado por `Costosa`) |
| `PUT /api/lista-compra/publica/productos/{id}` | Marca o desmarca un producto del hogar del enlace | `MarcarProductoCompraRequest` | `ProductoCompraDto` 200 | 404; 409 | enlace QR (limitado por `Costosa`) |

`ProductoCompraDto` = `{ id, nombre, cantidad, comprado }`. Permiso `lista.compra`: añadir, marcar, borrar y crear o revocar enlaces; por defecto lo tienen todos los miembros . La plantilla «Solo lectura» no lo incluye.

## 4. Ejemplos JSON

### Hogares

`GET /api/yo` con un solo hogar:

```json
{
  "userId": "7c1f6d3e-0b8a-4f55-9a53-1d2f6c1b9e10",
  "hogares": [ { "id": "a1b2c3d4-0000-4000-8000-000000000001", "nombre": "Casa Pérez", "cuentaComunActiva": false, "ahorroActivo": false, "tipo": "hogar", "recurrentesActivas": true, "liquidacionModo": "mensual" } ],
  "hogarActual": { "id": "a1b2c3d4-0000-4000-8000-000000000001", "nombre": "Casa Pérez", "cuentaComunActiva": false, "ahorroActivo": false, "tipo": "hogar", "recurrentesActivas": true, "liquidacionModo": "mensual" }
}
```

`POST /api/hogares`:

```json
{ "nombreHogar": "Casa Pérez", "nombreMiembro": "Ana" }
```
```json
{ "id": "a1b2c3d4-0000-4000-8000-000000000001", "nombre": "Casa Pérez", "cuentaComunActiva": false, "ahorroActivo": false, "tipo": "hogar", "recurrentesActivas": true, "liquidacionModo": "mensual" }
```

`POST /api/hogares` con tipo de grupo (`tipo` es opcional; sin él se crea un «hogar»):

```json
{ "nombreHogar": "Piso de la playa", "nombreMiembro": "Ana", "tipo": "viaje" }
```

**Tipo de grupo (`tipo`).** `HogarResumen` y `CrearHogarRequest` llevan un campo de texto `tipo` con los valores `hogar` (por defecto: si se omite en la creación, o si el hogar es anterior a este campo), `piso_compartido`, `viaje`, `evento` y `otro`. Cualquier otro valor da 400. El tipo se guarda en `hogar.tipo` y determina las categorías sembradas al crear el hogar (`hogar` mantiene las 6 de siempre; los demás tipos siembran sus categorías con perfil «Por partes»). El tipo no se cambia después.

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

### Enlaces de grupo

Un enlace de grupo es un **enlace reutilizable** para grupos de viaje o evento: quien lo abre se une al hogar sin invitación nominal, tantas veces como haga falta (la invitación de arriba es de un solo uso). Igual que el token de invitación, el token en claro solo se devuelve al crearlo y en base de datos queda su hash SHA-256. La API no genera enlaces: el front arma `<web>/unirse#token=<token>`, como con las invitaciones.

`POST /api/enlaces-grupo` (cuerpo opcional; sin `caducaEn` no caduca):

```json
{ "caducaEn": "2026-11-30T23:59:59+01:00" }
```
```json
{ "id": "d0000000-0000-4000-8000-000000000001", "token": "c2Vn...base64url...", "caducaEn": "2026-11-30T22:59:59+00:00" }
```

`GET /api/enlaces-grupo` devuelve cada enlace con `estado` (`activo`, `revocado` o `caducado`), `usos` (uniones hechas, informativo) y `creadoPor` (id del miembro), sin token:

```json
[{ "id": "d0000000-0000-4000-8000-000000000001", "creadoPor": "b0000000-0000-4000-8000-0000000000aa", "creadoEn": "2026-10-17T10:00:00+00:00", "caducaEn": null, "usos": 3, "estado": "activo" }]
```

`POST /api/enlaces-grupo/unirse` (sin hogar actual; `nombre` obligatorio):

```json
{ "token": "c2Vn...base64url...", "nombre": "Marta" }
```
```json
{ "id": "a1b2c3d4-0000-4000-8000-000000000001", "nombre": "Casa Pérez" }
```

Reglas: un enlace **revocado** o **caducado** no admite uniones (409, con `revocado` por encima de `caducado` si se dan los dos). Un usuario ya presente en el hogar recibe 409 aunque el enlace siga activo. Cada unión incrementa `usos` y no consume el enlace. Unirse no exige permiso: el hogar se obtiene del token, no se elige. Unirse siempre da de alta un miembro nuevo con el `nombre` indicado; el enlace nunca vincula un adulto existente sin cuenta, eso solo se hace por invitación con `miembroId` (`miembros.gestionar`). Crear, revocar y listar exigen el permiso `enlaces.gestionar`; crear y revocar quedan auditados como `enlace_grupo` con el id del enlace (sin token ni hash), y las uniones también quedan registradas como `enlace_grupo`. Unirse y revocar invalidan la caché de acceso como las invitaciones. Ninguna de estas operaciones puede dejar al hogar sin administrador: solo añaden miembros nuevos o dejan los existentes intactos.

### Categorías

`POST /api/categorias`:

```json
{ "nombre": "Supermercado", "categoriaPadreId": "d0000000-0000-4000-8000-000000000002", "perfilRepartoId": "e0000000-0000-4000-8000-000000000001" }
```
```json
{ "id": "d0000000-0000-4000-8000-000000000009", "nombre": "Supermercado", "categoriaPadreId": "d0000000-0000-4000-8000-000000000002", "perfilRepartoId": "e0000000-0000-4000-8000-000000000001", "aCargoCuentaComun": false }
```

Categoría a cargo de la cuenta común (con la cuenta activada; el servidor asigna el perfil de cuenta común si no se envía): `{ "nombre": "Comunidad", "categoriaPadreId": null, "perfilRepartoId": null, "aCargoCuentaComun": true }`.

Categoría marcada «predeterminada para gastos» (`predeterminadoGastos`, opcional, `false` por defecto; `CategoriaDto` lo devuelve siempre): `{ "nombre": "Supermercado", "categoriaPadreId": null, "perfilRepartoId": null, "aCargoCuentaComun": false, "predeterminadoGastos": true }`.

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
  "concepto": "Compra semanal", "gastoRecurrenteId": null, "aCargoCuentaComun": false, "pagadoDesdeAhorro": false, "personal": false,
  "repartos": [
    { "miembroId": "b0000000-0000-4000-8000-0000000000aa", "importeAsumido": 60.00 },
    { "miembroId": "b0000000-0000-4000-8000-0000000000dd", "importeAsumido": 40.00 }
  ]
}
```

### Gastos recurrentes

**Activación por hogar.** `hogar.recurrentes_activas` (true por defecto, también para los hogares existentes; `HogarResumen` lo devuelve como `recurrentesActivas`). Lo cambia quien tenga el permiso `hogar.funciones` con `PUT /api/hogar/recurrentes` (por defecto, solo los admins). Con ello desactivado no se crean plantillas ni se generan gastos (409), pero las plantillas existentes se conservan pausadas, y el `DELETE` y las consultas siguen funcionando. El cambio queda en el historial como el de los demás interruptores del hogar.

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
  "mes": "2026-10", "gastosTotales": 1200.00, "gastosPersonales": 35.00,
  "miembros": [
    { "miembroId": "b0000000-0000-4000-8000-0000000000aa", "nombre": "Ana", "pagado": 1000.00, "asumido": 700.00, "debeCuentaComun": 0.00, "personal": 0.00 },
    { "miembroId": "b0000000-0000-4000-8000-0000000000dd", "nombre": "Luis", "pagado": 200.00, "asumido": 500.00, "debeCuentaComun": 0.00, "personal": 35.00 }
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

`cuentaComun` (solo con la cuenta activada, si no es `null`): `{ "aportadoMes": 500.00, "gastadoMes": 120.00, "saldo": 280.00, "efectivo": 400.00, "pendiente": 120.00, "ahorroMes": 100.00, "ahorroDisponible": 100.00, "gastadoAhorroMes": 0.00 }`, con los saldos acumulados al final del mes (`aportadoMes`, `gastadoMes`, `gastadoAhorroMes` y `ahorroMes` son del propio mes; `gastadoMes` es lo gastado de la cuenta sin lo pagado desde el ahorro, que va en `gastadoAhorroMes`; `pendiente` es el total que la cuenta debe a quienes adelantaron gastos).

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

**Activación por hogar.** `hogar.cuenta_comun_activa` (false por defecto; `GET /api/cuenta-comun` la devuelve como `activa`). Con la cuenta sin activar responden 409 (`"La cuenta común no está activada en este hogar…"`): `PUT /aportaciones`, `POST` de reembolsos, ingresos y retiradas de ahorro, `POST`/`PUT /api/gastos` que queden a cargo de la cuenta (perfil `cuenta_comun`), `POST /api/gastos-recurrentes/generar` si alguna plantilla pendiente usa ese perfil, y marcar una categoría con `aCargoCuentaComun`. Los `DELETE` y las consultas siguen funcionando. El front oculta el perfil de cuenta común y la opción de pagar con la cuenta mientras no esté activa, y la pestaña Cuenta común deja de mostrarse.

**Ahorro por hogar.** `hogar.ahorro_activo` (false por defecto; `GET /api/cuenta-comun` lo devuelve como `ahorroActivo`, y `HogarResumen` como `cuentaComunActiva` y `ahorroActivo`). Con el ahorro sin activar responden 409 (`"El ahorro no está activado en este hogar…"`): `PUT /aportaciones` con `ahorro` > 0, `POST` de ingresos y retiradas de ahorro y `POST`/`PUT /api/gastos` con `pagadoDesdeAhorro`. Los `DELETE` y las consultas siguen funcionando. Los hogares con datos de ahorro quedaron activados por la migración.

**Su parte por persona** (`partes`, `PartePersonaDto[]`): para cada miembro con aportaciones o ingresos de ahorro suyos hasta el mes, `aportado` (para gastos, sin ahorro), `ahorrado` (parte de ahorro de sus aportaciones más sus ingresos aparte), `porcentajeGastos`/`porcentajeAhorro` (su peso sobre el total, 2 decimales), `parteSaldo` (saldo de gastos repartido en proporción a lo aportado), `parteAhorro` (ahorro disponible repartido en proporción a lo ahorrado) y `pendiente` (lo que la cuenta le debe por gastos que adelantó). El céntimo sobrante lo absorbe el último miembro (por id): las partes suman siempre el saldo y el ahorro disponible. Un saldo negativo se reparte igual (parte de cada uno en el descubierto). Un ingreso de ahorro se atribuye a quien lo registró. Ejemplo: Ana aporta 600 (200 de ahorro) y Beto 400 (sin ahorro) más 100 de ahorro ingresado por Beto; hay un gasto de 101. Saldo 699 → 349,50 cada uno (ambos pusieron 400 para gastos); ahorro disponible 300 → Ana 200 y Beto 100.

Error típico si se pasa de la deuda (409): `{ "error": "El importe supera la deuda pendiente entre ambos miembros (150.00).", "pendiente": 150.00 }`.

### Deudas con la cuenta común

`POST /api/cuenta-comun/deudas` (permiso `cuenta.movimientos`). `tipo` es `adelanto`, `devolucion` o `compensacion`; `fecha` es opcional (hoy UTC) y `desdeAhorro` también (`false`, el fondo de Hogar):

```json
{
  "miembroId": "b0000000-0000-4000-8000-0000000000aa",
  "tipo": "adelanto", "importe": 40.00, "fecha": "2026-10-05",
  "concepto": "Fontanero", "desdeAhorro": false
}
```
```json
{
  "id": "d0000000-0000-4000-8000-000000000001",
  "miembroId": "b0000000-0000-4000-8000-0000000000aa",
  "fecha": "2026-10-05", "importe": 40.00, "tipo": "adelanto", "desdeAhorro": false, "concepto": "Fontanero"
}
```

Después, `GET /api/cuenta-comun?mes=2026-10` incluye lo que debe cada persona (el efectivo ya refleja el pago de la cuenta) y los movimientos en el extracto de `GET /api/cuenta-comun/movimientos` (`adelanto_persona`, con importe negativo):

```json
{
  "efectivo": 560.00,
  "deudas": [
    { "miembroId": "b0000000-0000-4000-8000-0000000000aa", "nombre": "Ana", "importe": 40.00, "desdeAhorro": false }
  ]
}
```

Errores típicos (409): una devolución o compensación por encima de la deuda, `{ "error": "El importe supera lo que debe la persona (40.00).", "debe": 40.00 }`; una compensación por encima de lo que la cuenta le debe por reembolsos, `{ "error": "El importe supera lo que la cuenta le debe por reembolsar (0.00).", "pendiente": 0.00 }`; un adelanto del ahorro que no cabe, `{ "error": "El importe supera el ahorro disponible (20.00).", "disponible": 20.00 }`. Con un `tipo` desconocido responde 400.

`DELETE /api/cuenta-comun/deudas/{id}` responde 204. Rechazos típicos (409): un adelanto ya devuelto, `{ "error": "No se puede borrar este adelanto: dejaría devoluciones o compensaciones sin deuda (20.00).", "falta": 20.00 }`; una devolución del ahorro ya usada, `{ "error": "No se puede borrar este movimiento: ya se ha retirado o gastado ese ahorro (faltarían 60.00).", "falta": 60.00 }`.

**El ahorro cuenta las deudas del ahorro**: el ahorro disponible (y por tanto lo que admiten un gasto desde el ahorro, una retirada o un adelanto) resta lo que la cuenta ha adelantado desde él y suma lo que se ha devuelto o compensado, y los controles que protegen el ahorro (`PUT /api/cuenta-comun/aportaciones` al rebajar el ahorro, `DELETE` de un ingreso de ahorro y el cambio de frecuencia de las aportaciones) también lo tienen en cuenta: responden 409 si dejarían sin respaldo ese dinero.

### Lista de la compra

`PUT /api/lista-compra/activacion` (permiso `hogar.funciones`) y `POST /api/lista-compra/productos` (permiso `lista.compra`; `cantidad` es opcional y vale 1):

```json
{ "activa": true }
```
```json
{ "nombre": "Leche", "cantidad": 2 }
```
```json
{ "id": "c0000000-0000-4000-8000-000000000001", "nombre": "Leche", "cantidad": 2, "comprado": false }
```

`GET /api/lista-compra` devuelve los pendientes primero y, en cada grupo, por orden de alta:

```json
{
  "activa": true,
  "productos": [
    { "id": "c0000000-0000-4000-8000-000000000001", "nombre": "Leche", "cantidad": 2, "comprado": false },
    { "id": "c0000000-0000-4000-8000-000000000002", "nombre": "Pan", "cantidad": 1, "comprado": true }
  ]
}
```

Compartir por QR: `POST /api/lista-compra/enlaces` devuelve el token **una sola vez**; la URL del QR es `{front}/compra/{token}` y `POST /api/lista-compra/qr` con ese texto devuelve el SVG:

```json
{ "id": "e0000000-0000-4000-8000-000000000001", "token": "Xk3...q9A", "caducaEn": "2026-11-04T10:00:00+00:00" }
```
```json
{ "texto": "https://miparte.example/compra/Xk3...q9A" }
```
```json
{ "svg": "<svg xmlns=\"http://www.w3.org/2000/svg\" ...>...</svg>" }
```

Quien abre el QR no tiene sesión. Su cliente llama a las rutas públicas con la cabecera `X-Enlace-Lista: <token>`:

```http
GET /api/lista-compra/publica
X-Enlace-Lista: Xk3...q9A

POST /api/lista-compra/publica/productos
X-Enlace-Lista: Xk3...q9A
{ "nombre": "Huevos", "cantidad": 6 }

PUT /api/lista-compra/publica/productos/c0000000-0000-4000-8000-000000000001
X-Enlace-Lista: Xk3...q9A
{ "comprado": true }
```

Respuestas de error del acceso público: 404 si falta la cabecera o el token no existe; 409 `{ "error": "El enlace de la lista ha caducado o se ha revocado." }` con el enlace caducado o revocado, y 409 `{ "error": "La lista de la compra no está activada en este hogar." }` si el hogar la desactiva. Un producto de otro hogar da 404 aunque se conozca su id.

## 5. Reglas de negocio útiles para la UI

- **Gastos personales**: un gasto con `personal: true` solo lo ven y lo cambian quien paga (`pagadoPor`). No aparece en las listas, detalle, resumen, dashboard ni historial de los demás, ni siquiera de un admin; los endpoints responden 404 a quien no es el pagador. Solo quien paga puede anotarlo; si intenta anotar uno de otro, 400. Las plantillas recurrentes personales siguen la misma regla. Los gastos personales de un miembro desactivado se conservan, pero nadie los ve hasta que vuelva a activarse.

- **Hogar de un adulto**: el reparto funciona (el creador recibe el 100 %), pero no hay liquidación que mostrar: el único saldo es 0 y no hay transferencias. La UI puede ocultar la pantalla de liquidación con un solo adulto activo.
- **Importes**: mayores que 0, máximo 2 decimales y hasta 9.999.999.999,99. Más decimales dan 400. Conceptos de hasta 200 caracteres; nombres de hogar, miembro, categoría y perfil hasta 100.
- **Meses**: parámetro `mes` con formato estricto `YYYY-MM` (`2026-10`); otro formato da 400. En `POST /api/pagos-liquidacion` el campo `mes` es una fecha que debe ser día 1 (`2026-10-01`). Las respuestas devuelven `mes` como `YYYY-MM`, salvo `PagoLiquidacionDto.mes`, que es fecha.
- **Reparto guardado e inmutable**: cada gasto guarda sus `repartos` al crearse. Cambiar un perfil después no recalcula gastos antiguos. Solo `PUT /api/gastos/{id}` recalcula ese gasto. Editar una plantilla recurrente tampoco toca los gastos ya generados. El último miembro absorbe el céntimo sobrante.
- **Quién interviene**: el pagador es un adulto activo. El reparto (detalle de un perfil) admite adultos activos y miembros `a_cargo` cuyo responsable es un adulto activo; la parte de un `a_cargo` la asume su responsable (ver §3.4, miembros a cargo).
- **Recurrentes**: `diaMes` entre 1 y 28. `generar` es idempotente por mes y plantilla; una plantilla con gastos generados no se borra, se desactiva.
- **Semilla al crear hogar**: 5 perfiles para cualquier tipo (`Cuenta común`, `Cuenta común (ahorro)`, `Por partes`, `Porcentaje fijo`, `Individual`; el creador queda con 1 parte en "Por partes" y 100 % en "Porcentaje fijo"). Las 6 categorías estándar (Hipoteca/Alquiler, Alimentación, Suministros, Gastos varios de casa e Hijo con "Por partes"; Ocio personal con "Individual") solo aplican al tipo `hogar`; `piso_compartido`, `viaje`, `evento` y `otro` siembran sus categorías propias con "Por partes" (ver «Tipo de grupo» en §4). El creador es admin y adulto.
- **Quién soy**: `MiembroDto.esYo` es `true` en el miembro vinculado al usuario autenticado (en `GET /api/miembros`, `PUT` y `DELETE`; el alta siempre devuelve `false`). El front lo usa para saber si mostrar los controles de admin y cuál es "su" miembro.
- **Roles y permisos**: el rol (`admin` / `miembro`) solo decide la plantilla de permisos de quien no tiene lista propia; el hogar siempre conserva al menos un admin activo y vinculado (409). Un adulto responsable de miembros a cargo activos no se puede desactivar (409).
- **Permisos por miembro** (`miembro.permisos`, nulo = sin lista propia). `MiembroDto.permisos` devuelve los **efectivos**: la lista propia si existe; si no, la plantilla del rol (admin todos, miembro los marcados «defecto»); los adultos sin cuenta y los a cargo, ninguno. Cualquier adulto con cuenta, **admins incluidos**, puede tener cualquier combinación. Sin el permiso la API responde 403 `{ "error": "No tienes permiso para ..." }`; las lecturas siguen abiertas a cualquier miembro. Un miembro con `permisos.gestionar` los cambia con `PUT /api/miembros/{id}` y `permisos` (lista completa de claves; 400 clave desconocida; 409 si el destino no es un adulto con cuenta). Cambiar el `rol` sin enviar `permisos` devuelve al miembro a la plantilla del rol nuevo. **Invariante**: siempre queda al menos un adulto activo con cuenta con `permisos.gestionar` (409 si el cambio lo impide). Catálogo (19): `gastos.crear`, `gastos.editar`, `gastos.borrar`, `recurrentes.gestionar` (también `POST /api/gastos-recurrentes/generar`), `pagos.registrar` (crear y borrar pagos de liquidación), `mes.cerrar`, `cuenta.movimientos` (aportaciones, reembolsos y deudas con la cuenta), `ahorro.movimientos` (ingresos y retiradas), `categorias.gestionar`, `perfiles.gestionar`, `lista.compra` (productos y enlaces QR de la lista de la compra) (todos «defecto»); `mes.reabrir`, `historial.ver`, `miembros.gestionar` (añadir, renombrar a otros, desactivar, responsable), `invitaciones.crear`, `enlaces.gestionar` (crear, listar y revocar enlaces de grupo), `hogar.funciones` (cuenta común, ahorro, recurrentes y modo de liquidación) `permisos.gestionar` y `hogar.eliminar` (eliminar el hogar; sin «defecto»). Cualquiera puede renombrarse a sí mismo. Plantillas para la web (`CatalogoPermisos.Plantillas`): Solo lectura, Colaborador (defecto), Gestor (todo salvo `hogar.funciones`, `permisos.gestionar` y `hogar.eliminar`) y Administrador (todo).
- **Adultos sin cuenta** (`tipo` adulto y sin `UserId`; p. ej. viajes o eventos). Participan igual que cualquier adulto: pueden pagar gastos (`pagadoPor`), aparecen en los repartos, en la liquidación y en la cuenta común, y no tienen permisos (`permisos` vacío) ni pueden tener rol `admin` (400 al pedirlo; un dato heredado no cuenta como administrador en las comprobaciones de «siempre queda un admin»). Para que quede vinculado a una cuenta: un miembro con `miembros.gestionar` crea una invitación con `miembroId` (`POST /api/invitaciones`, que exige también `miembros.gestionar`) y la persona la acepta con `POST /api/invitaciones/aceptar`. Al aceptar, el miembro queda ligado al usuario (auditoría `vincular`, entidad `miembro`) y conserva su nombre, sus gastos y sus repartos; un usuario no puede figurar dos veces en el hogar y un miembro vinculado no admite otra invitación. Un adulto sin cuenta puede seguir sin cuenta indefinidamente.
- **Invitaciones**: el `token` en claro solo se devuelve en la respuesta de `POST /api/invitaciones` (en base de datos solo se guarda su hash): mostrarlo o copiarlo en ese momento. El front lo ofrece como enlace `<web>/unirse#token=<token>` (en el fragmento, que el navegador no envía al servidor); la API solo conoce el token y no genera enlaces. Caduca a los 7 días (`caducaEn`) y es de un solo uso. Quien acepta no necesita hogar previo. Si la invitación apunta a un `miembroId`, ese miembro queda vinculado al usuario; si no, hay que enviar `nombre` y entra como adulto con rol `miembro`.
- **Cierre de mes**: `GET /api/cierres-mes` dice qué meses están cerrados; en ellos la UI debe ocultar o desactivar alta, edición y borrado de gastos y la generación de recurrentes (el servidor los rechaza con 409). Cierra un mes quien tenga el permiso `mes.cerrar` y lo reabre quien tenga `mes.reabrir` (por defecto, cualquier miembro puede cerrar y solo un admin puede reabrir).
- **Periodo cerrado**: con el periodo propio del hogar cerrado (`POST /api/hogar/cerrar-periodo`), `POST /api/gastos`, `PUT /api/gastos/{id}` (fecha actual o nueva dentro del rango) y `DELETE /api/gastos/{id}` devuelven 409 `{ "error": "El periodo está cerrado." }`. El cierre de mes sigue aplicando por separado. `GET /api/hogares` y `GET /api/yo` devuelven en `HogarResumen` los campos `periodo` (texto `YYYY-MM-DD/YYYY-MM-DD`, o `YYYY-MM-DD/` si está abierto; null si el hogar usa meses), `periodoCerrado` (bool) y `aportacionFrecuencia` (`unica`, `mensual`, `bimestral`, `trimestral`, `semestral` o `anual`). `GET /api/cuenta-comun` incluye también `aportacionFrecuencia`.
- **Eliminaciones**: categorías y perfiles en uso devuelven 409; los miembros se desactivan, no se borran.

- **Deuda con la cuenta común**: lo que la cuenta le debe a una persona (por fondo) son sus gastos adelantados menos lo reembolsado y lo adelantado por la cuenta, más lo devuelto; se recalcula en cada lectura y puede ser negativo (lo que ella debe, hasta que lo devuelva). La deuda es un derecho de la cuenta, no efectivo: el adelanto baja el efectivo (o el ahorro) en cuanto se registra, la devolución lo repone y la compensación lo deja como está (quita pendiente de reembolso y lo guarda en el fondo). No entra en la liquidación entre personas. Un adelanto del ahorro no puede superar el ahorro disponible, contando lo ya adelantado y devuelto.
- **Lista de la compra**: se activa por hogar (`hogar.funciones`). Con ella desactivada, los productos y enlaces se conservan pero cualquier cambio da 409, también desde un enlace QR. Un enlace QR caduca a los 7 días, se puede revocar y solo da acceso a la lista de su hogar. La web la refresca sola cada 15 segundos; la API no tiene notificaciones push de la lista.

## 6. Nota de cambio en /api/yo

El campo `hogarId` de `GET /api/yo` ya no existe: ahora es `hogarActual` (objeto `{ id, nombre }` o `null`), y la lista completa va en `hogares`. En el código del front hay que usar `hogarActual.id` donde antes se leía `hogarId`.
