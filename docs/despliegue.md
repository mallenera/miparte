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

`migrar` se ejecuta con el usuario `postgres` (propietario del esquema). Core.Api **no**: usa el rol `miparte_api`.

### Rol de la base de datos para Core.Api (`miparte_api`)

`postgres` es propietario de las tablas: se salta la RLS y puede crear o borrar tablas, leer `auth.users` o vaciar la auditoría. Si la API se conectara con él, un fallo en la API o una cadena de conexión filtrada darían control total de la base. `migrar` (script `Scripts/Antes/20261027140000_rol_miparte_api.sql` y `Estructura/05_Seguridad`) crea el rol `miparte_api`, que solo puede:

- leer, insertar, actualizar y borrar filas de las tablas de `public` (en `auditoria`, solo leer e insertar), con una política RLS propia (`api_servicio`) que le deja ver todas las filas: el aislamiento por hogar dentro de la API lo sigue haciendo el filtro global de `MiParteDbContext`;
- nada más: ni DDL, ni `TRUNCATE`, ni el esquema `auth`, ni el journal de migraciones, ni las RPC `crear_hogar`/`aceptar_invitacion`, ni hacerse pasar por otro rol.

`EsquemaPostgresTests.RolDeLaApi_SoloLeeYEscribeFilasDePublic` lo comprueba, y en CI todas las pruebas de API contra PostgreSQL arrancan Core.Api con este rol (`MIPARTE_TEST_DB_API`).

El rol se crea **sin contraseña** (nunca va en el repositorio). Una vez, tras `migrar`:

1. Genera una contraseña larga sin `;` ni `=` (p. ej. `openssl rand -hex 32`).
2. Supabase → SQL Editor: `alter role miparte_api with password '<contraseña>';`
3. Render → `miparte-core` → Environment → `ConnectionStrings__Default`: la misma cadena del Session pooler cambiando el usuario a `miparte_api.<project-ref>` (el sufijo es el mismo que en `postgres.<project-ref>`) y la contraseña nueva. Render redespliega.
4. Comprueba que la web carga los datos del hogar y que el log de Render no muestra `permission denied` ni `password authentication failed`. Si falla, vuelve a la cadena anterior mientras lo revisas.
5. Cuando funcione, **cambia la contraseña de `postgres`** (Project Settings → Database → Reset database password): ha estado en Render y ya no debe estar en ningún servicio desplegado. Guárdala solo donde ejecutes `migrar`.

Según la documentación de Supabase, los roles propios con `LOGIN` funcionan en conexión directa y en los poolers sin registrarlos aparte. Si acabas de cambiar la contraseña, el Session pooler puede tardar un poco en aceptarla (`28P01`): prueba primero con conexión directa y reintenta.

Para cambiar la contraseña más adelante, repite los pasos 1-3. Una tabla nueva hereda los permisos de `miparte_api` (permisos por defecto de `00_Esquema.sql`), pero necesita también su política `api_servicio` en `05_Seguridad`; el test anterior falla si falta.

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

**Buscadores** (una vez): dar de alta `https://miparte.pages.dev/` en Google Search Console y en Bing Webmaster Tools (verificación por fichero HTML en `wwwroot` o por etiqueta `meta` en `index.html`) y enviar `https://miparte.pages.dev/sitemap.xml`. Qué ve un buscador y por qué está en `index.html`: ver la fila «Posicionamiento en buscadores» de [README.md](README.md).

## APK de Android (Trusted Web Activity)

La app de Android no es una app nativa: `android/` es un proyecto mínimo generado con [Bubblewrap](https://github.com/GoogleChromeLabs/bubblewrap) que abre `https://miparte.pages.dev` a pantalla completa con Chrome (Trusted Web Activity). Usa la misma PWA, la misma sesión, el mismo service worker y los mismos avisos push, y se actualiza con cada despliegue de la web: solo hay que sacar una APK nueva si cambian el nombre, el icono, los colores de arranque o el dominio.

- **Compilar y firmar**: `./scripts/compilar-apk.ps1` (Windows; usa el JDK y el SDK de Android que instala Visual Studio con la carga de trabajo de MAUI, o los de `JAVA_HOME`/`ANDROID_HOME`). Deja la APK en `android/salida/miparte.apk`. La primera vez crea `android/miparte.keystore` y pide su contraseña; `-Depuracion` compila sin clave propia para una prueba rápida.
- **Clave de firma**: `android/miparte.keystore` está en `.gitignore`. Guarda una copia y su contraseña fuera del equipo: sin ellas no se puede actualizar la app ya instalada, habría que desinstalarla y publicar otra.
- **Verificación del dominio**: Android solo quita la barra de direcciones si `https://miparte.pages.dev/.well-known/assetlinks.json` declara el paquete `dev.pages.miparte.twa` y la huella SHA-256 de la clave. El script escribe la huella en `src/Web/wwwroot/.well-known/assetlinks.json`; hay que subir ese cambio a `master` y esperar al despliegue antes de probar la APK. `tests/pwa/probar.mjs` comprueba que el fichero se sirve como JSON, y el service worker no lo precarga. Si la app se publicara en Google Play, que vuelve a firmarla, hay que añadir también la huella de Play.
- **Cambiar la configuración**: edita `android/twa-manifest.json` y regenera con `npx @bubblewrap/cli update` desde `android/` (necesita `~/.bubblewrap/config.json` con `jdkPath` y `androidSdkPath`). La regeneración reescribe `app/build.gradle`: hay que volver a poner `buildToolsVersion "36.0.0"` y `versionName`, y subir `appVersionCode` en cada APK que se distribuya. `bubblewrap build` no acepta el SDK de Visual Studio; por eso se compila con Gradle desde el script.
- **Requisitos en el móvil**: Android 7 o superior con Chrome; sin un navegador compatible, la app se abre como pestaña con barra de direcciones.

## Disponibilidad y estadísticas

### Caídas y tiempos de respuesta (UptimeRobot)

Tres monitores HTTP(s) con intervalo de 5 minutos y alerta por correo:

| Monitor | URL | Qué detecta |
|---|---|---|
| Web | `https://miparte.pages.dev/` | Cloudflare Pages no sirve el front |
| Core | `https://<miparte-core>.onrender.com/health/listo` | Core.Api caída **o** sin base de datos (503) |
| Asistente | `https://<miparte-assistant>.onrender.com/health` | Assistant.Api caída |

En el de Core, el tipo «Keyword» con la palabra `"ok"` es más estricto que solo el código 200. El historial de disponibilidad (%) y el
tiempo de respuesta de cada comprobación salen en el panel de UptimeRobot; con «Status page» se publica una página de estado.

**Plan gratuito de Render**: los servicios se duermen tras un rato sin peticiones y la primera tarda decenas de segundos en
despertar. Un monitor cada 5 minutos los mantiene despiertos (sin arranques en frío), pero consume horas de instancia de la cuota
mensual del plan para cada servicio que vigile; revisa la cuota en el panel de Render. Si no alcanza, sube el intervalo o vigila
solo Core: así el tiempo medido por UptimeRobot también incluirá los arranques en frío que sufre el usuario.

### Tiempos dentro de la API (logs de Render)

Core.Api y Assistant.Api escriben una línea por petición (categoría `MiParte.Peticiones`; `/health` no se registra):

```text
Petición GET /api/gastos -> 200 en 84 ms
Servicio listo 2310 ms después de arrancar el proceso
```

Solo método, plantilla de ruta (sin ids), código y milisegundos: nada del usuario. Desde `Registro__PeticionLentaMs` (por defecto
1000) la línea sale como aviso (`warn`), así que buscar `warn` en los logs de Render lista las peticiones lentas. Cada «Servicio
listo» es un arranque (en el plan gratuito, normalmente un arranque en frío). Para silenciarlo:
`Logging__LogLevel__MiParte.Peticiones=Warning` deja solo las lentas.

### Usuarios y uso (SQL Editor de Supabase)

Las altas salen de Supabase Auth, que es la fuente fiable. Guarda estas consultas en el SQL Editor; solo devuelven recuentos:

```sql
-- Altas por semana, confirmadas y activas (han entrado en los últimos 30 días)
select date_trunc('week', created_at)::date as semana, count(*) as altas,
       count(*) filter (where email_confirmed_at is not null) as confirmadas,
       count(*) filter (where last_sign_in_at > now() - interval '30 days') as activas_30d
from auth.users group by 1 order by 1;

-- Hogares creados por semana
select date_trunc('week', creado_en)::date as semana, count(*) as hogares from public.hogar group by 1 order by 1;

-- Gastos por mes del gasto (gasto no guarda cuándo se registró, solo su fecha)
select date_trunc('month', fecha)::date as mes, count(*) as gastos, count(distinct hogar_id) as hogares
from public.gasto group by 1 order by 1;
```

## Supabase Auth

Site URL `https://miparte.pages.dev` y Redirect URLs `https://miparte.pages.dev/**` (el botón de Google vuelve a `/login`).
