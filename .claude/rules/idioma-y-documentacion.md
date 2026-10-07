# Idioma, estilo y documentación

- Código de dominio, esquema SQL, comentarios, mensajes de error de API y texto de UI en **español**. Nombres de dominio fijos: `Hogar`, `Miembro`, `Gasto`, `PerfilReparto`, `Categoria`, `Liquidacion`. No los traduzcas ni inventes sinónimos.
- El producto se llama «Mi parte, tu parte» en textos y «mi parte · tu parte» (minúsculas) solo en el logotipo.
- Todo miembro público y de producción lleva comentario XML `///` (CodeRabbit lo exige, umbral 60 %). Los tests no lo necesitan.
- Escribe como el código de alrededor: misma densidad de comentarios, mismos nombres, mismos modismos. Comenta el porqué, no el qué.
- Antes de decidir algo de producto (reparto, cuenta común, chatbot, marca) consulta `docs/README.md`; si cambias una decisión, actualiza `docs/diseno-y-decisiones.md` o la tabla de estado de `docs/README.md` en el mismo cambio.
