namespace MiParte.Contracts;

/// <summary>Categoría de gasto del hogar.</summary>
/// <param name="Id">Identificador de la categoría.</param>
/// <param name="Nombre">Nombre de la categoría.</param>
/// <param name="CategoriaPadreId">Categoría padre, o null si es de primer nivel.</param>
/// <param name="PerfilRepartoId">Perfil de reparto asociado por defecto, o null si no tiene.</param>
/// <param name="ACargoCuentaComun">Si los gastos de la categoría van por defecto a cargo de la cuenta común (su perfil por defecto es el de cuenta común).</param>
public record CategoriaDto(Guid Id, string Nombre, Guid? CategoriaPadreId, Guid? PerfilRepartoId, bool ACargoCuentaComun = false);

/// <summary>Alta y edición de categoría (PUT reemplaza todos los campos).</summary>
/// <param name="Nombre">Nombre de la categoría.</param>
/// <param name="CategoriaPadreId">Categoría padre, o null si es de primer nivel.</param>
/// <param name="PerfilRepartoId">Perfil de reparto asociado por defecto, o null si no tiene. Con <paramref name="ACargoCuentaComun"/> debe ser el de cuenta común (si se omite, se asigna).</param>
/// <param name="ACargoCuentaComun">Si los gastos de la categoría van por defecto a cargo de la cuenta común; exige la cuenta común activada. Un perfil por defecto de cuenta común lo implica.</param>
public record GuardarCategoriaRequest(string Nombre, Guid? CategoriaPadreId, Guid? PerfilRepartoId, bool ACargoCuentaComun = false);
