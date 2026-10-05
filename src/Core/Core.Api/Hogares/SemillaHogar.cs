using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Hogares;

/// <summary>
/// Datos por defecto de un hogar nuevo. Replica exactamente la semilla de la función SQL
/// crear_hogar (supabase/migrations/20261005000000_multihogar_invitaciones_pagos.sql).
/// </summary>
public static class SemillaHogar
{
    /// <summary>Nombre del perfil de reparto proporcional a ingresos.</summary>
    public const string PerfilIngresos = "Proporcional a ingresos";

    /// <summary>Nombre del perfil de reparto por partes.</summary>
    public const string PerfilPartes = "Por partes";

    /// <summary>Nombre del perfil de reparto por porcentaje fijo.</summary>
    public const string PerfilPorcentaje = "Porcentaje fijo";

    /// <summary>Nombre del perfil de reparto individual.</summary>
    public const string PerfilIndividual = "Individual";

    /// <summary>Categoría de ejemplo y nombre del perfil de reparto por defecto.</summary>
    public static readonly IReadOnlyList<(string Categoria, string Perfil)> Categorias =
    [
        ("Hipoteca/Alquiler", PerfilIngresos),
        ("Alimentación", PerfilIngresos),
        ("Suministros", PerfilIngresos),
        ("Gastos varios de casa", PerfilIngresos),
        ("Hijo", PerfilPartes),
        ("Ocio personal", PerfilIndividual),
    ];

    /// <summary>
    /// Crea los 4 perfiles, los detalles del creador (1 parte en "Por partes", 100 % en "Porcentaje fijo",
    /// para que un hogar de un solo adulto funcione desde el inicio) y las categorías de ejemplo.
    /// Guarda por etapas (perfiles → detalles → categorías): EF no conoce las FK compuestas del SQL y
    /// no ordenaría los INSERT por sí solo. Hogar y creador deben estar ya guardados.
    /// </summary>
    public static async Task SembrarAsync(MiParteDbContext db, Guid hogarId, Guid creadorId, CancellationToken ct = default)
    {
        // Función local: añade al contexto un perfil de reparto del hogar (sin guardar) y lo devuelve.
        PerfilReparto Perfil(string nombre, ModoReparto modo)
        {
            var p = new PerfilReparto { Id = Guid.NewGuid(), HogarId = hogarId, Nombre = nombre, Modo = modo };
            db.PerfilesReparto.Add(p);
            return p;
        }

        var ingresos = Perfil(PerfilIngresos, ModoReparto.Ingresos);
        var partes = Perfil(PerfilPartes, ModoReparto.Partes);
        var porcentaje = Perfil(PerfilPorcentaje, ModoReparto.Porcentaje);
        var individual = Perfil(PerfilIndividual, ModoReparto.Individual);
        var porNombre = new Dictionary<string, PerfilReparto>
        {
            [PerfilIngresos] = ingresos, [PerfilPartes] = partes,
            [PerfilPorcentaje] = porcentaje, [PerfilIndividual] = individual,
        };

        await db.SaveChangesAsync(ct); // perfiles antes que sus detalles y categorías

        db.PerfilesRepartoDetalle.Add(new PerfilRepartoDetalle
            { Id = Guid.NewGuid(), HogarId = hogarId, PerfilId = partes.Id, MiembroId = creadorId, Valor = 1 });
        db.PerfilesRepartoDetalle.Add(new PerfilRepartoDetalle
            { Id = Guid.NewGuid(), HogarId = hogarId, PerfilId = porcentaje.Id, MiembroId = creadorId, Valor = 100 });

        foreach (var (nombre, perfil) in Categorias)
            db.Categorias.Add(new Categoria
                { Id = Guid.NewGuid(), HogarId = hogarId, Nombre = nombre, PerfilRepartoId = porNombre[perfil].Id });

        await db.SaveChangesAsync(ct);
    }
}
