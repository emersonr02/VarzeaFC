namespace Varzea.Data.Entities;

/// <summary>
/// Snapshot imutável de uma conquista de período (HANDOFF §7.6): "nunca derivar ranking
/// em tempo real, senão um rebalanceamento reescreve o passado." Score é uma cópia
/// congelada no momento do fecho, independente do que CareerSlot.Score valha depois.
///
/// UNIQUE(PlayerId, PeriodType, PeriodKey) em VarzeaDbContext é o que dá idempotência
/// ao job de fecho de período: rodar o fecho duas vezes pro mesmo período não duplica.
/// "Uma por período, sem aninhamento" (HANDOFF §2) é responsabilidade de quem escreve
/// aqui, não do schema — Top 1 e Top 10 do mesmo período são duas linhas com Tier
/// diferente, cabe à lógica do job decidir se ambas se aplicam ou só a mais alta.
/// </summary>
public sealed class Achievement
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    /// <summary>A carreira que rendeu a conquista. Nunca aponta pra uma linha apagada —
    /// CareerSlot só arquiva, nunca deleta, e o FK usa Restrict pra impedir isso no banco.</summary>
    public Guid CareerSlotId { get; set; }
    public CareerSlot CareerSlot { get; set; } = null!;

    public PeriodType PeriodType { get; set; }

    /// <summary>Identifica a instância do período: "2026-W32", "2026-08", "2026".</summary>
    public string PeriodKey { get; set; } = "";

    /// <summary>Ex.: "Top1", "Top5", "Top10", "BallonDOr", "TeamOfTheYear".</summary>
    public string Tier { get; set; } = "";

    public double Score { get; set; }
    public DateTimeOffset AwardedAt { get; set; }
}
