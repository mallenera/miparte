# Plan de acción

Estado a 2026-10-18. Este documento recoge lo que ya está hecho, lo que falta y en qué orden atacarlo. El detalle de cada tema (diseño frente a código) está en la tabla de [README.md](README.md); aquí solo se resume y se ordena.

## 1. Estado actual

- **Ramas**: todas las ramas de funcionalidad, seguridad, demo, arquitectura y documentación están fusionadas en `master` (PR #6 a #48). No hay ramas sin fusionar. Las ramas locales ya fusionadas pueden borrarse.
- **Hecho** (resumen; detalle en el README):
  - Reparto y liquidación: individual, porcentajes, partes y cuenta común; subcategorías; pagos parciales y personalizados; filtros de gastos; gastos personales fuera de la liquidación; liquidación por ciclos y periodo de viaje.
  - Cuenta común completa: aportaciones, reembolsos, ahorro, extracto mensual, saldo inicial, activación por hogar y por categoría, «su parte» y tarjeta en el resumen.
  - Cierre de mes con concurrencia controlada; gastos recurrentes; dashboard de gastos con filtro cruzado.
  - Permisos por miembro (17 permisos asignables), historial de cambios (auditoría), eliminar hogar, invitaciones por enlace.
  - Seguridad: escritura directa cerrada, límite de peticiones, cabeceras y CSP, contenedores sin privilegios, errores en ProblemDetails.
  - Login y registro con verificación por correo, modo demo local y cuenta demo real.
  - Arquitectura por capas en Core (`Core.Application` y repositorios), base de datos gestionada con DbUp (`MiParte.Database`).
  - Chatbot con cinco funciones de solo lectura (`Assistant.Api`), con el proveedor de pago actual pendiente de sustituir (ver fase 1, punto 3).
  - PWA con aviso de actualización y reintentos ante arranques lentos de Render.
  - Core.Api y Assistant.Api desplegados en Render (fase 1, punto 1).
- **Pendiente de verificación en entornos reales**: Supabase real, la cuenta demo real y la RLS (fase 1).

## 2. Plan de acción

Las fases van en orden de prioridad. Cada ítem indica su criterio de hecho.

### Fase 1 · Puesta en producción verificada (prioridad alta)

1. ~~**Desplegar Core.Api y Assistant.Api en Render.**~~ **Hecho** (2026-10-18). Pendiente solo confirmar que `/api/yo` acepta un token real en ambos (ya cubierto por el punto 4).
2. ~~**Verificar el despliegue con Docker.**~~ **Hecho** (2026-10-18): `docker compose up --build` levanta los tres servicios; web, core y assistant responden 200; nginx corre como `nginx` (uid 101) y Assistant como `app` (uid 1654); la CSP de la web llega sin hosts de Google. Comprobado en el navegador integrado: sin violaciones de CSP, fuentes cargadas y «Probar la demo» entra en Resumen. Service worker: en Chrome (DevTools, Application → Service Workers) está registrado y sin avisos; el fallo solo aparece en el navegador integrado de la app. Login con cuenta real de Supabase correcto y hogar cargado. Para que funcione hizo falta: `Cors__OrigenesPermitidos__0=http://localhost:8080` en `.env` y `CoreUrl` del front en el puerto 5001 (`d406b7a`). Levantar `docker compose up --build` (web :8080, core :5001, assistant :5002). Hecho cuando la web carga, inicia sesión y no hay violaciones de CSP en la consola.
   - **Resuelto** (2026-10-19): el aviso de «inseguro» de Docker Desktop venía de una instalación de Docker incorrecta del equipo, no de los Dockerfiles. Con la instalación corregida, el despliegue funciona sin avisos.
3. ~~**Sustituir el proveedor de pago del chatbot por una alternativa gratuita.**~~ **Hecho** (2026-10-20): Cloudflare Workers AI con `glm-4.7-flash` (commit `70aaf2a`). Sin herramientas de escritura. Detalle: elegido Cloudflare Workers AI (cuota gratuita de 10.000 Neurons/día; no usa el contenido para entrenar). Implementado `ClienteWorkersAi` con `Asistente__Proveedor=cloudflare` (`c28b1a2`), probado con un manejador falso. Configurado en Render el 2026-10-20 (`CLOUDFLARE_API_TOKEN`, `Asistente__CloudflareCuenta`, `Asistente__Proveedor=cloudflare`, `Asistente__CoreUrl` y `Cors__OrigenesPermitidos__0` en `miparte-assistant`) y `ASSISTANT_URL` en GitHub. El preflight de `/api/chat` ya devuelve el origen del front. Comprobado en producción: el chat responde con sesión iniciada desde `https://miparte.pages.dev`. Pasos en [asistente.md](asistente.md#despliegue-en-render-y-cloudflare). Detalle original: `Assistant.Api` ya abstrae el modelo tras `IClienteModelo`, así que basta una implementación nueva y un selector de proveedor (`Asistente__Proveedor`), sin tocar las funciones de consulta ni el contrato HTTP.
   - Opciones con plan gratuito (límites y condiciones se verifican antes de elegir; cambian con frecuencia):
     - **Groq** (recomendado para empezar): API compatible con OpenAI, modelos Llama de código abierto con llamadas a funciones, sin tarjeta. Límites bajos por minuto y por día.
     - **Google Gemini** (plan gratuito de la API de Gemini): buena calidad y ventana amplia de límites, pero en el plan gratuito Google puede usar lo enviado para mejorar sus productos. Revisar esa política antes de mandar datos reales del hogar.
     - **OpenRouter** con modelos `:free`: acceso a varios modelos con un solo endpoint; disponibilidad y límites variables.
     - **Ollama en local** (modelo abierto en el propio equipo): coste cero y datos fuera de terceros, pero no sirve para el despliegue en Render. Válido para desarrollo y para la demo en la defensa.
   - Recomendación: Groq para el despliegue, Ollama como alternativa local. Antes de elegir, confirmar límites y política de datos en la web del proveedor.
   - Hecho cuando las cinco funciones responden con llamadas a herramientas reales sobre un hogar, `docs/asistente.md` describe el nuevo proveedor y su configuración, y los tests siguen usando el modelo falso.
4. ~~**Configurar Supabase real.**~~ **Hecho** (2026-10-18). Migraciones aplicadas y el registro envía el código por correo (confirmado en el proyecto real). Seguridad (2026-10-18): `rls_auto_enable()` revocado para `public`, `anon` y `authenticated` (el trigger `ensure_rls` sigue activo). Leaked Password Protection: solo disponible en planes Pro de Supabase; en el plan actual queda desactivada y se acepta el riesgo. Alternativa posible sin coste: comprobación en el cliente contra la API de Pwned Passwords (k-anonymity), que exigiría añadir su host a connect-src de la CSP. Aceptado: `es_admin_hogar`, `hogar_tiene_otro_admin` y `mis_hogares` siguen ejecutables por `authenticated`, porque las políticas RLS las usan. Detalle antiguo:
   - Hecho (2026-10-18): `migrar` aplicado al proyecto real. Se crearon el journal `miparte_migraciones` y los 9 scripts pendientes; `pendientes` da 0. Estructura aplicada (72 ficheros), 28 políticas RLS, 18 tablas. Respaldo previo: `miparte-antes-de-migrar.dump` (carpeta temporal de sesión, no en el repo).
   - Advisors de seguridad: Leaked Password Protection desactivada (activarla en Auth); `rls_auto_enable()` ejecutable por `anon` y `authenticated` (revocar); `es_admin_hogar`, `hogar_tiene_otro_admin` y `mis_hogares` son SECURITY DEFINER para `authenticated` (intencionado por RLS; revisar que `anon` no las tenga).
   - Aplicar el esquema con `migrar` y revisar `pendientes`.
   - SMTP propio, `enable_confirmations` y la plantilla `supabase/templates/confirmacion.html`. Para «He olvidado la contraseña», pegar también `supabase/templates/recuperacion.html` en Authentication → Email Templates → Reset password; debe usar el código {{ .Token }}, no el enlace.
   - Redirect URLs de la web (sin el proveedor Google, ver fase 2).
   - Hecho cuando un registro nuevo recibe el código por correo.
5. ~~**Cuenta demo real.**~~ **Hecho** (2026-10-18): en el proyecto real existe el hogar «Casa de Ana y Marcos» (id fijo), la tarea `restablecer-demo` (diaria, 04:00 UTC) está activa con las últimas ejecuciones correctas y la cuenta entra con la contraseña nueva (`b0db25c`). Pendiente opcional: el workflow manual con `DEMO_DB_URL`. Ejecutar `supabase/seed/demo.sql` y `demo_cron.sql` (pg_cron, diario 04:00 UTC) y probar el workflow manual `restablecer-demo.yml`. Hecho cuando el restablecimiento deja la cuenta como la semilla.
   - **Qué hace falta**: las migraciones ya aplicadas en Supabase (la semilla escribe en las tablas del hogar); crear la cuenta `demo` con su contraseña, o dejar que la semilla la cree (la semilla borra y recrea el usuario de Auth).
   - **Dónde**: SQL Editor de Supabase (pegar `demo.sql`, después `demo_cron.sql`) o `psql "$DEMO_DB_URL" -f` con la cadena del Session pooler (puerto 5432) en una variable de entorno.
   - **Extensión**: activar `pg_cron` en Database → Extensions antes de `demo_cron.sql`.
   - **Comprobar**: `select * from cron.job where jobname = 'restablecer-demo'` y `cron.job_run_details`; entrar en la web con `demo`/`demo` y ver el hogar «Casa de Ana y Marcos».
   - **Workflow manual**: crear el entorno `demo` en GitHub y el secreto `DEMO_DB_URL` (opcional; el restablecimiento diario ya funciona sin él).
   - **Riesgo a tener en cuenta**: restablecer recrea el usuario de Auth, así que quien esté dentro tiene que volver a entrar. Un proyecto pausado por inactividad no ejecuta el cron.
6. ~~**Comprobar la RLS en Supabase real.**~~ **Hecho** (2026-10-18) en el proyecto real, con dos cuentas (A: hogares Beibis y Truca's Home; B: solo Truca's Home). Resultados, simulando el rol `authenticated` con su `sub` dentro de una transacción deshecha: B no ve el hogar Beibis ni sus gastos, miembros, reparto, categorías ni auditoría (0 filas); B sí ve Truca's Home (control). A ve sus dos hogares y el demo no le aparece. El rol `authenticated` solo tiene SELECT en las tablas de public; `anon` no tiene SELECT. Pendiente opcional: probar los 403 de Core.Api y Assistant.Api con tokens reales. Con dos hogares de prueba, confirmar que un usuario no lee ni escribe datos del otro (riesgo recogido en `docs/diseno-y-decisiones.md`). Hecho cuando el resultado queda anotado en `docs/`.
   - **Qué hace falta**: dos cuentas reales (A y B) con un hogar cada una, creadas por el registro de la web.
   - **Pruebas**:
     - Con el JWT de A, `GET /rest/v1/gasto?hogar_id=eq.<hogar de B>` debe devolver `[]`, y no debe poder insertar ni borrar en tablas de B (PostgREST).
     - Con el JWT de A, `GET /api/gastos` de Core.Api con `X-Hogar-Id` de B debe responder 403.
     - Con el JWT de A y `X-Hogar-Id` de B en Assistant.Api, la respuesta debe ser 403 y el modelo no debe llamarse.
   - Anotar las respuestas (no los tokens) en este documento.

### Fase 2 · Seguridad y limpieza de acceso (prioridad media)

7. ~~**Quitar de momento el acceso con Google.**~~ **Hecho** (2026-10-18): interruptor `FuncionesAcceso.GoogleActivo = false` (`src/Web/Autenticacion/FuncionesAcceso.cs`) que oculta el botón en Login y Registro; el flujo PKCE, `ServicioSesion.CompletarGoogleAsync` y la configuración se mantienen para reactivarlo poniendo el interruptor a `true`. `dotnet test src/Web.Tests`: 450 correctas. Hecho cuando la pantalla de acceso no muestra el botón y la fila de Google del README dice «oculto de momento».
8. ~~**Límites y cabeceras en Assistant.Api.**~~ **Hecho** (2026-10-18, `feature/fase2-seguridad-residual`): `ReenvioCabeceras` pasa a `src/BuildingBlocks/Seguridad` (`MiParte.Seguridad`) y Assistant.Api lo usa con `ProxyInverso__*`. El límite por minuto y por usuario ya existía; `ProxyInversoTests` cubre el arranque. Añadir `ForwardedHeaders` y límite de peticiones como en Core.Api (`LimitacionPeticiones`). Hecho cuando hay tests y el contrato 429 está en `docs/asistente.md`.
9. ~~**Quitar Google Fonts de la CSP.**~~ **Hecho** (2026-10-18): Fraunces (normal e itálica) y Manrope, en `src/Web/wwwroot/fonts` (latín, peso variable, licencia OFL), declaradas en `app.css`; sin hosts de fuentes en `index.html`, CSP ni `_headers`. Los `.woff2` se añaden al caché sin conexión del service worker. Pendiente: comprobar la pantalla en el navegador con el publish. Servir Fraunces y Manrope desde el propio sitio y retirar el dominio externo de `default.conf.template`. Hecho cuando la CSP no lista hosts de fuentes.
10. ~~**Revisar `HogarId!` en Core.Api.**~~ **Hecho** (2026-10-18): los cinco endpoints de escritura comprueban `HogarId` y responden 409 «No hay hogar seleccionado.» sin supresión. `dotnet test src/Core/Core.Tests`: 565 correctas, 42 omitidas (pruebas con PostgreSQL sin `MIPARTE_TEST_DB`). Sustituir el operador de supresión por una comprobación explícita o documentar por qué es seguro. Hecho cuando no quedan supresiones sin justificar.

11. ~~**Comprobar contraseñas filtradas sin plan Pro.**~~ **Hecho** (2026-10-19, probado en la web: una contraseña vulnerable da error): `ComprobadorContrasenas` consulta Pwned Passwords con el prefijo SHA-1 al registrarse (`Registro.razor`) y rechaza contraseñas filtradas; si la API no responde, deja pasar. La app no tiene cambio de contraseña, así que solo cubre el registro. Host añadido a `connect-src` en nginx y Cloudflare. Tests en `Web.Tests`: 455 correctas. Detalle original: Leaked Password Protection de Supabase exige plan Pro, así que se hace en la app: al registrarse y al cambiar la contraseña, consulta a la API de Pwned Passwords con solo los cinco primeros caracteres del hash SHA-1 (k-anonymity) y rechaza las contraseñas filtradas. Requiere añadir ese host a `connect-src` de `default.conf.template` y `_headers.template`, y tests con un manejador falso. Si la API no responde, se permite la contraseña y se avisa en el log (no se bloquea el registro por un fallo externo). Hecho cuando una contraseña de la lista se rechaza con mensaje en español y el resto pasan.

    - Revisión de CSP en el navegador (2026-10-19): sin violaciones en las pantallas visitadas (acceso, resumen, gastos, dashboard, recurrentes, categorías, cuenta común, configuración, historial). Google Fonts bloqueado; Pwned Passwords y Supabase accesibles; sin scripts inline; el único atributo `style` es el que pone el propio runtime de Blazor en `<html>`. Limitación: la consola del navegador integrado no conserva los errores de carga de cada página.

### Fase 3 · Decisiones de producto (las toma la pareja, no el código)

12. **Cerrar D1 (alojamiento) y D2 (base de datos y login).** En la práctica ya son Render y Supabase; falta marcarlas como decididas en `docs/diseno-y-decisiones.md`.
13. **Cerrar D3 y D4 (reparto del hijo y de la alimentación).** El perfil propio ya permite cualquier reparto; basta con que la pareja fije sus perfiles y se anote.
14. **Cerrar D7 (coste y dominio) y unificar la numeración.** `docs/README.md` marca D7 como resuelto (modelo de lenguaje) y este plan lo usaba para coste y dominio. Decidir presupuesto del chatbot (ahora gratuito, ver fase 1) y dominio; comprobar en la OEPM que la marca y el dominio están libres.

### Fase 4 · Funcionalidad pendiente (por utilidad)

15. **Notificaciones push del navegador.** Opt-in por usuario desde la PWA. Avisos cuando alguien agrega un gasto (a los demás miembros) y cuando se cierra una liquidación (ciclo o periodo), y al registrar un pago de liquidación. Diseño:
    - Tabla `suscripcion_push` (miembro, endpoint y claves) en `Estructura/`, con RLS y filtro por hogar.
    - `POST`/`DELETE /api/notificaciones/suscripcion` en Core.Api.
    - Envío desde Core.Application en segundo plano, sin hacer fallar la petición; claves VAPID en variables de entorno; librería WebPush de .NET.
    - Manejador `push` y clic en el service worker. Sin cambios de CSP.
    - Límite: en iOS solo con la PWA instalada. La cuenta demo no envía avisos.
    - Hecho cuando un gasto nuevo llega como notificación a otro miembro, y el aviso se puede activar y desactivar.
16. **Recurrentes que generen gastos personales** (pendiente en la fila de gastos personales).
17. **Asistente**: streaming de respuestas y chat en la cuenta demo.

18. **Alta de gastos por chat, con confirmación.** Herramienta de escritura en `Assistant.Api` que propone un gasto (concepto, importe, fecha, categoría, pagador y reparto) y solo lo guarda tras una confirmación explícita de la persona (segunda llamada con botón «Confirmar»). Usa el JWT y el hogar de la persona, así que Core.Api aplica sus permisos. El modelo no fija el importe ni el reparto definitivos: los muestra y los confirma la persona. Decisiones antes de empezar: campos mínimos que pide el chat, si el reparto sale del perfil de la categoría, y si la auditoría registra el alta como hecha por el asistente. Hecho cuando «apunta 12 € de café» deja un borrador, no guarda nada sin «Confirmar» y el gasto aparece en Gastos con el autor correcto.
19. **Extras del diseño**: presupuestos con alertas y exportación a Excel o PDF; categorización automática de gastos (D6, extra).
20. **Mejoras «Posterior»** del diseño, cada una con su propio análisis antes de empezar:
    - Avisos por correo (resumen mensual y recibos próximos).
    - Reembolsos y devoluciones ligados a un gasto.
    - Copia de seguridad y exportación completa.
    - Resumen anual.
    - Previsión de gasto fijo.
    - Foto del ticket adjunta (sin lectura automática).
    - Etiquetas transversales a las categorías.
    - Alta de gastos por chat, con confirmación antes de guardar.
    - Explicación del cambio de balance entre meses.
    - Simulador de reparto.
    - Reactivar el acceso con Google (fase 2, punto 7) cuando haya tiempo para revisarlo.

Fuera de alcance declarado: objetivos de ahorro e intereses.

### Fase 5 · Entrega del proyecto fin de máster

21. **Memoria**: incluir arquitectura por capas, decisiones D1-D7, pruebas (unitarias, PostgreSQL real, bUnit) y las líneas futuras de la fase 4.
22. **Reproducibilidad**: README de despliegue y de arranque local (`docs/despliegue.md`, `docs/demo.md`) revisado contra lo que se haya probado en la fase 1.
23. **Calidad final**: `dotnet test` completo y revisión de los comentarios XML de producción según `.claude/rules/idioma-y-documentacion.md`.

## 3. Orden sugerido

Fase 1 (los puntos 2 a 6 en el orden en que se desbloqueen; el 5 y el 6 necesitan Supabase real) → fase 3 en paralelo, porque son decisiones y no dependen del código → fase 2 → fase 4 según tiempo → fase 5 al final.
