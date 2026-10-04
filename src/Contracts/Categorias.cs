namespace MiParte.Contracts;

public record CategoriaDto(Guid Id, string Nombre, Guid? CategoriaPadreId, Guid? PerfilRepartoId);

/// <summary>Alta y edición de categoría (PUT reemplaza todos los campos).</summary>
public record GuardarCategoriaRequest(string Nombre, Guid? CategoriaPadreId, Guid? PerfilRepartoId);
