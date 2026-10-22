# Despliegue gratuito

Front en Cloudflare Pages, Core.Api en Render y datos/Auth en Supabase (planes gratuitos).

| Pieza | Dónde | Cómo se publica |
|---|---|---|
| Front (`src/Web`) | Cloudflare Pages, proyecto `miparte` | `.github/workflows/deploy-web.yml` en cada push a `master` que toque el front |
| Core.Api | Render, servicio `miparte-core` | Blueprint `render.yaml`; redespliega al pasar CI en `master` |
| Esquema | Supabase | `src/Database/MiParte.Database` (`migrar`) sobre la base del proyecto, **antes** de desplegar el código que la usa. Ver `pendientes` para comprobar qué falta |

## Esquema en Supabase

El esquema se aplica con el proyecto `MiParte.Database`, no con la CLI de Supabase. Con la cadena del Session pooler (puerto 5432) en una variable de entorno, sin escribirla en ningún fichero del repo:

```bash
dotnet run --project src/Database/MiParte.Database -- pendientes --conexion "$DATABASE_URL"
```

```bash
dotnet run --project src/Database/MiParte.Database -- migrar --conexion "$DATABASE_URL"
```

`pendientes` lista lo que falta sin tocar la base; `migrar` lo aplica y es idempotente. Detalle en `src/Database/MiParte.Database/Estructura/README.md`.

## Core.Api en Render

1. Render → New → Blueprint, conecta el repositorio y elige `render.yaml`.
2. Rellena las tres variables que pide:
   - `Supabase__Url`: `https://<project-ref>.supabase.co`.
   - `ConnectionStrings__Default`: Session pooler de Supabase (puerto 5432, no el 6543), ver `.env.example`. Usa el host exacto de Connect → Session pooler (los proyectos nuevos son `aws-1-...`; un host inexistente da `Name or service not known` en el log).
   - `Cors__OrigenesPermitidos__0`: `https://miparte.pages.dev` (solo el origen).
3. Cuando termine, `https://miparte-core.onrender.com/health` debe responder. Esa URL (la real que asigne Render) es la variable `CORE_URL` de GitHub Actions para el front.

El plan gratuito duerme el servicio tras 15 minutos sin tráfico: la primera petición tarda 30-60 s en despertarlo.

### Conexión TLS con verificación (`VerifyFull`)

`SSL Mode=Require` cifra pero no comprueba quién responde. Para validar el certificado del servidor:

1. Supabase → Project Settings → Database → **SSL Configuration** → *Download certificate* (fichero `.crt`; es público, no un secreto).
2. Render → `miparte-core` → Environment → **Secret Files** → *Add Secret File*: nombre `supabase-ca.crt`, contenido el del certificado. Queda en `/etc/secrets/supabase-ca.crt`.
3. Cambia `ConnectionStrings__Default` a `...;SSL Mode=VerifyFull;Root Certificate=/etc/secrets/supabase-ca.crt` (en lugar de `SSL Mode=Require`) y guarda; Render redespliega.
4. Si el log muestra un error de certificado o de nombre, no bajes a `Require`: corrige el certificado, la ruta de `Root Certificate` o el host (debe ser el del certificado). `VerifyFull` valida cadena y nombre del host; `VerifyCA` solo la cadena.

### Avisos del navegador (claves VAPID)

Los avisos push necesitan un par de claves VAPID en Core.Api. Se generan una sola vez, fuera del repo (con Node instalado: `npx web-push generate-vapid-keys`), y la privada no se guarda en ningún fichero del proyecto:

1. Render → `miparte-core` → Environment: `Notificaciones__VapidPublica` (clave pública), `Notificaciones__VapidPrivada` (clave privada, secreta) y `Notificaciones__Contacto` (`mailto:` de contacto). Render redespliega.
2. Comprueba `GET /api/notificaciones/clave-publica` con un token válido: 200 con la clave pública. Sin claves responde 404 y el front no ofrece los avisos.
3. Si cambias las claves, las suscripciones de los dispositivos dejan de funcionar: cada miembro tiene que volver a activar los avisos.

### Formulario de contacto (Resend)

`POST /api/contacto` envía el mensaje por la API HTTP de Resend. No usa SMTP porque el plan gratuito de Render bloquea los puertos SMTP de salida (25, 465 y 587). Sin configuración el endpoint responde 503 y la página de contacto ofrece escribir directamente por correo.

1. Crea una cuenta en Resend con el correo que debe recibir los mensajes (`mipartetuparte@gmail.com`) y genera una API key con permiso de envío. Es un secreto: no se guarda en ningún fichero del proyecto.
2. Render → `miparte-core` → Environment: `Correo__ResendApiKey` (la clave), `Correo__Destino` (`mipartetuparte@gmail.com`) y, opcionalmente, `Correo__Remitente`. Render redespliega.
3. Sin dominio propio, el remitente por defecto es `Mi parte, tu parte <onboarding@resend.dev>`. Con ese remitente Resend solo entrega al correo de la propia cuenta, que aquí es justo el destino. Si el destino es otro buzón, verifica un dominio en Resend y cambia `Correo__Remitente` a una dirección de ese dominio.
4. Prueba: envía un mensaje desde `/contacto`. Si la página dice que no se ha podido enviar, el log de Render muestra «Resend rechazó el correo de contacto: <código>» (403: remitente o destino no permitidos; 401: clave incorrecta).

Límite: 5 mensajes por hora y usuario o IP (`Limites__ContactoPorHora`).

## Assistant.Api en Render

El mismo Blueprint crea `miparte-assistant` (`src/Assistant/Dockerfile`). Pide `Supabase__Url`, `Asistente__CoreUrl` (URL de `miparte-core`, sin barra final), `ANTHROPIC_API_KEY` y `Cors__OrigenesPermitidos__0`; `Supabase__JwtSecret` solo si el proyecto aún usa el secreto HS256 legado. No necesita cadena de conexión. Sin `ANTHROPIC_API_KEY` el chat responde 503. La URL resultante es la variable `ASSISTANT_URL` de GitHub Actions (opcional: sin ella el chat queda desactivado en el front publicado).

## Errores 500

Core.Api responde `500 { "error": "Error interno del servidor." }` ante una excepción no controlada, con las cabeceras CORS ya aplicadas, y deja la traza en los logs de Render. Si el navegador muestra un 500, el motivo real está ahí, no en la consola del navegador.

## Front en Cloudflare Pages

Secretos de GitHub `CLOUDFLARE_API_TOKEN` y `CLOUDFLARE_ACCOUNT_ID`, y variable `CORE_URL` con la URL de Core.Api (y opcionalmente `ASSISTANT_URL`, ver arriba). El workflow la usa para `Api:CoreUrl` y para la CSP (`src/Web/cloudflare/_headers.template`, equivalente de `default.conf.template`). Escribe `Api:CoreUrl` y `Api:AssistantUrl` en `appsettings.json` **antes** de `dotnet publish`: el service worker precarga ese fichero comprobando el hash del manifiesto, así que no se puede modificar tras publicar. Antes de subir, `tests/pwa/probar.mjs` prueba la salida publicada con Chromium sobre un servidor que imita a Pages (redirección 308 de `/index.html`, fallback de SPA y `_headers`): primera y segunda visita, ruta profunda, sin conexión, aplicar una versión nueva con «Actualizar» y sin violaciones de CSP. Si falla, no se despliega. El mismo job (`pwa`) corre en cada PR desde `ci.yml`. En local, una vez: `npm ci --prefix tests/pwa` y `npx --prefix tests/pwa playwright install chromium`; después, `node tests/pwa/probar.mjs <salida>/wwwroot` (con `_headers` en la salida si se quiere comprobar la CSP).

## Supabase Auth

Site URL `https://miparte.pages.dev` y Redirect URLs `https://miparte.pages.dev/**` (el botón de Google vuelve a `/login`).
