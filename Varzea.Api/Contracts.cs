using Varzea.Engine.Model;
using Varzea.Engine.Scoring;

namespace Varzea.Api;

public sealed record StartRequest(ulong? Seed);

public sealed record LegendOption(string Name, int Rating);

public sealed record DraftRoundResponse(string Token, int Round, Attr Attribute, IReadOnlyList<LegendOption> Candidates);

public sealed record DraftRequest(string Token, int Pick);

public sealed record DraftCompleteResponse(string Token, int[] Attributes);

public sealed record PositionRequest(string Token, Pos Position, string Country);

public sealed record PositionLockedResponse(string Token, int Potential, string Role);

public sealed record AdvanceRequest(string Token, bool? Decision);

public sealed record AdvanceResponse(
    string Token, IReadOnlyList<SeasonResult> NewSeasons, PendingTransferOffer? PendingOffer, bool Finished);

public sealed record SaveRequest(string Token);

public sealed record SaveResponse(double Score, ScoreBreakdown Breakdown);

public sealed record AnnualChallengeResponse(int Period, ulong Seed);
