using Microsoft.JSInterop;

namespace MiParte.Web.Autenticacion;

/// <summary>Almacén clave-valor persistente del navegador (abstracción de <c>localStorage</c>).</summary>
public interface IAlmacenLocal
{
    /// <summary>Lee un valor, o null si no existe o el almacenamiento no está disponible.</summary>
    /// <param name="clave">Clave del valor.</param>
    Task<string?> LeerAsync(string clave);

    /// <summary>Guarda un valor; si el almacenamiento no está disponible, no hace nada.</summary>
    /// <param name="clave">Clave del valor.</param>
    /// <param name="valor">Valor a guardar.</param>
    Task EscribirAsync(string clave, string valor);

    /// <summary>Elimina un valor.</summary>
    /// <param name="clave">Clave del valor.</param>
    Task EliminarAsync(string clave);
}

/// <summary>Implementación sobre <c>window.localStorage</c> mediante JS interop.</summary>
public sealed class AlmacenLocalJs : IAlmacenLocal
{
    private readonly IJSRuntime _js;

    /// <summary>Crea el almacén.</summary>
    /// <param name="js">Runtime de JavaScript.</param>
    public AlmacenLocalJs(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public async Task<string?> LeerAsync(string clave)
    {
        try { return await _js.InvokeAsync<string?>("localStorage.getItem", clave); }
        catch (JSException) { return null; }
    }

    /// <inheritdoc />
    public async Task EscribirAsync(string clave, string valor)
    {
        try { await _js.InvokeVoidAsync("localStorage.setItem", clave, valor); }
        catch (JSException) { }
    }

    /// <inheritdoc />
    public async Task EliminarAsync(string clave)
    {
        try { await _js.InvokeVoidAsync("localStorage.removeItem", clave); }
        catch (JSException) { }
    }
}
