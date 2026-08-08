using Varzea.Engine.Model;

namespace Varzea.Api;

/// <summary>
/// Estado de uma carreira em progresso, ainda não salva. Segue a mesma filosofia da
/// CareerRecipe: é só receita (seed + escolhas), nunca resultado. Viaja no token
/// assinado devolvido ao cliente — o servidor não guarda sessão nenhuma.
/// </summary>
public sealed record CareerState(
    ulong Seed,
    string RulesetVersion,
    int[] DraftPicks,
    Pos? Position,
    string? Country,
    bool[] TransferChoices,
    int SeasonsRevealed = 0,
    /// <summary>Pedidos do painel Contrato + Técnico, um por temporada — ver
    /// AdvanceRequest.Request e CareerRecipe.SeasonRequests.</summary>
    SeasonRequestKind[]? SeasonRequests = null,
    /// <summary>Índices de propostas escolhidas quando o contrato vence sem renovação
    /// (Roadmap §9 Bloco 3, "múltiplas propostas") — ver AdvanceRequest.ContractChoiceIndex
    /// e CareerRecipe.ContractChoices.</summary>
    int[]? ContractChoices = null,
    /// <summary>Índice (0-2) do clube real escolhido entre as 3 opções iniciais do
    /// próprio país (Roadmap pós-§9, painel Clube) — ver /careers/clubs e
    /// CareerRecipe.StartingClubChoice. Null = ainda não escolhido.</summary>
    int? StartingClubChoice = null
);
