using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Hogares;

/// <summary>
/// Datos por defecto de un hogar nuevo. Replica exactamente la semilla de la función SQL
/// crear_hogar (supabase/migrations/20261005000000_multihogar_invitaciones_pagos.sql).
/// </summary>
public static class SemillaHogar
{
    public const string PerfilIngresos = "Proporcional a ingresos";
    public const string PerfilPartes = "Por partes";
    public const string PerfilPorcentaje = "Porcentaje fijo";
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
    /// Añade al contexto (sin guardar) los 4 perfiles, los detalles del creador (1 parte en "Por partes",
    /// 100 % en "Porcentaje fijo", para que un hogar de un solo adulto funcione desde el inicio) y las
    /// categorías de ejemplo.
    /// </summary>
    public static void Sembrar(MiParteDbContext db, Guid hogarId, Guid creadorId)
    {
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

        db.PerfilesRepartoDetalle.Add(new PerfilRepartoDetalle
            { Id = Guid.NewGuid(), HogarId = hogarId, PerfilId = partes.Id, MiembroId = creadorId, Valor = 1 });
        db.PerfilesRepartoDetalle.Add(new PerfilRepartoDetalle
            { Id = Guid.NewGuid(), HogarId = hogarId, PerfilId = porcentaje.Id, MiembroId = creadorId, Valor = 100 });

        foreach (var (nombre, perfil) in Categorias)
            db.Categorias.Add(new Categoria
                { Id = Guid.NewGuid(), HogarId = hogarId, Nombre = nombre, PerfilRepartoId = porNombre[perfil].Id });
    }
}
