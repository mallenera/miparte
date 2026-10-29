using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.ListaCompra;

/// <summary>Datos de la lista de la compra que ve quien tiene un enlace QR: solo los productos, nunca otros datos del hogar.</summary>
/// <param name="Productos">Productos del hogar.</param>
public sealed record ListaPublica(IReadOnlyList<ProductoListaCompra> Productos);

/// <summary>Enlace creado: el token en claro solo se devuelve aquí, una vez.</summary>
/// <param name="Id">Identificador del enlace.</param>
/// <param name="Token">Token en claro, para el QR.</param>
/// <param name="CaducaEn">Caducidad del enlace.</param>
public sealed record EnlaceListaEmitido(Guid Id, string Token, DateTimeOffset CaducaEn);

/// <summary>Un enlace QR tal como se lista: sin el token, que solo se conoce al crearlo.</summary>
/// <param name="Id">Identificador del enlace.</param>
/// <param name="CreadoPor">Miembro que lo creó.</param>
/// <param name="CreadoEn">Momento de creación.</param>
/// <param name="CaducaEn">Caducidad, o null si no caduca.</param>
/// <param name="Estado">Vigente, revocado o caducado en el momento de la consulta.</param>
public sealed record ResumenEnlaceLista(Guid Id, Guid CreadoPor, DateTimeOffset CreadoEn, DateTimeOffset? CaducaEn, EstadoEnlaceLista Estado);

/// <summary>Casos de uso de la lista de la compra: productos, activación del hogar y enlaces para añadir por QR.</summary>
public sealed class ServicioListaCompra(IRepositorioListaCompra repositorio, TimeProvider reloj)
{
    private const string Inactiva = "La lista de la compra no está activada en este hogar.";

    /// <summary>Si la lista de la compra está activada en el hogar.</summary>
    public Task<bool> ActivaAsync(Guid hogarId, CancellationToken ct) => repositorio.ActivaAsync(hogarId, ct);

    /// <summary>Activa o desactiva la lista de la compra; desactivarla no borra productos ni anula enlaces, solo los bloquea.</summary>
    public async Task<bool> ActivarAsync(Guid hogarId, bool activa, CancellationToken ct)
    {
        await repositorio.EstablecerActivaAsync(hogarId, activa, ct);
        return activa;
    }

    /// <summary>Productos del hogar; se devuelven aunque la lista esté desactivada.</summary>
    public Task<IReadOnlyList<ProductoListaCompra>> ListarAsync(Guid hogarId, CancellationToken ct) => repositorio.ProductosAsync(hogarId, ct);

    /// <summary>Añade un producto. Inválido si el nombre o la cantidad no valen; conflicto si la lista no está activada en el hogar.</summary>
    public async Task<Resultado<ProductoListaCompra>> AnadirAsync(Guid hogarId, string? nombre, int cantidad, CancellationToken ct)
    {
        if (!await repositorio.ActivaAsync(hogarId, ct)) return Resultado<ProductoListaCompra>.Conflicto(Inactiva);
        var error = ReglasListaCompra.ValidarNombre(nombre) ?? ReglasListaCompra.ValidarCantidad(cantidad);
        if (error is not null) return Resultado<ProductoListaCompra>.Invalido(error);

        var p = new ProductoListaCompra
        {
            Id = Guid.NewGuid(), HogarId = hogarId, Nombre = ReglasListaCompra.NormalizarNombre(nombre!), Cantidad = cantidad,
            Comprado = false, CreadoEn = reloj.GetUtcNow(),
        };
        await repositorio.GuardarProductoAsync(p, ct);
        return Resultado<ProductoListaCompra>.Ok(p);
    }

    /// <summary>Marca o desmarca un producto como comprado; no encontrado si no es del hogar.</summary>
    public async Task<Resultado<ProductoListaCompra>> MarcarAsync(Guid hogarId, Guid id, bool comprado, CancellationToken ct)
    {
        var p = await repositorio.ProductoAsync(hogarId, id, ct);
        if (p is null) return Resultado<ProductoListaCompra>.NoEncontrado();
        p.Comprado = comprado;
        await repositorio.ActualizarProductoAsync(p, ct);
        return Resultado<ProductoListaCompra>.Ok(p);
    }

    /// <summary>Borra un producto; no encontrado si no es del hogar.</summary>
    public async Task<Resultado<bool>> EliminarAsync(Guid hogarId, Guid id, CancellationToken ct)
        => await repositorio.EliminarProductoAsync(hogarId, id, ct) ? Resultado<bool>.Ok(true) : Resultado<bool>.NoEncontrado();

    /// <summary>
    /// Crea un enlace para añadir y marcar productos por QR. Sin <paramref name="caducaEn"/> caduca a los 7 días. Conflicto si la lista
    /// no está activada en el hogar; inválido si la caducidad ya pasó.
    /// </summary>
    public async Task<Resultado<EnlaceListaEmitido>> CrearEnlaceAsync(Guid hogarId, Guid miembroId, DateTimeOffset? caducaEn, CancellationToken ct)
    {
        if (!await repositorio.ActivaAsync(hogarId, ct)) return Resultado<EnlaceListaEmitido>.Conflicto(Inactiva);
        var ahora = reloj.GetUtcNow();
        var caduca = caducaEn ?? ahora + ReglasListaCompra.VigenciaEnlace;
        if (caduca <= ahora) return Resultado<EnlaceListaEmitido>.Invalido("La caducidad del enlace ya ha pasado.");

        var token = TokenInvitacion.Generar();
        var enlace = new EnlaceListaCompra
        {
            Id = Guid.NewGuid(), HogarId = hogarId, TokenHash = TokenInvitacion.Hash(token), CreadoPor = miembroId,
            CreadoEn = ahora, CaducaEn = caduca, Activo = true,
        };
        await repositorio.GuardarEnlaceAsync(enlace, ct);
        return Resultado<EnlaceListaEmitido>.Ok(new EnlaceListaEmitido(enlace.Id, token, caduca));
    }

    /// <summary>Enlaces QR del hogar con su estado, del más reciente al más antiguo; se devuelven aunque la lista esté desactivada.</summary>
    public async Task<IReadOnlyList<ResumenEnlaceLista>> ListarEnlacesAsync(Guid hogarId, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        return (await repositorio.EnlacesAsync(hogarId, ct))
            .Select(e => new ResumenEnlaceLista(e.Id, e.CreadoPor, e.CreadoEn, e.CaducaEn, ReglasListaCompra.Estado(e.Activo, e.CaducaEn, ahora)))
            .ToList();
    }

    /// <summary>Revoca un enlace: deja de admitir cambios, pero se conserva. No encontrado si no es del hogar.</summary>
    public async Task<Resultado<bool>> RevocarEnlaceAsync(Guid hogarId, Guid id, CancellationToken ct)
    {
        var e = await repositorio.EnlaceAsync(hogarId, id, ct);
        if (e is null) return Resultado<bool>.NoEncontrado();
        e.Activo = false;
        await repositorio.ActualizarEnlaceAsync(e, ct);
        return Resultado<bool>.Ok(true);
    }

    /// <summary>Productos de la lista a la que da acceso un token. Conflicto si el enlace está revocado o caducado, o la lista desactivada.</summary>
    public async Task<Resultado<ListaPublica>> PublicaAsync(string? token, CancellationToken ct)
    {
        var r = await ResolverAsync(token, ct);
        if (r.Error is not null) return Resultado<ListaPublica>.Fallo(r.Error);
        if (r.Enlace is null) return Resultado<ListaPublica>.NoEncontrado();
        return Resultado<ListaPublica>.Ok(new ListaPublica(await repositorio.ProductosAsync(r.Enlace.HogarId, ct)));
    }

    /// <summary>Añade un producto a través de un enlace QR (mismas reglas que <see cref="AnadirAsync"/>).</summary>
    public async Task<Resultado<ProductoListaCompra>> AnadirPublicoAsync(string? token, string? nombre, int cantidad, CancellationToken ct)
    {
        var r = await ResolverAsync(token, ct);
        if (r.Error is not null) return Resultado<ProductoListaCompra>.Fallo(r.Error);
        if (r.Enlace is null) return Resultado<ProductoListaCompra>.NoEncontrado();
        return await AnadirAsync(r.Enlace.HogarId, nombre, cantidad, ct);
    }

    /// <summary>Marca o desmarca un producto a través de un enlace QR; solo sobre productos del hogar del enlace.</summary>
    public async Task<Resultado<ProductoListaCompra>> MarcarPublicoAsync(string? token, Guid id, bool comprado, CancellationToken ct)
    {
        var r = await ResolverAsync(token, ct);
        if (r.Error is not null) return Resultado<ProductoListaCompra>.Fallo(r.Error);
        if (r.Enlace is null) return Resultado<ProductoListaCompra>.NoEncontrado();
        return await MarcarAsync(r.Enlace.HogarId, id, comprado, ct);
    }

    /// <summary>
    /// Enlace vigente para un token. Sin enlace y sin error si el token no existe; con error si el enlace está revocado o caducado,
    /// o si la lista del hogar está desactivada.
    /// </summary>
    private async Task<(EnlaceListaCompra? Enlace, ErrorAplicacion? Error)> ResolverAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return (null, null);
        var enlace = await repositorio.EnlacePorHashAsync(TokenInvitacion.Hash(token), ct);
        if (enlace is null) return (null, null);
        if (!ReglasListaCompra.Vigente(enlace.Activo, enlace.CaducaEn, reloj.GetUtcNow()))
            return (null, new ErrorAplicacion(TipoError.Conflicto, "El enlace de la lista ha caducado o se ha revocado."));
        if (!await repositorio.ActivaAsync(enlace.HogarId, ct)) return (null, new ErrorAplicacion(TipoError.Conflicto, Inactiva));
        return (enlace, null);
    }
}
