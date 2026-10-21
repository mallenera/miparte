using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Perfiles;

/// <summary>Casos de uso de los perfiles de reparto (porcentaje, partes, cuenta común o individual).</summary>
public sealed class ServicioPerfiles(IRepositorioPerfiles repositorio)
{
    private const string NombreRepetido = "Ya existe un perfil con ese nombre.";

    /// <summary>Perfiles del hogar con su detalle, ordenados por nombre.</summary>
    public Task<IReadOnlyList<PerfilReparto>> ListarAsync(CancellationToken ct) => repositorio.ListarAsync(ct);

    /// <summary>Un perfil con su detalle; null si no existe.</summary>
    public Task<PerfilReparto?> ObtenerAsync(Guid id, CancellationToken ct) => repositorio.ObtenerAsync(id, ct);

    /// <summary>
    /// Crea un perfil con su detalle. Inválido si el nombre, el modo o el detalle no valen; conflicto si el nombre ya existe.
    /// <paramref name="predeterminadoGastos"/> marca el perfil como predeterminado para gastos (lo propone el asistente).
    /// </summary>
    public async Task<Resultado<PerfilReparto>> CrearAsync(
        Guid hogarId, string? nombre, string? modo, IReadOnlyList<ValorMiembro>? detalle, bool predeterminadoGastos, CancellationToken ct)
    {
        var v = await ValidarAsync(nombre, modo, detalle, null, ct);
        if (v.Error is not null) return v.Error;

        var perfil = new PerfilReparto
        {
            Id = Guid.NewGuid(), HogarId = hogarId, Nombre = v.Nombre, Modo = v.Modo, PredeterminadoGastos = predeterminadoGastos,
        };
        perfil.Detalles.AddRange(v.Detalle.Select(d => Nuevo(hogarId, perfil.Id, d)));
        repositorio.Anadir(perfil);
        return await repositorio.GuardarAsync(ct) ? Resultado<PerfilReparto>.Ok(perfil) : Resultado<PerfilReparto>.Conflicto(NombreRepetido);
    }

    /// <summary>Sustituye nombre, modo, detalle y marca «predeterminado para gastos»; los gastos ya guardados no se recalculan.</summary>
    public async Task<Resultado<PerfilReparto>> ActualizarAsync(
        Guid id, string? nombre, string? modo, IReadOnlyList<ValorMiembro>? detalle, bool predeterminadoGastos, CancellationToken ct)
    {
        var perfil = await repositorio.ObtenerParaEditarAsync(id, ct);
        if (perfil is null) return Resultado<PerfilReparto>.NoEncontrado();
        var v = await ValidarAsync(nombre, modo, detalle, id, ct);
        if (v.Error is not null) return v.Error;

        perfil.Nombre = v.Nombre;
        perfil.Modo = v.Modo;
        perfil.PredeterminadoGastos = predeterminadoGastos;
        repositorio.ReemplazarDetalle(perfil, v.Detalle.Select(d => Nuevo(perfil.HogarId, perfil.Id, d)).ToList());
        return await repositorio.GuardarAsync(ct) ? Resultado<PerfilReparto>.Ok(perfil) : Resultado<PerfilReparto>.Conflicto(NombreRepetido);
    }

    /// <summary>
    /// Borra el perfil y su detalle. Si lo usan categorías, gastos o gastos recurrentes hace falta <paramref name="moverAId"/>: sin
    /// él es conflicto (con <c>enUso</c> entre los datos extra, para que el cliente pregunte a qué perfil moverlos); con él, todo
    /// pasa al perfil de destino en el mismo guardado. El destino debe ser otro perfil y, como las categorías a cargo de la cuenta
    /// común exigen su perfil, o ambos son de cuenta común o ninguno lo es.
    /// </summary>
    public async Task<Resultado<bool>> EliminarAsync(Guid id, Guid? moverAId, CancellationToken ct)
    {
        var perfil = await repositorio.ObtenerParaEditarAsync(id, ct);
        if (perfil is null) return Resultado<bool>.NoEncontrado();

        var movidos = false;
        if (await repositorio.EnUsoAsync(id, ct))
        {
            if (moverAId is not { } destinoId)
                return Resultado<bool>.Conflicto(
                    "El perfil está en uso por categorías, gastos o gastos recurrentes.", new Dictionary<string, object> { ["enUso"] = true });
            if (destinoId == id) return Resultado<bool>.Invalido("El perfil de destino debe ser otro.");
            var destino = await repositorio.ObtenerAsync(destinoId, ct);
            if (destino is null) return Resultado<bool>.Invalido("El perfil de destino no existe en este hogar.");
            if ((destino.Modo == ModoReparto.CuentaComun) != (perfil.Modo == ModoReparto.CuentaComun))
                return Resultado<bool>.Invalido("El perfil de destino debe ser de cuenta común si el que se borra lo es, y no serlo si no.");

            await repositorio.ReasignarUsoAsync(id, destinoId, ct);
            movidos = true;
        }

        repositorio.Eliminar(perfil);
        if (await repositorio.GuardarAsync(ct)) return Resultado<bool>.Ok(true);
        return Resultado<bool>.Conflicto(movidos
            ? "No se ha podido mover el uso del perfil al perfil de destino."
            : "El perfil está en uso.");
    }

    private static PerfilRepartoDetalle Nuevo(Guid hogarId, Guid perfilId, ValorMiembro d) => new()
    {
        Id = Guid.NewGuid(), HogarId = hogarId, PerfilId = perfilId, MiembroId = d.MiembroId, Valor = d.Valor,
    };

    private sealed record Validado(string Nombre, ModoReparto Modo, IReadOnlyList<ValorMiembro> Detalle, Resultado<PerfilReparto>? Error);

    /// <summary>
    /// Valida nombre, modo y detalle: porcentaje y partes exigen adultos activos sin repetir y valores no negativos, con
    /// porcentajes que suman 100 o alguna parte mayor que 0; cuenta común e individual no admiten detalle. El nombre no puede repetirse.
    /// </summary>
    private async Task<Validado> ValidarAsync(
        string? nombreTexto, string? modoTexto, IReadOnlyList<ValorMiembro>? detalleTexto, Guid? idActual, CancellationToken ct)
    {
        static Validado Mal(string mensaje) => new("", default, [], Resultado<PerfilReparto>.Invalido(mensaje));

        if (ReglasPerfil.ValidarNombre(nombreTexto, out var nombre) is { } e) return Mal(e);
        if (!TextoModoReparto.TryParse(modoTexto, out var modo))
            return Mal("Modo no válido: use porcentaje, partes, cuenta_comun o individual.");

        var detalle = detalleTexto ?? [];
        if (ReglasPerfil.ValidarEstructura(modo, detalle) is { } estructura) return Mal(estructura);
        if (detalle.Count > 0)
        {
            var ids = detalle.Select(d => d.MiembroId).ToList();
            if (await repositorio.ContarParticipantesAsync(ids, ct) != ids.Count)
                return Mal("El detalle solo puede incluir adultos activos o miembros a cargo de un adulto activo.");
        }

        if (ReglasPerfil.ValidarValores(modo, detalle) is { } valores) return Mal(valores);
        if (await repositorio.NombreEnUsoAsync(nombre, idActual, ct))
            return new("", default, [], Resultado<PerfilReparto>.Conflicto(NombreRepetido));

        return new Validado(nombre, modo, detalle, null);
    }
}
