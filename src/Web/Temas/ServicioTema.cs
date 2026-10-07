using Microsoft.JSInterop;
using MiParte.Web.Autenticacion;

namespace MiParte.Web.Temas;

/// <summary>Tema visual disponible. <paramref name="Id"/> es el valor de <c>data-tema</c> en <c>&lt;html&gt;</c>; vacío sigue al sistema.</summary>
/// <param name="Id">Identificador estable (se guarda en <c>localStorage</c>).</param>
/// <param name="Nombre">Nombre para mostrar.</param>
public sealed record Tema(string Id, string Nombre);

/// <summary>
/// Elección del tema visual. La aplica <c>window.miparteTema</c> (definido en <c>index.html</c>, que también lo
/// aplica antes de pintar para evitar el parpadeo) y se recuerda en <c>localStorage</c>.
/// </summary>
public sealed class ServicioTema
{
    /// <summary>Clave de <c>localStorage</c>; debe coincidir con la de <c>index.html</c>.</summary>
    public const string Clave = "miparte.tema";

    /// <summary>Id del tema que sigue al sistema (claro u oscuro).</summary>
    public const string Sistema = "sistema";

    /// <summary>Temas ofrecidos, en el orden del selector.</summary>
    public static IReadOnlyList<Tema> Todos { get; } =
    [
        new(Sistema, "Sistema"),
        new("claro", "Claro"),
        new("oscuro", "Oscuro"),
        new("oceano", "Océano"),
        new("bosque", "Bosque"),
        new("medianoche", "Medianoche"),
        new("grafito", "Grafito"),
    ];

    private readonly IAlmacenLocal _almacen;
    private readonly IJSRuntime _js;

    /// <summary>Crea el servicio.</summary>
    /// <param name="almacen">Almacén local.</param>
    /// <param name="js">Runtime de JavaScript.</param>
    public ServicioTema(IAlmacenLocal almacen, IJSRuntime js)
    {
        _almacen = almacen;
        _js = js;
    }

    /// <summary>Id del tema elegido.</summary>
    public string Actual { get; private set; } = Sistema;

    /// <summary>Se dispara al cambiar de tema.</summary>
    public event Action? Cambiado;

    /// <summary>Lee el tema guardado; uno desconocido se trata como «sistema».</summary>
    public async Task IniciarAsync()
    {
        var guardado = await _almacen.LeerAsync(Clave);
        Actual = Todos.Any(t => t.Id == guardado) ? guardado! : Sistema;
    }

    /// <summary>Cambia de tema, lo aplica a la página y lo recuerda.</summary>
    /// <param name="id">Id de uno de <see cref="Todos"/>; otro valor se ignora.</param>
    public async Task ElegirAsync(string id)
    {
        if (Todos.All(t => t.Id != id)) return;
        Actual = id;
        await _almacen.EscribirAsync(Clave, id);
        try { await _js.InvokeVoidAsync("miparteTema.aplicar", id); }
        catch (JSException) { }
        Cambiado?.Invoke();
    }
}
