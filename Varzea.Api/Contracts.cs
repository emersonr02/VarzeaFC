using Varzea.Engine.Model;
using Varzea.Engine.Scoring;

namespace Varzea.Api;

public sealed record StartRequest(ulong? Seed);

public sealed record LegendOption(string Name, int Rating);

public sealed record DraftRoundResponse(string Token, int Round, Attr Attribute, IReadOnlyList<LegendOption> Candidates);

public sealed record DraftRequest(string Token, int Pick);

public sealed record PositionPotential(Pos Position, int Potential);

/// <summary>Potencial em TODAS as posições, não só a escolhida — o front precisa disso
/// pra deixar o jogador comparar antes de travar (ver varzea-lendas.html original), e os
/// pesos por posição só existem no balance.json do servidor, não podem ser duplicados no
/// cliente sem quebrar "servidor autoritativo" (HANDOFF §2).</summary>
public sealed record DraftCompleteResponse(string Token, int[] Attributes, IReadOnlyList<PositionPotential> Potentials);

public sealed record PositionRequest(string Token, Pos Position, string Country);

public sealed record PositionLockedResponse(string Token, int Potential, string Role);

public sealed record ClubOptionsRequest(string Token);

/// <summary>3 clubes reais do país já escolhido (Roadmap pós-§9, painel Clube) — o
/// jogador escolhe um em /careers/clubs/choose antes de a carreira começar a rodar de
/// verdade. Ver CareerSimulator.StartingClubOptions.</summary>
public sealed record ClubOptionsResponse(string Token, IReadOnlyList<string> Options);

public sealed record ChooseClubRequest(string Token, int Choice);

public sealed record ClubChosenResponse(string Token, string ClubName);

/// <summary>Request só é anexado a SeasonRequests quando Decision e ContractChoiceIndex
/// são ambos null — ver /careers/advance. Enviar Request junto de uma resolução de
/// pausa não faz sentido: o pedido é feito ANTES de avançar pra próxima temporada, nunca
/// junto da resposta a uma pausa que já surgiu na temporada corrente. Decision resolve
/// PendingOffer; ContractChoiceIndex resolve PendingContractChoice (Roadmap §9 Bloco 3,
/// "múltiplas propostas") — nunca os dois preenchidos no mesmo POST, já que os dois
/// tipos de pausa são mutuamente exclusivos (CareerProgress.AwaitingDecision).</summary>
/// <summary>RevealAll (true): não limita a revelação a uma temporada por chamada — usado
/// só por "pular tudo" no front, que já resolve toda pausa sozinho num laço e quer o
/// menor número de viagens de rede possível. Omitido/false: uma chamada revela no máximo
/// UMA temporada nova (ver CareerSimulator.AdvanceCareer, revealLimit) — bug real
/// corrigido: sem este limite, uma carreira sem pausa no início podia revelar 6+
/// temporadas numa chamada só, e o jogador só conseguia usar os painéis de pedido uma
/// vez a cada tantas temporadas em vez de uma vez por temporada.</summary>
public sealed record AdvanceRequest(
    string Token, bool? Decision, SeasonRequestKind? Request = null, int? ContractChoiceIndex = null,
    bool? RevealAll = null);

public sealed record AdvanceResponse(
    string Token, IReadOnlyList<SeasonResult> NewSeasons, PendingTransferOffer? PendingOffer,
    PendingContractChoice? PendingContractChoice, bool Finished);

/// <summary>
/// PlayerId e SlotIndex são opcionais: sem eles (ou sem Postgres configurado),
/// /careers/save só calcula o score e não persiste nada — o comportamento de hoje.
/// PlayerId existe só como FK provisória; não há autenticação real ainda (ver
/// Varzea.Data.Entities.Player).
/// </summary>
public sealed record SaveRequest(string Token, Guid? PlayerId = null, int? SlotIndex = null);

/// <summary>Agregados da carreira inteira pro veredito final. O cliente já viu cada
/// temporada individualmente via /careers/advance, mas somar de novo no front duplicaria
/// lógica sem necessidade — o servidor já tem os totais prontos em CareerResult.</summary>
public sealed record CareerTotals(
    int PeakOverall, int Seasons, int TotalGoals, int TotalAssists, int TotalTackles, int TotalCleanSheets, int TotalCaps);

public sealed record SaveResponse(
    double Score, ScoreBreakdown Breakdown, int? SavedToSlot,
    IReadOnlyDictionary<TitleKind, int> TitleCounts, CareerTotals Totals);

public sealed record AnnualChallengeResponse(int Period, ulong Seed);

/// <summary>Países vêm do balance.json (JSON versionado, fora do código — HANDOFF §2),
/// não podem ser hardcoded no front sob risco de o front ficar fora de sincronia com o
/// ruleset quando ele mudar.</summary>
public sealed record MetaResponse(string RulesetVersion, IReadOnlyList<string> Countries);
