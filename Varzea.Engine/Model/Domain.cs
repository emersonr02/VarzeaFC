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
    TeamOfTheYear,          // melhor da sua posição no ano — dá paridade entre posições por construção
    SouthAmericanTeamOfTheYear, // análogo ao TeamOfTheYear, só pra ligas southAmerican (Roadmap §9 Bloco 1)
    KingOfAmerica           // "Rei da América" — análogo ao BallonDOr, gated pelo SouthAmericanTeamOfTheYear
}

public sealed record Legend(string Name, int[] Ratings)
{
    public int Get(Attr a) => Ratings[(int)a];
}

/// <summary>
/// Pedido do jogador ao clube/comissão técnica, feito no dashboard ANTES de avançar
/// pra próxima temporada — no máximo um por temporada (painel Contrato + Técnico,
/// roadmap pós-§9). Diferente de tudo que existia antes (transferência/renovação são
/// sempre o SERVIDOR que pausa e pergunta): aqui é o jogador que inicia.
/// </summary>
public enum SeasonRequestKind
{
    None,
    RequestRenewal,             // pede renovação antecipada, mesmo sem o contrato vencer
    RequestLeaveAtContractEnd,  // avisa que quer sair quando o contrato atual vencer
    RequestRaise,                // pede aumento — sem sistema de dinheiro, afeta só moral
    RequestCaptaincy,            // pede a braçadeira — concessão fica pra sempre
    RequestSetPieces,            // pede bolas paradas — concessão fica pra sempre, sobe gols/assist.
    RequestLeaveNow              // pede pra sair JÁ (corte de escopo do Bloco 2 fechado) — busca oferta garantida, custa moral
}

/// <summary>Qual valor de moral um "dilema fictício" mexeu nesta temporada (Roadmap §9
/// Bloco 2, corte de escopo fechado) — deixa o front escolher um texto variado em vez de
/// uma mensagem genérica única.</summary>
public enum DilemmaTarget { None, Team, Coach, Crowd }

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
    string RulesetVersion,
    /// <summary>Um pedido por temporada, indexado por ordem de chegada — ver
    /// CareerSimulator.RunCareer (seasonIndex). Null/vazio = nenhum pedido feito,
    /// comportamento idêntico ao motor antes deste recurso existir (opt-in, não muda a
    /// amostra do Monte Carlo).</summary>
    SeasonRequestKind[]? SeasonRequests = null,
    /// <summary>Índice da proposta aceita quando o contrato vence sem renovação (Roadmap
    /// §9 Bloco 3, corte de escopo fechado — "1+ propostas"), na ordem em que as decisões
    /// de PendingContractChoice apareceram. -1 ou fora dos limites = recusou todas, fica
    /// de contrato curto de "prova". Mesmo padrão de TransferChoices, mas com índice em
    /// vez de bool porque "1 de N" não cabe num bool[]. Null/vazio = comportamento padrão
    /// (não muda a amostra do Monte Carlo).</summary>
    int[]? ContractChoices = null
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

    // --- Moral (Roadmap §9 Bloco 2) ---
    // Três valores separados (equipe/técnico/torcida), -1.0..+1.0, valor ao FIM da
    // temporada. Evoluem automaticamente a partir dos resultados da própria temporada
    // (RNG derivado de Recipe.Seed, mesma garantia de determinismo do resto do motor —
    // ver CareerSimulator.RunCareer) e realimentam perf na temporada seguinte.
    public double TeamMorale { get; init; }
    public double CoachMorale { get; init; }
    public double CrowdMorale { get; init; }

    /// <summary>Recusou uma proposta de um clube maior nesta temporada — eleva muito a
    /// moral (decisão do produto, Roadmap §9 Bloco 2). O front usa isto pra narrar.</summary>
    public bool DeclinedBiggerClub { get; init; }

    /// <summary>Um evento aleatório de vestiário/torcida mexeu na moral fora do fluxo de
    /// transferência (Roadmap §9 Bloco 2).</summary>
    public bool MoraleDilemma { get; init; }

    /// <summary>Qual valor o dilema mexeu (None quando MoraleDilemma é false), se foi pra
    /// cima ou pra baixo, e uma variante (0-2) pro front escolher entre textos diferentes
    /// pro mesmo alvo/direção — fecha o corte de escopo "conteúdo narrativo variado" do
    /// Bloco 2.</summary>
    public DilemmaTarget DilemmaTarget { get; init; }
    public bool DilemmaPositive { get; init; }
    public int DilemmaVariant { get; init; }

    /// <summary>
    /// Versão inicial de "jogador pode pedir pra sair" (Roadmap §9 Bloco 2): dispara
    /// AUTOMATICAMENTE quando a moral média fica muito baixa por 2+ temporadas seguidas,
    /// em vez de uma ação manual do jogador — corte de escopo deliberado desta sessão
    /// (ver HANDOFF §9). O próprio pedido custa mais moral e aumenta a chance de oferta.
    /// </summary>
    public bool AskedToLeave { get; init; }

    // --- Contrato (Roadmap §9 Bloco 3) ---
    /// <summary>O contrato venceu nesta temporada — sempre dispara uma decisão de
    /// renovação (não é probabilístico como as ofertas de fora do ciclo).</summary>
    public bool ContractExpiring { get; init; }

    /// <summary>Quando ContractExpiring, indica se o clube renovou automaticamente
    /// (sem decisão do jogador) ou se veio uma proposta de fora (ver HadTransferOffer).</summary>
    public bool ContractRenewed { get; init; }

    /// <summary>Temporadas restantes no contrato atual, contadas a partir do fim desta
    /// temporada — pro dashboard mostrar "3 temporadas restantes" sem esperar a
    /// expiração. Painel Contrato + Técnico, roadmap pós-§9.</summary>
    public int ContractYearsRemaining { get; init; }

    // --- Pedidos do jogador (painel Contrato + Técnico) ---
    public bool IsCaptain { get; init; }
    public bool HasSetPieces { get; init; }

    /// <summary>O pedido feito ANTES desta temporada (via SeasonRequests), e se foi
    /// concedido. None = nenhum pedido feito nesta temporada.</summary>
    public SeasonRequestKind RequestMade { get; init; }
    public bool RequestGranted { get; init; }
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
    int Goals, int Assists, int Tackles, int CleanSheets, int LeaguePosition,
    /// <summary>Veio de um contrato vencido sem renovação (Roadmap §9 Bloco 3), não de
    /// uma oferta de fora do ciclo — muda a narrativa no front. Desde o corte de escopo
    /// "múltiplas propostas" fechado, a não-renovação sempre usa PendingContractChoice
    /// em vez disto — este campo fica sempre false na prática, mantido por
    /// compatibilidade com o resto do fluxo de oferta única (fora do ciclo).</summary>
    bool ContractExpiring = false);

/// <summary>Uma proposta concreta entre as N que aparecem quando o contrato vence sem
/// renovação (Roadmap §9 Bloco 3, corte de escopo fechado — "1+ propostas"). ClubTier é
/// absoluto (não relativo ao tier atual, ao contrário de PendingTransferOffer.Upgrade).</summary>
public sealed record ContractProposalOption(int ClubTier, bool Upgrade);

/// <summary>
/// 1-3 propostas simultâneas quando o contrato vence sem renovação (natural ou via
/// RequestLeaveAtContractEnd) — o jogador escolhe uma (por índice, CareerRecipe.
/// ContractChoices) ou recusa todas e fica de contrato curto de "prova". Ofertas de FORA
/// do ciclo de contrato continuam usando PendingTransferOffer (uma só), sem mudança.
/// </summary>
public sealed record PendingContractChoice(int Age, int Overall, IReadOnlyList<ContractProposalOption> Proposals);

/// <summary>
/// Resultado de rodar a carreira até a próxima decisão pendente, ou até o fim.
/// Existe porque SimulateCareer exige TransferChoices/ContractChoices completos de
/// antemão; AdvanceCareer permite alimentar essas decisões uma a uma (fluxo interativo de
/// /careers/advance). PendingOffer e PendingContractChoice nunca vêm preenchidos ao
/// mesmo tempo — são dois tipos de pausa mutuamente exclusivos.
/// </summary>
public sealed class CareerProgress
{
    public required CareerResult Result { get; init; }
    public PendingTransferOffer? PendingOffer { get; init; }
    public PendingContractChoice? PendingContractChoice { get; init; }
    public bool AwaitingDecision => PendingOffer is not null || PendingContractChoice is not null;
}
