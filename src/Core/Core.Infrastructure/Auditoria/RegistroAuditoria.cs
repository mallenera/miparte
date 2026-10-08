using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Infrastructure.Auditoria;

/// <summary>
/// Convierte los cambios pendientes del <see cref="ChangeTracker"/> en eventos de auditoría. Lo invoca
/// <c>MiParteDbContext.SaveChanges</c> justo antes de guardar, de modo que el evento y el cambio comparten
/// transacción: no puede haber cambio sin rastro. Un gasto y un perfil se auditan como una unidad con sus filas
/// hijas (reparto y detalle), no fila a fila.
/// </summary>
public static class RegistroAuditoria
{
    /// <summary>Acción de auditoría: alta.</summary>
    public const string Crear = "crear";
    /// <summary>Acción de auditoría: modificación.</summary>
    public const string Editar = "editar";
    /// <summary>Acción de auditoría: borrado.</summary>
    public const string Borrar = "borrar";
    /// <summary>Acción de auditoría: un usuario queda vinculado a un miembro.</summary>
    public const string Vincular = "vincular";
    /// <summary>Acción de auditoría: una invitación se usa.</summary>
    public const string Usar = "usar";

    /// <summary>Propiedades que nunca van al rastro: la clave ya es la entidad, el hogar es la columna y el hash del token es un secreto.</summary>
    private static readonly HashSet<string> Excluidas = ["Id", "HogarId", nameof(InvitacionHogar.TokenHash)];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Crea un evento a mano, para los cambios que no pasan por el ChangeTracker (p. ej. <c>ExecuteUpdate</c>).</summary>
    /// <param name="hogarId">Hogar al que pertenece.</param>
    /// <param name="usuarioId">Autor del cambio.</param>
    /// <param name="accion">Una de las constantes de acción.</param>
    /// <param name="entidad">Tipo de entidad (gasto, miembro...).</param>
    /// <param name="entidadId">Identificador de la entidad.</param>
    /// <param name="antes">Valores anteriores, o null.</param>
    /// <param name="despues">Valores nuevos, o null.</param>
    public static EventoAuditoria Manual(
        Guid hogarId, Guid? usuarioId, string accion, string entidad, Guid entidadId,
        object? antes = null, object? despues = null)
        => new()
        {
            Id = Guid.NewGuid(), HogarId = hogarId, UsuarioId = usuarioId, Cuando = DateTimeOffset.UtcNow,
            Accion = accion, Entidad = entidad, EntidadId = entidadId,
            Antes = antes is null ? null : JsonSerializer.Serialize(antes, Json),
            Despues = despues is null ? null : JsonSerializer.Serialize(despues, Json),
        };

    /// <summary>Genera los eventos de todo lo que hay pendiente de guardar en el tracker.</summary>
    /// <param name="tracker">Seguimiento de cambios del contexto (se llama antes de guardar).</param>
    /// <param name="usuarioId">Autor de los cambios.</param>
    public static List<EventoAuditoria> Capturar(ChangeTracker tracker, Guid? usuarioId)
    {
        tracker.DetectChanges();
        var entradas = tracker.Entries().ToList();
        var eventos = new List<EventoAuditoria>();
        var ahora = DateTimeOffset.UtcNow;

        // Raíces auditables con cambios propios, más los padres cuyo único cambio está en sus hijas
        // (un perfil al que solo se le cambia el detalle).
        var raices = new List<EntityEntry>();
        foreach (var e in entradas)
        {
            if (Nombre(e.Entity) is null || e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;
            raices.Add(e);
        }
        foreach (var hija in entradas.Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var padreId = hija.Entity switch
            {
                GastoReparto r => (Tipo: typeof(Gasto), Id: r.GastoId),
                PerfilRepartoDetalle d => (Tipo: typeof(PerfilReparto), Id: d.PerfilId),
                _ => (Tipo: null!, Id: Guid.Empty),
            };
            if (padreId.Tipo is null) continue;
            var padre = entradas.FirstOrDefault(p => p.Entity.GetType() == padreId.Tipo && IdDe(p.Entity) == padreId.Id);
            if (padre is not null && !raices.Contains(padre)) raices.Add(padre);
        }

        foreach (var raiz in raices)
        {
            var evento = Evento(raiz, entradas, usuarioId, ahora);
            if (evento is not null) eventos.Add(evento);
        }
        return eventos;
    }

    private static EventoAuditoria? Evento(EntityEntry raiz, List<EntityEntry> todas, Guid? usuarioId, DateTimeOffset ahora)
    {
        var antes = new Dictionary<string, object?>();
        var despues = new Dictionary<string, object?>();
        string accion;

        switch (raiz.State)
        {
            case EntityState.Added:
                accion = Crear;
                foreach (var p in Propiedades(raiz)) despues[p.Metadata.Name] = p.CurrentValue;
                break;
            case EntityState.Deleted:
                accion = Borrar;
                foreach (var p in Propiedades(raiz)) antes[p.Metadata.Name] = p.OriginalValue;
                break;
            default:
                accion = Editar;
                foreach (var p in Propiedades(raiz).Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)))
                {
                    antes[p.Metadata.Name] = p.OriginalValue;
                    despues[p.Metadata.Name] = p.CurrentValue;
                }
                break;
        }

        // Filas hijas: se guarda la lista completa antes y después, y solo si ha cambiado (o si es alta/baja).
        var (nombreHijas, hijasAntes, hijasDespues) = Hijas(raiz.Entity, todas);
        if (nombreHijas is not null)
        {
            var sa = JsonSerializer.Serialize(hijasAntes, Json);
            var sd = JsonSerializer.Serialize(hijasDespues, Json);
            if (accion == Crear) despues[nombreHijas] = hijasDespues;
            else if (accion == Borrar) antes[nombreHijas] = hijasAntes;
            else if (sa != sd)
            {
                antes[nombreHijas] = hijasAntes;
                despues[nombreHijas] = hijasDespues;
            }
        }

        if (accion == Editar && antes.Count == 0 && despues.Count == 0) return null;

        var hogar = raiz.Entity is Hogar h ? h.Id : (Guid)raiz.Property("HogarId").CurrentValue!;
        return new EventoAuditoria
        {
            Id = Guid.NewGuid(), HogarId = hogar, UsuarioId = usuarioId, Cuando = ahora,
            Accion = accion, Entidad = Nombre(raiz.Entity)!, EntidadId = IdDe(raiz.Entity),
            Antes = antes.Count == 0 ? null : JsonSerializer.Serialize(antes, Json),
            Despues = despues.Count == 0 ? null : JsonSerializer.Serialize(despues, Json),
        };
    }

    /// <summary>Filas hijas del gasto (reparto) o del perfil (detalle) antes y después del guardado, ordenadas por miembro.</summary>
    private static (string? Nombre, List<object> Antes, List<object> Despues) Hijas(object raiz, List<EntityEntry> todas)
    {
        switch (raiz)
        {
            case Gasto g:
            {
                var hijas = todas.Where(e => e.Entity is GastoReparto r && r.GastoId == g.Id).ToList();
                return ("repartos",
                    Instantanea(hijas, EntityState.Added, original: true, "MiembroId", "ImporteAsumido"),
                    Instantanea(hijas, EntityState.Deleted, original: false, "MiembroId", "ImporteAsumido"));
            }
            case PerfilReparto p:
            {
                var hijas = todas.Where(e => e.Entity is PerfilRepartoDetalle d && d.PerfilId == p.Id).ToList();
                return ("detalle",
                    Instantanea(hijas, EntityState.Added, original: true, "MiembroId", "Valor"),
                    Instantanea(hijas, EntityState.Deleted, original: false, "MiembroId", "Valor"));
            }
            default:
                return (null, [], []);
        }
    }

    /// <summary>Foto de las hijas: con <paramref name="original"/> los valores previos (sin las recién añadidas); si no, los actuales (sin las borradas).</summary>
    private static List<object> Instantanea(
        List<EntityEntry> hijas, EntityState excluir, bool original, string clave, string valor)
        => hijas.Where(h => h.State != excluir)
            .Select(h => (Miembro: (Guid)Valor(h, clave, original)!, Dato: Valor(h, valor, original)))
            .OrderBy(x => x.Miembro)
            .Select(x => (object)new Dictionary<string, object?> { [clave] = x.Miembro, [valor] = x.Dato })
            .ToList();

    private static object? Valor(EntityEntry e, string propiedad, bool original)
        => original ? e.Property(propiedad).OriginalValue : e.Property(propiedad).CurrentValue;

    private static IEnumerable<PropertyEntry> Propiedades(EntityEntry e)
        => e.Properties.Where(p => !Excluidas.Contains(p.Metadata.Name) && !p.Metadata.IsShadowProperty());

    private static Guid IdDe(object entidad) => entidad switch
    {
        Hogar x => x.Id, Miembro x => x.Id, InvitacionHogar x => x.Id, Gasto x => x.Id, GastoRecurrente x => x.Id,
        PagoLiquidacionRegistro x => x.Id, AportacionCuenta x => x.Id, ReembolsoCuenta x => x.Id,
        RetiradaAhorro x => x.Id, DepositoAhorro x => x.Id, PerfilReparto x => x.Id, Categoria x => x.Id, _ => Guid.Empty,
    };

    /// <summary>Nombre con el que se registra el tipo de entidad, o null si no se audita.</summary>
    private static string? Nombre(object entidad) => entidad switch
    {
        Hogar => "hogar", Miembro => "miembro", InvitacionHogar => "invitacion", Gasto => "gasto",
        GastoRecurrente => "gasto_recurrente", PagoLiquidacionRegistro => "pago_liquidacion",
        AportacionCuenta => "aportacion_cuenta", ReembolsoCuenta => "reembolso_cuenta",
        RetiradaAhorro => "retirada_ahorro", DepositoAhorro => "deposito_ahorro", PerfilReparto => "perfil_reparto", Categoria => "categoria", _ => null,
    };
}
