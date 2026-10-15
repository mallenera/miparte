using MiParte.Contracts;
using MiParte.Core.Domain;
using MiParte.Web.Api;

namespace MiParte.Web.Modelos;

/// <summary>
/// Estado y orquestación de la matriz de permisos de un miembro: borrador de checks, plantillas rápidas y guardado. La API es
/// quien aplica las reglas (siempre debe quedar alguien con el permiso de cambiar permisos). No depende de Blazor.
/// </summary>
/// <param name="api">Cliente de Core.Api.</param>
public sealed class ModeloPermisos(CoreApiClient api)
{
    private readonly HashSet<string> _borrador = [];

    /// <summary>Miembro cuyo editor está abierto.</summary>
    public Guid? Abierto { get; set; }

    /// <summary>Si se está guardando.</summary>
    public bool Guardando { get; private set; }

    /// <summary>Error del último guardado.</summary>
    public string? Error { get; private set; }

    /// <summary>
    /// Cambia al aplicar una plantilla para recrear las casillas: el navegador no debe quedarse con marcas que ya no coinciden con
    /// el borrador.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Permisos del catálogo agrupados por área.</summary>
    public static IEnumerable<IGrouping<string, Permiso>> Grupos => CatalogoPermisos.Todos.GroupBy(p => p.Grupo);

    /// <summary>Si el permiso está marcado en el borrador.</summary>
    public bool Marcado(string clave) => _borrador.Contains(clave);

    /// <summary>Abre o cierra el editor del miembro; al abrir parte de sus permisos actuales.</summary>
    public void Alternar(MiembroDto m)
    {
        Error = null;
        if (Abierto == m.Id)
        {
            Abierto = null;
            return;
        }

        Abierto = m.Id;
        Aplicar(m.Permisos ?? []);
    }

    /// <summary>Sustituye el borrador por las claves dadas (una plantilla, todo o nada).</summary>
    public void Aplicar(IEnumerable<string> claves)
    {
        _borrador.Clear();
        foreach (var clave in claves) _borrador.Add(clave);
        Version++;
    }

    /// <summary>Marca o desmarca un permiso del borrador.</summary>
    public void Marcar(string clave, bool marcado)
    {
        if (marcado) _borrador.Add(clave);
        else _borrador.Remove(clave);
    }

    /// <summary>Guarda el borrador como los permisos propios del miembro; devuelve el aviso, o null si la API lo rechazó.</summary>
    public async Task<string?> GuardarAsync(MiembroDto m)
    {
        Error = null;
        Guardando = true;
        try
        {
            await api.ActualizarMiembroAsync(
                m.Id, new ActualizarMiembroRequest(Permisos: CatalogoPermisos.Todos.Select(p => p.Clave).Where(_borrador.Contains).ToList()));
            Abierto = null;
            return $"Permisos de {m.Nombre} guardados";
        }
        catch (ApiException e)
        {
            Error = e.Message;
            return null;
        }
        finally
        {
            Guardando = false;
        }
    }
}
