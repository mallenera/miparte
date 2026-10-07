# Imagen de marca

Una sola imagen para app, web, documentos y presentaciones. Origen: hoja de identidad v2 (`referencia/identidad-de-marca.dc.html`). Los tokens ya implementados están en `src/Web/wwwroot/css/app.css`; el logotipo en `src/Web/Componentes/Logotipo.razor` y `Simbolo.razor`.

## Nombre
- En textos: **Mi parte, tu parte**.
- En el logotipo: siempre en minúsculas, «mi parte · tu parte» (horizontal) o en dos líneas (portadas, presentaciones).

## Símbolo
Círculo partido en dos mitades desiguales (60/40) girado 45°: naranja = *mi parte*, burdeos = *tu parte*. A 32 y 16 px se usa la variante sin hueco entre mitades (la de `favicon.svg`).

```svg
<svg viewBox="0 0 100 100">
  <g transform="rotate(45 50 50)">
    <path d="M42 11 A33 33 0 0 0 42 77 Z" fill="#E8742A"/>
    <path d="M48 14 A40 40 0 0 1 48 94 Z" fill="#7A1F33"/>
  </g>
</svg>
```

Reglas de uso: margen libre = mitad del alto del símbolo; tamaño mínimo 16 px (símbolo), 120 px de ancho (horizontal) y 96 px (dos líneas); **no girar, deformar ni recolorear**. Sobre burdeos, la mitad burdeos pasa a crema `#FBF5EF` y el texto a crema con «·» y «tu parte» en `#F6B07A`.

## Colores

| Uso | Color | Claro | Oscuro |
|---|---|---|---|
| Principal, botones, barra de resumen | Burdeos | `#7A1F33` | `#E08A9C` (texto) / `#A32D40` (fondo) |
| Hover, cabeceras | Burdeos profundo | `#4A1220` | — |
| Acento, símbolo | Naranja | `#E8742A` | `#F2904A` |
| Texto naranja sobre claro | Naranja texto | `#C4551A` (en la web `#A8480F` para contraste) | `#F2904A` |
| Fondo | Crema | `#FBF5EF` | `#1C1114` |
| Superficie | Blanco | `#FFFFFF` | `#2A1A1F` |
| Texto | Tinta | `#24161A` | `#F5ECE6` |
| Texto secundario | Tinta 2 | `#6B5A5E` | `#C8A9AD` |
| Líneas | — | `#E6D8CC` | `#47282F` |
| Positivo (te deben) | Verde | `#2E7D5B` | `#7FD39B` |
| Negativo (debes) | Rojo | `#C62828` | `#FF8F86` |
| Aviso | Ámbar | `#9A6B00` | — |
| Información | Azul | `#2F5D8A` | — |

Los colores de estado **nunca** son burdeos ni naranja y **siempre** van con signo, icono o texto (no solo color).

### Color por miembro
Cada miembro tiene color + inicial (`--m0..--m7`): burdeos, naranja, verde azulado `#0F766E`, azul `#2F5DA8`, violeta `#7B3FA0`, oliva `#6B7D1E`, magenta `#C2185B`, marrón `#5C5148` (variantes claras en modo oscuro). La API no guarda el color: el front lo deriva del orden por id.

## Tipografía
- **Fraunces** (serif): solo logotipo y titulares grandes. La cursiva naranja de «tu parte» es exclusiva del logotipo.
- **Manrope**: toda la interfaz, importes incluidos (cifras tabulares `font-variant-numeric: tabular-nums`).
- Se cargan desde Google Fonts (`Fraunces` 400-600 + cursiva, `Manrope` 400-700).

## Iconos de app y PWA
Símbolo blanco/crema sobre burdeos con esquinas redondeadas (`rx` ≈ 22 %). Ya generados en `src/Web/wwwroot/` (`favicon.*`, `apple-touch-icon.png`, `icon-192/512.png`, `icon-maskable-512.png`).

## Modo oscuro
Propio, no una inversión automática: se activa con `prefers-color-scheme` y con `data-theme="dark|light"` en `<html>`.

## Adaptación a pantallas (responsive)

Tres tramos en `wwwroot/css/app.css`: móvil (hasta 640 px, usable desde 375 px), tablet (641-1023 px) y escritorio (desde 1024 px, contenido de hasta 1080 px). Los márgenes laterales respetan las muescas (`safe-area-inset-*`), los menús de la cabecera ocupan todo el ancho en móvil, los campos usan 16 px para evitar el zoom de iOS y en pantallas táctiles (`pointer: coarse`) los controles miden al menos 44 px.

## Temas

El usuario elige tema en su menú (esquina superior derecha) y se recuerda en `localStorage` (`miparte.tema`). Se aplica con el atributo `data-tema` de `<html>`; sin él se sigue al sistema (claro u oscuro). Temas: **Claro** y **Oscuro** (los de marca), **Océano** (azul petróleo y ámbar), **Bosque** (verde y dorado) y **Medianoche** (índigo oscuro). Cada tema redefine los mismos tokens de `app.css` (`--wine` es el color principal y `--orange` el de acento, sea cual sea su tono); los componentes nunca llevan colores propios. Los estados (ok/err/warn) y los colores de miembro no cambian en los temas claros, y en los oscuros usan la variante oscura de marca. Un tema nuevo = un bloque `[data-tema="..."]` en `app.css` + una entrada en `ServicioTema.Todos`.
