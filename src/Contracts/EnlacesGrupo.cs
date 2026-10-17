namespace MiParte.Contracts;

/// <summary>Alta de un enlace de grupo reutilizable. Sin <paramref name="CaducaEn"/> el enlace no caduca.</summary>
/// <param name="CaducaEn">Fecha a partir de la cual deja de admitir uniones; debe ser posterior a ahora. Null = sin caducidad.</param>
public record CrearEnlaceGrupoRequest(DateTimeOffset? CaducaEn = null);

/// <summary>
/// Enlace recién creado. El <paramref name="Token"/> en claro solo se devuelve en esta respuesta; en base de datos queda su hash.
/// </summary>
/// <param name="Id">Identificador del enlace (para revocarlo).</param>
/// <param name="Token">Token en claro que se comparte para unirse al grupo.</param>
/// <param name="CaducaEn">Caducidad, o null si no caduca.</param>
public record EnlaceGrupoCreado(Guid Id, string Token, DateTimeOffset? CaducaEn);

/// <summary>Enlace de grupo tal como lo lista el hogar: nunca incluye el token.</summary>
/// <param name="Id">Identificador del enlace.</param>
/// <param name="CreadoPor">Miembro que lo creó.</param>
/// <param name="CreadoEn">Fecha de creación.</param>
/// <param name="CaducaEn">Caducidad, o null si no caduca.</param>
/// <param name="Usos">Número de uniones hechas con el enlace (informativo).</param>
/// <param name="Estado">«activo», «revocado» o «caducado».</param>
public record EnlaceGrupoDto(Guid Id, Guid CreadoPor, DateTimeOffset CreadoEn, DateTimeOffset? CaducaEn, int Usos, string Estado);

/// <summary>
/// Unión a un grupo con un enlace. <paramref name="Nombre"/> es obligatorio salvo que se indique <paramref name="MiembroId"/>, un adulto
/// existente sin cuenta al que se vincula quien se une.
/// </summary>
/// <param name="Token">Token en claro del enlace.</param>
/// <param name="Nombre">Nombre del nuevo miembro, si no se vincula a uno existente.</param>
/// <param name="MiembroId">Adulto existente sin cuenta al que vincularse, en lugar de crear uno nuevo.</param>
public record UnirseEnlaceGrupoRequest(string Token, string? Nombre = null, Guid? MiembroId = null);
