namespace Varzea.Engine.Model;

public enum Attr { Pac = 0, Sho = 1, Pas = 2, Dri = 3, Def = 4, Phy = 5, Ski = 6, Wea = 7 }

public enum Pos { GK, CB, FB, DM, CM, AM, W, SS, ST }

public enum TitleKind
{
    LeagueTop5,       // liga de elite
    LeagueMid,        // liga intermediária
    LeagueMinor,      // liga menor
    DomesticCup,
    ContinentalSecondary, // Europa League / Sudamericana
    ContinentalPrimary,   // Champions / Libertadores
    WorldCup,
    BallonDOr,
    TeamOfTheYear   // melhor da sua posição no ano — dá paridade entre posições por construção
}

public sealed record Legend(string Name, int[] Ratings)
{
    public int Get(Attr a) => Ratings[(int)a];
}

/// <summary>
/// A "receita" da carreira. É ISTO que vai pro banco quando o usuário salva —
/// nunca o placar. O servidor re-simula a partir daqui e calcula o score sozinho,
/// o que dá anti-cheat e replay eterno em ~200 bytes.
/// </summary>
public sealed record CareerRecipe(
    ulong Seed,
    string Country,
    int[] DraftPicks,       // 8 índices (0..2), um por rodada
    Pos Position,
    bool[] TransferChoices, // aceita/recusa, na ordem em que as ofertas apareceram
    string RulesetVersion
);

public sealed class SeasonResult
{
    public int Age { get; init; }
    public int Overall { get; init; }
    public int ClubTier { get; init; }
    public int Apps { get; init; }
    public int Goals { get; init; }
    public int Assists { get; init; }
    public int Tackles { get; init; }
    public int CleanSheets { get; init; }
    public int LeaguePosition { get; init; }
    public InjurySeverity Injury { get; init; }
    public List<TitleKind> Titles { get; } = new();
    public int Caps { get; init; }
    public bool InTeamOfTheYear { get; init; }
    public bool HadTransferOffer { get; init; }
    public bool AcceptedTransfer { get; init; }
}

public enum InjurySeverity { None, Minor, Moderate, Severe, CareerEnding }

public sealed class CareerResult
{
    public CareerRecipe Recipe { get; init; } = null!;
    public Pos Position { get; init; }
    public string RoleName { get; init; } = "";
    public int Potential { get; init; }
    public int PeakOverall { get; init; }
    public int Seasons { get; init; }
    public int TotalGoals { get; init; }
    public int TotalAssists { get; init; }
    public int TotalTackles { get; init; }
    public int TotalCleanSheets { get; init; }
    public int TotalCaps { get; init; }
    public List<SeasonResult> Timeline { get; } = new();

    /// <summary>Contagem por tipo de título — entrada direta do scorer.</summary>
    public Dictionary<TitleKind, int> TitleCounts { get; } = new();

    public int CountOf(TitleKind k) => TitleCounts.TryGetValue(k, out var v) ? v : 0;

    public void AddTitle(TitleKind k)
        => TitleCounts[k] = CountOf(k) + 1;
}

/// <summary>
/// Uma oferta de transferência ainda sem decisão. A temporada em que ela ocorreu NÃO
/// entra no Timeline até ser decidida — Timeline só guarda temporadas fechadas.
/// Upgrade=true significa que aceitar sobe um tier; false, que aceitar desce um tier
/// (uma saída de uma fase ruim, não necessariamente rebaixamento).
/// </summary>
public sealed record PendingTransferOffer(
    int Age, int Overall, int ClubTier, bool Upgrade,
    int Goals, int Assists, int Tackles, int CleanSheets, int LeaguePosition);

/// <summary>
/// Resultado de rodar a carreira até a próxima decisão pendente, ou até o fim.
/// Existe porque SimulateCareer exige TransferChoices completo de antemão; AdvanceCareer
/// permite alimentar essas decisões uma a uma (fluxo interativo de /careers/advance).
/// </summary>
public sealed class CareerProgress
{
    public required CareerResult Result { get; init; }
    public PendingTransferOffer? PendingOffer { get; init; }
    public bool AwaitingDecision => PendingOffer is not null;
}
