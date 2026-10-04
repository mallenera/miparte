namespace MiParte.Contracts;

/// <summary>Categoría de gasto del hogar.</summary>
/// <param name="Id">Identificador de la categoría.</param>
/// <param name="Nombre">Nombre de la categoría.</param>
/// <param name="CategoriaPadreId">Categoría padre, o null si es de primer nivel.</param>
/// <param name="PerfilRepartoId">Perfil de reparto asociado por defecto, o null si no tiene.</param>
public record CategoriaDto(Guid Id, string Nombre, Guid? CategoriaPadreId, Guid? PerfilRepartoId);

/// <summary>Alta y edición de categoría (PUT reemplaza todos los campos).</summary>
/// <param name="Nombre">Nombre de la categoría.</param>
/// <param name="CategoriaPadreId">Categoría padre, o null si es de primer nivel.</param>
/// <param name="PerfilRepartoId">Perfil de reparto asociado por defecto, o null si no tiene.</param>
public record GuardarCategoriaRequest(string Nombre, Guid? CategoriaPadreId, Guid? PerfilRepartoId);
