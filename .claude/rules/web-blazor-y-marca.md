---
paths:
  - "src/Web/**"
  - "src/Web.Tests/**"
---
# Front Blazor WASM y marca

Guía completa de marca en `docs/marca.md`; maqueta de referencia en `docs/referencia/maqueta.html`.

- Estilos solo con los tokens de `wwwroot/css/app.css` (variables CSS, **sin Bootstrap**). No pongas hex sueltos en componentes: usa variables y respeta el modo oscuro.
- Fraunces solo en logotipo y titulares grandes; Manrope en el resto. Importes con cifras tabulares.
- Estado (ok/err/warn) nunca en burdeos ni naranja y siempre con texto o signo, no solo color. Contraste AA; controles táctiles ≥ 36-42 px; usable a 375 px de ancho.
- Cada miembro = color + inicial (`Avatar`, `ColorMiembro`, `--m0..--m7`).
- Patrón de página: la página se envuelve en `<RequiereHogar>` y delega en un componente `Vista*` que carga los datos; las protegidas llevan `[Authorize]`.
- `wwwroot/appsettings.json` es público: **nunca secretos**, solo `Supabase:Url`, `Supabase:AnonKey` (anon), `Api:CoreUrl`.
- Cada pantalla nueva con comportamiento lleva tests bUnit en `src/Web.Tests` usando `ApiFalsa`.
- Para ver el resultado usa el perfil `web` de `.claude/launch.json` (puerto 5211) y comprueba en claro, oscuro y móvil.
- Lógica fuera de las vistas: el estado y las llamadas a la API de una pestaña van en un modelo de `src/Web/Modelos` (patrón `ModeloGastos`: sin Blazor, con test sobre `ApiFalsa`; ya hay uno por pestaña principal) y las reglas de presentación en clases de `src/Web/Reparto`; la vista solo enlaza, pinta y gestiona lo que depende del DOM. Un importe se interpreta siempre con `EntradaImporte`.
- Modo demo: `ServidorDemo` ejecuta los `Servicio*` reales de `Core.Application` sobre `RepositoriosEnMemoria`; no añadas reglas ni validaciones en la demo, cámbialas en `Core.Domain`/`Core.Application`. Si añades un endpoint a `CoreApiClient`, enrútalo en `ServidorDemo.ResponderAsync`.
