# Asistente (chatbot) y contrato de Assistant.Api

Implementación del chatbot descrito en [diseno-y-decisiones.md](diseno-y-decisiones.md) («Chatbot con tool calling»). El modelo de lenguaje no calcula ni escribe SQL: elige una función de consulta, el código la ejecuta con los datos del hogar y el modelo solo redacta la respuesta.

## Arquitectura

```
Front (Blazor) ──JWT + X-Hogar-Id──▶ Assistant.Api ──JWT + X-Hogar-Id──▶ Core.Api ──▶ Postgres (RLS + filtro por hogar)
                                          │
                                          └──▶ Proveedor de modelo: Cloudflare Workers AI (producción) o API de Anthropic (Messages)
```

- **Sin acceso propio a la base de datos.** Assistant.Api no tiene cadena de conexión: consulta `GET /api/miembros`, `/api/categorias`, `/api/gastos`, `/api/resumen` y `/api/liquidacion` de Core.Api reenviando el JWT de la persona y su `X-Hogar-Id`. La multitenencia (RLS y filtro global por `hogar_id`) la sigue aplicando Core.Api; el asistente nunca ve datos de otro hogar aunque el modelo lo pidiera.
- **Autenticación.** `MiParte.Auth` valida el JWT de Supabase igual que en Core.Api (`Supabase__Url`, `Supabase__JwtSecret`).
- **Hogar.** La cabecera `X-Hogar-Id` es obligatoria. Antes de llamar al modelo se hace una consulta a Core.Api con ese hogar: si la persona no es miembro activo, 403 y el modelo no se invoca.
- **Cliente del modelo.** `IClienteModelo` abstrae el proveedor. `Asistente__Proveedor` elige `ClienteAnthropic` (SDK oficial, `Anthropic` en NuGet, por defecto) o `ClienteWorkersAi` (Cloudflare Workers AI, REST `/ai/run/{modelo}`, cuota gratuita diaria; Cloudflare dice no usar el contenido para entrenar ni conservarlo más allá de la petición). Modelo por defecto `claude-sonnet-5-5`, configurable con `Asistente__Modelo`. **Streaming**: `POST /api/chat/stream` entrega el texto en vivo (SSE). Con Anthropic el texto llega según se genera (`CreateStreaming`); con Workers AI, o cualquier proveedor sin streaming real, la respuesta llega de una vez (el método por defecto de `IClienteModelo`).
- **Solo lectura, más un borrador.** Las cinco funciones de consulta no modifican nada. `proponer_gasto` solo prepara un borrador: no escribe. **La única escritura es `POST /api/chat/confirmar-gasto`**, que llama la persona con su botón «Confirmar» (una segunda llamada, nunca la propuesta del modelo). Guarda el gasto en Core.Api con el JWT y el hogar de la persona, así que Core aplica sus permisos (`gastos.crear`) y sus reglas. La categoría y el perfil son los marcados como predeterminados para gastos **en ese momento** (el cliente no los envía; sin exactamente uno de cada, 409). El pagador lo elige la persona al confirmar. La petición lleva `X-Origen: asistente` y Core registra el alta con origen `asistente` en la auditoría (el autor sigue siendo quien confirma; la cabecera es una etiqueta del cliente, no una prueba de origen).

## Funciones de consulta

| Función | Argumentos | Fuente en Core.Api |
|---|---|---|
| `gasto_total` | `categoria?` (incluye subcategorías), `persona?`, `mes?`, `anio?` (sin mes suma el año) | `GET /api/gastos?mes=` (12 llamadas si es el año entero) |
| `gasto_por_categoria` | `mes`, `anio?` | `GET /api/resumen` |
| `balance_mes` | `mes`, `anio?` | `GET /api/resumen` (pagado, asumido y diferencia por miembro) |
| `liquidacion_mes` | `mes`, `anio?` | `GET /api/liquidacion` (nombres en lugar de ids) |
| `comparar_meses` | `mes_a`, `anio_a?`, `mes_b`, `anio_b?` | `GET /api/resumen` de ambos meses |
| `proponer_gasto` | `importe` (> 0, máx. 2 decimales), `concepto` (máx. 200), `fecha` (`AAAA-MM-DD`, obligatoria: no se supone) | `GET /api/categorias` y `GET /api/perfiles`; solo lee. Devuelve el borrador (categoría y perfil predeterminados); si no hay exactamente uno de cada, error para que el modelo lo explique |

Reglas del diseño que se cumplen en código: si falta el año se asume el en curso (o el anterior si el mes aún no ha llegado) y el resultado lleva `anioAsumido: true` para que el modelo lo diga; si la pregunta no encaja en ninguna función, el prompt del sistema obliga a decirlo en vez de improvisar. Los argumentos del modelo se validan como entrada no fiable; un error de validación vuelve al modelo como resultado de herramienta con `is_error` para que se lo explique a la persona.

## Protección frente a prompt injection y abuso

- **Los datos son datos.** Los resultados de las funciones viajan siempre como `tool_result` dentro de `{"datos": ...}`, nunca en el prompt del sistema, y las instrucciones del sistema dicen que nombres, categorías y conceptos son texto escrito por usuarios que no debe obedecerse. Los textos de la base de datos se limpian (sin caracteres de control, máximo 60 caracteres) antes de volver al modelo; los conceptos de los gastos ni siquiera se envían.
- **Superficie mínima.** El modelo dispone de cinco funciones de lectura y de `proponer_gasto`, que solo devuelve un borrador; ninguna escribe. Una inyección que tuviera éxito podría como mucho proponer un gasto, que la persona ve, confirma o descarta. El alta nunca depende del texto del modelo: solo `POST /api/chat/confirmar-gasto` escribe, y lo llama la persona. Ni el importe ni la fecha ni el reparto los fija el modelo: los revisa la persona en la tarjeta del borrador.
- **El historial del cliente es de la persona.** Solo se aceptan roles `user` y `assistant` (otro rol da 400), con máximo `Asistente__MaxMensajes` (20) mensajes de `Asistente__MaxCaracteresMensaje` (1500) caracteres; el último debe ser de `user`.
- **Bucle acotado.** Máximo 5 vueltas y 8 llamadas a funciones por pregunta (`Asistente__MaxIteraciones`, `Asistente__MaxLlamadasHerramienta`); `max_tokens` 1024 por turno.
- **Coste.** Límite de `Asistente__MensajesPorMinuto` (10) preguntas por usuario y de 60 peticiones por minuto en total (429 con `Retry-After`); cuerpo máximo 64 KB. Las peticiones sin sesión se cuentan por IP, que tras el proxy de Render es la del cliente solo con `ProxyInverso__*` configurado (ver Configuración).
- **Secretos.** `ANTHROPIC_API_KEY` solo se lee del entorno (nunca de `appsettings` ni del repo); no se registra en logs ni en errores. Los fallos del proveedor llegan a la persona como un 503 genérico.
- **Front.** Las respuestas se pintan como texto (Blazor escapa; no hay `MarkupString`), la CSP sigue sin scripts en línea y solo añade el origen del asistente a `connect-src` (`ASSISTANT_URL`).

## Contrato HTTP

Base local: `http://localhost:5002` (tanto con `dotnet run` como con docker compose). Errores como en Core.Api: `{ "error": "mensaje en español" }`.

### `GET /health`
Sin autenticación: `{ "service": "assistant", "status": "ok" }`.

### `POST /api/chat`
Cabeceras: `Authorization: Bearer <access_token>` y `X-Hogar-Id: <guid>`.

```json
{ "mensajes": [
  { "rol": "user", "texto": "¿Cuánto gasté en alimentación en junio?" }
] }
```
```json
{ "respuesta": "En junio (2026) el hogar gastó 412,30 € en Alimentación.", "herramientas": ["gasto_total"] }
```

`mensajes` es el historial reciente en orden cronológico (el cliente lo guarda; el servidor no tiene estado). `herramientas` lista las funciones consultadas, para mostrarlas a la persona.

| Código | Cuándo |
|---|---|
| 200 | `ChatResponse` |
| 400 | Falta o es inválido `X-Hogar-Id`; conversación vacía, rol no permitido, último mensaje que no es de `user`, mensaje vacío o demasiado largo |
| 401 | Sin token válido, o Core.Api lo rechaza por caducado |
| 403 | La persona no es miembro activo de ese hogar |
| 413 | Cuerpo mayor que `Asistente__MaxCuerpoBytes` |
| 429 | Demasiadas preguntas por minuto |
| 502 | Core.Api no responde (el asistente no inventa cifras: no contesta) |
| 503 | El modelo no está disponible o falta `ANTHROPIC_API_KEY` |

`ChatResponse.borrador` es el gasto propuesto en esa respuesta (`BorradorGastoDto`: `importe`, `concepto`, `fecha`, `categoria`, `perfil`) o `null`. Nada está guardado todavía.

### `POST /api/chat/stream`
Misma petición que `/api/chat` y mismas cabeceras y errores (400, 401, 403, 429, 413 antes de empezar). Respuesta `text/event-stream` con eventos en orden:

- `event: texto` con `data: {"texto": "..."}`: trozos de la respuesta según se generan. El cliente los muestra en vivo.
- `event: fin` con `data:` igual a la `ChatResponse` completa. Es la respuesta definitiva: si difiere de los trozos, manda esta.
- `event: error` con `data: {"error": "..."}`: solo si el fallo llega ya empezado el flujo. Antes de empezar, el error es una respuesta JSON normal con su código.

La conversación no se guarda en el servidor; el historial lo manda el cliente igual que en `/api/chat`.

### `POST /api/chat/confirmar-gasto`
La única escritura del asistente; la llama el front solo cuando la persona pulsa «Confirmar» en la tarjeta del borrador.

El front tiene además un alta guiada («Añadir gasto» en la carátula del chat): pregunta importe, concepto y fecha de uno en uno (la fecha como `DD/MM/YY`) sin llamar a la API, y al completarla envía al asistente un mensaje con los datos en formato técnico (importe con punto, fecha `AAAA-MM-DD`) para que proponga el borrador con `proponer_gasto`. El contrato HTTP no cambia; la lógica está en `ModeloAltaGastoChat` (`src/Web/Modelos`).

```json
{ "importe": 12.00, "concepto": "Café", "fecha": "2026-10-06", "pagadoPor": "b0000000-0000-4000-8000-0000000000aa",
  "categoriaId": "d0000000-0000-4000-8000-000000000009", "perfilRepartoId": "e0000000-0000-4000-8000-000000000001",
  "categoria": "Supermercado", "perfil": "Casa" }
```

`pagadoPor` es un miembro adulto activo elegido por la persona. `categoriaId` y `perfilRepartoId` son los que se mostraron en el borrador (el front los copia de `borrador`). `categoria` y `perfil` son los nombres mostrados. El servidor compara ids y nombres con los predeterminados para gastos **en el momento de confirmar**: si coinciden, guarda el gasto; si no (el hogar ha cambiado la categoría o el perfil marcados, o les ha cambiado el nombre, desde la propuesta), **no guarda nada** y responde **409** con un mensaje en español que pide volver a pedir el gasto para ver la propuesta actualizada. Responde **201** con el `GastoResponse` guardado. Errores: 400 (importe, concepto, fecha o pagador no válidos), 401, 403 o 409 con el mensaje de Core.Api (por ejemplo, sin permiso `gastos.crear`, o el hogar no tiene exactamente un predeterminado de categoría o de perfil), 429, 502 y 503. Con Core.Api rechazando el gasto, nada queda guardado. En el front, un 409 deja el borrador descartado con el aviso.

DTOs en `src/Contracts/Asistente.cs` (`ChatRequest`, `MensajeChatDto`, `ChatResponse`, `BorradorGastoDto`, `ConfirmarGastoRequest`).

## Configuración

| Variable | Valor por defecto | Notas |
|---|---|---|
| `ANTHROPIC_API_KEY` | (vacío) | Secreto. Sin ella el chat responde 503 |
| `Asistente__Proveedor` | `anthropic` | `anthropic` o `cloudflare` (Workers AI, cuota gratuita diaria). Con `cloudflare` no se usa la clave de Anthropic |
| `CLOUDFLARE_API_TOKEN` | (vacío) | Secreto: token de la API de Cloudflare con permiso Workers AI. Sin él el chat responde 503 |
| `Asistente__CloudflareCuenta` | (vacío) | Identificador de la cuenta de Cloudflare (no es secreto) |
| `Asistente__CloudflareModelo` | `@cf/zai-org/glm-4.7-flash` | Modelo de Workers AI con llamadas a herramientas (comprobar en el catálogo) |
| `Asistente__Modelo` | `claude-sonnet-5-5` | Cualquier modelo de la Messages API con tool use |
| `Asistente__CoreUrl` | `http://localhost:5001` | En docker compose, `http://core:8080` |
| `Asistente__MaxTokens` | 1024 | Salida por turno |
| `Asistente__MaxIteraciones` / `Asistente__MaxLlamadasHerramienta` | 5 / 8 | Bucle de herramientas |
| `Asistente__MaxMensajes` / `Asistente__MaxCaracteresMensaje` | 20 / 1500 | Historial aceptado |
| `Asistente__MensajesPorMinuto` | 10 | Por usuario |
| `Supabase__Url`, `Supabase__JwtSecret` | | Igual que Core.Api |
| `Cors__OrigenesPermitidos__0` | | Origen del front |
| `ProxyInverso__Proxies__N` / `ProxyInverso__Redes__N` | (vacío) | IPs o redes CIDR de los proxies de confianza. Una IP inválida impide arrancar. Mismo criterio que Core.Api (`MiParte.Seguridad.ReenvioCabeceras`) |
| `ProxyInverso__Confiar` | `false` | `true` acepta cualquier remitente directo; solo en Render, donde el contenedor únicamente es alcanzable por el proxy |

Front: `Api:AssistantUrl` en `wwwroot/appsettings.json` (público, sin secretos) y, para la CSP, `ASSISTANT_URL` (nginx) o la variable de repositorio `ASSISTANT_URL` (Cloudflare). Si falta, el asistente flotante avisa de que no está configurada.

## Despliegue en Render y Cloudflare

Assistant.Api en Render (servicio `miparte-assistant`), front en Cloudflare Pages (`https://miparte.pages.dev`). Variables que Render no rellena solas (`sync: false` en `render.yaml`):

| Dónde | Variable | Valor |
|---|---|---|
| Render, `miparte-assistant` | `Supabase__Url` | `https://<proyecto>.supabase.co`, igual que en Core.Api. Sin ella ningún token es válido (401) |
| Render, `miparte-assistant` | `Asistente__CoreUrl` | `https://miparte-core.onrender.com`. Sin ella el asistente llama a `localhost:5001` y responde 502 |
| Render, `miparte-assistant` | `Cors__OrigenesPermitidos__0` | `https://miparte.pages.dev` (solo el origen: el core no sirve aquí) |
| Render, `miparte-assistant` | `Asistente__Proveedor` | `cloudflare` |
| Render, `miparte-assistant` | `CLOUDFLARE_API_TOKEN` | Secreto: token con permiso Workers AI |
| Render, `miparte-assistant` | `Asistente__CloudflareCuenta` | Account ID de Cloudflare |
| GitHub, variables de Actions | `ASSISTANT_URL` | `https://miparte-assistant.onrender.com` (o la que asigne Render). Se usa al publicar el front |

Tras cambiar `ASSISTANT_URL`, relanza el workflow «Desplegar web» (`workflow_dispatch` desde `master`): solo se ejecuta solo al cambiar el front.

Comprobación rápida del CORS (debe salir `access-control-allow-origin: https://miparte.pages.dev`):

```bash
curl -s -i -X OPTIONS https://miparte-assistant.onrender.com/api/chat -H "Origin: https://miparte.pages.dev" -H "Access-Control-Request-Method: POST" -H "Access-Control-Request-Headers: authorization,content-type" | grep -i access-control
```

Síntomas frecuentes: sin cabecera CORS en el preflight, falta `Cors__OrigenesPermitidos__0` o su valor no es el del front; 401 con sesión válida, falta `Supabase__Url`; 502, falta `Asistente__CoreUrl` (el log muestra `localhost:5001`) o el servicio se está despertando.

## Probarlo en local

```bash
export ANTHROPIC_API_KEY=sk-ant-...        # solo en tu terminal
export Supabase__Url=https://xxxx.supabase.co
# Solo si el proyecto aún usa el secreto HS256 legado (el mismo valor en Core.Api y Assistant.Api; nunca lo subas al repo):
export Supabase__JwtSecret="<secreto-hs256-del-proyecto>"
export ConnectionStrings__Default="..."
dotnet run --project src/Core/Core.Api --urls http://localhost:5001
Asistente__CoreUrl=http://localhost:5001 dotnet run --project src/Assistant/Assistant.Api   # http://localhost:5002
dotnet run --project src/Web                                                                 # Api:AssistantUrl ya apunta a :5002
```

Sin clave se puede ver el asistente flotante y comprobar el 503; el modo demo del front responde con un texto fijo. Los tests (`dotnet test src/Assistant/Assistant.Tests`) usan un cliente de modelo falso y no llaman a la API de Anthropic.

## Pendiente

- Cancelar una pregunta en curso (el streaming no tiene botón de parar).
- Pruebas con el proveedor de producción (Cloudflare Workers AI): pendientes. Sin ellas, la tarjeta del borrador no está verificada en navegador con el modelo real.
- Etiqueta `X-Origen` sin verificar: el origen «asistente» lo declara el cliente con el JWT de la persona. Si se quiere una prueba real, haría falta un canal de confianza entre Assistant.Api y Core.Api (decisión pendiente).
- Streaming: `ClienteWorkersAi` (el proveedor gratuito de producción) no implementa streaming real; la respuesta llega completa de una vez por el respaldo de `ClienteModelo`. El streaming real solo existe con Anthropic (`CreateStreaming`), que compila pero no se ha probado contra la API real.
- Memoria de la conversación entre sesiones (hoy vive en la página).
- Pruebas manuales con el modelo real, evaluación de calidad de las respuestas y ajuste del prompt.
- Límite diario de coste por hogar.
