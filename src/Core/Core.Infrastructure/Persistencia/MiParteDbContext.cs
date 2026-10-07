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
    /// <summary>Hogar de la petición en curso; los query filters lo leen en cada consulta.</summary>
    private Guid? HogarId => hogarActual.HogarId;

    /// <summary>Conjunto de <see cref="Hogar"/> del hogar actual.</summary>
    public DbSet<Hogar> Hogares => Set<Hogar>();
    /// <summary>Conjunto de <see cref="Miembro"/> del hogar actual.</summary>
    public DbSet<Miembro> Miembros => Set<Miembro>();
    /// <summary>Conjunto de <see cref="PerfilReparto"/> del hogar actual.</summary>
    public DbSet<PerfilReparto> PerfilesReparto => Set<PerfilReparto>();
    /// <summary>Conjunto de <see cref="PerfilRepartoDetalle"/> del hogar actual.</summary>
    public DbSet<PerfilRepartoDetalle> PerfilesRepartoDetalle => Set<PerfilRepartoDetalle>();
    /// <summary>Conjunto de <see cref="Categoria"/> del hogar actual.</summary>
    public DbSet<Categoria> Categorias => Set<Categoria>();
    /// <summary>Conjunto de <see cref="GastoRecurrente"/> del hogar actual.</summary>
    public DbSet<GastoRecurrente> GastosRecurrentes => Set<GastoRecurrente>();
    /// <summary>Conjunto de <see cref="Gasto"/> del hogar actual.</summary>
    public DbSet<Gasto> Gastos => Set<Gasto>();
    /// <summary>Conjunto de <see cref="GastoReparto"/> del hogar actual.</summary>
    public DbSet<GastoReparto> GastosReparto => Set<GastoReparto>();

    /// <summary>Conjunto de <see cref="InvitacionHogar"/> del hogar actual.</summary>
    public DbSet<InvitacionHogar> Invitaciones => Set<InvitacionHogar>();
    /// <summary>Conjunto de <see cref="PagoLiquidacionRegistro"/> del hogar actual.</summary>
    public DbSet<PagoLiquidacionRegistro> PagosLiquidacion => Set<PagoLiquidacionRegistro>();

    /// <summary>
    /// Fija tablas, precisiones y conversiones de enums a texto, y aplica a cada entidad el
    /// filtro global por hogar (toda entidad nueva con HogarId necesita su HasQueryFilter).
    /// </summary>
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Hogar>(e =>
        {
            e.ToTable("hogar");
            e.Property(x => x.CreadoEn).HasDefaultValueSql("now()"); // lo asigna la base de datos
            e.HasQueryFilter(x => x.Id == HogarId);
        });

        b.Entity<Miembro>(e =>
        {
            e.ToTable("miembro");
            e.Property(x => x.Tipo).HasConversion(
                v => v == TipoMiembro.Adulto ? "adulto" : "a_cargo",
                v => v == "adulto" ? TipoMiembro.Adulto : TipoMiembro.ACargo);
            e.Property(x => x.Rol).HasConversion(
                v => v == RolMiembro.Admin ? "admin" : "miembro",
                v => v == "admin" ? RolMiembro.Admin : RolMiembro.Miembro);
            e.HasQueryFilter(x => x.HogarId == HogarId);
        });

        b.Entity<InvitacionHogar>(e =>
        {
            e.ToTable("invitacion_hogar");
            e.HasKey(x => x.Id);
            e.Property(x => x.CreadaEn).HasDefaultValueSql("now()");
            // FK (hogar_id, miembro_id) -> miembro(hogar_id, id): se aplica en SQL; EF solo mapea columnas.
            e.HasQueryFilter(x => x.HogarId == HogarId);
        });

        b.Entity<PagoLiquidacionRegistro>(e =>
        {
            e.ToTable("pago_liquidacion");
            e.HasKey(x => x.Id);
            e.Property(x => x.Importe).HasPrecision(12, 2);
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

    /// <summary>Convierte el modo de reparto al texto almacenado en la columna de la base de datos.</summary>
    private static string ModoATexto(ModoReparto m) => m switch
    {
        ModoReparto.Porcentaje => "porcentaje",
        ModoReparto.Partes => "partes",
        ModoReparto.CuentaComun => "cuenta_comun",
        ModoReparto.Individual => "individual",
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    /// <summary>Convierte el texto de la base de datos al modo de reparto; falla si el valor es desconocido.</summary>
    private static ModoReparto TextoAModo(string t) => t switch
    {
        "porcentaje" => ModoReparto.Porcentaje,
        "partes" => ModoReparto.Partes,
        "cuenta_comun" => ModoReparto.CuentaComun,
        "individual" => ModoReparto.Individual,
        _ => throw new InvalidOperationException($"Modo de reparto desconocido: {t}"),
    };
}
