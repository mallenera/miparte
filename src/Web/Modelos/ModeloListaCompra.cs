using System.Globalization;
using MiParte.Contracts;
using MiParte.Web.Api;

namespace MiParte.Web.Modelos;

/// <summary>
/// Estado de la lista de la compra: la del hogar (con sesión) o la de un enlace QR (sin sesión, con su token). Sin Blazor: la vista solo
/// pinta, enlaza y gestiona lo que depende del DOM.
/// </summary>
/// <param name="api">Cliente de la API de Core.</param>
/// <param name="token">Token del enlace QR si la lista se abre desde el QR; null para la lista del hogar.</param>
public sealed class ModeloListaCompra(CoreApiClient api, string? token = null)
{
    /// <summary>Intervalo entre refrescos automáticos de la lista, en milisegundos.</summary>
    public const int IntervaloRefresco = 15_000;

    /// <summary>Si la lista se abre desde un enlace QR, sin sesión.</summary>
    public bool Publica => token is not null;

    /// <summary>Si la lista de la compra está activada en el hogar.</summary>
    public bool Activa { get; private set; }

    /// <summary>Productos de la lista: pendientes primero.</summary>
    public List<ProductoCompraDto> Productos { get; private set; } = [];

    /// <summary>Nombre que escribe la persona para un producto nuevo.</summary>
    public string Nombre { get; set; } = "";

    /// <summary>Cantidad que escribe la persona; vacío se toma como 1.</summary>
    public string Cantidad { get; set; } = "";

    /// <summary>Si la primera carga sigue en curso.</summary>
    public bool Cargando { get; private set; } = true;

    /// <summary>Si hay una operación en curso.</summary>
    public bool Enviando { get; private set; }

    /// <summary>Último error, o null.</summary>
    public string? Error { get; private set; }

    /// <summary>Enlaces QR del hogar, del más reciente al más antiguo. Sin token: solo se conoce al crearlo.</summary>
    public List<EnlaceListaDto> Enlaces { get; private set; } = [];

    /// <summary>Miembros del hogar, para mostrar quién creó cada enlace.</summary>
    public List<MiembroDto> Miembros { get; private set; } = [];

    /// <summary>Enlaces que todavía admiten cambios.</summary>
    public IEnumerable<EnlaceListaDto> EnlacesVigentes => Enlaces.Where(e => e.Estado == "vigente");

    /// <summary>Enlace QR creado en esta sesión de la página, si lo hay. El token solo está aquí.</summary>
    public EnlaceListaCreado? Enlace { get; private set; }

    /// <summary>SVG del QR del enlace creado, o null.</summary>
    public string? Qr { get; private set; }

    /// <summary>Productos pendientes de comprar.</summary>
    public IEnumerable<ProductoCompraDto> Pendientes => Productos.Where(p => !p.Comprado);

    /// <summary>Productos ya en el carro.</summary>
    public IEnumerable<ProductoCompraDto> Comprados => Productos.Where(p => p.Comprado);

    /// <summary>Si el nombre y la cantidad escritos se pueden enviar.</summary>
    public bool NuevoValido => NombreNormalizado.Length is >= 1 and <= 100 && CantidadEscrita is not null;

    private string NombreNormalizado => Nombre.Trim();

    /// <summary>Cantidad escrita como entero entre 1 y 999; vacío vale 1; null si no es válida.</summary>
    private int? CantidadEscrita
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Cantidad)) return 1;
            return int.TryParse(Cantidad.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) && c is >= 1 and <= 999
                ? c : null;
        }
    }

    /// <summary>Carga la lista (la primera vez). Si falla deja el mensaje en <see cref="Error"/>.</summary>
    public async Task CargarAsync()
    {
        try
        {
            await RecargarAsync();
        }
        catch (ApiException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Cargando = false;
        }
    }

    /// <summary>Refresca la lista sin parpadeo; los errores de refresco no reemplazan una operación del usuario.</summary>
    public async Task RefrescarAsync()
    {
        if (Enviando) return;
        try
        {
            await RecargarAsync();
        }
        catch (ApiException)
        {
            // Un fallo puntual del refresco no tapa el estado; se reintenta en el siguiente intervalo.
        }
    }

    /// <summary>Añade el producto escrito. Devuelve el aviso de éxito o null si hay error (queda en <see cref="Error"/>).</summary>
    public Task<string?> AnadirAsync() => EjecutarAsync(async () =>
    {
        var cantidad = CantidadEscrita ?? throw new ApiException(System.Net.HttpStatusCode.BadRequest, "La cantidad debe estar entre 1 y 999.");
        var peticion = new CrearProductoCompraRequest(NombreNormalizado, cantidad);
        if (Publica) await api.AnadirProductoPublicoAsync(token!, peticion);
        else await api.CrearProductoCompraAsync(peticion);
        Nombre = "";
        Cantidad = "";
        await RecargarAsync();
        return "Producto añadido";
    });

    /// <summary>Marca o desmarca un producto como comprado.</summary>
    public Task<string?> MarcarAsync(ProductoCompraDto p, bool comprado) => EjecutarAsync(async () =>
    {
        if (Publica) await api.MarcarProductoPublicoAsync(token!, p.Id, comprado);
        else await api.MarcarProductoCompraAsync(p.Id, comprado);
        await RecargarAsync();
        return comprado ? "Marcado como comprado" : "Vuelve a la lista";
    });

    /// <summary>Borra un producto de la lista del hogar (no disponible desde un enlace QR).</summary>
    public Task<string?> EliminarAsync(ProductoCompraDto p) => EjecutarAsync(async () =>
    {
        await api.EliminarProductoCompraAsync(p.Id);
        await RecargarAsync();
        return "Producto borrado";
    });

    /// <summary>Activa o desactiva la lista de la compra del hogar.</summary>
    public Task<string?> ActivarAsync(bool activa) => EjecutarAsync(async () =>
    {
        await api.ActivarListaCompraAsync(activa);
        await RecargarAsync();
        return activa ? "Lista de la compra activada" : "Lista de la compra desactivada";
    });

    /// <summary>
    /// Crea un enlace QR nuevo y genera su código, que apunta a la lista pública sobre <paramref name="baseAplicacion"/>. El token
    /// solo se muestra mientras la página está abierta.
    /// </summary>
    /// <param name="baseAplicacion">Dirección base de la aplicación web.</param>
    public Task<string?> CrearEnlaceAsync(string baseAplicacion) => EjecutarAsync(async () =>
    {
        Enlace = await api.CrearEnlaceListaCompraAsync();
        Qr = (await api.GenerarQrListaCompraAsync(UrlPublica(baseAplicacion, Enlace.Token))).Svg;
        await RecargarEnlacesAsync();
        return "Enlace QR creado";
    });

    /// <summary>Revoca el enlace QR de esta página; deja de admitir cambios.</summary>
    public Task<string?> RevocarEnlaceAsync()
        => Enlace is null ? Task.FromResult<string?>("No hay enlace que revocar") : RevocarEnlaceAsync(Enlace.Id);

    /// <summary>Revoca un enlace QR de la lista (también uno antiguo, que ya no se puede mostrar); deja de admitir cambios.</summary>
    /// <param name="id">Enlace a revocar.</param>
    public Task<string?> RevocarEnlaceAsync(Guid id) => EjecutarAsync(async () =>
    {
        await api.RevocarEnlaceListaCompraAsync(id);
        if (Enlace?.Id == id)
        {
            Enlace = null;
            Qr = null;
        }

        await RecargarEnlacesAsync();
        return "Enlace revocado";
    });

    /// <summary>Nombre de quien creó un enlace.</summary>
    /// <param name="enlace">Enlace listado.</param>
    public string Creador(EnlaceListaDto enlace) => MiParte.Web.Formato.NombreMiembro(enlace.CreadoPor, Miembros);

    /// <summary>Dirección de la lista pública para un token, sobre la base de la aplicación.</summary>
    /// <param name="baseAplicacion">Dirección base de la aplicación web.</param>
    /// <param name="tokenEnlace">Token del enlace.</param>
    public static string UrlPublica(string baseAplicacion, string tokenEnlace) => $"{baseAplicacion.TrimEnd('/')}/compra/{tokenEnlace}";

    private async Task RecargarAsync()
    {
        if (Publica)
        {
            var lista = await api.ObtenerListaPublicaAsync(token!);
            Activa = lista.Activa;
            Productos = lista.Productos.ToList();
            return;
        }

        var del = await api.ObtenerListaCompraAsync();
        Activa = del.Activa;
        Productos = del.Productos.ToList();
        if (Activa && Miembros.Count == 0) await RecargarMiembrosAsync();
        if (Activa) await RecargarEnlacesAsync();
    }

    // Los enlaces y los nombres de quien los creó son información secundaria: si fallan, la lista sigue sirviendo.
    private async Task RecargarEnlacesAsync()
    {
        try
        {
            Enlaces = await api.ListarEnlacesListaCompraAsync();
        }
        catch (ApiException)
        {
            Enlaces = [];
        }
    }

    private async Task RecargarMiembrosAsync()
    {
        try
        {
            Miembros = await api.ListarMiembrosAsync();
        }
        catch (ApiException)
        {
            Miembros = [];
        }
    }

    private async Task<string?> EjecutarAsync(Func<Task<string?>> operacion)
    {
        Enviando = true;
        Error = null;
        try
        {
            return await operacion();
        }
        catch (ApiException ex)
        {
            Error = ex.Message;
            return null;
        }
        finally
        {
            Enviando = false;
        }
    }
}
