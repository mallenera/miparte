---
paths:
  - "src/Core/Core.Api/**"
  - "src/Contracts/**"
---
# Core.Api y contratos

- Minimal API organizada por carpeta de dominio (`Hogares`, `Miembros`, `Reparto`, `Gastos`). Cada grupo expone `MapXxx` y exige autorización.
- Endpoints que funcionan sin hogar (hogares, `/api/yo`, aceptar invitación) llevan el metadato `SinHogarActual`; el resto exige hogar resuelto por `HogarActualMiddleware`.
- Validación: errores `{ "error": "mensaje en español" }` con 400/403/404/409. Importes > 0 y máx. 2 decimales; meses `YYYY-MM`; recurrentes `diaMes` 1-28.
- Roles y permisos: el rol solo fija la plantilla de permisos; cada adulto con cuenta puede tener los que se le asignen (también un admin); siempre queda un admin activo y vinculado y alguien con `permisos.gestionar`. Las escrituras exigen un permiso del catálogo con `.RequierePermiso(...)` (403 sin él); sin lista propia, el admin los tiene todos.
- El token de invitación en claro solo se devuelve al crearla; en BD solo el hash.
- Los DTOs compartidos con el front viven en `src/Contracts` y se serializan en camelCase.
- **Al cambiar un endpoint o DTO actualiza `docs/api.md`** en el mismo cambio.
- No registres secretos, tokens ni cadenas de conexión en logs ni en el repo (`ConnectionStrings__Default`, `Supabase__JwtSecret` solo por entorno).
