using MiParte.Contracts;

namespace MiParte.Web.Reparto;

/// <summary>
/// Filtros de la lista de Gastos que se aplican en el cliente porque la API no los ofrece: categoría con sus subcategorías y
/// quién paga (no quién asume).
/// </summary>
/// <param name="Categorias">Identificadores de la categoría elegida y sus descendientes; null sin filtro.</param>
/// <param name="Paga">Solo lo que paga esta persona; null sin filtro.</param>
/// <param name="SoloCuenta">Solo lo que paga la cuenta común o el ahorro (sin pagador).</param>
public sealed record FiltroLista(IReadOnlySet<Guid>? Categorias = null, Guid? Paga = null, bool SoloCuenta = false);

/// <summary>Cifras de la lista de gastos que se muestra.</summary>
/// <param name="TotalHogar">Suma de los gastos del hogar (sin personales).</param>
/// <param name="CuantosHogar">Cuántos gastos del hogar hay.</param>
/// <param name="TotalPersonales">Suma de los gastos personales, que van fuera de la liquidación.</param>
/// <param name="HayPersonales">Si hay algún gasto personal.</param>
public sealed record TotalesLista(decimal TotalHogar, int CuantosHogar, decimal TotalPersonales, bool HayPersonales);

/// <summary>Reglas de presentación de la lista de gastos que no dependen de Blazor: filtrado, orden, totales y perfiles ofrecidos.</summary>
public static class ListaGastos
{
    /// <summary>Aplica los filtros del cliente y deja la lista del más reciente al más antiguo (empates por id).</summary>
    /// <param name="gastos">Gastos recibidos de la API.</param>
    /// <param name="filtro">Filtros a aplicar.</param>
    public static List<GastoResponse> Filtrar(IEnumerable<GastoResponse> gastos, FiltroLista filtro)
    {
        var lista = gastos;
        if (filtro.Categorias is { } ids) lista = lista.Where(g => ids.Contains(g.CategoriaId));
        // Quién paga (no quién asume): lo que reproduce el filtro de persona del Dashboard.
        if (filtro.Paga is { } paga) lista = lista.Where(g => g.PagadoPor == paga);
        else if (filtro.SoloCuenta) lista = lista.Where(g => g.PagadoPor is null);
        return lista.OrderByDescending(g => g.Fecha).ThenBy(g => g.Id).ToList();
    }

    /// <summary>Totales de los gastos mostrados: los personales van aparte y no entran en el total del hogar.</summary>
    /// <param name="gastos">Gastos mostrados.</param>
    public static TotalesLista Totales(IReadOnlyCollection<GastoResponse> gastos)
    {
        var hogar = gastos.Where(g => !g.Personal).ToList();
        var personales = gastos.Where(g => g.Personal).ToList();
        return new TotalesLista(hogar.Sum(g => g.Importe), hogar.Count, personales.Sum(g => g.Importe), personales.Count > 0);
    }

    /// <summary>
    /// Perfiles que se ofrecen en el formulario: el de cuenta común solo con la cuenta activada y el de cuenta común (ahorro)
    /// solo con el ahorro activado (si no, la API rechazaría el gasto). Al editar un gasto cuyo perfil ya no se ofrece se mantiene
    /// como opción, para que el formulario muestre lo que envía.
    /// </summary>
    /// <param name="perfiles">Perfiles del hogar.</param>
    /// <param name="cuentaActiva">Si el hogar tiene activada la cuenta común.</param>
    /// <param name="perfilEnEdicion">Perfil del gasto que se está editando; null al crear.</param>
    /// <param name="ahorroActivo">Si el hogar tiene activado el ahorro (exige además la cuenta activa).</param>
    public static IReadOnlyList<PerfilRepartoDto> PerfilesOfrecidos(
        IReadOnlyList<PerfilRepartoDto> perfiles, bool cuentaActiva, Guid? perfilEnEdicion, bool ahorroActivo = false)
    {
        var ofrecidos = perfiles.Where(p => p.Modo switch
        {
            ModosReparto.CuentaComun => cuentaActiva,
            ModosReparto.CuentaComunAhorro => cuentaActiva && ahorroActivo,
            _ => true,
        }).ToList();
        if (perfilEnEdicion is { } id && perfiles.FirstOrDefault(p => p.Id == id) is { } actual && !ofrecidos.Contains(actual))
            ofrecidos.Add(actual);
        return ofrecidos;
    }
}
