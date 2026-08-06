using Microsoft.EntityFrameworkCore;
using Varzea.Data.Entities;
using Varzea.Engine.Model;

namespace Varzea.Data;

public sealed class VarzeaDbContext(DbContextOptions<VarzeaDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<CareerSlot> CareerSlots => Set<CareerSlot>();
    public DbSet<Achievement> Achievements => Set<Achievement>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Player>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.DisplayName).HasMaxLength(64);
        });

        model.Entity<CareerSlot>(e =>
        {
            e.HasKey(c => c.Id);

            // ulong não cabe em bigint (64-bit COM sinal) do Postgres; numeric(20,0)
            // cobre o range inteiro de ulong sem perder precisão.
            e.Property(c => c.Seed).HasColumnType("numeric(20,0)");
            e.Property(c => c.Country).HasMaxLength(32);
            e.Property(c => c.RulesetVersion).HasMaxLength(16);
            e.Property(c => c.Position).HasConversion<string>().HasMaxLength(8);

            // int[] e bool[] são tipos nativos do Postgres — Npgsql mapeia direto,
            // sem precisar de tabela auxiliar pra 8 picks ou até ~40 decisões de transferência.
            e.Property(c => c.DraftPicks).HasColumnType("integer[]");
            e.Property(c => c.TransferChoices).HasColumnType("boolean[]");

            e.HasOne(c => c.Player).WithMany().HasForeignKey(c => c.PlayerId);

            // Só um slot ATIVO por (jogador, índice) — a versão arquivada de um slot
            // sobrescrito continua na tabela (pode ter Achievement apontando pra ela).
            e.HasIndex(c => new { c.PlayerId, c.SlotIndex })
                .IsUnique()
                .HasFilter("NOT \"Archived\"");
        });

        model.Entity<Achievement>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.PeriodType).HasConversion<string>().HasMaxLength(8);
            e.Property(a => a.PeriodKey).HasMaxLength(16);
            e.Property(a => a.Tier).HasMaxLength(24);

            e.HasOne(a => a.Player).WithMany().HasForeignKey(a => a.PlayerId);

            // Restrict, não Cascade: apagar (ou tentar apagar) um CareerSlot referenciado
            // por uma conquista tem que falhar no banco, não silenciosamente arrastar a
            // conquista junto. "Carreira referenciada por conquista nunca pode ser
            // apagada, só arquivada" (HANDOFF §7.6) vira uma restrição de verdade aqui.
            e.HasOne(a => a.CareerSlot).WithMany()
                .HasForeignKey(a => a.CareerSlotId)
                .OnDelete(DeleteBehavior.Restrict);

            // A idempotência do job de fecho de período inteira vem desse índice:
            // rodar o fecho duas vezes pro mesmo período não duplica a conquista.
            e.HasIndex(a => new { a.PlayerId, a.PeriodType, a.PeriodKey }).IsUnique();
        });
    }
}
