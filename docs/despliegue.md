# Despliegue gratuito

Front en Cloudflare Pages, Core.Api en Render y datos/Auth en Supabase (planes gratuitos).

| Pieza | Dónde | Cómo se publica |
|---|---|---|
| Front (`src/Web`) | Cloudflare Pages, proyecto `miparte` | `.github/workflows/deploy-web.yml` en cada push a `master` que toque el front |
| Core.Api | Render, servicio `miparte-core` | Blueprint `render.yaml`; redespliega al pasar CI en `master` |
| Esquema | Supabase | Migraciones de `supabase/migrations` aplicadas a mano (`supabase db push`) **antes** de desplegar el código que las usa |

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
4. Si el log muestra un error de certificado o de nombre, vuelve a `Require` mientras lo revisas: `VerifyFull` exige que el nombre del host coincida con el del certificado. Con `VerifyCA` solo se valida la cadena.

## Errores 500

Core.Api responde `500 { "error": "Error interno del servidor." }` ante una excepción no controlada, con las cabeceras CORS ya aplicadas, y deja la traza en los logs de Render. Si el navegador muestra un 500, el motivo real está ahí, no en la consola del navegador.

## Front en Cloudflare Pages

Secretos de GitHub `CLOUDFLARE_API_TOKEN` y `CLOUDFLARE_ACCOUNT_ID`, y variable `CORE_URL` con la URL de Core.Api. El workflow la usa para `Api:CoreUrl` y para la CSP (`src/Web/cloudflare/_headers.template`, equivalente de `default.conf.template`).

## Supabase Auth

Site URL `https://miparte.pages.dev` y Redirect URLs `https://miparte.pages.dev/**` (el botón de Google vuelve a `/login`).
