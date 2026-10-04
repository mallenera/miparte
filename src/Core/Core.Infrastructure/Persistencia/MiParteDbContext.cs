using Microsoft.EntityFrameworkCore;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>
/// Mapea el esquema definido en supabase/migrations (el esquema NO lo gestiona EF).
/// Las consultas se filtran por hogar_id con un global query filter, porque el rol
/// con el que se conecta el servicio puede saltarse la RLS de Supabase.
/// </summary>
public class MiParteDbContext(DbContextOptions<MiParteDbContext> options, IHogarActual hogarActual)
    : DbContext(options)
{
    private Guid? HogarId => hogarActual.HogarId;

    public DbSet<Hogar> Hogares => Set<Hogar>();
    public DbSet<Miembro> Miembros => Set<Miembro>();
    public DbSet<PerfilReparto> PerfilesReparto => Set<PerfilReparto>();
    public DbSet<PerfilRepartoDetalle> PerfilesRepartoDetalle => Set<PerfilRepartoDetalle>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Ingreso> Ingresos => Set<Ingreso>();
    public DbSet<GastoRecurrente> GastosRecurrentes => Set<GastoRecurrente>();
    public DbSet<Gasto> Gastos => Set<Gasto>();
    public DbSet<GastoReparto> GastosReparto => Set<GastoReparto>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Hogar>(e =>
        {
            e.ToTable("hogar");
            e.HasQueryFilter(x => x.Id == HogarId);
        });

        b.Entity<Miembro>(e =>
        {
            e.ToTable("miembro");
            e.Property(x => x.Tipo).HasConversion(
                v => v == TipoMiembro.Adulto ? "adulto" : "a_cargo",
                v => v == "adulto" ? TipoMiembro.Adulto : TipoMiembro.ACargo);
            e.HasQueryFilter(x => x.HogarId == HogarId);
        });

        b.Entity<PerfilReparto>(e =>
        {
            e.ToTable("perfil_reparto");
            e.Property(x => x.Modo).HasConversion(
                v => ModoATexto(v),
                v => TextoAModo(v));
            e.HasMany(x => x.Detalles).WithOne().HasForeignKey(x => x.PerfilId);
            e.HasQueryFilter(x => x.HogarId == HogarId);
        });

        b.Entity<PerfilRepartoDetalle>(e =>
        {
            e.ToTable("perfil_reparto_detalle");
            e.Property(x => x.Valor).HasPrecision(12, 4);
            e.HasQueryFilter(x => x.HogarId == HogarId);
        });

        b.Entity<Categoria>(e =>
        {
            e.ToTable("categoria");
            e.HasQueryFilter(x => x.HogarId == HogarId);
        });

        b.Entity<Ingreso>(e =>
        {
            e.ToTable("ingreso");
            e.Property(x => x.Importe).HasPrecision(12, 2);
            e.HasQueryFilter(x => x.HogarId == HogarId);
        });

        b.Entity<GastoRecurrente>(e =>
        {
            e.ToTable("gasto_recurrente");
            e.Property(x => x.Importe).HasPrecision(12, 2);
            e.HasQueryFilter(x => x.HogarId == HogarId);
        });

        b.Entity<Gasto>(e =>
        {
            e.ToTable("gasto");
            e.Property(x => x.Importe).HasPrecision(12, 2);
            e.HasMany(x => x.Repartos).WithOne().HasForeignKey(x => x.GastoId);
            e.HasQueryFilter(x => x.HogarId == HogarId);
        });

        b.Entity<GastoReparto>(e =>
        {
            e.ToTable("gasto_reparto");
            e.HasKey(x => new { x.GastoId, x.MiembroId });
            e.Property(x => x.ImporteAsumido).HasPrecision(12, 2);
            e.HasQueryFilter(x => x.HogarId == HogarId);
        });
    }

    private static string ModoATexto(ModoReparto m) => m switch
    {
        ModoReparto.Porcentaje => "porcentaje",
        ModoReparto.Partes => "partes",
        ModoReparto.Ingresos => "ingresos",
        ModoReparto.Individual => "individual",
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    private static ModoReparto TextoAModo(string t) => t switch
    {
        "porcentaje" => ModoReparto.Porcentaje,
        "partes" => ModoReparto.Partes,
        "ingresos" => ModoReparto.Ingresos,
        "individual" => ModoReparto.Individual,
        _ => throw new InvalidOperationException($"Modo de reparto desconocido: {t}"),
    };
}
