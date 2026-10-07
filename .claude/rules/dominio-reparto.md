---
paths:
  - "src/Core/Core.Domain/**"
  - "src/Core/Core.Tests/**"
---
# Dominio: reparto y liquidación

Reglas de `docs/diseno-y-decisiones.md` (§ Reglas de reparto) que el código no puede romper:

- Importes en `decimal` (o céntimos enteros), **nunca** `float`/`double`. Redondeo a 2 decimales y el **último miembro absorbe el céntimo sobrante**: la suma del reparto siempre es el importe del gasto.
- Modos: `porcentaje` (suma 100), `partes` (pesos), `cuenta_comun` (el gasto lo asume la cuenta común: sin reparto entre personas, sin deuda y fuera de la liquidación) e `individual` (100 % de quien paga). No se guardan ingresos.
- Quién **paga** y quién **asume** son datos distintos; la diferencia genera la deuda. Gastos de la cuenta común (cuando exista) no generan deuda entre personas.
- Solo adultos activos pagan y reparten; los `a_cargo` se asignan a su responsable según el perfil.
- Un gasto guardado **no se recalcula** al cambiar % o perfiles; solo con `PUT` del gasto.
- `Core.Domain` no depende de nada (ni EF, ni ASP.NET). La lógica nueva va aquí con tests, no en los endpoints.
- Todo cambio de reparto o liquidación lleva tests: modos, caso A=2X/B=X (900 € → 600/300), reparto 2:1, céntimo sobrante, y que un gasto guardado no cambie.
