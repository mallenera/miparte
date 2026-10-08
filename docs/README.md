# Documentación del proyecto

Punto de entrada a todo lo que define *Mi parte, tu parte*. Léelo antes de tocar dominio, esquema o interfaz.

| Documento | Para qué sirve | Fuente de verdad de |
|---|---|---|
| [diseno-y-decisiones.md](diseno-y-decisiones.md) | Objetivo, alcance MVP, reglas de reparto, cuenta común, chatbot, arquitectura, decisiones D1-D7, plan y riesgos | Decisiones de producto |
| [modelo-de-datos.md](modelo-de-datos.md) | Tablas, relaciones e invariantes del esquema | Modelo (junto a `supabase/migrations`) |
| [marca.md](marca.md) | Logotipo, paleta, tipografía y reglas de uso | Imagen de marca |
| [api.md](api.md) | Referencia de endpoints y DTOs de `Core.Api` para el front | Contrato HTTP |
| [referencia/maqueta.html](referencia/maqueta.html) | Maqueta interactiva con datos de ejemplo y la lógica de reparto/liquidación/cuenta común en JS | Comportamiento esperado de la UI |
| [referencia/identidad-de-marca.dc.html](referencia/identidad-de-marca.dc.html) | Hoja de identidad original (código fuente del lienzo de diseño) | Variantes del logotipo |

Los dos ficheros de `referencia/` son exportaciones de los artefactos de claude.ai «Mi parte, tu parte» (maqueta) y «Mi parte tu parte — Identidad»; el segundo necesita el runtime del lienzo para renderizarse, así que para consultar colores y logotipo usa [marca.md](marca.md). Se abren con doble clic (la maqueta, en cualquier navegador).

## Jerarquía cuando hay conflicto

1. `supabase/migrations/*.sql` y el código mandan sobre la documentación.
2. `docs/api.md` describe el contrato real; se actualiza en el mismo cambio que un endpoint o DTO.
3. `diseno-y-decisiones.md` es la intención de producto; si el código se aparta a propósito, anótalo aquí abajo.

## Estado: diseño frente a código

Actualizado el 2026-10-07. Mantén esta tabla al día cuando cierres o cambies algo.

| Tema del diseño | Estado en el código |
|---|---|
| Ingresos del hogar **no se guardan**; perfiles = individual, porcentajes, partes y cuenta común | **Hecho** (sin commitear): migración `20261007000000_perfiles_cuenta_comun_sin_ingresos.sql` (elimina `ingreso`, sustituye el modo `ingresos` por `cuenta_comun`, añade `gasto.a_cargo_cuenta_comun`; no ejecutada contra Postgres real en local). Un gasto con perfil de cuenta común no se reparte ni genera deuda. |
| Login y registro con verificación de correo | **Hecho**: registro en dos pasos (cuenta → código de 6 dígitos por correo, `POST /verify` con `type=signup`, reenvío con `/resend`), contraseña de mín. 8 caracteres con letra y número, botón de ojo para mostrarla (`CampoContrasena`). Requiere `enable_confirmations`, **SMTP propio** en el proyecto alojado (el SMTP predeterminado de Supabase solo envía a miembros del equipo) y la plantilla `supabase/templates/confirmacion.html` (usa `{{ .Token }}`); en un proyecto Supabase real hay que pegarla en Authentication → Emails → Confirm signup. |
| Cuenta común: aportaciones, saldo, reembolsos | **Hecho** (sin commitear): migración `20261008000000_cuenta_comun_aportaciones_reembolsos.sql` (no ejecutada contra Postgres real en local), `Core.Domain/CuentaComun.cs`, endpoints `/api/cuenta-comun` (`docs/api.md` §3.9) y tests. Front hecho: pestaña «Cuenta común» (`VistaCuentaComun`: saldo, efectivo, aportaciones por adulto y reembolsos). El formulario de gastos ofrece «Cuenta común» como pagador si hay aportaciones (migración `20261009000000_gasto_pagado_por_cuenta_comun.sql`, `gasto.pagado_por` nulo). **Pendiente**: indicador «a cargo de la cuenta» por categoría, activación por hogar y «su parte» por persona; resumen con saldos de la cuenta. |
| Categorías con subcategorías y perfil por defecto | Esquema con `categoria_padre_id`; la UI aún no gestiona subcategorías. |
| Acceso con Google | **Hecho** en el front: `BotonGoogle` en Login y Registro redirige a `/auth/v1/authorize?provider=google`; el flujo es PKCE (verificador de un solo uso guardado en el navegador; evita el CSRF de login): Supabase vuelve a `/login?code=…` y `ServicioSesion.CompletarGoogleAsync` lo canjea por la sesión. Requiere activar el proveedor Google en Supabase (Client ID/Secret de Google Cloud) y añadir la URL de la web a las Redirect URLs. |
| Gastos, recurrentes, pagos de liquidación y resumen | API hecha. Front: Gastos y pestaña Resumen (`VistaResumen`: resumen del mes por persona y categoría, saldos, transferencias sugeridas con «Registrar pago» por el importe completo y pagos registrados con borrado). Pestaña Recurrentes (`VistaRecurrentes`: plantillas con alta, edición, pausa y borrado, y botón «Generar gastos del mes», que es lo que dispara la generación porque no hay proceso en segundo plano). Faltan pago parcial/personalizado y filtros. |
| Gastos personales fuera de la liquidación, cierre de mes | Sin implementar. |
| Chatbot con tool calling (5 funciones) | `Assistant.Api` es un esqueleto (`/health`). |
| Invitación al hogar | Hecha (token de un solo uso, 7 días, hash en BD). |
| D5 «Streamlit» | **Superada**: se optó por Blazor WASM + minimal API .NET + Supabase. |
| D1/D2/D6/D7 (hosting, dominio, alcance IA, modelo de lenguaje) | Siguen pendientes de decidir en el diseño. |
