# Asistente (chatbot) y contrato de Assistant.Api

Implementación del chatbot descrito en [diseno-y-decisiones.md](diseno-y-decisiones.md) («Chatbot con tool calling»). El modelo de lenguaje no calcula ni escribe SQL: elige una función de consulta, el código la ejecuta con los datos del hogar y el modelo solo redacta la respuesta.

## Arquitectura

```
Front (Blazor) ──JWT + X-Hogar-Id──▶ Assistant.Api ──JWT + X-Hogar-Id──▶ Core.Api ──▶ Postgres (RLS + filtro por hogar)
                                          │
                                          └──▶ API de Anthropic (Messages, modelo configurable)
```

- **Sin acceso propio a la base de datos.** Assistant.Api no tiene cadena de conexión: consulta `GET /api/miembros`, `/api/categorias`, `/api/gastos`, `/api/resumen` y `/api/liquidacion` de Core.Api reenviando el JWT de la persona y su `X-Hogar-Id`. La multitenencia (RLS y filtro global por `hogar_id`) la sigue aplicando Core.Api; el asistente nunca ve datos de otro hogar aunque el modelo lo pidiera.
- **Autenticación.** `MiParte.Auth` valida el JWT de Supabase igual que en Core.Api (`Supabase__Url`, `Supabase__JwtSecret`).
- **Hogar.** La cabecera `X-Hogar-Id` es obligatoria. Antes de llamar al modelo se hace una consulta a Core.Api con ese hogar: si la persona no es miembro activo, 403 y el modelo no se invoca.
- **Cliente del modelo.** `IClienteModelo` abstrae el proveedor. `Asistente__Proveedor` elige `ClienteAnthropic` (SDK oficial, `Anthropic` en NuGet, por defecto) o `ClienteWorkersAi` (Cloudflare Workers AI, REST `/ai/run/{modelo}`, cuota gratuita diaria; Cloudflare dice no usar el contenido para entrenar ni conservarlo más allá de la petición). Modelo por defecto `claude-sonnet-5-5`, configurable con `Asistente__Modelo`. Sin streaming de momento (pendiente).
- **Solo lectura.** Las cinco funciones del diseño no modifican nada. No hay herramientas de escritura porque el diseño no las contempla («solo lee datos del hogar»); si se añadieran, tendrían que pedir confirmación explícita antes de ejecutarse (propuesta de la herramienta, botón «Confirmar» en el front y una segunda llamada).

## Funciones de consulta

| Función | Argumentos | Fuente en Core.Api |
|---|---|---|
| `gasto_total` | `categoria?` (incluye subcategorías), `persona?`, `mes?`, `anio?` (sin mes suma el año) | `GET /api/gastos?mes=` (12 llamadas si es el año entero) |
| `gasto_por_categoria` | `mes`, `anio?` | `GET /api/resumen` |
| `balance_mes` | `mes`, `anio?` | `GET /api/resumen` (pagado, asumido y diferencia por miembro) |
| `liquidacion_mes` | `mes`, `anio?` | `GET /api/liquidacion` (nombres en lugar de ids) |
| `comparar_meses` | `mes_a`, `anio_a?`, `mes_b`, `anio_b?` | `GET /api/resumen` de ambos meses |

Reglas del diseño que se cumplen en código: si falta el año se asume el en curso (o el anterior si el mes aún no ha llegado) y el resultado lleva `anioAsumido: true` para que el modelo lo diga; si la pregunta no encaja en ninguna función, el prompt del sistema obliga a decirlo en vez de improvisar. Los argumentos del modelo se validan como entrada no fiable; un error de validación vuelve al modelo como resultado de herramienta con `is_error` para que se lo explique a la persona.

## Protección frente a prompt injection y abuso

- **Los datos son datos.** Los resultados de las funciones viajan siempre como `tool_result` dentro de `{"datos": ...}`, nunca en el prompt del sistema, y las instrucciones del sistema dicen que nombres, categorías y conceptos son texto escrito por usuarios que no debe obedecerse. Los textos de la base de datos se limpian (sin caracteres de control, máximo 60 caracteres) antes de volver al modelo; los conceptos de los gastos ni siquiera se envían.
- **Superficie mínima.** El modelo solo dispone de cinco funciones de lectura sobre el hogar de la persona; aunque una inyección tuviera éxito, no hay nada que modificar ni otro hogar al que llegar.
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

DTOs en `src/Contracts/Asistente.cs` (`ChatRequest`, `MensajeChatDto`, `ChatResponse`).

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

- Streaming de la respuesta (SSE) y cancelar una pregunta en curso.
- Herramientas de escritura con confirmación explícita (hoy fuera del diseño).
- Memoria de la conversación entre sesiones (hoy vive en la página).
- Pruebas manuales con el modelo real, evaluación de calidad de las respuestas y ajuste del prompt.
- Límite diario de coste por hogar.
