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
    bool[] TransferChoices
);
