using System.Security.Cryptography;
using System.Text;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>Reglas de los miembros del hogar que no dependen de persistencia.</summary>
public static class ReglasMiembros
{
    /// <summary>Longitud máxima del nombre de un miembro.</summary>
    public const int MaxLongitudNombre = 100;

    /// <summary>
    /// Si un miembro con el rol, los permisos propios y el estado dados podría cambiar permisos: activo, adulto con cuenta y con
    /// el permiso efectivo. Sirve para comprobar que el hogar siempre conserva a alguien que pueda arreglarlo.
    /// </summary>
    public static bool PuedeGestionarPermisos(Miembro m, RolMiembro rol, List<string>? permisos, bool activo)
        => activo && CatalogoPermisos.Efectivos(new Miembro { Tipo = m.Tipo, UserId = m.UserId, Rol = rol, Permisos = permisos })
            .Contains(CatalogoPermisos.PermisosGestionar);

    /// <summary>Si dos listas propias de permisos son iguales como conjuntos (null = sin lista propia).</summary>
    public static bool MismosPermisos(List<string>? a, List<string>? b)
        => a is null || b is null ? a is null && b is null : a.ToHashSet().SetEquals(b);
}

/// <summary>Token de invitación: se entrega en claro una sola vez y en base de datos solo se guarda su hash.</summary>
public static class TokenInvitacion
{
    /// <summary>Tiempo durante el que una invitación puede aceptarse desde su creación (7 días).</summary>
    public static readonly TimeSpan Vigencia = TimeSpan.FromDays(7);

    /// <summary>Genera un token aleatorio de 256 bits en Base64 apto para URL.</summary>
    public static string Generar()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>SHA-256 en hexadecimal minúscula del token en UTF-8 (igual que aceptar_invitacion en SQL).</summary>
    public static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
