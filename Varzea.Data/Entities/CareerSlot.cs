using Varzea.Engine.Model;

namespace Varzea.Data.Entities;

/// <summary>
/// Uma carreira salva num dos 10 slots geríveis do jogador (HANDOFF §7.8). Guarda a
/// RECEITA (o que a Recipe precisa pra re-simular) mais o score e o RulesetVersion
/// CONGELADOS no momento do save — regra invioláveis do motor (HANDOFF §2): "Toda
/// carreira salva grava a RulesetVersion usada e o score congelado." O score não é a
/// fonte de verdade (a receita é, e dá pra re-simular e conferir a qualquer momento),
/// mas fica cacheado aqui porque recalcular em toda leitura de ranking seria caro.
///
/// Nunca é apagada (só Archived=true) porque uma Achievement pode referenciá-la — ver
/// o índice único parcial em VarzeaDbContext, que só vale pra slots ativos.
/// </summary>
public sealed class CareerSlot
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    /// <summary>0-9. Junto com PlayerId forma a identidade "lógica" do slot; pode haver
    /// várias linhas históricas com o mesmo (PlayerId, SlotIndex) se as antigas
    /// estiverem arquivadas — só uma pode estar ativa por vez.</summary>
    public int SlotIndex { get; set; }
    public bool Archived { get; set; }

    // ---- Receita (CareerRecipe) ----
    public decimal Seed { get; set; } // ulong não cabe em bigint do Postgres; numeric(20,0) cabe
    public string Country { get; set; } = "";
    public int[] DraftPicks { get; set; } = Array.Empty<int>();
    public Pos Position { get; set; }
    public bool[] TransferChoices { get; set; } = Array.Empty<bool>();
    public string RulesetVersion { get; set; } = "";

    // ---- Score congelado no save (ver comentário da classe) ----
    public double Score { get; set; }
    public double TitlesScore { get; set; }
    public double AwardsScore { get; set; }
    public double ProductionScore { get; set; }
    public double PeakScore { get; set; }

    public DateTimeOffset SavedAt { get; set; }
}
