> **Documento de diseño original** (exportado del documento «App de gastos e ingresos del hogar: diseño y decisiones», 3-oct-2026). Es la fuente de las decisiones de producto. Donde choque con el código, manda el código y `docs/api.md`; las diferencias conocidas están en [README.md](README.md#estado-diseño-frente-a-código).

# App de gastos e ingresos del hogar: diseño y decisiones

2026-10-03 · Ana

## Resumen y objetivo

Mi parte, tu parte (el nombre de la app) registra los ingresos y gastos mensuales de un hogar y calcula cuánto debe asumir cada miembro y quién debe dinero a quién, con un chatbot que responde preguntas como "¿cuánto gasté en alimentación en junio?".

Es el proyecto fin de máster de desarrollo en IA (BigSchool). Se usará en un hogar concreto: una pareja y un hijo que es solo de uno de los dos. Por eso el reparto de gastos es el corazón del diseño.

**Datos de partida acordados:**

- Acceso desde web y desde Android, sin app nativa.
- Login con correo y contraseña, y con Google.
- Nube gratuita o de bajo coste, con dominio propio económico.
- Quien no tiene hijo (A) ingresa el doble que quien lo tiene (B).
- Hay gastos que se reparten 2 contra 1.
- Stack libre, pero sencillo de manejar.

**Acuerdos posteriores:**

- Cuenta común opcional: cada persona aporta un importe fijo al mes y la cuenta paga los gastos que se elijan.
- Los ingresos del hogar no se guardan en base de datos; solo se guardan el porcentaje del reparto proporcional y las aportaciones a la cuenta común.
- Cada gasto guarda el modo de reparto y los porcentajes aplicados, para que ningún cambio posterior rompa los gastos ya introducidos.

## Identidad de marca

La app y la web usan una sola imagen de marca, definida en la [hoja de identidad](https://claude.ai/artifact/RtXFht9dSLVpYRLKfXwoMA); cualquier pantalla, documento o presentación debe seguirla.

- **Nombre:** en textos se escribe "Mi parte, tu parte"; en el logotipo, siempre en minúsculas.
- **Símbolo:** un círculo partido en dos mitades desiguales (60/40) girado 45°: naranja (mi parte) y burdeos (tu parte). A 32 y 16 px se usa la versión sin hueco entre mitades.
- **Logotipo:** horizontal en una línea ("mi parte · tu parte") para cabecera web y barra de app; en dos líneas para portadas y presentación.
- **Tipografía:** Fraunces para logotipo y titulares grandes (la itálica naranja solo en el logotipo); Manrope para toda la interfaz e importes.
- **Reglas de uso:** margen libre = mitad del alto del símbolo; mínimos de 16 px (símbolo), 120 px de ancho (horizontal) y 96 px (dos líneas); no girar, deformar ni recolorear.

| Uso | Color | Hex (claro) | Hex (oscuro) |
|---|---|---|---|
| Principal, botones, barra de resumen | Burdeos | #7A1F33 | #E08A9C (texto) |
| Hover, cabeceras | Burdeos profundo | #4A1220 | — |
| Acento, símbolo | Naranja | #E8742A | #F2904A |
| Texto naranja sobre claro | Naranja texto | #C4551A | #F2904A |
| Fondo | Crema | #FBF5EF | #1C1114 |
| Texto | Tinta | #24161A | #F5ECE6 |
| Positivo (te deben) | Verde | #2E7D5B | — |
| Negativo (debes) | Rojo | #C62828 | — |
| Aviso | Ámbar | #9A6B00 | — |
| Información | Azul | #2F5D8A | — |

Los colores de estado nunca usan burdeos ni naranja y van siempre con signo o icono. Pendiente: comprobar en la OEPM que la marca y el dominio están libres.

## Alcance funcional

El MVP cubre el registro, el reparto y la liquidación mensual; el resto se añade por fases según el tiempo disponible.

| Bloque | Funcionalidad | Fase |
|---|---|---|
| Base | Miembros del hogar, gastos, categorías y subcategorías personalizables; los ingresos del hogar no se guardan | MVP |
| Reparto | Perfiles de reparto reutilizables, sobrescribibles en cada gasto; quién paga (una persona o la cuenta común) y quién asume; cada gasto guarda el modo y los porcentajes aplicados | MVP |
| Cuenta común | Aportación fija mensual por persona; la cuenta paga los gastos elegidos y su saldo se acumula | MVP |
| Liquidación | Saldo mensual entre miembros ("A debe a B X €"); los gastos de la cuenta común no generan deuda | MVP |
| Comodidad | Gastos recurrentes que se generan solos cada mes; edición y filtros | MVP |
| Análisis | Dashboard mensual: gastos por categoría y por persona, entradas y salidas de la cuenta común; comparativa con meses anteriores | MVP |
| IA | Chatbot de consulta con tool calling | MVP |
| Control | Presupuestos por categoría con aviso al acercarse al límite | Posterior |
| Control | Gastos anuales prorrateados (seguro, IBI) y objetivos de ahorro | Posterior |
| Datos | Exportación a Excel/PDF; importación de movimientos bancarios (CSV) | Posterior |
| IA | Categorización automática por descripción; detección de anomalías; previsión del mes siguiente; lectura de tickets (OCR) | Posterior |

Lo que no entre en el MVP queda documentado como líneas futuras en la memoria del máster.

## Configuración del hogar y resumen mensual

Antes de registrar gastos, el hogar se configura una vez; después, cada pantalla muestra el resumen del mes en curso.

**Configuración del hogar:**

- Número de personas y mantenimiento de miembros: adultos que aportan y personas a cargo (el hijo), cada una con su responsable.
- Porcentaje del reparto proporcional: se escribe a mano o se calcula con una calculadora de ingresos que no guarda los importes.
- Perfil de reparto por defecto de cada persona a cargo.

**Cuenta común (opcional):**

- Importe fijo que aporta cada adulto a la cuenta cada mes, guardado por mes y persona.
- Al registrar un gasto se elige la cuenta común como pagador; ese gasto no genera deuda entre personas.
- Tarjeta en el resumen con lo que aporta cada persona, su parte de los gastos de la cuenta, la diferencia y el saldo acumulado.

**Mantenimiento de categorías:**

- Alta, edición y baja de categorías y subcategorías.
- Cada categoría lleva su perfil de reparto por defecto, por ejemplo alimentación con uno y hipoteca y gastos varios de casa con otro; se puede cambiar en cada gasto.

**Gráficos:** gasto por categoría y por persona, con el mes seleccionable.

**Resumen mensual, siempre visible:** una línea fija en la parte superior con el importe de cada persona y la deuda, por ejemplo "Persona A (xxxx €) · Persona B (xxxx €) · A debe a B (xxxx €)". Cada importe es lo que esa persona ha asumido en el mes; la deuda es lo asumido menos lo que ha pagado de su bolsillo; los gastos de la cuenta común no generan deuda. Si hay cuenta común, también muestra el saldo de la cuenta común y el saldo de cada persona. Se recalcula al añadir o editar cualquier gasto.

## Mejoras adicionales

Tres mejoras entran en el MVP porque hacen utilizable el resumen "A debe a B"; el resto se añade después por orden de utilidad.

| Mejora | Qué hace | Fase |
|---|---|---|
| Pagos entre miembros | Se anota cuando A paga a B y la deuda baja | MVP |
| Cierre de mes | Un mes cerrado no admite cambios en sus gastos | MVP |
| Gastos personales | Gastos de una sola persona que no entran en la liquidación | MVP |
| Reembolsos y devoluciones | Importe negativo ligado al gasto original | Posterior |
| Etiquetas | Marcas transversales a las categorías (vacaciones, reforma, Navidad) con su propio resumen | Posterior |
| Alta de gastos por chat | "He gastado 45 € en el súper": el bot propone categoría y reparto, y guarda solo tras confirmar | Posterior |
| Por qué cambió el balance | Pregunta al chatbot que compara meses y explica la diferencia | Posterior |
| Simulador de reparto | Muestra qué cambia si varía el porcentaje, reutilizando la lógica de reparto | Posterior |
| Resumen anual | Totales del año por categoría y por persona | Posterior |
| Previsión de gasto fijo | Recurrentes más gastos prorrateados, para conocer el mínimo del mes | Posterior |
| Foto del ticket | Imagen adjunta al gasto, sin lectura automática | Posterior |
| Avisos por correo | Resumen mensual y recibos próximos | Posterior |
| Invitación al hogar | Alta de la pareja por correo con permisos; la persona a cargo no necesita login | Posterior |
| Historial de cambios | Quién modificó qué y cuándo | **Hecho** (rama `feature/security`; solo lo lee un admin, `docs/api.md` §3.10) |
| Copia de seguridad y exportación | Descarga completa de los datos del hogar | Posterior |

Estas mejoras necesitarán tablas nuevas, por ejemplo para pagos, etiquetas e invitaciones; se definen cuando cada una entre en desarrollo. El historial de cambios ya está hecho (tabla `auditoria`, ver `docs/modelo-de-datos.md`).

## Reglas de reparto de gastos

Cada gasto tiene un perfil de reparto; quien paga (`pagado_por`) y quien asume el gasto son datos distintos, y la diferencia entre ambos genera la deuda. Si paga la cuenta común, no hay deuda entre personas.

**Modos de reparto:**

| Modo | Cómo funciona | Ejemplo |
|---|---|---|
| Porcentaje | % fijo por persona; deben sumar 100 | 60 / 40 |
| Por partes | Pesos por persona | A=2, B=1 → 66,7 % / 33,3 % |
| Proporcional a ingresos | Usa un porcentaje fijo del hogar, escrito a mano o calculado con una calculadora de ingresos que no se guarda | A gana 2X, B gana X → 66,7 % / 33,3 % |
| Individual | 100 % de una persona | Ocio personal |

**Caso de vuestro hogar.** A (sin hijo) ingresa 2X y B (con hijo) ingresa X. En un gasto de 900 € pagado por A con reparto proporcional a ingresos, A asume 600 € y B asume 300 €, por lo que B debe 300 € a A.

El % proporcional es fijo para todo el hogar: se escribe a mano o se calcula con una calculadora de ingresos que no guarda los importes. Si cambian los sueldos, se actualiza y solo afecta a los gastos nuevos. Cada gasto guarda su modo de reparto, los porcentajes aplicados y lo que asume cada persona, así que cambiar el porcentaje, las partes o los perfiles después no altera los gastos ya introducidos.

**Tratamiento del hijo.** Es una decisión de criterio vuestra, no técnica. Las opciones están en la tabla de decisiones (D3). La app permite combinarlas: cada categoría lleva un perfil por defecto, por ejemplo:

- Casa y suministros → proporcional a ingresos.
- Alimentación → proporcional a ingresos, o por partes 2:1 si se cuenta al hijo.
- Gastos del hijo → el perfil que acordéis.
- Ocio personal → individual.

**Redondeo.** Los importes se manejan en céntimos o con `Decimal`, nunca con `float`. El último miembro absorbe el céntimo sobrante para que el reparto siempre sume el importe del gasto.

## Cuenta común

La cuenta común es un pagador más: los gastos que paga no entran en la deuda entre personas y su saldo se calcula aparte. Se activa por hogar y exige al menos dos adultos; cada adulto tiene su aportación fija mensual, y un cambio de importe vale desde ese mes en adelante.

**Gasto pagado por una persona y cargado a la cuenta común** (por ejemplo, la hipoteca que paga Persona A desde su cuenta):

- La cuenta común asume el 100 % del gasto (el reparto proporcional ya se hizo con las aportaciones): el gasto no se reparte entre personas, se guarda como "cuenta común 100 %" y no genera deuda entre ellas.
- La cuenta común queda debiendo ese importe a quien lo pagó, como reembolso pendiente.
- El reembolso se registra como un pago de la cuenta común a la persona; al registrarlo baja el pendiente y el efectivo de la cuenta, pero no su saldo.
- Cada categoría tiene un indicador "a cargo de la cuenta común" que propone esta opción por defecto al crear el gasto.

**Saldos en el resumen** (positivo = a favor):

- Cuenta común: aportaciones acumuladas menos gastos a cargo de la cuenta. El efectivo es el saldo más los reembolsos pendientes (los gastos los adelantan personas: el dinero de la cuenta no baja hasta reembolsarlos).
- Persona A y Persona B: lo que la cuenta les debe por reembolsos pendientes, más o menos la deuda entre personas del mes.

**Ahorro dentro de la cuenta común** (decisión posterior al MVP): cada aportación mensual se reparte en una parte para gastos y otra de ahorro, en euros y por persona (`aportacion_cuenta.ahorro`, entre 0 y el importe; 0 por defecto, lo que deja todo como antes).

- El ahorro no cuenta para gastos: el saldo y el efectivo se calculan sobre la parte de gastos (aportado − ahorro − gastado).
- Además de lo apartado en las aportaciones, se puede ingresar dinero aparte (`deposito_ahorro`): el ahorro inicial al empezar a usar la app, un premio de lotería, un regalo... Suma al ahorro sin tocar el saldo de gastos.
- Un gasto puede pagarse desde el ahorro (`gasto.pagado_desde_ahorro`): se descuenta del ahorro disponible y no del saldo, no lo adelanta nadie (sin reembolso pendiente), va a cargo de la cuenta común (sin reparto ni deuda entre personas) y no puede superar el ahorro disponible.
- El ahorro disponible es el acumulado (aportaciones más ingresos aparte) menos las retiradas (`retirada_ahorro`) y lo gastado desde el ahorro: sacar dinero del ahorro (por ejemplo, para unas vacaciones) baja solo el ahorro, y no se puede retirar más de lo ahorrado.
- El ahorro disponible nunca queda en negativo: la API rechaza rebajar el ahorro de una aportación o eliminar un ingreso si ya se retiró o se gastó ese dinero.
- Fuera de alcance por ahora: objetivos de ahorro (meta e importe objetivo) e intereses.

- **Aportaciones:** cada adulto aporta un importe fijo al mes, guardado por mes y persona. Es lo único que se guarda del dinero que entra en el hogar.
- **Saldo:** aportaciones menos gastos pagados por la cuenta, acumulado mes a mes. El sobrante se queda en la cuenta.
- **Su parte:** para cada persona, la suma de lo que le corresponde de los gastos de la cuenta según el reparto de cada gasto. La diferencia con lo que aporta indica si aporta de más o de menos.
- **Aviso:** si el saldo acumulado es negativo, el resumen indica cuánto falta para cubrir los gastos.
- **Limitación:** el reparto individual necesita que pague una persona, así que no se combina con la cuenta común.

## Chatbot con tool calling

El modelo de lenguaje no calcula ni escribe SQL libre: traduce la pregunta a una función predefinida, el código ejecuta la consulta real y el modelo solo redacta la respuesta. Así no inventa cifras.

- La persona escribe la pregunta.
- El modelo la convierte en una llamada a función, por ejemplo `gasto_total(categoria="Alimentación", mes=6, año=2026)`.
- El código consulta la base de datos con las reglas de acceso del hogar.
- El modelo redacta la respuesta con el resultado devuelto.

**Funciones iniciales:**

| Función | Responde a |
|---|---|
| `gasto_total` | "¿Cuánto gasté en alimentación en junio?" (filtros: categoría, mes, año, persona) |
| `gasto_por_categoria` | "¿En qué se nos fue el dinero en septiembre?" |
| `balance_mes` | "¿Cómo cerramos el mes?" |
| `liquidacion_mes` | "¿Quién debe a quién?" |
| `comparar_meses` | "¿Gastamos más que el mes pasado?" |

**Límites de diseño:**

- Si la pregunta no encaja en ninguna función, el bot lo dice en vez de improvisar.
- Si falta el año ("junio"), el bot asume el año en curso o el último junio con datos, y lo indica en la respuesta.
- Solo lee datos del hogar de la persona que pregunta.
- Es una decisión de diseño defendible en la memoria: más fiable que text-to-SQL y fácil de probar con tests.

## Arquitectura y despliegue

Una sola aplicación en Docker atiende a la web y al móvil, y delega el login y los datos en Supabase.

> Diagrama embebido en el original: arquitectura de 3 bloques (interfaz, aplicación, Supabase) y un modelo de lenguaje.

El navegador solo habla con la app; la app lee y escribe en Supabase y, desde el chatbot, consulta al modelo de lenguaje.

| Pieza | Opción propuesta | Notas |
|---|---|---|
| Interfaz | Streamlit (Python) | Trae componentes de chat; se abre en el navegador del móvil y puede añadirse a la pantalla de inicio |
| Aplicación | Docker en Render o Railway | Admiten dominio propio; Streamlit Community Cloud es gratis pero no lo permite |
| Base de datos y login | Supabase (PostgreSQL y Auth) | Correo con contraseña y Google; el login con Google requiere credenciales OAuth gratuitas en Google Cloud Console |
| Dominio | Cualquier registrador (.com o .es) | Coste anual bajo |

Los planes cambian: antes de decidir hay que comprobar las condiciones vigentes. Los planes gratuitos suelen dormir la app o pausar la base de datos tras un tiempo sin uso, algo tolerable en uso familiar.

## Modelo de datos

PostgreSQL (Supabase) con nueve tablas base (las mejoras posteriores añaden otras); cada registro cuelga de un hogar para aislar los datos de cada familia. Los ingresos del hogar no se guardan: solo el porcentaje proporcional y las aportaciones a la cuenta común.

| Tabla | Contenido |
|---|---|
| `hogar` | Nombre del hogar, porcentaje del reparto proporcional y si usa cuenta común |
| `miembro` | Adulto o persona a cargo (con su responsable), enlazado al usuario del login si lo tiene (`user_id`) |
| `perfil_reparto` | Nombre y modo: porcentaje, partes, proporcional o individual |
| `perfil_reparto_detalle` | Valor (% o partes) por miembro; el modo proporcional usa el porcentaje del hogar |
| `categoria` | Nombre, categoría padre (subcategorías) y perfil de reparto por defecto |
| `aportacion_cuenta` | Miembro, mes e importe fijo que aporta a la cuenta común |
| `gasto_recurrente` | Plantilla mensual: importe, categoría, quién paga, perfil, día del mes, activo |
| `gasto` | Fecha, importe, categoría, pagado por (miembro o cuenta común), si se carga a la cuenta común, modo de reparto aplicado, concepto, recurrente de origen |
| `gasto_reparto` | Importe asumido y porcentaje aplicado por cada miembro, calculados al registrar el gasto |

**Decisiones de diseño:**

- `gasto_reparto` guarda el resultado de cada gasto y el gasto guarda su modo de reparto: el histórico no cambia si luego se modifican el porcentaje, las partes o los perfiles.
- Importes en `numeric(12,2)`; el redondeo lo absorbe el último miembro.
- Row Level Security activada en Supabase para que cada hogar solo vea sus datos.
- Validaciones en base de datos: importes positivos, día del mes entre 1 y 28.

## Decisiones a tomar

Siete decisiones condicionan el desarrollo; la D3 es la única que depende de un acuerdo entre vosotros y no de la técnica.

| # | Decisión | Opciones | Recomendación | Estado |
|---|---|---|---|---|
| D1 | Dónde se aloja la app | Render o Railway con Docker; Streamlit Community Cloud (gratis, pero sin dominio propio) | Render o Railway con Docker, porque admiten dominio propio | Pendiente |
| D2 | Base de datos y login | Supabase (PostgreSQL + login con correo y Google); SQLite local con login propio | Supabase: incluye ambos logins y plan gratuito | Pendiente |
| D3 | Cómo se reparten los gastos del hijo | 100 % B; proporcional a ingresos (A asume 2/3); perfil propio (por ejemplo 50/50 o 70/30) | Perfil propio configurable; el porcentaje lo acordáis vosotros | Pendiente |
| D4 | Reparto de la alimentación | Proporcional a ingresos; por partes 2:1 contando al hijo | Por decidir según si el hijo cuenta como una parte | Pendiente |
| D5 | Interfaz | Streamlit; FastAPI con frontend web instalable (PWA) | Streamlit para el MVP; migrar después reutilizando lógica y base de datos | Pendiente |
| D6 | Alcance de la IA | Solo chatbot; chatbot más categorización automática de gastos | Chatbot en el MVP; categorización como extra si da tiempo | Pendiente |
| D7 | Modelo de lenguaje del chatbot y dominio | Proveedor, modelo y coste por definir; registrador del dominio por elegir | Comprobar requisitos del máster y precios actuales antes de decidir | Pendiente |

## Plan por fases, riesgos y calidad

El orden va de lo más delicado (el cálculo) a lo más vistoso (el chatbot), para tener siempre algo que funciona.

- **Base:** miembros, categorías y subcategorías, alta de gastos, cuenta común con aportaciones y perfiles de reparto.
- **Cálculo:** liquidación mensual con tests unitarios de todos los modos de reparto, incluido el caso A=2X, B=X y el reparto 2 contra 1, y de que un gasto guardado no cambie al cambiar el porcentaje.
- **Comodidad:** gastos recurrentes, edición, filtros y dashboard mensual.
- **Chatbot:** las cinco funciones iniciales con tool calling.
- **Extras si da tiempo:** presupuestos con alertas, exportación a Excel/PDF, categorización automática.

**Riesgos y cómo se cubren:**

| Riesgo | Mitigación |
|---|---|
| Errores de reparto o de redondeo | Tests unitarios; `Decimal` o céntimos; el último miembro absorbe el sobrante |
| Cambiar porcentajes, partes o perfiles altera gastos ya introducidos | Cada gasto guarda el modo, los porcentajes aplicados y el importe de cada persona; test que comprueba que un gasto guardado no cambia |
| El chatbot inventa cifras | Solo funciones predefinidas; si no hay función, responde que no puede |
| Planes gratuitos que duermen la app o pausan la base de datos | Aceptable para uso familiar; comprobar las condiciones vigentes antes de contratar |
| Datos de un hogar visibles a otro | Row Level Security en Supabase, probada con dos hogares de prueba |
| Alcance demasiado grande para el máster | MVP cerrado; el resto como líneas futuras en la memoria |

**Calidad técnica que puntúa en la memoria:** validaciones de entrada (importes positivos, porcentajes que suman 100), tests de la lógica de reparto y de las funciones del chatbot, README y despliegue reproducible con Docker.
