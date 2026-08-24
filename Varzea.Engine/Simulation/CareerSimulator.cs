using Varzea.Engine.Model;
using Varzea.Engine.Rng;
using Varzea.Engine.Ruleset;

namespace Varzea.Engine.Simulation;

/// <summary>
/// Motor puro: sem I/O, sem relógio, sem estado global.
/// SimulateCareer(recipe) é uma função — mesma entrada, mesma saída, sempre.
/// </summary>
public sealed class CareerSimulator
{
    private readonly GameRuleset _rules;
    private readonly ClubDirectory _clubs;
    private readonly List<Legend> _legends;

    public CareerSimulator(GameRuleset rules, ClubDirectory clubs)
    {
        _rules = rules;
        _clubs = clubs;
        _legends = rules.BuildLegends();
    }

    // ---------- DRAFT ----------

    /// <summary>
    /// Reconstrói as 8 rodadas do draft. Cada rodada oferece 3 lendas para um atributo;
    /// a lenda escolhida sai do pool (é o "roubo").
    /// </summary>
    public int[] ResolveDraft(ulong seed, int[] picks)
    {
        if (picks.Length != 8) throw new ArgumentException("draft precisa de 8 escolhas");

        var (rng, pool) = ReplayDraft(seed, Array.Empty<int>());
        var attrs = new int[8];
        for (int round = 0; round < 8; round++)
        {
            var candidates = DrawCandidates(rng, pool);
            int choice = Math.Clamp(picks[round], 0, candidates.Count - 1);
            var chosen = candidates[choice];
            attrs[round] = chosen.Get((Attr)round);
            pool.Remove(chosen);
        }
        return attrs;
    }

    /// <summary>
    /// As 3 lendas da PRÓXIMA rodada (índice = picksSoFar.Length), dado o que já foi
    /// escolhido. Existe porque o pool depende das escolhas reais: a lenda tirada sai
    /// do pool, as outras duas voltam — então a API não pode revelar as 8 rodadas de
    /// uma vez, só uma rodada por chamada (ver HANDOFF §7.5).
    /// </summary>
    public IReadOnlyList<Legend> PreviewNextDraftRound(ulong seed, int[] picksSoFar)
    {
        if (picksSoFar.Length >= 8) throw new ArgumentException("draft já tem as 8 escolhas");
        var (rng, pool) = ReplayDraft(seed, picksSoFar);
        return DrawCandidates(rng, pool);
    }

    /// <summary>Rebobina o RNG e o pool até o ponto imediatamente após as escolhas dadas.</summary>
    private (Pcg32 Rng, List<Legend> Pool) ReplayDraft(ulong seed, int[] picksSoFar)
    {
        var rng = Pcg32.Derive(seed, "draft");
        var pool = new List<Legend>(_legends);
        foreach (var pick in picksSoFar)
        {
            var candidates = DrawCandidates(rng, pool);
            int choice = Math.Clamp(pick, 0, candidates.Count - 1);
            pool.Remove(candidates[choice]);
        }
        return (rng, pool);
    }

    /// <summary>Candidatos por rodada de draft. 2, não 3 (Roadmap §9 Bloco 1) — decisão
    /// de produto, escolha mais tensa e rápida.</summary>
    private const int DraftCandidatesPerRound = 2;

    /// <summary>Quanto a moral média (-1..+1) desloca perf (Roadmap §9 Bloco 2). Perf já
    /// varia por ruído ±8 (ver RunCareer) — 6.0 deixa a moral comparável a esse ruído,
    /// sem dominar os outros sistemas.</summary>
    private const double MoralPerfWeight = 6.0;

    /// <summary>Quanto a fadiga acumulada (0..~1.2) desconta perf (painel Saúde,
    /// roadmap pós-§9) — único sistema desta rodada que NÃO é opt-in (acumula um pouco
    /// toda temporada, mesmo sem o jogador tocar no painel), então precisa de
    /// recalibração via Monte Carlo após implementado (ver HANDOFF §9). Chute inicial na
    /// mesma ordem de grandeza de MoralPerfWeight.</summary>
    private const double FatiguePerfWeight = 8.0;

    /// <summary>Teto de fadiga acumulada — sem isto, uma carreira longa sem nenhum
    /// descanso acumularia fadiga sem limite (ver acúmulo de fim de temporada abaixo).</summary>
    private const double FatigueMax = 1.2;

    /// <summary>"Mais eventos como lesões que influenciam a carreira real" (roadmap
    /// pós-§9): lesão Severe não reseta no offseason como se nada tivesse acontecido —
    /// a temporada SEGUINTE carrega uma penalidade de perf enquanto o jogador recupera
    /// ritmo de jogo. Mesma ordem de grandeza de FatiguePerfWeight/MoralPerfWeight, mas
    /// menor — é um efeito de UMA temporada, não acumulado.</summary>
    private const double InjuryRecoveryPerfPenalty = 6.0;

    // As vagas de acesso/rebaixamento agora vêm do REGULAMENTO DE CADA PAÍS
    // (CountryDef.PromotionSpots/RelegationSpots, em balance.json) — as constantes
    // globais que ficavam aqui (2 sobem / 4 descem pra todo mundo) não batiam com liga
    // nenhuma de verdade.

    private static List<Legend> DrawCandidates(Pcg32 rng, List<Legend> pool)
    {
        var working = new List<Legend>(pool);
        var candidates = new List<Legend>();
        for (int i = 0; i < DraftCandidatesPerRound && working.Count > 0; i++)
        {
            int idx = rng.NextInt(0, working.Count - 1);
            candidates.Add(working[idx]);
            working.RemoveAt(idx);
        }
        return candidates;
    }

    /// <summary>
    /// Over ponderado por posição, mesma base do FIFA: soma dos atributos
    /// multiplicados por pesos que somam 1.0.
    /// </summary>
    public int OverallFor(int[] attrs, Pos pos)
    {
        var w = _rules.WeightsFor(pos);
        double sum = 0;
        for (int i = 0; i < 8; i++) sum += attrs[i] * w[i];
        return (int)Math.Round(sum);
    }

    /// <summary>Primeira role cujo atributo bate o mínimo vence (ordem do JSON = prioridade).</summary>
    public RoleDef ResolveRole(int[] attrs, Pos pos)
    {
        foreach (var role in _rules.RolesFor(pos))
            if (attrs[(int)role.AttrEnum] >= role.Min)
                return role;
        return new RoleDef { Name = "Indefinido", Min = 0 };
    }

    // ---------- CARREIRA ----------

    /// <summary>Roda a carreira inteira. Exige TransferChoices já completo — usado pelo
    /// Monte Carlo e por /careers/save (que já recebe a receita fechada).</summary>
    public CareerResult SimulateCareer(CareerRecipe recipe, bool includeMatches = false) =>
        RunCareer(recipe, interactive: false, revealLimit: null, includeMatches).Result;

    /// <summary>
    /// Roda a carreira até a próxima oferta/proposta pendente sem decisão, até
    /// <paramref name="revealLimit"/> temporadas reveladas (null = sem limite, roda até
    /// o fim ou a próxima pausa), ou até o fim. Não precisa serializar RNG entre
    /// chamadas: como a carreira inteira custa microssegundos, cada chamada re-simula do
    /// zero com a receita um pouco mais completa (uma decisão a mais) — determinístico
    /// por construção, e mantém a API sem sessão de servidor (ver Varzea.Api.CareerState).
    ///
    /// revealLimit existe porque, sem ele, uma carreira sem NENHUMA pausa (raro no início,
    /// quando ofertas/propostas ainda não dispararam) roda inteira numa chamada só — bug
    /// real encontrado: o jogador clicava "Avançar" uma vez e via 6+ temporadas de uma
    /// vez, sem chance de usar os painéis Contrato/Técnico/Empresário/Clube no meio
    /// ("nenhum botão faz nada" era, na prática, "só dava pra pedir uma vez a cada 6
    /// temporadas"). O padrão normal (uma chamada = uma temporada nova) usa revealLimit;
    /// "pular tudo" no front passa null pra manter o atalho de ir até o fim/próxima pausa
    /// numa única viagem de rede.
    /// </summary>
    public CareerProgress AdvanceCareer(CareerRecipe recipe, int? revealLimit = null, bool includeMatches = false) =>
        RunCareer(recipe, interactive: true, revealLimit, includeMatches);

    /// <summary>
    /// As 3 opções de clube real (Roadmap pós-§9, painel Clube) pro país/posição/draft
    /// dados — mesma sequência de RNG que RunCareer consome até este ponto (retireAge,
    /// peakAge, gap, tier), pra que a opção realmente escolhida bata com o que a
    /// carreira de verdade vai usar. Existe fora de RunCareer porque a UI precisa
    /// mostrar as opções ANTES da carreira começar a rodar de verdade (mesmo padrão de
    /// PreviewNextDraftRound pro draft).
    /// </summary>
    public IReadOnlyList<string> StartingClubOptions(ulong seed, string countryName, int[] draftPicks, Pos position)
    {
        var attrs = ResolveDraft(seed, draftPicks);
        int potential = OverallFor(attrs, position);
        var curve = _rules.Curve;
        var rng = Pcg32.Derive(seed, "career");
        rng.NextInt(curve.MinRetireAge, curve.MaxRetireAge);
        rng.NextInt(curve.MinPeakAge, curve.MaxPeakAge);
        rng.NextInt(curve.MinPotentialGap, curve.MaxPotentialGap);
        int tier = StartingTier(potential, rng);
        return SampleDistinctClubs(_clubs.PoolFor(countryName, tier), 3, rng);
    }

    private static List<string> SampleDistinctClubs(IReadOnlyList<string> pool, int count, Pcg32 rng)
    {
        var remaining = new List<string>(pool);
        var chosen = new List<string>();
        for (int i = 0; i < count && remaining.Count > 0; i++)
        {
            int idx = rng.NextInt(0, remaining.Count - 1);
            chosen.Add(remaining[idx]);
            remaining.RemoveAt(idx);
        }
        return chosen;
    }

    private CareerProgress RunCareer(CareerRecipe recipe, bool interactive, int? revealLimit, bool includeMatches)
    {
        var attrs = ResolveDraft(recipe.Seed, recipe.DraftPicks);
        var pos = recipe.Position;
        int potential = OverallFor(attrs, pos);
        var role = ResolveRole(attrs, pos);
        var factors = _rules.FactorsFor(pos);
        var country = _rules.Countries[recipe.Country];
        var curve = _rules.Curve;

        var rng = Pcg32.Derive(recipe.Seed, "career");

        int retireAge = rng.NextInt(curve.MinRetireAge, curve.MaxRetireAge);
        int peakAge = rng.NextInt(curve.MinPeakAge, curve.MaxPeakAge);
        int gap = rng.NextInt(curve.MinPotentialGap, curve.MaxPotentialGap);
        int overall = Math.Clamp(potential - gap, 40, Math.Max(41, potential - 4));

        int tier = StartingTier(potential, rng);
        // 3 clubes reais candidatos ao tier inicial (Roadmap pós-§9, painel Clube) —
        // mesma sequência de sorteio de StartingClubOptions, consumida aqui de novo pra
        // manter o rng em sincronia com o que a UI já mostrou. StartingClubChoice fora
        // dos limites (ou null, como o Monte Carlo sempre manda) cai na opção 0 — nunca
        // pausa, carreira roda até o fim do mesmo jeito de antes deste recurso existir.
        var startingClubOptions = SampleDistinctClubs(_clubs.PoolFor(recipe.Country, tier), 3, rng);
        int startingClubIdx = recipe.StartingClubChoice is { } sc && sc >= 0 && sc < startingClubOptions.Count ? sc : 0;
        string clubName = startingClubOptions.Count > 0 ? startingClubOptions[startingClubIdx] : "Clube Amador";
        // Guarda o clube "dono do contrato" durante um empréstimo (RequestLoan) — ver
        // uso junto de parentTier mais abaixo; precisa ser o MESMO clube de antes do
        // empréstimo, não um novo sorteio no mesmo tier.
        string parentClubName = "";
        // País do CLUBE (roadmap pós-§9, transferência internacional) — separado de
        // `country` (nacionalidade, usada só pra seleção/prêmios individuais Sul-
        // Americanos e NUNCA muda). Começa igual à nacionalidade; só diverge depois de
        // uma proposta internacional aceita (ver bloco de aceite de proposta abaixo).
        string clubCountry = recipe.Country;
        var clubCountryRules = country;
        // Mundo vivo: quem está em cada divisão muda ao longo da carreira.
        var universe = new LeagueUniverse(_clubs);
        // Índice de PendingContractChoice (Roadmap §9 Bloco 3, corte de escopo fechado —
        // "1+ propostas"; desde "propostas de mais clubes" no roadmap pós-§9, é o ÚNICO
        // índice de proposta que existe, dentro ou fora do ciclo de contrato) — indexando
        // recipe.ContractChoices (int, não bool, já que "1 de N" não cabe num bool[]).
        int contractChoiceIdx = 0;

        // --- CONTRATO (Roadmap §9 Bloco 3) ---
        // Duração decidida pela FASE da carreira (crescendo/pico/declinando), não pelo
        // potencial bruto — um veterano de potencial alto mas overall caindo (o exemplo
        // do produto: "tipo o Modric... o over só cai") recebe contrato curto do mesmo
        // jeito. contractYear conta quantas temporadas já se passaram no contrato atual;
        // quando bate contractDuration, vira decisão de renovação (ver bloco abaixo).
        int contractYear = 0;
        int contractDuration = NextContractDuration(curve.StartAge, peakAge, retireAge, rng);

        var result = new CareerResult
        {
            Recipe = recipe,
            Position = pos,
            RoleName = role.Name,
            Potential = potential
        };

        int peak = overall, goals = 0, assists = 0, tackles = 0, cs = 0, caps = 0, seasons = 0;

        // --- MORAL (Roadmap §9 Bloco 2) ---
        // Três valores separados (equipe/técnico/torcida), -1.0..+1.0, começam neutros.
        // Puramente função de recipe+RNG (mesmo domínio "career" já derivado acima) —
        // sem estado novo pra serializar entre chamadas de AdvanceCareer.
        double teamMorale = 0, coachMorale = 0, crowdMorale = 0;
        int lowMoraleStreak = 0;

        // --- PEDIDOS DO JOGADOR (painel Contrato + Técnico) ---
        // Mesma garantia de determinismo: seasonIndex conta temporadas simuladas nesta
        // re-execução (0-based), e requests[seasonIndex] é lido de forma pura — sem
        // estado serializado entre chamadas de AdvanceCareer.
        var requests = recipe.SeasonRequests ?? Array.Empty<SeasonRequestKind>();
        var contractChoices = recipe.ContractChoices ?? Array.Empty<int>();
        int seasonIndex = 0;
        bool isCaptain = false, hasSetPieces = false, wantsToLeaveAtContractEnd = false;
        // Empréstimo (painel Empresário, RequestLoan): -1 = não está emprestado. Quando
        // >= 0, guarda o tier do clube DONO do contrato pra restaurar no início da
        // temporada seguinte — o empréstimo dura sempre exatamente uma temporada.
        int parentTier = -1;

        // --- FADIGA (painel Saúde, roadmap pós-§9) ---
        // Único sistema desta rodada SEMPRE ATIVO (não opt-in) — acumula um pouco a
        // cada temporada jogada, mesmo sem nenhum SeasonRequest. Começa em 0 (jogador
        // de 16 anos, descansado); nunca sai de [0, FatigueMax] (ver Clamp abaixo).
        double fatigue = 0;
        bool hasPersonalTrainer = false;

        // Recuperação de lesão grave (roadmap pós-§9, "eventos que influenciam a
        // carreira real"): true logo depois de uma temporada com InjurySeverity.Severe,
        // consumido (lido e resetado) no TOPO da temporada seguinte — mesmo padrão de
        // parentTier pro empréstimo, um efeito de exatamente uma temporada.
        bool recoveringFromSevereInjury = false;

        // Acesso/rebaixamento (roadmap pós-§9): -1 = nenhuma mudança automática de tier
        // pendente. Setado no fim da temporada (depois da tabela sair), consumido no
        // TOPO da seguinte — mesmo padrão de parentTier, mas com prioridade MENOR: um
        // empréstimo restaurando o clube dono do contrato sempre vence (ver
        // "if (parentTier >= 0) ... else if (pendingAutoTier >= 0)" abaixo).
        int pendingAutoTier = -1;

        for (int age = curve.StartAge; age <= retireAge; age++)
        {
            // Uma chamada = uma temporada nova (ver AdvanceCareer) — pausa ANTES de
            // simular a próxima, mesmo padrão de "temporada não entra no Timeline até
            // fechar" das outras pausas, mas sem consumir nenhum request/decisão desta
            // temporada (ela nem começou). ReachedRevealLimit distingue isto de "carreira
            // realmente acabou" pro Finished da API.
            if (interactive && revealLimit is { } limit && result.Timeline.Count >= limit)
            {
                return new CareerProgress
                {
                    Result = Finalize(result, peak, seasons, goals, assists, tackles, cs, caps),
                    ReachedRevealLimit = true
                };
            }
            if (parentTier >= 0)
            {
                tier = parentTier;
                clubName = parentClubName;
                parentTier = -1;
                pendingAutoTier = -1; // empréstimo termina, acesso/rebaixamento do clube emprestado não conta
            }
            else if (pendingAutoTier >= 0)
            {
                // Acesso/rebaixamento muda a DIVISÃO, nunca a identidade do clube — bug
                // real encontrado: sorteava um clube novo aleatório na hora de subir/
                // descer, como se o jogador tivesse sido transferido sem escolher nada
                // ("mudei de clube do nada depois de ser promovido"). O clube que sobe é
                // o MESMO clube, agora competindo numa divisão diferente; só o tier
                // (usado pra bucket de força/pool) muda.
                tier = pendingAutoTier;
                pendingAutoTier = -1;
            }

            // curva de evolução: cresce em direção ao potencial, estabiliza, decai
            if (age < peakAge)
                overall += Math.Max(1, (int)Math.Round((potential - overall) * curve.GrowthRate))
                           + (rng.Chance(0.30) ? 1 : 0);
            else if (age <= peakAge + 3)
                overall += rng.NextInt(-1, 2);
            else
                overall -= rng.NextInt(1, 4);

            // Potencial é um TETO real, nunca um alvo aproximado — bug real encontrado:
            // Math.Max(1, ...) acima força crescimento mínimo de +1 mesmo já em cima do
            // potencial, e a fase de platô (+1/0/-1 aleatório) não tinha teto nenhum,
            // deixando overall passar do potencial sorteado (relatado: "potencial era
            // 89, cheguei aos 96"). Math.Min aqui garante que isso nunca mais aconteça,
            // em qualquer fase da curva.
            overall = Math.Min(overall, potential);
            overall = Math.Clamp(overall, 35, 99);
            peak = Math.Max(peak, overall);

            int target = _rules.Tiers.First(t => t.Tier == tier).Target;
            double perf = overall - target + (rng.NextDouble() * 16 - 8);

            // Moral realimenta perf (Roadmap §9 Bloco 2) — usa o valor ao FIM da
            // temporada anterior; a moral desta temporada só é conhecida depois dos
            // resultados abaixo, então não pode se autoalimentar na mesma iteração.
            double moraleAtStart = (teamMorale + coachMorale + crowdMorale) / 3.0;
            perf += moraleAtStart * MoralPerfWeight;

            // Fadiga desconta perf (painel Saúde) — mesmo padrão de moraleAtStart: usa o
            // valor acumulado ATÉ O FIM da temporada anterior, nunca o desta (calculado
            // só depois, no acúmulo de fim de temporada abaixo).
            double fatigueAtStart = fatigue;
            perf -= fatigueAtStart * FatiguePerfWeight;

            // Recuperação de lesão grave: lido e resetado AQUI (efeito de uma única
            // temporada) — a marcação pra próxima carreira é feita mais abaixo, depois
            // que a lesão DESTA temporada é conhecida.
            bool recoveringThisSeason = recoveringFromSevereInjury;
            recoveringFromSevereInjury = false;
            if (recoveringThisSeason) perf -= InjuryRecoveryPerfPenalty;

            // --- PEDIDOS DO JOGADOR (painel Contrato + Técnico, roadmap pós-§9) ---
            // Resolvido AQUI (antes de apps/Output) pra que bolas paradas já valha nesta
            // mesma temporada, não só a partir da próxima. Um pedido por temporada.
            var request = seasonIndex < requests.Length ? requests[seasonIndex] : SeasonRequestKind.None;
            bool requestGranted = false;
            // Só vale NESTA temporada — declarado aqui (não com os loop-locals antes do
            // for) porque, ao contrário de wantsToLeaveAtContractEnd, não precisa
            // sobreviver até uma condição futura.
            bool forceOfferSearch = false;
            switch (request)
            {
                case SeasonRequestKind.RequestRenewal:
                {
                    // Mesma fórmula do gatilho de expiração mais abaixo. Como isto roda
                    // ANTES do bloco "--- CONTRATO / TRANSFERÊNCIA ---", se a renovação
                    // for concedida agora, contractExpiring (calculado lá a partir do
                    // contractYear já zerado) sai false sozinho — sem precisar de guard.
                    double renewChanceNow = Math.Clamp(0.55 + perf / 120.0 + moraleAtStart * 0.20
                        - (age >= retireAge - 1 ? 0.30 : 0), 0.05, 0.90);
                    if (rng.Chance(renewChanceNow))
                    {
                        requestGranted = true;
                        contractYear = 0;
                        contractDuration = NextContractDuration(age, peakAge, retireAge, rng);
                    }
                    break;
                }
                case SeasonRequestKind.RequestLeaveAtContractEnd:
                    // É uma declaração de intenção, não uma decisão do clube — "aceita"
                    // por construção. Consumida no bloco de contrato quando o prazo vencer.
                    wantsToLeaveAtContractEnd = true;
                    requestGranted = true;
                    break;
                case SeasonRequestKind.RequestRaise:
                {
                    // Sem sistema de dinheiro (fora de escopo — painel Empresário não
                    // construído): concedido/negado só mexe em moral, não em número
                    // nenhum de salário real.
                    double raiseChance = Math.Clamp((overall - target) / 40.0 + moraleAtStart * 0.25 + 0.15, 0.05, 0.80);
                    requestGranted = rng.Chance(raiseChance);
                    if (requestGranted)
                    {
                        coachMorale = Math.Clamp(coachMorale + 0.10, -1.0, 1.0);
                        teamMorale = Math.Clamp(teamMorale + 0.05, -1.0, 1.0);
                    }
                    else
                    {
                        coachMorale = Math.Clamp(coachMorale - 0.08, -1.0, 1.0);
                    }
                    break;
                }
                case SeasonRequestKind.RequestCaptaincy:
                    if (!isCaptain)
                    {
                        double capChance = Math.Clamp((overall - 70) / 60.0 + moraleAtStart * 0.20
                            + (age >= 24 ? 0.15 : 0), 0.05, 0.85);
                        requestGranted = rng.Chance(capChance);
                        if (requestGranted) isCaptain = true;
                    }
                    break;
                case SeasonRequestKind.RequestSetPieces:
                    if (!hasSetPieces)
                    {
                        double spChance = Math.Clamp((overall - 72) / 55.0 + moraleAtStart * 0.15 + 0.10, 0.05, 0.75);
                        requestGranted = rng.Chance(spChance);
                        if (requestGranted) hasSetPieces = true;
                    }
                    break;
                case SeasonRequestKind.RequestLeaveNow:
                    // Corte de escopo do Bloco 2 fechado: em vez do gatilho automático por
                    // moral baixa sustentada (moralPressure, já existente), o jogador pode
                    // pedir pra sair JÁ. Busca garantida (não só chance maior) — resolvida
                    // no bloco "--- CONTRATO / TRANSFERÊNCIA ---" mais abaixo. A ação em si
                    // prejudica a relação, concedida ou não a busca (texto original do
                    // Bloco 2: "a ação em si prejudica a relação").
                    forceOfferSearch = true;
                    requestGranted = true;
                    teamMorale = Math.Clamp(teamMorale - 0.08, -1.0, 1.0);
                    coachMorale = Math.Clamp(coachMorale - 0.10, -1.0, 1.0);
                    break;
                case SeasonRequestKind.RequestLoan:
                    // Painel Empresário: declaração de intenção, "aceita" por construção
                    // (mesmo padrão de RequestLeaveAtContractEnd) — só não pode empilhar
                    // com um empréstimo já em curso. Um tier abaixo por UMA temporada
                    // (restaurado no topo da iteração seguinte, ver parentTier acima).
                    // target/perf desta temporada já foram calculados com o tier ANTIGO
                    // (linhas acima) — a mudança só aparece no ClubTier da temporada
                    // (season.ClubTier = tier é lido depois do switch) e no que vier a
                    // seguir (ofertas/contrato usam o tier atual).
                    if (parentTier < 0 && tier > 1)
                    {
                        requestGranted = true;
                        parentTier = tier;
                        parentClubName = clubName;
                        tier = Math.Max(1, tier - 1);
                        clubName = _clubs.PickClub(clubCountry, tier, rng);
                    }
                    break;
                case SeasonRequestKind.RequestPromiseTitle:
                    // Declaração pública pra torcida/imprensa — "aceita" por construção
                    // (mesmo padrão de RequestLeaveAtContractEnd); o que importa de
                    // verdade (cumpriu ou quebrou) é resolvido lá embaixo, no bloco
                    // "--- LIGA ---", quando season.LeaguePosition já é conhecido.
                    requestGranted = true;
                    break;
                case SeasonRequestKind.RequestPersonalTrainer:
                    // Painel Saúde: concessão fica pra sempre (mesmo padrão de
                    // isCaptain/hasSetPieces) — reduz a taxa de acúmulo de fadiga daí em
                    // diante (ver acúmulo de fim de temporada abaixo).
                    if (!hasPersonalTrainer)
                    {
                        hasPersonalTrainer = true;
                        requestGranted = true;
                    }
                    break;
                // RequestRest e RequestPlayInjured não são resolvidos aqui: o primeiro
                // precisa de `apps` (só calculado mais abaixo), o segundo precisa saber
                // se a lesão sorteada (RollInjury, logo abaixo) realmente aconteceu —
                // ambos são lidos diretamente de `request` nos pontos certos.
            }
            seasonIndex++;

            var injury = RollInjury(rng, age);

            // Jogar lesionado (painel Saúde): decidido ANTES da lesão ser sorteada (like
            // todo o sistema de pedidos — um por temporada, escolhido no dashboard antes
            // de "Avançar"), então é uma aposta: "se eu me machucar esta temporada, jogo
            // mesmo assim". Só tem efeito se realmente houve lesão; senão o pedido não
            // muda nada (fica RequestGranted=false, narrado como "não precisou" no front).
            bool playedThroughInjury = false;
            if (request == SeasonRequestKind.RequestPlayInjured && injury != InjurySeverity.None)
            {
                playedThroughInjury = true;
                requestGranted = true;
                if (rng.Chance(0.08)) injury = EscalateInjury(injury);
            }

            // Marca a recuperação pra temporada SEGUINTE (não CareerEnding — aí não há
            // "temporada seguinte" nesta carreira).
            recoveringFromSevereInjury = injury == InjurySeverity.Severe;

            if (injury == InjurySeverity.CareerEnding)
            {
                // Mesmo aqui (fim abrupto de carreira, antes do resto da temporada ser
                // resolvido), os estados já decididos ATÉ este ponto da iteração são
                // reais e valem a pena carregar — sem isto, concessões permanentes como
                // braçadeira/bolas paradas "somem" retroativamente do Timeline se a
                // última temporada da carreira for justamente a que encerra tudo.
                // Campos calculados só MAIS ABAIXO (dilemas, contrato, transferência)
                // ficam no valor padrão — a temporada nunca chegou lá.
                result.Timeline.Add(new SeasonResult
                {
                    Age = age, Overall = overall, ClubTier = tier, ClubName = clubName, ClubCountry = clubCountry,
                    MarketValue = MarketValueOf(overall, age, tier),
                    Injury = injury, LeaguePosition = 20,
                    TeamMorale = teamMorale, CoachMorale = coachMorale, CrowdMorale = crowdMorale,
                    IsCaptain = isCaptain, HasSetPieces = hasSetPieces, OnLoan = parentTier >= 0,
                    Fatigue = fatigue, HasPersonalTrainer = hasPersonalTrainer,
                    RequestMade = request, RequestGranted = requestGranted,
                    RecoveringFromInjury = recoveringThisSeason
                });
                break;
            }

            bool restedThisSeason = request == SeasonRequestKind.RequestRest;

            int apps = playedThroughInjury
                ? Math.Clamp(rng.NextInt(24, 36), 4, 38)
                : Math.Clamp(rng.NextInt(24, 36) - InjuryCost(injury, rng)
                    - (recoveringThisSeason ? rng.NextInt(3, 8) : 0), 4, 38);
            if (restedThisSeason)
            {
                apps = Math.Max(1, apps / 2);
                requestGranted = true;
            }

            // Acúmulo de fadiga de fim de temporada (painel Saúde) — sempre ativo, não
            // depende de nenhum SeasonRequest ter sido feito. Descansar reduz em vez de
            // acumular; jogar lesionado soma um custo extra por cima do acúmulo normal
            // (apps não foi cortado pela lesão, então o acúmulo normal já é o de uma
            // temporada cheia). Clamp em [0, FatigueMax] pra nunca crescer sem limite.
            if (restedThisSeason)
                fatigue = Math.Clamp(fatigue - 0.15, 0, FatigueMax);
            else
                fatigue = Math.Clamp(fatigue + (apps / 34.0) * (hasPersonalTrainer ? 0.025 : 0.04), 0, FatigueMax);
            if (playedThroughInjury)
                fatigue = Math.Clamp(fatigue + 0.12, 0, FatigueMax);

            // Bolas paradas (painel Técnico, roadmap pós-§9): bônus fica pra sempre uma
            // vez concedido, soma direto no roleMod que Output() já usa.
            int sg = Output(rng, overall, factors.Attack, 30, perf, apps, role.GoalsMod + (hasSetPieces ? 0.12 : 0));
            int sa = Output(rng, overall, factors.Passing, 22, perf, apps, role.AssistsMod + (hasSetPieces ? 0.10 : 0));
            int st = Output(rng, overall, factors.Defending, 95, perf, apps, role.DefenseMod);
            int scs = (int)Math.Round(apps * Math.Clamp(0.10 + factors.Defending * (0.22 + perf / 160.0), 0, 0.60));

            // Tabela de classificação real (Roadmap pós-§9, painel Clube) — os outros
            // clubes da MESMA divisão do jogador jogam pontos corridos entre si; a
            // força do próprio jogador nessa tabela é a força de base do clube +
            // perf (bom o bastante pra puxar o time, ruim o bastante pra afundar).
            // Divisão VIVA (ver LeagueUniverse): reflete quem subiu e desceu nas
            // temporadas anteriores, não a lista fixa de clubs.json.
            universe.EnsureMember(clubCountry, tier >= 3, clubName);
            var rivals = universe.Division(clubCountry, tier >= 3).Where(c => c != clubName).ToList();
            double ownStrength = _clubs.BaseStrength(clubCountry, clubName) + perf;
            var (leaguePosition, leagueTable, fixtures, roundStandings) =
                SimulateLeagueTable(clubName, ownStrength, rivals, clubCountry, rng, captureRoundStandings: includeMatches);

            // Modo "jogo a jogo": detalha as partidas a partir dos resultados que a
            // tabela já decidiu. RNG derivado — não consome nada do fluxo da carreira.
            var matches = includeMatches
                ? BuildMatches(recipe.Seed, age, fixtures, apps, sg, sa, perf)
                : (IReadOnlyList<MatchResult>)Array.Empty<MatchResult>();
            double seasonRating = matches.Count > 0 && matches.Any(m => m.Played)
                ? Math.Round(matches.Where(m => m.Played).Average(m => m.Rating), 2)
                : 0;

            // --- ACESSO / REBAIXAMENTO (roadmap pós-§9) ---
            // Grandes (tier 5) ficam de fora — lista curada de clubes tradicionalmente
            // fortes, só muda pra lá via proposta de transferência (já existente), nunca
            // por tabela. tier 1-2 (divisão 2) top 2 sobe pro chão da divisão 1 (tier 3);
            // tier 3-4 (divisão 1, sem contar os grandes) fundo 4 desce pro topo da
            // divisão 2 (tier 2) — aplica no TOPO da temporada SEGUINTE (mesmo padrão de
            // parentTier), nunca no meio desta. leagueTable.Count é o total de clubes na
            // tabela (rivais + o próprio).
            // Vagas de acesso/rebaixamento são do REGULAMENTO DE CADA PAÍS (ver
            // CountryDef.PromotionSpots/RelegationSpots): Brasil troca 4, a maioria da
            // Europa troca 3. Antes era 2 sobem / 4 descem pra todo mundo, o que não
            // batia com liga nenhuma.
            int promotionSpots = clubCountryRules.PromotionSpots;
            int relegationSpots = clubCountryRules.RelegationSpots;
            bool promoted = false, relegated = false;
            if (tier is 1 or 2 && leaguePosition <= promotionSpots)
            {
                pendingAutoTier = 3;
                promoted = true;
            }
            else if (tier is 3 or 4 && leaguePosition > leagueTable.Count - relegationSpots)
            {
                pendingAutoTier = 2;
                relegated = true;
            }

            // O MUNDO se move junto: os outros clubes também sobem e descem. Sem isto as
            // divisões eram listas fixas e "o time que subiu comigo" simplesmente não
            // aparecia na divisão nova no ano seguinte (bug real relatado).
            ApplyPromotionRelegation(universe, clubCountry, tier >= 3, leagueTable,
                promotionSpots, relegationSpots, recipe.Seed, age);

            var season = new SeasonResult
            {
                Age = age, Overall = overall, ClubTier = tier, ClubName = clubName, ClubCountry = clubCountry, Apps = apps,
                Promoted = promoted, Relegated = relegated,
                PromotionSpots = promotionSpots, RelegationSpots = relegationSpots,
                Matches = matches, SeasonRating = seasonRating,
                RoundStandings = roundStandings,
                MarketValue = MarketValueOf(overall, age, tier),
                Goals = sg, Assists = sa, Tackles = st, CleanSheets = scs,
                Injury = injury,
                LeaguePosition = leaguePosition,
                LeagueTable = leagueTable,
                RecoveringFromInjury = recoveringThisSeason
            };

            // --- LIGA ---
            if (season.LeaguePosition == 1)
            {
                var kind = clubCountryRules.LeagueGrade switch
                {
                    3 => TitleKind.LeagueTop5,
                    2 => TitleKind.LeagueMid,
                    _ => TitleKind.LeagueMinor
                };
                season.Titles.Add(kind);
                result.AddTitle(kind);
            }

            // Promessa de título (painel Clube, roadmap pós-§9): resolvida assim que
            // season.LeaguePosition é conhecido — cumprida (campeão da liga NACIONAL,
            // não copas/continental) dá um bônus de moral grande (empilha com o que a
            // torcida já sente por ser campeã); quebrada custa moral, promessa vazia.
            bool promisedTitle = request == SeasonRequestKind.RequestPromiseTitle;
            bool promiseFulfilled = false;
            if (promisedTitle)
            {
                promiseFulfilled = season.LeaguePosition == 1;
                if (promiseFulfilled)
                {
                    teamMorale = Math.Clamp(teamMorale + 0.15, -1.0, 1.0);
                    crowdMorale = Math.Clamp(crowdMorale + 0.20, -1.0, 1.0);
                }
                else
                {
                    teamMorale = Math.Clamp(teamMorale - 0.10, -1.0, 1.0);
                    crowdMorale = Math.Clamp(crowdMorale - 0.15, -1.0, 1.0);
                }
            }

            // --- COPA NACIONAL ---
            if (rng.Chance(Math.Clamp(0.10 + perf / 260.0, 0.03, 0.30)) && rng.Chance(0.40))
            {
                season.Titles.Add(TitleKind.DomesticCup);
                result.AddTitle(TitleKind.DomesticCup);
            }

            // --- CONTINENTAL ---
            double contChance = _rules.Tiers.First(t => t.Tier == tier).ContinentalChance;
            if (contChance > 0 && rng.Chance(contChance))
            {
                bool primary = tier >= 4;
                if (rng.Chance(Math.Clamp(0.35 + perf / 200.0, 0.10, 0.65)))
                {
                    var k = primary ? TitleKind.ContinentalPrimary : TitleKind.ContinentalSecondary;
                    season.Titles.Add(k);
                    result.AddTitle(k);
                }
            }

            // --- SELEÇÃO / COPA DO MUNDO (ciclo de 4 anos) ---
            // Bug real relatado: "joguei copa do mundo na série B" — convocação só
            // olhava overall, nunca o nível do clube. Seleção nacional exige estar na
            // 1ª divisão (tier>=3): ninguém é convocado jogando na 2ª, por mais bem
            // avaliado que esteja no papel — o olheiro da seleção não vê quem não
            // aparece na vitrine certa. Dentro da 1ª divisão, ainda pesa a mão: tier 3
            // (fundo da divisão) bem mais raro que tier 5 (grandes).
            int seasonCaps = 0;
            double tierCapFactor = tier switch { 5 => 1.0, 4 => 0.7, 3 => 0.35, _ => 0.0 };
            if (overall >= 76 && age >= 18 && age <= 35 && tier >= 3 && rng.Chance(Math.Clamp(0.35 * tierCapFactor, 0, 0.35)))
                seasonCaps += rng.NextInt(1, 8);

            bool wcYear = (age - curve.StartAge) % 4 == 2;
            if (wcYear && overall >= 74 && age >= 18 && tier >= 3)
            {
                double callUp = Math.Clamp((overall - 70) / 60.0 + country.Strength / 40.0, 0.05, 0.60) * tierCapFactor;
                if (rng.Chance(callUp))
                {
                    seasonCaps += rng.NextInt(3, 7);
                    double winChance = Math.Clamp(
                        0.05 + (overall - 78) / 260.0 + (country.Strength - 5) / 90.0, 0.005, 0.18);
                    if (rng.Chance(winChance))
                    {
                        season.Titles.Add(TitleKind.WorldCup);
                        result.AddTitle(TitleKind.WorldCup);
                    }
                }
            }

            // --- PRÊMIOS INDIVIDUAIS ---
            // Equipe do Ano: melhor de cada posição. A chance NÃO depende de gols nem
            // de desarmes, só de quão acima do nível mundial o jogador está — por isso
            // goleiro e zagueiro competem em pé de igualdade com atacante.
            //
            // Gated por força da liga (Roadmap §9 Bloco 1): fora do top-5 europeu a
            // chance é multiplicada por um fator << 1. Como a Bola de Ouro só concorre
            // quem entrou na Equipe do Ano, o efeito composto já deixa a Bola de Ouro
            // "quase nunca" pra ligas de grade 1 sem precisar de outro gate separado.
            double leagueGradeAwardFactor = clubCountryRules.LeagueGrade switch
            {
                3 => 1.00,
                2 => 0.35,
                _ => 0.10
            };

            bool toty = false;
            double totyChance = Math.Clamp(((overall - 84) / 38.0
                + (season.LeaguePosition == 1 ? 0.06 : 0)
                + (season.Titles.Contains(TitleKind.ContinentalPrimary) ? 0.05 : 0)
                + (season.Titles.Contains(TitleKind.WorldCup) ? 0.04 : 0)) * leagueGradeAwardFactor, 0, 0.40);
            if (rng.Chance(totyChance))
            {
                toty = true;
                season.Titles.Add(TitleKind.TeamOfTheYear);
                result.AddTitle(TitleKind.TeamOfTheYear);
            }

            // Bola de Ouro: só concorre quem entrou na Equipe do Ano.
            // Sem esse gate o prêmio volta a ser refém de quem faz gol.
            if (toty)
            {
                double wc = Math.Clamp(((overall - 88) / 50.0
                    + (season.LeaguePosition == 1 ? 0.10 : 0)
                    + (season.Titles.Contains(TitleKind.ContinentalPrimary) ? 0.14 : 0)
                    + (season.Titles.Contains(TitleKind.WorldCup) ? 0.16 : 0)
                    + role.TitleMod) * leagueGradeAwardFactor, 0.00, 0.42);
                if (rng.Chance(wc))
                {
                    season.Titles.Add(TitleKind.BallonDOr);
                    result.AddTitle(TitleKind.BallonDOr);
                }
            }

            // --- EQUIPE DO ANO DA AMÉRICA / REI DA AMÉRICA ---
            // Análogo aos prêmios globais acima, mas escopado a ligas southAmerican
            // (Roadmap §9 Bloco 1 — reabre a decisão travada da secção 2 sobre só dois
            // prêmios individuais). O objetivo NÃO é competir com os prêmios globais —
            // um jogador de liga fora do top-5 europeu dificilmente ganha a Equipe do
            // Ano global, mas ainda deve ter uma vitrine regional alcançável.
            if (country.SouthAmerican)
            {
                bool saToty = false;
                double saTotyChance = Math.Clamp((overall - 83) / 38.0
                    + (season.LeaguePosition == 1 ? 0.04 : 0)
                    + (season.Titles.Contains(TitleKind.ContinentalPrimary) ? 0.04 : 0), 0, 0.33);
                if (rng.Chance(saTotyChance))
                {
                    saToty = true;
                    season.Titles.Add(TitleKind.SouthAmericanTeamOfTheYear);
                    result.AddTitle(TitleKind.SouthAmericanTeamOfTheYear);
                }

                if (saToty)
                {
                    double koa = Math.Clamp((overall - 89) / 45.0
                        + (season.LeaguePosition == 1 ? 0.06 : 0)
                        + (season.Titles.Contains(TitleKind.ContinentalPrimary) ? 0.10 : 0)
                        + role.TitleMod, 0.00, 0.26);
                    if (rng.Chance(koa))
                    {
                        season.Titles.Add(TitleKind.KingOfAmerica);
                        result.AddTitle(TitleKind.KingOfAmerica);
                    }
                }
            }

            // --- CONTRATO / TRANSFERÊNCIA (Roadmap §9 Bloco 3, substitui o gatilho só-perf
            // do Bloco 2) ---
            // moralPressure: versão funcional de "pedir pra sair" (Bloco 2) — moral muito
            // baixa aumenta a chance de oferta mesmo sem queda de perf.
            bool moralPressure = moraleAtStart < -0.4;
            bool contractExpiring = contractYear >= contractDuration;
            bool offer = false, upgrade = false, contractRenewed = false;
            // Não-renovação (Roadmap §9 Bloco 3, corte de escopo fechado — "1+
            // propostas"): em vez de uma oferta só, o jogador escolhe entre estas.
            // PendingTransferOffer (offer/upgrade acima) continua só pra fora do ciclo.
            List<ContractProposalOption>? contractProposals = null;

            // "Nova regra": antes dos 18, o jogador está preso ao clube de base — nenhuma
            // proposta chega, dentro ou fora do ciclo de contrato (bug/pedido real:
            // "não posso sair do meu clube até os 18 anos, recebi proposta antes da
            // primeira temporada"). Contrato que vence antes disso renova sozinho, sem
            // rolagem nenhuma — é cedo demais pra qualquer clube arriscar perder um
            // garoto de base pra concorrência.
            if (age < 18)
            {
                if (contractExpiring)
                {
                    contractRenewed = true;
                    contractYear = 0;
                    contractDuration = NextContractDuration(age, peakAge, retireAge, rng);
                }
            }
            else if (contractExpiring)
            {
                if (wantsToLeaveAtContractEnd)
                {
                    // Jogador já avisou (painel Contrato, roadmap pós-§9) que quer sair
                    // quando o contrato vencesse — pula a rolagem de renovação e vai
                    // direto pro caminho de "não renovou".
                    contractProposals = GenerateContractProposals(clubCountry, tier, overall, target, rng, currentClub: clubName);
                    wantsToLeaveAtContractEnd = false;
                }
                else
                {
                    // Decisão de renovação: sempre acontece quando o contrato vence — não é
                    // probabilística como as ofertas de fora do ciclo. Moral (Bloco 2) e forma
                    // recente pesam a favor; idade perto da aposentadoria pesa contra.
                    double renewChance = Math.Clamp(0.55 + perf / 120.0 + moraleAtStart * 0.20
                        - (age >= retireAge - 1 ? 0.30 : 0), 0.05, 0.90);
                    if (rng.Chance(renewChance))
                    {
                        contractRenewed = true;
                    }
                    else
                    {
                        contractProposals = GenerateContractProposals(clubCountry, tier, overall, target, rng, currentClub: clubName);
                    }
                }
            }
            else
            {
                // Fora do ciclo de contrato: gatilho por forma (perf) ou pressão de moral
                // (Bloco 2), MAIS uma chance de "olheiro" proporcional ao nível do jogador
                // (Roadmap §9 Bloco 3: "propostas chegam frequentemente fora da expiração,
                // proporcional ao nível do jogador") — antes só o gatilho de perf existia.
                // forceOfferSearch (RequestLeaveNow, corte do Bloco 2): busca garantida,
                // não só chance maior — tem prioridade sobre os gatilhos probabilísticos.
                // Se o contrato expira NESSA MESMA temporada, o bloco "if (contractExpiring)"
                // acima já assume — o pedido fica absorvido pelo fluxo de contrato (raro,
                // aceitável: os dois significam "quero sair" de qualquer forma).
                //
                // Roadmap pós-§9, "propostas de mais clubes": fora do ciclo agora também
                // gera 1-3 propostas concretas (mesmo mecanismo de GenerateContractProposals
                // já usado na não-renovação), em vez de uma oferta binária única — mas cada
                // gatilho ainda IMPÕE sua direção (forceDirection), senão recalcular do zero
                // dentro do gerador podia contradizer o gatilho (ex: perf>14 não garantia
                // mais upgrade de verdade) e derrubar a progressão de tier da amostra
                // inteira — foi exatamente o que quebrou ContinentalPrimary (caiu a 0% no
                // Monte Carlo) na primeira tentativa desta feature.
                double scoutingChance = Math.Clamp((overall - 70) / 200.0, 0, 0.15);
                bool triggered = false;
                bool direction = false;
                if (forceOfferSearch) { triggered = true; direction = overall >= target; }
                else if (perf > 14 && tier < 5 && rng.Chance(0.45)) { triggered = true; direction = true; }
                else if ((perf < -16 || moralPressure) && tier > 1 && rng.Chance(moralPressure ? 0.45 : 0.30)) { triggered = true; direction = false; }
                else if (tier < 5 && rng.Chance(scoutingChance)) { triggered = true; direction = true; }
                if (triggered) contractProposals = GenerateContractProposals(clubCountry, tier, overall, target, rng, direction, currentClub: clubName);
            }

            bool accepted = false;
            if (contractProposals is not null)
            {
                if (interactive && contractChoiceIdx >= contractChoices.Length)
                {
                    // Sem decisão ainda — pausa aqui, mesma regra de "temporada não entra
                    // no Timeline até fechar" do PendingOffer.
                    return new CareerProgress
                    {
                        Result = Finalize(result, peak, seasons, goals, assists, tackles, cs, caps),
                        PendingContractChoice = new PendingContractChoice(age, overall, contractProposals, contractExpiring)
                    };
                }
                int choice = contractChoiceIdx < contractChoices.Length ? contractChoices[contractChoiceIdx] : -1;
                contractChoiceIdx++;
                offer = true; // pro SeasonResult.HadTransferOffer, igual ao fluxo de fora do ciclo
                if (choice >= 0 && choice < contractProposals.Count)
                {
                    accepted = true;
                    string previousClubName = clubName;
                    upgrade = contractProposals[choice].Upgrade;
                    tier = Math.Clamp(contractProposals[choice].ClubTier, 1, 5);
                    // O clube é o MESMO já sorteado quando a proposta foi gerada (ver
                    // GenerateContractProposals) — nunca um re-sorteio aqui, senão o
                    // clube mostrado ao jogador na hora de escolher poderia divergir do
                    // clube realmente aplicado (bug real encontrado e corrigido).
                    clubName = contractProposals[choice].ClubName;
                    // Transferência internacional (roadmap pós-§9, "não vi uma partida
                    // com transferências para outra liga") — troca o país do CLUBE (não
                    // a nacionalidade, essa nunca muda) quando a proposta aceita vem de
                    // fora; clubCountryRules segue junto pra LeagueGrade bater com a
                    // liga nova a partir da temporada seguinte.
                    clubCountry = contractProposals[choice].Country;
                    clubCountryRules = _rules.Countries[clubCountry];
                    // Transferência aceita muda o clube JÁ (efetivo na temporada seguinte,
                    // igual a qualquer outra mudança de clube) — cancela um acesso/
                    // rebaixamento automático que essa mesma temporada tenha disparado,
                    // senão o topo do próximo loop sobrescreveria o clube recém-aceito.
                    // Só vale quando o clube REALMENTE muda: se o jogador "aceitou" ficar
                    // onde já estava, ele desce/sobe junto com o clube, senão dava pra
                    // driblar um rebaixamento sem sair do lugar (bug real relatado:
                    // "meu time foi rebaixado e no ano seguinte disputou a mesma liga").
                    if (contractProposals[choice].ClubName != previousClubName)
                        pendingAutoTier = -1;
                }
                // choice inválido/-1: accepted fica false, tier não muda aqui — o bloco de
                // reinício de contrato mais abaixo já trata "recusou todas" como o
                // contrato curto de "prova" (mesmo comportamento de antes).
            }
            // Não existe mais um caminho de oferta única fora do contractProposals — desde
            // "propostas de mais clubes" (roadmap pós-§9), TODA proposta (dentro ou fora do
            // ciclo de contrato) passa pelo bloco acima. PendingTransferOffer/TransferChoices
            // ficam só como histórico de API (nunca mais retornados por RunCareer); ver
            // HANDOFF pra um follow-up de remover essa infraestrutura agora morta.

            // Reinicia a contagem do contrato sempre que um contrato NOVO começa: renovou,
            // aceitou uma proposta (qualquer origem), ou recusou a única proposta na não-
            // renovação (fica de "prova", contrato curto). Do contrário, mais um ano se
            // passou no contrato atual.
            if (contractRenewed || (offer && accepted))
            {
                contractYear = 0;
                contractDuration = NextContractDuration(age, peakAge, retireAge, rng);
            }
            else if (contractExpiring && offer && !accepted)
            {
                contractYear = 0;
                contractDuration = Math.Max(1, Math.Min(2, retireAge - age));
            }
            else
            {
                contractYear++;
            }

            // --- MORAL: evolução ao fim da temporada (Roadmap §9 Bloco 2) ---
            // Recusar proposta de clube maior eleva muito a moral — decisão do produto.
            // Só conta pra esse bônus fora do ciclo de contrato: recusar a proposta da
            // não-renovação é uma aposta em si mesmo, não lealdade a um contrato vigente.
            bool declinedBiggerClub = offer && upgrade && !accepted && !contractExpiring;
            if (declinedBiggerClub) { teamMorale += 0.20; coachMorale += 0.15; crowdMorale += 0.30; }

            if (season.LeaguePosition == 1) { teamMorale += 0.15; coachMorale += 0.15; crowdMorale += 0.20; }
            else if (season.LeaguePosition >= 15) { teamMorale -= 0.08; coachMorale -= 0.10; crowdMorale -= 0.05; }

            if (season.Titles.Contains(TitleKind.ContinentalPrimary)) { crowdMorale += 0.25; coachMorale += 0.10; }
            // Holofote individual: torcida ama, mas desperta uma ponta de ciúme no grupo.
            if (toty || season.Titles.Contains(TitleKind.BallonDOr)) { crowdMorale += 0.15; teamMorale -= 0.03; }

            if (injury is InjurySeverity.Moderate or InjurySeverity.Severe) { coachMorale -= 0.05; crowdMorale -= 0.03; }

            // Braçadeira (painel Técnico, roadmap pós-§9): pequeno ganho de moral toda
            // temporada como capitão — orgulho/liderança, sem revogação.
            if (isCaptain) { teamMorale = Math.Clamp(teamMorale + 0.03, -1.0, 1.0); }

            // "Dilemas fictícios" (Roadmap §9 Bloco 2, corte de escopo fechado): expõe QUAL
            // valor mexeu, se foi pra cima ou pra baixo, e uma variante — o front escolhe
            // entre textos diferentes (data/dilemmas.ts) em vez de uma mensagem genérica.
            bool moraleDilemma = rng.Chance(0.12);
            var dilemmaTarget = DilemmaTarget.None;
            bool dilemmaPositive = false;
            int dilemmaVariant = 0;
            if (moraleDilemma)
            {
                double swing = rng.NextDouble() * 0.30 - 0.15;
                dilemmaPositive = swing >= 0;
                dilemmaVariant = rng.NextInt(0, 2);
                switch (rng.NextInt(0, 2))
                {
                    case 0: teamMorale += swing; dilemmaTarget = DilemmaTarget.Team; break;
                    case 1: coachMorale += swing; dilemmaTarget = DilemmaTarget.Coach; break;
                    default: crowdMorale += swing; dilemmaTarget = DilemmaTarget.Crowd; break;
                }
            }

            teamMorale = Math.Clamp(teamMorale, -1.0, 1.0);
            coachMorale = Math.Clamp(coachMorale, -1.0, 1.0);
            crowdMorale = Math.Clamp(crowdMorale, -1.0, 1.0);

            // "Jogador pode pedir pra sair" — versão inicial automática (Roadmap §9
            // Bloco 2): dispara sozinho quando a moral média fica muito baixa por 2+
            // temporadas seguidas, em vez de uma ação manual do jogador (corte de escopo
            // deliberado desta sessão — ver HANDOFF §9). O próprio pedido custa moral;
            // o efeito funcional sobre a chance de oferta já rola na PRÓXIMA temporada
            // via moralPressure acima (usa moraleAtStart, que carrega este resultado).
            double moraleAfter = (teamMorale + coachMorale + crowdMorale) / 3.0;
            bool askedToLeave = false;
            if (moraleAfter < -0.5)
            {
                lowMoraleStreak++;
                if (lowMoraleStreak >= 2)
                {
                    askedToLeave = true;
                    teamMorale = Math.Clamp(teamMorale - 0.10, -1.0, 1.0);
                    coachMorale = Math.Clamp(coachMorale - 0.10, -1.0, 1.0);
                }
            }
            else
            {
                lowMoraleStreak = 0;
            }

            goals += sg; assists += sa; tackles += st; cs += scs; seasons++; caps += seasonCaps;

            var finished = new SeasonResult
            {
                Age = season.Age, Overall = season.Overall, ClubTier = season.ClubTier, ClubName = season.ClubName,
                ClubCountry = season.ClubCountry,
                Matches = season.Matches, SeasonRating = season.SeasonRating, MarketValue = season.MarketValue,
                RoundStandings = season.RoundStandings,
                Apps = season.Apps, Goals = season.Goals, Assists = season.Assists,
                Tackles = season.Tackles, CleanSheets = season.CleanSheets,
                LeaguePosition = season.LeaguePosition, LeagueTable = season.LeagueTable, Injury = season.Injury,
                Caps = seasonCaps, InTeamOfTheYear = toty,
                HadTransferOffer = offer, AcceptedTransfer = accepted,
                TeamMorale = teamMorale, CoachMorale = coachMorale, CrowdMorale = crowdMorale,
                DeclinedBiggerClub = declinedBiggerClub, MoraleDilemma = moraleDilemma, AskedToLeave = askedToLeave,
                DilemmaTarget = dilemmaTarget, DilemmaPositive = dilemmaPositive, DilemmaVariant = dilemmaVariant,
                ContractExpiring = contractExpiring, ContractRenewed = contractRenewed,
                ContractYearsRemaining = Math.Max(0, contractDuration - contractYear),
                IsCaptain = isCaptain, HasSetPieces = hasSetPieces, OnLoan = parentTier >= 0,
                Fatigue = fatigue, HasPersonalTrainer = hasPersonalTrainer,
                PromisedTitle = promisedTitle, PromiseFulfilled = promiseFulfilled,
                RequestMade = request, RequestGranted = requestGranted,
                RecoveringFromInjury = season.RecoveringFromInjury,
                Promoted = season.Promoted, Relegated = season.Relegated,
                PromotionSpots = season.PromotionSpots, RelegationSpots = season.RelegationSpots
            };
            finished.Titles.AddRange(season.Titles);
            result.Timeline.Add(finished);
        }

        return new CareerProgress { Result = Finalize(result, peak, seasons, goals, assists, tackles, cs, caps) };
    }

    private static CareerResult Finalize(CareerResult r, int peak, int seasons,
        int g, int a, int t, int cs, int caps)
    {
        var final = new CareerResult
        {
            Recipe = r.Recipe, Position = r.Position, RoleName = r.RoleName,
            Potential = r.Potential, PeakOverall = peak, Seasons = seasons,
            TotalGoals = g, TotalAssists = a, TotalTackles = t,
            TotalCleanSheets = cs, TotalCaps = caps
        };
        final.Timeline.AddRange(r.Timeline);
        foreach (var kv in r.TitleCounts) final.TitleCounts[kv.Key] = kv.Value;
        return final;
    }

    private int StartingTier(int potential, Pcg32 rng)
    {
        if (potential >= 90) return rng.Chance(0.5) ? 2 : 3;
        if (potential >= 80) return 2;
        if (potential >= 65) return rng.Chance(0.7) ? 1 : 2;
        return 1;
    }

    /// <summary>
    /// Duração do próximo contrato, em temporadas (Roadmap §9 Bloco 3). Decidida pela
    /// FASE da carreira — crescendo (idade &lt; pico), no auge, ou declinando (idade &gt;
    /// pico+3) — não pelo potencial bruto: um jogador de potencial altíssimo mas overall
    /// já caindo recebe contrato curto do mesmo jeito (ex. do produto: "tipo o Modric...
    /// o over só cai"). Nunca ultrapassa a idade de aposentadoria já sorteada.
    /// </summary>
    private static int NextContractDuration(int age, int peakAge, int retireAge, Pcg32 rng)
    {
        int baseDuration = age < peakAge ? rng.NextInt(4, 5)
            : age <= peakAge + 3 ? rng.NextInt(3, 4)
            : rng.NextInt(1, 2);
        return Math.Max(1, Math.Min(baseDuration, retireAge - age));
    }

    /// <summary>
    /// 1-3 propostas quando o contrato vence sem renovação (Roadmap §9 Bloco 3, corte de
    /// escopo fechado — "1+ propostas de acordo com overall, potencial, atuações
    /// recentes e idade"). Nunca pula mais de um tier de cada vez em relação ao atual,
    /// pra ficar compatível com o resto do motor (tier 1-5 como única dimensão de
    /// qualidade de clube — não há clubes nomeados no nível do motor).
    /// </summary>
    /// <param name="forceDirection">Quando informado, força a direção da proposta
    /// principal (true=upgrade, false=downgrade) em vez de recalcular por overall vs.
    /// padrão do tier — usado pelos gatilhos fora do ciclo (Roadmap pós-§9, "propostas
    /// de mais clubes"), que já embutem sua própria lógica de direção (ex: perf alto
    /// SEMPRE garante busca por um clube maior, igual ao antigo caminho de oferta única;
    /// sem isso, recalcular do zero aqui podia contradizer o gatilho e derrubar a
    /// progressão de tier da amostra inteira — foi exatamente o que quebrou
    /// ContinentalPrimary na primeira tentativa desta feature, ver HANDOFF). Null (a
    /// não-renovação de contrato, que não tem essa noção de "gatilho") mantém o cálculo
    /// original.</param>
    /// <summary>Chance de a proposta PRIMÁRIA (nunca a lateral/esticada) vir de outro
    /// país — só cogitada quando já se está num clube grande (tier 5) buscando outro
    /// tier 5 (upgrade lateral de prestígio: domesticamente não dá mais pra "subir").
    /// Roadmap pós-§9: "até agora não vi uma partida com transferências para outra
    /// liga" — o motor nunca cruzava fronteira nenhuma antes disso.</summary>
    private const double InternationalProposalChance = 0.22;
    private const int InternationalOverallThreshold = 80;

    private List<ContractProposalOption> GenerateContractProposals(
        string country, int tier, int overall, int target, Pcg32 rng, bool? forceDirection = null,
        string? currentClub = null)
    {
        var proposals = new List<ContractProposalOption>();

        bool qualifiesUpgrade = forceDirection ?? (overall >= target);

        // TETO DE MERCADO PELO OVERALL — quem decide o tamanho do clube interessado é o
        // nível do jogador, não a divisão onde ele está hoje. Antes a proposta era
        // sempre tier±1, então um camisa 9 de overall 86 preso na 2ª divisão só recebia
        // sondagem do vizinho de tabela (bug real relatado: "tava com over 86 na Série B
        // e só veio Mirassol e Coritiba"). Agora ele salta direto pro nível que merece.
        int ceilingByOverall = overall >= 84 ? 5 : overall >= 78 ? 4 : overall >= 70 ? 3 : 2;
        int primaryTier = qualifiesUpgrade
            ? Math.Clamp(Math.Max(tier + 1, ceilingByOverall), 1, 5)
            : Math.Clamp(tier - 1, 1, 5);

        // Só a proposta PRIMÁRIA pode ser internacional — lateral/esticada continuam
        // domésticas, mantendo a maior parte da mecânica já testada intocada. Não exige
        // qualifiesUpgrade (bug encontrado nesta sessão: o target do tier 5 é 92 — como
        // "subir" além do tier 5 não existe, qualifiesUpgrade ficava sempre falso lá em
        // cima e a transferência internacional nunca disparava de verdade). Ir pra fora
        // é sempre um "upgrade" de prestígio por construção.
        // Também não exige mais estar NO tier 5: quem manda é o overall — clube europeu
        // garimpa jogador bom na 2ª divisão de outro país, é o caminho mais comum de
        // carreira que existe.
        string primaryCountry = country;
        bool international = false;
        if (overall >= InternationalOverallThreshold && primaryTier >= 4 && rng.Chance(InternationalProposalChance))
        {
            var foreign = _clubs.PickForeignCountry(country, rng);
            if (foreign is not null) { primaryCountry = foreign; primaryTier = Math.Max(primaryTier, 4); international = true; }
        }

        // Clube sorteado AQUI, na geração — o mesmo nome mostrado na proposta é o que
        // vira o clube real se aceita (ver aceite mais abaixo). Sem isto, o front tinha
        // que inventar um nome cosmético pra mostrar (bug real encontrado: "aceitei
        // proposta do Porto Imperial e fui parar no RB Leipzig" — nomes inventados sem
        // nenhuma relação com o clube de verdade sorteado só na hora de aceitar).
        proposals.Add(new ContractProposalOption(primaryTier, international || qualifiesUpgrade,
            _clubs.PickClub(primaryCountry, primaryTier, rng, currentClub), primaryCountry));

        // Segunda proposta: lateral (mesmo tier atual, "fresh start" noutro clube) —
        // chance cresce com o overall, representando mais clubes de olho num jogador bom.
        double lateralChance = Math.Clamp((overall - 65) / 45.0, 0.15, 0.85);
        if (rng.Chance(lateralChance) && primaryTier != tier)
            proposals.Add(new ContractProposalOption(tier, false, _clubs.PickClub(country, tier, rng, currentClub), country));

        // CONCORRÊNCIA PELO JOGADOR — quanto melhor ele é, mais clubes entram na
        // disputa. Antes o teto era 3 propostas e, na prática, quase sempre 1 ou 2:
        // craque nenhum tem uma proposta só. Agora vai até 5 pra quem é elite.
        int extraSuitors = overall >= 86 ? 3 : overall >= 80 ? 2 : overall >= 74 ? 1 : 0;
        for (int i = 0; i < extraSuitors; i++)
        {
            // Pretendentes variam entre o nível da proposta principal e um degrau
            // abaixo — clubes diferentes disputando o mesmo jogador.
            int suitorTier = Math.Clamp(primaryTier - (i % 2), 1, 5);
            string suitorCountry = country;
            // Parte do assédio de um jogador de elite vem de fora.
            if (overall >= InternationalOverallThreshold && rng.Chance(0.35))
            {
                var foreign = _clubs.PickForeignCountry(country, rng);
                if (foreign is not null) suitorCountry = foreign;
            }
            string suitorClub = _clubs.PickClub(suitorCountry, suitorTier, rng, currentClub);
            // Nunca dois cartões do mesmo clube na mesma janela.
            if (proposals.Any(p => p.ClubName == suitorClub)) continue;
            proposals.Add(new ContractProposalOption(suitorTier, suitorTier > tier, suitorClub, suitorCountry));
        }

        return proposals;
    }

    /// <summary>Escala arbitrária de força de time — só precisa manter ordem e dar
    /// diferenças plausíveis num modelo tipo Elo; 30 pontos de diferença já deixa o mais
    /// forte favorito claro sem tornar o resultado garantido (ver PWinDenominator).</summary>
    private const double EloScale = 30.0;
    private const double BaseDrawChance = 0.24;

    /// <summary>
    /// Tabela de classificação real de pontos corridos (Roadmap pós-§9, painel Clube) —
    /// os clubes RIVAIS da mesma divisão do jogador jogam entre si E contra o jogador,
    /// turno E returno (cada par joga duas vezes, ida e volta — mesma estrutura de uma
    /// liga de verdade com N clubes, 2×(N-1) jogos), pontos por vitória/empate/derrota
    /// (3/1/0). Turno único (17 jogos, 51 pontos no máximo) dava campeão com totais
    /// irreconhecíveis pra quem conhece futebol de verdade (bug real relatado: "campeão
    /// com 34 pontos?????") — dobrar os jogos aproxima a escala de pontos da realidade
    /// (34 jogos pra 18 clubes, como a Bundesliga de verdade) sem mudar a lógica de
    /// quem é mais forte. A força do jogador (<paramref name="ownStrength"/>) já vem
    /// calculada pelo chamador a partir do clube + perf da temporada; os rivais recebem
    /// a força de base do próprio clube (ver ClubDirectory.BaseStrength) mais um ruído
    /// fixo pra a tabela não ficar idêntica toda temporada.
    /// </summary>
    /// <summary>Uma partida do jogador extraída da própria simulação de pontos corridos
    /// (modo "jogo a jogo"). Outcome: 1=vitória, 0=empate, -1=derrota, do ponto de vista
    /// do clube do jogador.</summary>
    private readonly record struct PlayerFixture(string Opponent, bool Home, int Outcome);

    /// <summary>
    /// Quem está em cada divisão AGORA, por país — muda ao longo da carreira conforme
    /// clubes sobem e descem. Antes as divisões eram as listas fixas de clubs.json e
    /// nenhum clube jamais trocava de divisão: o time que subiu junto com o jogador
    /// sumia do mapa no ano seguinte (bug real relatado). Vive por carreira, criado sob
    /// demanda pro país onde o jogador está (transferência internacional cria o do país
    /// novo na hora).
    /// </summary>
    private sealed class LeagueUniverse
    {
        private readonly ClubDirectory _clubs;
        private readonly Dictionary<string, (List<string> Div1, List<string> Div2)> _byCountry = new();

        public LeagueUniverse(ClubDirectory clubs) => _clubs = clubs;

        private (List<string> Div1, List<string> Div2) For(string country)
        {
            if (!_byCountry.TryGetValue(country, out var u))
            {
                var cc = _clubs.Countries.TryGetValue(country, out var c) ? c : _clubs.Countries.Values.First();
                u = (new List<string>(cc.Divisao1), new List<string>(cc.Divisao2));
                _byCountry[country] = u;
            }
            return u;
        }

        public List<string> Division(string country, bool firstDivision)
        {
            var (d1, d2) = For(country);
            return firstDivision ? d1 : d2;
        }

        /// <summary>Garante que o clube do jogador esteja na divisão em que ele joga —
        /// pode não estar depois de uma transferência que cruza divisões. Troca de lugar
        /// com um clube da outra divisão pra manter os DOIS tamanhos estáveis (nada
        /// "some" do mundo).</summary>
        public void EnsureMember(string country, bool firstDivision, string club)
        {
            var (d1, d2) = For(country);
            var here = firstDivision ? d1 : d2;
            var there = firstDivision ? d2 : d1;
            if (here.Contains(club)) return;

            there.Remove(club);
            if (here.Count > 0)
            {
                // O último daqui cede o lugar e vai pra outra divisão.
                var displaced = here[^1];
                here[^1] = club;
                if (!there.Contains(displaced)) there.Add(displaced);
            }
            else here.Add(club);
        }

        public void Swap(string country, IReadOnlyList<string> goingUp, IReadOnlyList<string> goingDown)
        {
            var (d1, d2) = For(country);
            foreach (var c in goingUp) { if (d2.Remove(c) && !d1.Contains(c)) d1.Add(c); }
            foreach (var c in goingDown) { if (d1.Remove(c) && !d2.Contains(c)) d2.Add(c); }
        }
    }

    /// <summary>
    /// Move clubes entre as divisões no fim da temporada. A divisão do JOGADOR usa a
    /// tabela real que acabou de ser simulada; a outra divisão (que o jogador não
    /// disputou) é ordenada por força + ruído, com RNG DERIVADO — não consome sorteio
    /// nenhum do fluxo da carreira, então não mexe na calibração.
    /// </summary>
    private void ApplyPromotionRelegation(
        LeagueUniverse universe, string country, bool playerInFirstDivision,
        IReadOnlyList<LeagueTableRow> playerTable, int promotionSpots, int relegationSpots,
        ulong seed, int age)
    {
        var otherRng = Pcg32.Derive(seed, $"otherdiv:{country}:{age}");
        var other = universe.Division(country, !playerInFirstDivision)
            .OrderByDescending(c => _clubs.BaseStrength(country, c) + otherRng.NextDouble() * 24 - 12)
            .ToList();

        List<string> up, down;
        if (playerInFirstDivision)
        {
            down = playerTable.TakeLast(Math.Min(relegationSpots, playerTable.Count)).Select(r => r.ClubName).ToList();
            up = other.Take(Math.Min(promotionSpots, other.Count)).ToList();
        }
        else
        {
            up = playerTable.Take(Math.Min(promotionSpots, playerTable.Count)).Select(r => r.ClubName).ToList();
            down = other.TakeLast(Math.Min(relegationSpots, other.Count)).ToList();
        }
        universe.Swap(country, up, down);
    }

    private (int Position, List<LeagueTableRow> Table, List<PlayerFixture> Fixtures,
             List<IReadOnlyList<LeagueTableRow>> RoundStandings) SimulateLeagueTable(
        string ownClub, double ownStrength, IReadOnlyList<string> rivals, string countryName, Pcg32 rng,
        bool captureRoundStandings)
    {
        var strength = new Dictionary<string, double> { [ownClub] = ownStrength };
        foreach (var rival in rivals)
            strength[rival] = _clubs.BaseStrength(countryName, rival) + (rng.NextDouble() * 20 - 10);

        var points = strength.Keys.ToDictionary(k => k, _ => 0);
        var names = strength.Keys.ToList();
        // Partidas do PRÓPRIO jogador, capturadas da mesma simulação que decide os
        // pontos — nunca um segundo sorteio paralelo, senão placar e classificação
        // poderiam se contradizer ("ganhei 3 jogos mas a tabela diz 0 pontos").
        var fixtures = new List<PlayerFixture>();
        // Classificação ao FIM de cada rodada (tabela "simultânea"): sem isso não dá
        // pra mostrar a tabela evoluindo junto com o jogo a jogo, porque o laço antigo
        // era "todos os pares do turno, depois todos do returno" — não existia rodada.
        var roundStandings = new List<IReadOnlyList<LeagueTableRow>>();

        foreach (var round in BuildSchedule(names))
        {
            foreach (var (home, away) in round)
            {
                double diff = strength[home] - strength[away];
                double pHome = 1.0 / (1.0 + Math.Pow(10, -diff / EloScale));
                double drawChance = Math.Clamp(BaseDrawChance - Math.Abs(diff) * 0.002, 0.10, 0.30);
                double roll = rng.NextDouble();
                int outcomeHome;
                if (roll < drawChance) { points[home] += 1; points[away] += 1; outcomeHome = 0; }
                else if (roll < drawChance + (1 - drawChance) * pHome) { points[home] += 3; outcomeHome = 1; }
                else { points[away] += 3; outcomeHome = -1; }

                if (home == ownClub) fixtures.Add(new PlayerFixture(away, true, outcomeHome));
                else if (away == ownClub) fixtures.Add(new PlayerFixture(home, false, -outcomeHome));
            }
            if (captureRoundStandings) roundStandings.Add(RankTable(names, points, strength, ownClub));
        }

        var table = RankTable(names, points, strength, ownClub);
        int position = table.ToList().FindIndex(r => r.IsPlayerClub) + 1;
        return (position, table.ToList(), fixtures, roundStandings);
    }

    /// <summary>Ordena a classificação pelos critérios de sempre (pontos, depois força,
    /// depois nome) — extraído porque agora é usado tanto no fim quanto a cada rodada
    /// (tabela "simultânea").</summary>
    private static IReadOnlyList<LeagueTableRow> RankTable(
        List<string> names, Dictionary<string, int> points, Dictionary<string, double> strength, string ownClub) =>
        names.OrderByDescending(n => points[n])
             .ThenByDescending(n => strength[n])
             .ThenBy(n => n, StringComparer.Ordinal)
             .Select(n => new LeagueTableRow(n, points[n], n == ownClub))
             .ToList();

    /// <summary>
    /// Calendário de pontos corridos de verdade (método do círculo): N-1 rodadas por
    /// turno, e em CADA rodada todo clube joga exatamente uma vez — é isso que permite
    /// a tabela evoluir junto com as partidas ("tabela simultânea"). Antes o laço era
    /// "todos os pares", sem noção de rodada: dava o mesmo total de jogos, mas não dava
    /// pra dizer em que rodada cada um aconteceu, e o clube do jogador acabava com o
    /// turno inteiro em casa. Mando alterna por rodada e inverte no returno.
    /// </summary>
    private static List<List<(string Home, string Away)>> BuildSchedule(List<string> clubs)
    {
        const string Bye = " BYE";
        var list = new List<string>(clubs);
        if (list.Count % 2 != 0) list.Add(Bye); // divisão ímpar: um folga por rodada
        int n = list.Count, roundsPerLeg = n - 1;
        var schedule = new List<List<(string, string)>>();

        for (int leg = 0; leg < 2; leg++)
        {
            var rot = new List<string>(list);
            for (int r = 0; r < roundsPerLeg; r++)
            {
                var round = new List<(string, string)>();
                for (int k = 0; k < n / 2; k++)
                {
                    string a = rot[k], b = rot[n - 1 - k];
                    if (a == Bye || b == Bye) continue;
                    bool aHome = (r % 2 == 0) ^ (leg == 1);
                    round.Add(aHome ? (a, b) : (b, a));
                }
                schedule.Add(round);
                // Rotaciona todos menos o primeiro — o círculo do método.
                var tail = rot[n - 1];
                rot.RemoveAt(n - 1);
                rot.Insert(1, tail);
            }
        }
        return schedule;
    }

    /// <summary>
    /// Detalha as partidas do jogador (modo "jogo a jogo") a partir dos resultados que a
    /// tabela JÁ decidiu: inventa um placar plausível pra cada vitória/empate/derrota e
    /// espalha os gols/assistências que a temporada já fechou pelas partidas em que ele
    /// entrou em campo. Usa um RNG DERIVADO (domínio "matches") em vez do rng da
    /// carreira — assim detalhar partidas não consome nenhum sorteio do fluxo principal
    /// e não muda nada do que já estava calibrado (Monte Carlo continua idêntico).
    /// </summary>
    private static List<MatchResult> BuildMatches(
        ulong seed, int age, IReadOnlyList<PlayerFixture> fixtures,
        int apps, int goals, int assists, double perf)
    {
        var rng = Pcg32.Derive(seed, $"matches:{age}");
        var matches = new List<MatchResult>(fixtures.Count);
        if (fixtures.Count == 0) return matches;

        // A ordem das partidas já é a do calendário real (BuildSchedule, método do
        // círculo), com mando alternando por rodada — não precisa mais reordenar aqui.

        // Quais rodadas o jogador disputou: `apps` da temporada pode ser maior que o
        // número de rodadas da liga (conta copas/seleção) ou menor (lesão/rodízio).
        int played = Math.Clamp(apps, 0, fixtures.Count);
        var playedRounds = new HashSet<int>();
        var pool = Enumerable.Range(0, fixtures.Count).ToList();
        for (int i = 0; i < played && pool.Count > 0; i++)
        {
            int idx = rng.NextInt(0, pool.Count - 1);
            playedRounds.Add(pool[idx]);
            pool.RemoveAt(idx);
        }

        // Distribui gols/assistências só entre as partidas jogadas.
        var playedList = playedRounds.OrderBy(r => r).ToList();
        var goalsBy = new Dictionary<int, int>();
        var assistsBy = new Dictionary<int, int>();
        if (playedList.Count > 0)
        {
            for (int g = 0; g < goals; g++)
            {
                int r = playedList[rng.NextInt(0, playedList.Count - 1)];
                goalsBy[r] = goalsBy.GetValueOrDefault(r) + 1;
            }
            for (int a = 0; a < assists; a++)
            {
                int r = playedList[rng.NextInt(0, playedList.Count - 1)];
                assistsBy[r] = assistsBy.GetValueOrDefault(r) + 1;
            }
        }

        for (int r = 0; r < fixtures.Count; r++)
        {
            var f = fixtures[r];
            // Placar plausível a partir do resultado já decidido pela tabela.
            int winner = 1 + rng.NextInt(0, 2);              // 1-3 gols pra quem venceu
            int loser = winner - 1 - rng.NextInt(0, Math.Max(0, winner - 1)); // sempre menos
            int drawGoals = rng.NextInt(0, 2);
            int gf = f.Outcome switch { 1 => winner, -1 => Math.Max(0, loser), _ => drawGoals };
            int ga = f.Outcome switch { 1 => Math.Max(0, loser), -1 => winner, _ => drawGoals };

            bool didPlay = playedRounds.Contains(r);
            int pg = didPlay ? Math.Min(goalsBy.GetValueOrDefault(r), gf) : 0;
            int pa = didPlay ? assistsBy.GetValueOrDefault(r) : 0;

            double rating = 0;
            if (didPlay)
            {
                // Base pela forma da temporada, ajustada pelo que ele fez E pelo
                // resultado do time — nota alta em derrota é possível, mas mais rara.
                rating = 6.0 + perf / 40.0 + pg * 0.9 + pa * 0.5
                       + f.Outcome * 0.35 + (rng.NextDouble() * 0.8 - 0.4);
                rating = Math.Clamp(Math.Round(rating, 1), 3.0, 10.0);
            }

            matches.Add(new MatchResult(r + 1, f.Opponent, f.Home, gf, ga, pg, pa, didPlay, rating));
        }

        return matches;
    }

    /// <summary>Valor de mercado de vitrine, em milhões de euros — deriva de overall,
    /// idade e nível do clube. Não entra em nenhum cálculo do motor nem no placar: é só
    /// um número pra dashboard, no espírito dos sites de mercado.</summary>
    private static double MarketValueOf(int overall, int age, int tier)
    {
        // Curva exponencial no overall (a diferença entre 85 e 90 vale muito mais que
        // entre 60 e 65), com pico de idade por volta dos 25 e queda forte depois dos 30.
        double baseValue = Math.Pow(Math.Max(0, overall - 40) / 10.0, 3.0) * 2.4;
        double ageFactor = age <= 25 ? 0.55 + (age - 16) * 0.05
                         : age <= 29 ? 1.0 - (age - 25) * 0.07
                         : Math.Max(0.06, 0.72 - (age - 29) * 0.12);
        double tierFactor = 0.75 + tier * 0.07;
        // Piso de 0.1M: um profissional nunca "não vale nada" na vitrine, e 0.0M na
        // tela parece bug em vez de número.
        return Math.Max(0.1, Math.Round(baseValue * ageFactor * tierFactor, 1));
    }

    /// <summary>Fisher-Yates com o RNG informado — usado só na ordenação do calendário
    /// (RNG derivado), nunca no fluxo principal da carreira.</summary>
    private static void Shuffle<T>(List<T> list, Pcg32 rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(0, i);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static InjurySeverity RollInjury(Pcg32 rng, int age)
    {
        if (age >= 30 && rng.Chance(0.006)) return InjurySeverity.CareerEnding;
        double roll = rng.NextDouble();
        if (roll < 0.02) return InjurySeverity.Severe;
        if (roll < 0.07) return InjurySeverity.Moderate;
        if (roll < 0.17) return InjurySeverity.Minor;
        return InjurySeverity.None;
    }

    private static int InjuryCost(InjurySeverity s, Pcg32 rng) => s switch
    {
        InjurySeverity.Minor => rng.NextInt(3, 8),
        InjurySeverity.Moderate => rng.NextInt(8, 16),
        InjurySeverity.Severe => rng.NextInt(16, 26),
        _ => 0
    };

    /// <summary>Um degrau mais grave — usado por RequestPlayInjured (painel Saúde): 8%
    /// de chance de piorar quando o jogador insiste em jogar machucado.</summary>
    private static InjurySeverity EscalateInjury(InjurySeverity s) => s switch
    {
        InjurySeverity.Minor => InjurySeverity.Moderate,
        InjurySeverity.Moderate => InjurySeverity.Severe,
        InjurySeverity.Severe => InjurySeverity.CareerEnding,
        _ => s
    };

    private static int Output(Pcg32 rng, int overall, double factor, double baseline,
        double perf, int apps, double roleMod)
    {
        double baseVal = overall / 99.0 * baseline * factor;
        double v = baseVal * (1 + perf / 70.0) * (apps / 34.0)
                   * (0.70 + rng.NextDouble() * 0.60) * (1 + roleMod);
        return Math.Max(0, (int)Math.Round(v));
    }
}
