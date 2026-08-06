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

public sealed record AdvanceRequest(string Token, bool? Decision);

public sealed record AdvanceResponse(
    string Token, IReadOnlyList<SeasonResult> NewSeasons, PendingTransferOffer? PendingOffer, bool Finished);

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
