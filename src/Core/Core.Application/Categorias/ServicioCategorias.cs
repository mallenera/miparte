using MiParte.Core.Application.Gastos;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Categorias;

/// <summary>Datos de una categoría que se crea o se edita.</summary>
/// <param name="Nombre">Nombre; único dentro de su nivel.</param>
/// <param name="CategoriaPadreId">Categoría padre; null en las de primer nivel.</param>
/// <param name="PerfilRepartoId">Perfil de reparto por defecto; null sin perfil.</param>
/// <param name="ACargoCuentaComun">Si sus gastos van a cargo de la cuenta común.</param>
/// <param name="PredeterminadoGastos">Si está marcada «predeterminada para gastos» (la propone el asistente al dar de alta un gasto).</param>
public sealed record DatosCategoria(
    string? Nombre, Guid? CategoriaPadreId, Guid? PerfilRepartoId, bool ACargoCuentaComun, bool PredeterminadoGastos = false);

/// <summary>Casos de uso de las categorías de gasto (jerárquicas, con perfil de reparto opcional).</summary>
public sealed class ServicioCategorias(IRepositorioCategorias repositorio, IRepositorioGastos gastos)
{
    private const string NombreRepetido = "Ya existe una categoría con ese nombre en ese nivel.";

    /// <summary>Categorías del hogar ordenadas por nombre.</summary>
    public Task<IReadOnlyList<Categoria>> ListarAsync(CancellationToken ct) => repositorio.ListarAsync(ct);

    /// <summary>Crea una categoría. Inválida si los datos no valen; conflicto si el nombre ya existe en ese nivel o la cuenta común no está activada.</summary>
    public async Task<Resultado<Categoria>> CrearAsync(Guid hogarId, DatosCategoria datos, CancellationToken ct)
    {
        var v = await ValidarAsync(datos, null, ct);
        if (v.Error is not null) return v.Error;

        var categoria = new Categoria
        {
            Id = Guid.NewGuid(), HogarId = hogarId, Nombre = v.Nombre, CategoriaPadreId = datos.CategoriaPadreId,
            PerfilRepartoId = v.PerfilId, ACargoCuentaComun = v.ACargo, PredeterminadoGastos = datos.PredeterminadoGastos,
        };
        repositorio.Anadir(categoria);
        return await repositorio.GuardarAsync(ct) ? Resultado<Categoria>.Ok(categoria) : Resultado<Categoria>.Conflicto(NombreRepetido);
    }

    /// <summary>Actualiza nombre, categoría padre y perfil de reparto. No encontrada si no existe; inválida si crearía un ciclo.</summary>
    public async Task<Resultado<Categoria>> ActualizarAsync(Guid id, DatosCategoria datos, CancellationToken ct)
    {
        var categoria = await repositorio.ObtenerParaEditarAsync(id, ct);
        if (categoria is null) return Resultado<Categoria>.NoEncontrado();
        var v = await ValidarAsync(datos, id, ct);
        if (v.Error is not null) return v.Error;

        categoria.Nombre = v.Nombre;
        categoria.CategoriaPadreId = datos.CategoriaPadreId;
        categoria.PerfilRepartoId = v.PerfilId;
        categoria.ACargoCuentaComun = v.ACargo;
        categoria.PredeterminadoGastos = datos.PredeterminadoGastos;
        return await repositorio.GuardarAsync(ct) ? Resultado<Categoria>.Ok(categoria) : Resultado<Categoria>.Conflicto(NombreRepetido);
    }

    /// <summary>
    /// Borra la categoría. Conflicto si tiene subcategorías, o si tiene gastos o gastos recurrentes y no se indica
    /// <paramref name="moverAId"/> (con <c>enUso</c> entre los datos extra, para que el cliente pregunte a qué categoría moverlos);
    /// con destino, los gastos y plantillas pasan a ella en el mismo guardado.
    /// </summary>
    public async Task<Resultado<bool>> EliminarAsync(Guid id, Guid? moverAId, CancellationToken ct)
    {
        var categoria = await repositorio.ObtenerParaEditarAsync(id, ct);
        if (categoria is null) return Resultado<bool>.NoEncontrado();
        if (await repositorio.TieneSubcategoriasAsync(id, ct)) return Resultado<bool>.Conflicto("La categoría tiene subcategorías.");

        var movidos = false;
        if (await repositorio.TieneGastosAsync(id, ct))
        {
            if (moverAId is not { } destinoId)
                return Resultado<bool>.Conflicto("La categoría tiene gastos asociados.", new Dictionary<string, object> { ["enUso"] = true });
            if (destinoId == id) return Resultado<bool>.Invalido("La categoría de destino debe ser otra.");
            if (await repositorio.ObtenerParaEditarAsync(destinoId, ct) is null)
                return Resultado<bool>.Invalido("La categoría de destino no existe en este hogar.");

            await repositorio.ReasignarGastosAsync(id, destinoId, ct);
            movidos = true;
        }

        repositorio.Eliminar(categoria);
        if (await repositorio.GuardarAsync(ct)) return Resultado<bool>.Ok(true);
        return Resultado<bool>.Conflicto(movidos
            ? "No se han podido mover los gastos a la categoría de destino."
            : "La categoría está en uso.");
    }

    private sealed record Validada(string Nombre, Guid? PerfilId, bool ACargo, Resultado<Categoria>? Error);

    /// <summary>
    /// Valida los datos y devuelve el nombre normalizado, el perfil definitivo y si va a cargo de la cuenta común.
    /// «A cargo de la cuenta común» y el perfil de cuenta común son la misma decisión: uno implica el otro, y sin perfil se asigna el
    /// de cuenta común del hogar.
    /// </summary>
    private async Task<Validada> ValidarAsync(DatosCategoria d, Guid? idActual, CancellationToken ct)
    {
        static Validada Mal(string m) => new("", null, false, Resultado<Categoria>.Invalido(m));

        if (ReglasCategoria.ValidarNombre(d.Nombre, out var nombre) is { } e) return Mal(e);

        PerfilReparto? perfil = null;
        if (d.PerfilRepartoId is { } perfilId)
        {
            perfil = await repositorio.PerfilAsync(perfilId, ct);
            if (perfil is null) return Mal("El perfil de reparto no existe en este hogar.");
        }

        var aCargo = d.ACargoCuentaComun || perfil?.Modo == ModoReparto.CuentaComun;
        if (aCargo)
        {
            if (perfil is null)
            {
                perfil = await repositorio.PerfilDeCuentaComunAsync(ct);
                if (perfil is null) return Mal("El hogar no tiene un perfil de cuenta común: créalo antes de marcar la categoría.");
            }
            else if (perfil.Modo != ModoReparto.CuentaComun)
            {
                return Mal("Una categoría a cargo de la cuenta común debe usar el perfil de cuenta común.");
            }

            // Pedir el indicador en una categoría que no lo tenía exige la cuenta activada; editar una ya marcada, o elegir su perfil
            // de cuenta común a secas, no.
            var yaMarcada = idActual is { } actual && await repositorio.EstaACargoDeLaCuentaAsync(actual, ct);
            if (d.ACargoCuentaComun && !yaMarcada && !await gastos.CuentaComunActivaAsync(ct))
                return new("", null, false, Resultado<Categoria>.Conflicto(Mensajes.CuentaInactiva));
        }

        if (d.CategoriaPadreId is { } padreId)
        {
            if (padreId == idActual) return Mal("Una categoría no puede ser su propio padre.");

            var padres = await repositorio.PadresAsync(ct);
            if (!padres.ContainsKey(padreId)) return Mal("La categoría padre no existe en este hogar.");

            // Sin ciclos: subiendo desde el padre no debe aparecer la categoría que se edita.
            if (idActual is { } id && ReglasCategoria.CreariaCiclo(id, padreId, padres)) return Mal("La categoría padre crearía un ciclo.");
        }

        if (await repositorio.NombreEnNivelAsync(d.CategoriaPadreId, nombre, idActual, ct))
            return new("", null, false, Resultado<Categoria>.Conflicto(NombreRepetido));

        return new Validada(nombre, perfil?.Id, aCargo, null);
    }
}
