using MiParte.Contracts;

namespace MiParte.Web.Api;

/// <summary>Categorías y perfiles de reparto del hogar.</summary>
public sealed partial class CoreApiClient
{
    /// <summary>Categorías del hogar actual (<c>GET /api/categorias</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<CategoriaDto>> ListarCategoriasAsync(CancellationToken ct = default) =>
        _nucleo.ObtenerListaAsync<CategoriaDto>("api/categorias", ct);

    /// <summary>Crea una categoría (<c>POST /api/categorias</c>).</summary>
    /// <param name="peticion">Nombre, categoría padre y perfil por defecto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<CategoriaDto> CrearCategoriaAsync(GuardarCategoriaRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<CategoriaDto>(HttpMethod.Post, "api/categorias", peticion, ct);

    /// <summary>Reemplaza todos los campos de una categoría (<c>PUT /api/categorias/{id}</c>).</summary>
    /// <param name="id">Categoría a editar.</param>
    /// <param name="peticion">Nuevos valores.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<CategoriaDto> GuardarCategoriaAsync(Guid id, GuardarCategoriaRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<CategoriaDto>(HttpMethod.Put, $"api/categorias/{id}", peticion, ct);

    /// <summary>
    /// Elimina una categoría sin subcategorías (<c>DELETE /api/categorias/{id}</c>). Si tiene gastos hay que indicar a qué categoría
    /// pasan; sin destino la API responde 409 y la excepción lleva <see cref="ApiException.EnUso"/>.
    /// </summary>
    /// <param name="id">Categoría a eliminar.</param>
    /// <param name="moverA">Categoría que recibe sus gastos y plantillas, o null si no tiene.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarCategoriaAsync(Guid id, Guid? moverA = null, CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Delete, moverA is { } d ? $"api/categorias/{id}?moverA={d}" : $"api/categorias/{id}", ct);

    /// <summary>Perfiles de reparto con su detalle (<c>GET /api/perfiles</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<PerfilRepartoDto>> ListarPerfilesAsync(CancellationToken ct = default) =>
        _nucleo.ObtenerListaAsync<PerfilRepartoDto>("api/perfiles", ct);

    /// <summary>Crea un perfil de reparto (<c>POST /api/perfiles</c>).</summary>
    /// <param name="peticion">Nombre, modo y detalle.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<PerfilRepartoDto> CrearPerfilAsync(GuardarPerfilRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<PerfilRepartoDto>(HttpMethod.Post, "api/perfiles", peticion, ct);

    /// <summary>Reemplaza nombre, modo y detalle de un perfil (<c>PUT /api/perfiles/{id}</c>).</summary>
    /// <param name="id">Perfil a editar.</param>
    /// <param name="peticion">Nuevos valores.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<PerfilRepartoDto> GuardarPerfilAsync(Guid id, GuardarPerfilRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<PerfilRepartoDto>(HttpMethod.Put, $"api/perfiles/{id}", peticion, ct);

    /// <summary>
    /// Elimina un perfil (<c>DELETE /api/perfiles/{id}</c>). Si está en uso hay que indicar a qué perfil pasa; sin destino la API
    /// responde 409 y la excepción lleva <see cref="ApiException.EnUso"/>.
    /// </summary>
    /// <param name="id">Perfil a eliminar.</param>
    /// <param name="moverA">Perfil que recibe sus categorías, gastos y plantillas, o null si no está en uso.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarPerfilAsync(Guid id, Guid? moverA = null, CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Delete, moverA is { } d ? $"api/perfiles/{id}?moverA={d}" : $"api/perfiles/{id}", ct);
}
