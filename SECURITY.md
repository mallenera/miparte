# Política de seguridad

«Mi parte, tu parte» es un proyecto fin de máster que gestiona datos económicos de hogares reales. Agradecemos cualquier aviso responsable sobre fallos de seguridad.

## Versiones con soporte

Solo se mantiene la versión desplegada desde la rama `master` (web en `https://miparte.pages.dev`, Core.Api y Assistant.Api) y la APK de Android, que abre esa misma web. Las ramas antiguas y las copias desplegadas por terceros no reciben correcciones.

| Versión | Soporte |
| ------- | ------- |
| `master` (desplegada) | Sí |
| Cualquier otra | No |

## Cómo informar de una vulnerabilidad

**No abras un issue público ni un pull request** con los detalles de la vulnerabilidad.

Usa el aviso privado de GitHub: pestaña **Security → Report a vulnerability** de este repositorio (o directamente <https://github.com/mallenera/miparte/security/advisories/new>). Si no puedes usarlo, abre un issue sin detalles pidiendo un canal de contacto privado.

Incluye, en la medida de lo posible:

- Componente afectado (front Blazor, Core.Api, Assistant.Api, base de datos/RLS, APK).
- Descripción del problema y su impacto (p. ej. acceso a datos de otro hogar, escalada de permisos).
- Pasos para reproducirlo, peticiones HTTP de ejemplo o prueba de concepto.
- Versión o commit con el que lo has comprobado.

## Qué puedes esperar

- Acuse de recibo en un plazo de **7 días**.
- Una primera valoración (si se acepta, gravedad y plan) en un plazo de **14 días**.
- Aviso cuando la corrección esté desplegada y, si lo deseas, mención en el aviso de seguridad publicado.

Como es un proyecto académico mantenido por una sola persona, los plazos son orientativos. No hay programa de recompensas.

## Alcance

Nos interesan especialmente:

- Acceso a datos de un hogar ajeno (fallos en RLS, en el filtro global por `hogar_id` o en la resolución de `X-Hogar-Id`).
- Saltarse los permisos por miembro (`RequierePermiso`) o los roles.
- Validación del JWT de Supabase o gestión de la sesión en el front.
- Tokens de invitación o de enlaces QR de la lista de la compra (reutilización, predicción, fuga).
- Inyección de instrucciones en el asistente que le haga revelar datos de otros hogares o salirse de sus funciones de solo lectura.
- Saltarse la CSP o las cabeceras de seguridad del front.

Fuera de alcance:

- Vulnerabilidades en servicios de terceros (Supabase, Cloudflare Pages, Render, Anthropic, Resend); infórmalas a su proveedor.
- Ataques de denegación de servicio, pruebas de carga o envío masivo de formularios.
- Ingeniería social o ataques físicos.
- Informes de escáneres automáticos sin una explotación demostrada.
- Ausencia de cabeceras o buenas prácticas sin impacto concreto.

## Normas para la investigación

- Usa solo cuentas y hogares propios; no accedas, modifiques ni borres datos de otras personas.
- Si accedes por accidente a datos ajenos, detente, no los guardes y avísanos.
- No hagas pruebas que degraden el servicio.
- Da un plazo razonable para corregir antes de publicar nada.

Si actúas de buena fe y respetas estas normas, no emprenderemos ninguna acción contra ti.

## Secretos

El repositorio no debe contener secretos: las claves (`ANTHROPIC_API_KEY`, `Supabase__JwtSecret`, cadena de conexión, clave de Resend, clave de firma de la APK) se pasan por variables de entorno y el `.env` está ignorado por git. Si encuentras un secreto publicado por error, avísanos por el canal privado para rotarlo.
