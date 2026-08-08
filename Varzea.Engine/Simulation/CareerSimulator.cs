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
    public CareerResult SimulateCareer(CareerRecipe recipe) => RunCareer(recipe, interactive: false).Result;

    /// <summary>
    /// Roda a carreira até a próxima oferta de transferência sem decisão em
    /// recipe.TransferChoices, ou até o fim. Não precisa serializar RNG entre chamadas:
    /// como a carreira inteira custa microssegundos, cada chamada re-simula do zero com
    /// a receita um pouco mais completa (uma decisão a mais) — determinístico por
    /// construção, e mantém a API sem sessão de servidor (ver Varzea.Api.CareerState).
    /// </summary>
    public CareerProgress AdvanceCareer(CareerRecipe recipe) => RunCareer(recipe, interactive: true);

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

    private CareerProgress RunCareer(CareerRecipe recipe, bool interactive)
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

        for (int age = curve.StartAge; age <= retireAge; age++)
        {
            if (parentTier >= 0)
            {
                tier = parentTier;
                clubName = parentClubName;
                parentTier = -1;
            }

            // curva de evolução: cresce em direção ao potencial, estabiliza, decai
            if (age < peakAge)
                overall += Math.Max(1, (int)Math.Round((potential - overall) * curve.GrowthRate))
                           + (rng.Chance(0.30) ? 1 : 0);
            else if (age <= peakAge + 3)
                overall += rng.NextInt(-1, 2);
            else
                overall -= rng.NextInt(1, 4);

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
                        clubName = _clubs.PickClub(recipe.Country, tier, rng);
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
                    Age = age, Overall = overall, ClubTier = tier, ClubName = clubName,
                    Injury = injury, LeaguePosition = 20,
                    TeamMorale = teamMorale, CoachMorale = coachMorale, CrowdMorale = crowdMorale,
                    IsCaptain = isCaptain, HasSetPieces = hasSetPieces, OnLoan = parentTier >= 0,
                    Fatigue = fatigue, HasPersonalTrainer = hasPersonalTrainer,
                    RequestMade = request, RequestGranted = requestGranted
                });
                break;
            }

            bool restedThisSeason = request == SeasonRequestKind.RequestRest;

            int apps = playedThroughInjury
                ? Math.Clamp(rng.NextInt(24, 36), 4, 38)
                : Math.Clamp(rng.NextInt(24, 36) - InjuryCost(injury, rng), 4, 38);
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
            var rivals = _clubs.LeagueRivals(recipe.Country, tier, clubName);
            double ownStrength = _clubs.BaseStrength(recipe.Country, clubName) + perf;
            var (leaguePosition, leagueTable) = SimulateLeagueTable(clubName, ownStrength, rivals, recipe.Country, rng);

            var season = new SeasonResult
            {
                Age = age, Overall = overall, ClubTier = tier, ClubName = clubName, Apps = apps,
                Goals = sg, Assists = sa, Tackles = st, CleanSheets = scs,
                Injury = injury,
                LeaguePosition = leaguePosition,
                LeagueTable = leagueTable
            };

            // --- LIGA ---
            if (season.LeaguePosition == 1)
            {
                var kind = country.LeagueGrade switch
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
            int seasonCaps = 0;
            if (overall >= 76 && age >= 18 && age <= 35 && rng.Chance(0.35))
                seasonCaps += rng.NextInt(1, 8);

            bool wcYear = (age - curve.StartAge) % 4 == 2;
            if (wcYear && overall >= 74 && age >= 18)
            {
                double callUp = Math.Clamp((overall - 70) / 60.0 + country.Strength / 40.0, 0.05, 0.60);
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
            double leagueGradeAwardFactor = country.LeagueGrade switch
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

            if (contractExpiring)
            {
                if (wantsToLeaveAtContractEnd)
                {
                    // Jogador já avisou (painel Contrato, roadmap pós-§9) que quer sair
                    // quando o contrato vencesse — pula a rolagem de renovação e vai
                    // direto pro caminho de "não renovou".
                    contractProposals = GenerateContractProposals(tier, overall, target, rng);
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
                        contractProposals = GenerateContractProposals(tier, overall, target, rng);
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
                if (triggered) contractProposals = GenerateContractProposals(tier, overall, target, rng, direction);
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
                    upgrade = contractProposals[choice].Upgrade;
                    tier = Math.Clamp(contractProposals[choice].ClubTier, 1, 5);
                    clubName = _clubs.PickClub(recipe.Country, tier, rng);
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
                RequestMade = request, RequestGranted = requestGranted
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
    private static List<ContractProposalOption> GenerateContractProposals(
        int tier, int overall, int target, Pcg32 rng, bool? forceDirection = null)
    {
        var proposals = new List<ContractProposalOption>();

        bool qualifiesUpgrade = forceDirection ?? (overall >= target);
        int primaryTier = Math.Clamp(tier + (qualifiesUpgrade ? 1 : -1), 1, 5);
        proposals.Add(new ContractProposalOption(primaryTier, qualifiesUpgrade));

        // Segunda proposta: lateral (mesmo tier atual, "fresh start" noutro clube) —
        // chance cresce com o overall, representando mais clubes de olho num jogador bom.
        double lateralChance = Math.Clamp((overall - 65) / 45.0, 0.15, 0.85);
        if (rng.Chance(lateralChance) && primaryTier != tier)
            proposals.Add(new ContractProposalOption(tier, false));

        // Terceira proposta: "esticada" — só pra quem está muito bem, um clube ainda
        // maior que o da proposta principal.
        if (overall >= 85 && tier < 5)
        {
            int stretchTier = Math.Clamp(tier + 1, 1, 5);
            if (!proposals.Any(p => p.ClubTier == stretchTier))
                proposals.Add(new ContractProposalOption(stretchTier, true));
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
    /// um turno único (cada par joga uma vez), pontos por vitória/empate/derrota
    /// (3/1/0). A força do jogador (<paramref name="ownStrength"/>) já vem calculada
    /// pelo chamador a partir do clube + perf da temporada; os rivais recebem a força
    /// de base do próprio clube (ver ClubDirectory.BaseStrength) mais um ruído fixo
    /// pra a tabela não ficar idêntica toda temporada.
    /// </summary>
    private (int Position, List<LeagueTableRow> Table) SimulateLeagueTable(
        string ownClub, double ownStrength, IReadOnlyList<string> rivals, string countryName, Pcg32 rng)
    {
        var strength = new Dictionary<string, double> { [ownClub] = ownStrength };
        foreach (var rival in rivals)
            strength[rival] = _clubs.BaseStrength(countryName, rival) + (rng.NextDouble() * 20 - 10);

        var points = strength.Keys.ToDictionary(k => k, _ => 0);
        var names = strength.Keys.ToList();
        for (int i = 0; i < names.Count; i++)
        {
            for (int j = i + 1; j < names.Count; j++)
            {
                double diff = strength[names[i]] - strength[names[j]];
                double pHome = 1.0 / (1.0 + Math.Pow(10, -diff / EloScale));
                double drawChance = Math.Clamp(BaseDrawChance - Math.Abs(diff) * 0.002, 0.10, 0.30);
                double roll = rng.NextDouble();
                if (roll < drawChance) { points[names[i]] += 1; points[names[j]] += 1; }
                else if (roll < drawChance + (1 - drawChance) * pHome) points[names[i]] += 3;
                else points[names[j]] += 3;
            }
        }

        var ranked = names
            .OrderByDescending(n => points[n])
            .ThenByDescending(n => strength[n])
            .ThenBy(n => n, StringComparer.Ordinal)
            .ToList();

        int position = ranked.IndexOf(ownClub) + 1;
        var table = ranked.Select(n => new LeagueTableRow(n, points[n], n == ownClub)).ToList();
        return (position, table);
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
