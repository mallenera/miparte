namespace MiParte.Contracts;

/// <summary>Producto de la lista de la compra del hogar.</summary>
/// <param name="Id">Identificador del producto.</param>
/// <param name="Nombre">Nombre del producto.</param>
/// <param name="Cantidad">Cantidad (de 1 a 999).</param>
/// <param name="Comprado">Si ya está en el carro.</param>
public record ProductoCompraDto(Guid Id, string Nombre, int Cantidad, bool Comprado);

/// <summary>
/// Lista de la compra: si la función está activada en el hogar y sus productos. Se devuelve aunque esté desactivada, y el
/// acceso público (por enlace QR) la recibe con <paramref name="Activa"/> a true.
/// </summary>
/// <param name="Activa">Si el hogar tiene activada la lista de la compra.</param>
/// <param name="Productos">Pendientes primero, luego comprados, por fecha de alta.</param>
public record ListaCompraResponse(bool Activa, IReadOnlyList<ProductoCompraDto> Productos);

/// <summary>PUT /api/lista-compra/activacion (permiso <c>hogar.funciones</c>).</summary>
/// <param name="Activa">Activa (true) o desactiva (false) la lista de la compra del hogar.</param>
public record ActivarListaCompraRequest(bool Activa);

/// <summary>POST /api/lista-compra/productos y el alta por enlace QR.</summary>
/// <param name="Nombre">Nombre del producto (obligatorio, hasta 100 caracteres).</param>
/// <param name="Cantidad">Cantidad; si es null se usa 1.</param>
public record CrearProductoCompraRequest(string Nombre, int? Cantidad = null);

/// <summary>PUT /api/lista-compra/productos/{id} y el marcado por enlace QR.</summary>
/// <param name="Comprado">Marca (true) o desmarca (false) el producto como comprado.</param>
public record MarcarProductoCompraRequest(bool Comprado);

/// <summary>Enlace QR creado. El token en claro solo se devuelve aquí; después no vuelve a verse.</summary>
/// <param name="Id">Identificador del enlace (para revocarlo).</param>
/// <param name="Token">Token en claro, que va en la URL del QR.</param>
/// <param name="CaducaEn">Caducidad del enlace.</param>
public record EnlaceListaCreado(Guid Id, string Token, DateTimeOffset CaducaEn);

/// <summary>Enlace QR de la lista de la compra tal como se lista. No lleva el token: solo se devuelve al crearlo.</summary>
/// <param name="Id">Identificador del enlace (para revocarlo).</param>
/// <param name="CreadoPor">Miembro que lo creó.</param>
/// <param name="CreadoEn">Momento de creación.</param>
/// <param name="CaducaEn">Caducidad, o null si no caduca.</param>
/// <param name="Estado">«vigente», «revocado» o «caducado».</param>
public record EnlaceListaDto(Guid Id, Guid CreadoPor, DateTimeOffset CreadoEn, DateTimeOffset? CaducaEn, string Estado);

/// <summary>POST /api/lista-compra/qr: texto que codifica el QR en formato SVG.</summary>
/// <param name="Texto">Texto a codificar (la URL de la lista, por ejemplo). Hasta 500 caracteres.</param>
public record QrListaRequest(string Texto);

/// <summary>Respuesta de POST /api/lista-compra/qr.</summary>
/// <param name="Svg">Código QR en SVG, listo para mostrarse.</param>
public record QrListaResponse(string Svg);
