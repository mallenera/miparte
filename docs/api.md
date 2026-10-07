# API de Core.Api (referencia para el front Blazor WASM)

Referencia extraída del código de `src/Core/Core.Api` y de los DTOs de `src/Contracts`. Los DTOs se pueden reutilizar directamente desde el proyecto `Contracts` en el front.

Convenciones generales:

- JSON en camelCase (valores por defecto de minimal API). Fechas `DateOnly` como `"2026-10-04"`; `DateTimeOffset` en ISO 8601; Guid como cadena.
- Errores de validación: `{ "error": "mensaje en español" }` con el código HTTP indicado (400, 403, 404, 409). Algunos 404 y los 204 no llevan cuerpo.
- Base URL local: `http://localhost:5001` (docker compose) o la de `launchSettings.json`.
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
| `GET /api/miembros` | Miembros activos del hogar, por nombre | - | `MiembroDto[]` 200 | - | miembro |
| `POST /api/miembros` | Alta de persona sin cuenta (`tipo`: `adulto` o `a_cargo`) | `CrearMiembroRequest` | `MiembroDto` 201 | 400 nombre vacío/> 100, tipo inválido, `a_cargo` sin responsable adulto activo, adulto con responsable; 403 no admin | admin |
| `PUT /api/miembros/{id}` | Edita; campos `null` = sin cambios | `ActualizarMiembroRequest` | `MiembroDto` 200 | 400 nombre vacío, rol inválido, responsable no válido o miembro no `a_cargo`; 403; 404; 409 último admin vinculado, o responsable de miembros a cargo activos | admin; un miembro solo puede cambiar su propio `nombre` |
| `DELETE /api/miembros/{id}` | Borrado lógico (= PUT `activo=false`, mismas reglas) | - | `MiembroDto` 200 | igual que PUT | admin (un no admin recibe 403) |
| `POST /api/invitaciones` | Crea invitación. `miembroId` opcional: vincula a un miembro existente sin usuario; sin él, quien acepte entra como nuevo adulto | `CrearInvitacionRequest` (cuerpo opcional) | `InvitacionCreada` 201 | 403 no admin; 404 miembro; 409 miembro desactivado o ya vinculado | admin |
| `POST /api/invitaciones/aceptar` | Acepta con el token; devuelve el hogar al que se une (SinHogarActual) | `AceptarInvitacionRequest` | `HogarResumen` 200 | 400 token vacío, o nombre obligatorio si la invitación no apunta a un miembro (máx. 100); 404 token no válido; 409 usada, caducada, ya perteneces al hogar, miembro no disponible o ya vinculado | autenticado |

### 3.3 Categorías

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/categorias` | Lista (por nombre) | - | `CategoriaDto[]` 200 | - | miembro |
| `POST /api/categorias` | Crea (subcategorías vía `categoriaPadreId`) | `GuardarCategoriaRequest` | `CategoriaDto` 201 | 400 nombre vacío/> 100, perfil o padre inexistente; 409 nombre duplicado en ese nivel | miembro |
| `PUT /api/categorias/{id}` | Reemplaza todos los campos | `GuardarCategoriaRequest` | `CategoriaDto` 200 | 400 (igual, más padre = ella misma o ciclo), 404, 409 duplicada | miembro |
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

Los ingresos del hogar **no se guardan** y no hay endpoints de ingresos. Los perfiles de reparto son cuatro modos: `individual` (100 % de quien paga), `porcentaje` (por porcentajes, suman 100), `partes` (por número de personas/partes) y `cuenta_comun`. Un gasto con perfil `cuenta_comun` lo asume la cuenta común: `GastoResponse.aCargoCuentaComun` es `true`, `repartos` va vacío, no genera deuda entre personas y no entra en la liquidación ni en el «pagado» del resumen (sí en `gastosTotales` y en el total por categoría). Aún no existen aportaciones, saldo ni reembolsos de la cuenta común.

### 3.6 Gastos

| Método y ruta | Descripción | Request | Response | Errores | Permisos |
|---|---|---|---|---|---|
| `GET /api/gastos?mes=YYYY-MM&categoriaId=` | Lista con repartos; filtros opcionales | - | `GastoResponse[]` 200 | 400 mes inválido; 409 | miembro |
| `GET /api/gastos/{id}` | Uno | - | `GastoResponse` 200 | 404; 409 | miembro |
| `POST /api/gastos` | Crea; el servidor calcula y guarda el reparto | `GastoRequest` | `GastoResponse` 201 | 400 importe, concepto, fecha, categoría o perfil inexistente, pagador no adulto activo, perfil sin valores para los adultos, sin adultos; 409 | miembro |
| `PUT /api/gastos/{id}` | Edita y recalcula el reparto de este gasto | `GastoRequest` | `GastoResponse` 200 | 400, 404, 409 | miembro |
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
| `GET /api/resumen?mes=YYYY-MM` | Gastos del mes, por miembro y por categoría. `mes` obligatorio | - | `ResumenMensualResponse` 200 | 400 mes inválido; 409 sin hogar | miembro |
| `GET /api/liquidacion?mes=YYYY-MM` | Saldos (ya descontando pagos), transferencias sugeridas y pagos registrados. `mes` obligatorio | - | `LiquidacionResponse` 200 | 400; 409 | miembro |
| `POST /api/pagos-liquidacion` | Registra un pago entre dos miembros del mes | `CrearPagoLiquidacionRequest` | `PagoLiquidacionDto` 201 | 400 mes no es día 1, mismo miembro, importe, miembros fuera del hogar; 409 importe mayor que la deuda pendiente (`{ error, pendiente }`) o sin hogar | miembro |
| `DELETE /api/pagos-liquidacion/{id}` | Elimina un pago | - | 204 | 404; 409 sin hogar | miembro |

Total: 39 endpoints de negocio (4 hogares/yo, 6 miembros/invitaciones, 4 categorías, 5 perfiles, 5 gastos, 6 recurrentes, 4 resumen/liquidación/pagos) más `GET /health`.

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
{ "id": "d0000000-0000-4000-8000-000000000009", "nombre": "Supermercado", "categoriaPadreId": "d0000000-0000-4000-8000-000000000002", "perfilRepartoId": "e0000000-0000-4000-8000-000000000001" }
```

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
    { "miembroId": "b0000000-0000-4000-8000-0000000000aa", "nombre": "Ana", "pagado": 1000.00, "asumido": 700.00 },
    { "miembroId": "b0000000-0000-4000-8000-0000000000dd", "nombre": "Luis", "pagado": 200.00, "asumido": 500.00 }
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

Error típico si se pasa de la deuda (409): `{ "error": "El importe supera la deuda pendiente entre ambos miembros (150.00).", "pendiente": 150.00 }`.

## 5. Reglas de negocio útiles para la UI

- **Hogar de un adulto**: el reparto funciona (el creador recibe el 100 %), pero no hay liquidación que mostrar: el único saldo es 0 y no hay transferencias. La UI puede ocultar la pantalla de liquidación con un solo adulto activo.
- **Importes**: mayores que 0, máximo 2 decimales y hasta 9.999.999.999,99. Más decimales dan 400. Conceptos de hasta 200 caracteres; nombres de hogar, miembro, categoría y perfil hasta 100.
- **Meses**: parámetro `mes` con formato estricto `YYYY-MM` (`2026-10`); otro formato da 400. En `POST /api/pagos-liquidacion` el campo `mes` es una fecha que debe ser día 1 (`2026-10-01`). Las respuestas devuelven `mes` como `YYYY-MM`, salvo `PagoLiquidacionDto.mes`, que es fecha.
- **Reparto guardado e inmutable**: cada gasto guarda sus `repartos` al crearse Cambiar un perfil después no recalcula gastos antiguos. Solo `PUT /api/gastos/{id}` recalcula ese gasto. Editar una plantilla recurrente tampoco toca los gastos ya generados. El último miembro absorbe el céntimo sobrante.
- **Quién interviene**: pagador y reparto solo se asignan a adultos activos. Los miembros `a_cargo` necesitan un responsable adulto activo.
- **Recurrentes**: `diaMes` entre 1 y 28. `generar` es idempotente por mes y plantilla; una plantilla con gastos generados no se borra, se desactiva.
- **Semilla al crear hogar**: 4 perfiles (`Cuenta común`, `Por partes`, `Porcentaje fijo`, `Individual`; el creador queda con 1 parte en "Por partes" y 100 % en "Porcentaje fijo") y 6 categorías (Hipoteca/Alquiler, Alimentación, Suministros, Gastos varios de casa e Hijo con "Por partes"; Ocio personal con "Individual"). El creador es admin y adulto.
- **Quién soy**: `MiembroDto.esYo` es `true` en el miembro vinculado al usuario autenticado (en `GET /api/miembros`, `PUT` y `DELETE`; el alta siempre devuelve `false`). El front lo usa para saber si mostrar los controles de admin y cuál es "su" miembro.
- **Roles**: solo admin crea miembros e invitaciones y modifica a otros; el hogar siempre conserva al menos un admin activo y vinculado (409). Un adulto responsable de miembros a cargo activos no se puede desactivar (409).
- **Invitaciones**: el `token` en claro solo se devuelve en la respuesta de `POST /api/invitaciones` (en base de datos solo se guarda su hash): mostrarlo o copiarlo en ese momento. Caduca a los 7 días (`caducaEn`) y es de un solo uso. Quien acepta no necesita hogar previo. Si la invitación apunta a un `miembroId`, ese miembro queda vinculado al usuario; si no, hay que enviar `nombre` y entra como adulto con rol `miembro`.
- **Eliminaciones**: categorías y perfiles en uso devuelven 409; los miembros se desactivan, no se borran.

## 6. Nota de cambio en /api/yo

El campo `hogarId` de `GET /api/yo` ya no existe: ahora es `hogarActual` (objeto `{ id, nombre }` o `null`), y la lista completa va en `hogares`. En el código del front hay que usar `hogarActual.id` donde antes se leía `hogarId`.
