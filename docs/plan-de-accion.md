# Plan de acción

Estado a 2026-10-17. Este documento recoge lo que ya está hecho, lo que falta y en qué orden atacarlo. El detalle de cada tema (diseño frente a código) está en la tabla de [README.md](README.md); aquí solo se resume y se ordena.

## 1. Estado actual

- **Ramas**: todas las ramas de funcionalidad, seguridad, demo, arquitectura y documentación están fusionadas en `master` (PR #6 a #40). No hay ramas sin fusionar. Las ramas locales `feature/*`, `fix/*` y `refactor/*` que ya aparecen como fusionadas pueden borrarse.
- **Hecho** (resumen; detalle en el README):
  - Reparto y liquidación: individual, porcentajes, partes y cuenta común; subcategorías; pagos parciales y personalizados; filtros de gastos; gastos personales fuera de la liquidación.
  - Cuenta común completa: aportaciones, reembolsos, ahorro, extracto mensual, saldo inicial, activación por hogar y por categoría, «su parte» y tarjeta en el resumen.
  - Cierre de mes con concurrencia controlada; gastos recurrentes; dashboard de gastos con filtro cruzado.
  - Permisos por miembro (17 permisos asignables), historial de cambios (auditoría), eliminar hogar, invitaciones por enlace.
  - Seguridad: escritura directa cerrada, límite de peticiones, cabeceras y CSP, contenedores sin privilegios, errores en ProblemDetails.
  - Login y registro con verificación por correo, acceso con Google (PKCE), modo demo local y cuenta demo real.
  - Arquitectura por capas en Core (`Core.Application` y repositorios), base de datos gestionada con DbUp (`MiParte.Database`).
  - Chatbot con cinco funciones de solo lectura (`Assistant.Api`).
  - PWA con aviso de actualización y reintentos ante arranques lentos de Render.
- **Pendiente de verificación en entornos reales**: nada de lo anterior se ha probado todavía en Supabase real, Render, Docker ni con la API real de Anthropic (ver fase 1).

## 2. Plan de acción

Las fases van en orden de prioridad. Cada ítem indica su criterio de hecho.

### Fase 1 · Puesta en producción verificada (prioridad alta)

1. **Desplegar Core.Api y Assistant.Api en Render.** Configurar `ConnectionStrings__Default`, `Supabase__Url`, `Supabase__JwtSecret`, `Cors__OrigenesPermitidos__N`, `ProxyInverso__*` y `ANTHROPIC_API_KEY` (solo en Assistant). Hecho cuando `/health` responde 200 y `/api/yo` acepta un token real.
2. **Verificar el despliegue con Docker.** Levantar `docker compose up --build` (web :8080, core :5001, assistant :5002) y comprobar nginx sin privilegios. Hecho cuando la web carga, inicia sesión y no hay violaciones de CSP en la consola.
3. **Probar el chatbot con la API real de Anthropic.** Las cinco funciones con datos de un hogar real, y los límites de historial, bucle y preguntas por minuto. Hecho cuando las respuestas cuadran con las cifras de Resumen y `docs/asistente.md` refleja lo probado.
4. **Configurar Supabase real.**
   - Aplicar el esquema con `migrar` y revisar `pendientes`.
   - SMTP propio, `enable_confirmations` y la plantilla `supabase/templates/confirmacion.html`.
   - Proveedor Google (Client ID/Secret) y Redirect URLs.
   - Hecho cuando un registro nuevo recibe el código por correo y el acceso con Google completa el flujo.
5. **Cuenta demo real.** Ejecutar `supabase/seed/demo.sql` y `demo_cron.sql` (pg_cron, diario 04:00 UTC) y probar el workflow manual `restablecer-demo.yml`. Hecho cuando el restablecimiento deja la cuenta como la semilla.
6. **Comprobar la RLS en Supabase real.** Con dos hogares de prueba, confirmar que un usuario no lee ni escribe datos del otro (riesgo recogido en `docs/diseno-y-decisiones.md`). Hecho cuando el resultado queda anotado en la memoria o en `docs/`.

### Fase 2 · Seguridad residual (prioridad media)

7. **Límites y cabeceras en Assistant.Api.** Añadir `ForwardedHeaders` y límite de peticiones como en Core.Api (`LimitacionPeticiones`). Hecho cuando hay tests y el contrato 429 está en `docs/asistente.md`.
8. **Quitar Google Fonts de la CSP.** Servir Fraunces y Manrope desde el propio sitio y retirar el dominio externo de `default.conf.template`. Hecho cuando la CSP no lista hosts de fuentes.
9. **Revisar `HogarId!` en Core.Api.** Sustituir el operador de supresión por una comprobación explícita o documentar por qué es seguro. Hecho cuando no quedan supresiones sin justificar.

### Fase 3 · Decisiones de producto (las toma la pareja, no el código)

10. **Cerrar D1 (alojamiento) y D2 (base de datos y login).** En la práctica ya son Render y Supabase; falta marcarlas como decididas en `docs/diseno-y-decisiones.md`.
11. **Cerrar D3 y D4 (reparto del hijo y de la alimentación).** El perfil propio ya permite cualquier reparto; basta con que la pareja fije sus perfiles y se anote.
12. **Cerrar D7 (coste y dominio).** Decidir presupuesto de la API de Anthropic y dominio. Comprobar en la OEPM que la marca y el dominio están libres (pendiente desde `diseno-y-decisiones.md`).

### Fase 4 · Funcionalidad pendiente (por utilidad)

13. **Recurrentes que generen gastos personales** (pendiente en la fila de gastos personales).
14. **Asistente**: streaming de respuestas y chat en la cuenta demo.
15. **Extras del diseño**: presupuestos con alertas y exportación a Excel o PDF; categorización automática de gastos (D6, extra).
16. **Mejoras «Posterior»** del diseño, cada una con su propio análisis antes de empezar:
    - Reembolsos y devoluciones ligados a un gasto.
    - Copia de seguridad y exportación completa.
    - Avisos por correo (resumen mensual y recibos próximos).
    - Resumen anual.
    - Previsión de gasto fijo.
    - Foto del ticket adjunta (sin lectura automática).
    - Etiquetas transversales a las categorías.
    - Alta de gastos por chat, con confirmación antes de guardar.
    - Explicación del cambio de balance entre meses.
    - Simulador de reparto.

Fuera de alcance declarado: objetivos de ahorro e intereses.

### Fase 5 · Entrega del proyecto fin de máster

17. **Memoria**: incluir arquitectura por capas, decisiones D1-D7, pruebas (unitarias, PostgreSQL real, bUnit) y las líneas futuras de la fase 4.
18. **Reproducibilidad**: README de despliegue y de arranque local (`docs/despliegue.md`, `docs/demo.md`) revisado contra lo que se haya probado en la fase 1.
19. **Calidad final**: `dotnet test` completo y revisión de los comentarios XML de producción según `.claude/rules/idioma-y-documentacion.md`.

## 3. Orden sugerido

Fase 1 (sobre todo 1, 4 y 5, que bloquean la demo) → fase 3 en paralelo, porque son decisiones y no dependen del código → fase 2 → fase 4 según tiempo → fase 5 al final.
