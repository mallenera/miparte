using System.Text.Json;

namespace MiParte.Contracts;

/// <summary>
/// Un cambio del historial del hogar. <paramref name="Antes"/> y <paramref name="Despues"/> son objetos JSON con
/// los campos afectados (en una edición, solo los que cambian; al crear solo <c>despues</c>, al borrar solo <c>antes</c>).
/// </summary>
/// <param name="Id">Identificador del evento.</param>
/// <param name="Cuando">Instante del cambio.</param>
/// <param name="UsuarioId">Usuario que lo hizo; null si no consta.</param>
/// <param name="Autor">Nombre del miembro vinculado a ese usuario en el hogar, si existe.</param>
/// <param name="Accion">crear, editar, borrar, vincular o usar.</param>
/// <param name="Entidad">Tipo de entidad: hogar, miembro, invitacion, gasto, gasto_recurrente, pago_liquidacion, aportacion_cuenta, reembolso_cuenta, retirada_ahorro, perfil_reparto o categoria.</param>
/// <param name="EntidadId">Identificador de la entidad afectada.</param>
/// <param name="Antes">Valores anteriores, o null.</param>
/// <param name="Despues">Valores nuevos, o null.</param>
public record EventoAuditoriaDto(
    Guid Id, DateTimeOffset Cuando, Guid? UsuarioId, string? Autor, string Accion, string Entidad, Guid EntidadId,
    JsonElement? Antes, JsonElement? Despues);
