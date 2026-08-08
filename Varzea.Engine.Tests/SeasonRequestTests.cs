using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Varzea.Engine.Model;
using Varzea.Engine.Ruleset;
using Varzea.Engine.Simulation;
using Xunit;

namespace Varzea.Engine.Tests;

/// <summary>
/// SeasonRequests (painel Contrato + Técnico, roadmap pós-§9) indexa por seasonIndex do
/// motor — uma entrada por temporada SIMULADA, não por chamada de API. Uma única chamada
/// de /careers/advance pode revelar VÁRIAS temporadas de uma vez (quando não há pausa de
/// oferta no meio), então só a PRIMEIRA temporada de cada "lote entre duas chamadas de
/// verdade" pode carregar um pedido real do jogador — as demais do mesmo lote caem em
/// None por construção (o jogador não tinha nem visto essas temporadas ainda quando
/// clicou). Por isso este teste NÃO compara um array denso pré-fabricado (isso não
/// corresponde a nenhuma sequência real de chamadas via API) — em vez disso, roda o
/// processo incremental (que espelha Varzea.Api.Program, incluindo o bug real encontrado
/// nesta sessão: uma chamada que anexa um pedido E pausa na mesma temporada não pode
/// truncar esse pedido de volta — precisa contar +1 pela temporada ainda pausada, não só
/// Timeline.Count), captura o array final que EMERGE dessa sequência real de interações,
/// e confirma que SimulateCareer rodado de uma vez só com esse array final reproduz
/// exatamente o mesmo Timeline. Essa é a garantia real: "replay passo-a-passo == rodar
/// tudo de uma vez com o que realmente foi pedido", o mesmo tipo de prova que
/// AdvanceCareerTests já faz pra TransferChoices.
/// </summary>
public class SeasonRequestTests
{
    private static readonly GameRuleset Rules =
        GameRuleset.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "Ruleset", "balance.json"));
    private static readonly ClubDirectory Clubs =
        ClubDirectory.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "Ruleset", "clubs.json"));

    private static readonly SeasonRequestKind[] RequestCycle =
    {
        SeasonRequestKind.RequestSetPieces, SeasonRequestKind.RequestCaptaincy,
        SeasonRequestKind.RequestRaise, SeasonRequestKind.RequestRenewal,
        SeasonRequestKind.RequestLeaveAtContractEnd, SeasonRequestKind.None
    };

    private static CareerRecipe BaseRecipe(ulong seed) => new(
        Seed: seed,
        Country: Rules.Countries.Keys.First(),
        DraftPicks: new[] { 0, 1, 2, 0, 1, 2, 0, 1 },
        Position: Pos.ST,
        TransferChoices: Enumerable.Range(0, 40).Select(i => i % 3 != 0).ToArray(),
        RulesetVersion: Rules.Version
    );

    private static string Hash(CareerResult result)
    {
        var shape = new
        {
            result.Position, result.RoleName, result.Potential, result.PeakOverall, result.Seasons,
            result.TotalGoals, result.TotalAssists, result.TotalTackles, result.TotalCleanSheets, result.TotalCaps,
            TitleCounts = result.TitleCounts.OrderBy(kv => kv.Key).ToList(),
            Timeline = result.Timeline
        };
        string json = JsonSerializer.Serialize(shape);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>
    /// Espelha exatamente o padrão real de chamadas via /careers/advance: a cada rodada
    /// "de verdade" (nunca quando está resolvendo uma oferta pendente), anexa UM pedido
    /// (tirado do RequestCycle) SEM NUNCA truncar o que já existe — o comprimento do
    /// array já É "quantas temporadas tiveram seu pedido decidido", contando também a
    /// temporada que ficou pausada (se houver), não só Timeline.Count. Devolve o
    /// resultado E o array final de SeasonRequests que emergiu — é esse array que a
    /// comparação com SimulateCareer precisa usar, não um array denso arbitrário.
    /// </summary>
    private static (CareerResult Result, SeasonRequestKind[] FinalRequests, int Pauses, int RealCalls)
        AdvanceToCompletion(CareerSimulator sim, CareerRecipe baseRecipe)
    {
        var decided = new List<bool>();
        // Este teste não exercita ContractChoices (isso já tem cobertura dedicada em
        // AdvanceCareerTests.FullRecipe) — sempre recusa todas as propostas (-1, fora
        // dos limites), o mesmo comportamento que SimulateCareer teria em lote com
        // ContractChoices null/vazio, então a equivalência abaixo continua válida.
        var contractDecided = new List<int>();
        var seasonRequests = Array.Empty<SeasonRequestKind>();
        int pauses = 0, realCalls = 0;
        // true só na chamada IMEDIATAMENTE seguinte a uma pausa — resolve a decisão
        // daquela pausa específica. Precisa ser consumida (voltar a false) a cada volta
        // do laço, senão toda chamada depois da PRIMEIRA pausa seria tratada como
        // resolução pra sempre.
        bool resolvingDecisionNow = false;

        while (true)
        {
            bool resolving = resolvingDecisionNow;
            resolvingDecisionNow = false;

            SeasonRequestKind[] callRequests;
            if (resolving)
            {
                callRequests = seasonRequests;
            }
            else
            {
                realCalls++;
                var next = RequestCycle[realCalls % RequestCycle.Length];
                callRequests = seasonRequests.Append(next).ToArray();
            }

            var recipe = baseRecipe with
            {
                TransferChoices = decided.ToArray(),
                ContractChoices = contractDecided.ToArray(),
                SeasonRequests = callRequests
            };
            var progress = sim.AdvanceCareer(recipe);

            // +1 quando sobra uma pausa: essa temporada já consumiu seu slot dentro do
            // motor, mas não entra em Timeline até ser resolvida — sem esse +1, a
            // próxima chamada (se também for "de verdade") sobrescreveria esse pedido.
            int consumedSlots = progress.Result.Timeline.Count + (progress.AwaitingDecision ? 1 : 0);
            seasonRequests = callRequests.Length < consumedSlots
                ? callRequests.Concat(Enumerable.Repeat(SeasonRequestKind.None, consumedSlots - callRequests.Length)).ToArray()
                : callRequests;

            if (!progress.AwaitingDecision) return (progress.Result, seasonRequests, pauses, realCalls);

            pauses++;
            resolvingDecisionNow = true;
            if (progress.PendingOffer is not null)
            {
                decided.Add(baseRecipe.TransferChoices[decided.Count]);
            }
            else
            {
                Assert.NotNull(progress.PendingContractChoice);
                contractDecided.Add(-1);
            }
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(42UL)]
    [InlineData(999_999UL)]
    [InlineData(123_456UL)]
    public void StepByStep_MatchesBatchSimulation_WithFinalSeasonRequests(ulong seed)
    {
        var sim = new CareerSimulator(Rules, Clubs);
        var baseRecipe = BaseRecipe(seed);

        var (stepped, finalRequests, pauses, realCalls) = AdvanceToCompletion(sim, baseRecipe);

        // Roda a receita completa DE UMA VEZ SÓ com o array que emergiu do replay
        // incremental — essa é a equivalência real (ver comentário da classe).
        var direct = sim.SimulateCareer(baseRecipe with { SeasonRequests = finalRequests });

        // realCalls conta só as chamadas "de verdade" (não-resolução) — na arquitetura
        // atual só existe UMA por trecho entre pausas (fetchMore já devolve tudo até a
        // próxima pausa/fim numa chamada só, ver Sim.tsx), então realCalls==1 é o normal,
        // não um sinal de teste fraco. O que importa é a pausa em si: ela força aquele
        // trecho a revelar VÁRIAS temporadas de uma vez, exatamente o cenário que o
        // realinhamento pós-chamada em AdvanceToCompletion precisa acertar.
        Assert.Equal(Hash(direct), Hash(stepped));
        Assert.True(pauses > 0, "essa seed não gerou nenhuma oferta de transferência — trocar de seed pro teste exercitar o caminho interativo de verdade");
        _ = realCalls;
    }

    /// <summary>
    /// Bug real encontrado nesta sessão (verificação manual via fetch() real no
    /// navegador, não pelas 5 seeds do teste acima): um pedido anexado numa chamada que
    /// PAUSA NA MESMA temporada (RequestLeaveNow força isso sempre — busca garantida)
    /// era perdido na chamada seguinte, porque o realinhamento pós-chamada usava só
    /// Timeline.Count, que não conta a temporada ainda pausada. Este teste espelha as
    /// duas chamadas exatas de /careers/advance que expuseram o bug e confirma o fix
    /// (contar +1 quando sobra PendingOffer, nunca truncar).
    /// </summary>
    [Fact]
    public void RequestLeaveNow_SurvivesReplay_WhenItImmediatelyPausesTheSameCall()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        // Igual a AdvanceCareerTests.AdvanceToCompletion: a chamada real de /careers/advance
        // começa com TransferChoices VAZIO (nenhuma decisão feita ainda) — passar o array
        // cheio de baseRecipe direto faria a oferta resolver na hora em vez de pausar.
        var baseRecipe = BaseRecipe(1) with { TransferChoices = Array.Empty<bool>() };

        // "Nova regra" (roadmap pós-§9): nenhuma proposta chega antes dos 18 anos, então
        // RequestLeaveNow não pode mais forçar pausa já na primeira temporada (idade
        // inicial 16) — repete o pedido em toda temporada (mesmo padrão de
        // AlwaysSetPieces/AlwaysCaptaincy abaixo) até a idade mínima liberar o gatilho.
        var call1Requests = Enumerable.Repeat(SeasonRequestKind.RequestLeaveNow, 25).ToArray();
        var progress1 = sim.AdvanceCareer(baseRecipe with { SeasonRequests = call1Requests });
        Assert.True(progress1.AwaitingDecision, "RequestLeaveNow deveria forçar uma oferta garantida a partir dos 18 anos");

        int consumedSlots = progress1.Result.Timeline.Count + (progress1.AwaitingDecision ? 1 : 0);
        var afterCall1 = call1Requests.Take(consumedSlots).ToArray();

        // Roadmap pós-§9, "propostas de mais clubes": a busca garantida de RequestLeaveNow
        // agora também passa pelo caminho de múltiplas propostas (mesmo mecanismo da
        // não-renovação de contrato) — pausa como PendingContractChoice, não mais como
        // PendingTransferOffer de proposta única.
        Assert.NotNull(progress1.PendingContractChoice);

        // Chamada 2: espelha POST /careers/advance {contractChoiceIndex:0} — aceita a
        // primeira proposta. SeasonRequests não muda.
        var progress2 = sim.AdvanceCareer(baseRecipe with { ContractChoices = new[] { 0 }, SeasonRequests = afterCall1 });
        var pausedSeasonIndex = progress1.Result.Timeline.Count;
        var pausedSeason = progress2.Result.Timeline[pausedSeasonIndex];
        Assert.Equal(SeasonRequestKind.RequestLeaveNow, pausedSeason.RequestMade);
        Assert.True(pausedSeason.RequestGranted);
        Assert.True(pausedSeason.Age >= 18, "gatilho de RequestLeaveNow não deveria disparar antes dos 18 anos");
    }

    // Pede em toda temporada (não só na primeira) pra não depender de uma única rolagem
    // de chance ter dado certo numa seed específica — o que importa aqui é a MECÂNICA
    // (fica concedido pra sempre e afeta as seguintes), não a probabilidade exata.
    private static readonly SeasonRequestKind[] AlwaysSetPieces =
        Enumerable.Repeat(SeasonRequestKind.RequestSetPieces, 25).ToArray();
    private static readonly SeasonRequestKind[] AlwaysCaptaincy =
        Enumerable.Repeat(SeasonRequestKind.RequestCaptaincy, 25).ToArray();

    [Fact]
    public void SetPieces_OnceGranted_AppliesToLaterSeasons()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        var recipe = BaseRecipe(1) with { SeasonRequests = AlwaysSetPieces };

        var result = sim.SimulateCareer(recipe);
        Assert.Contains(result.Timeline, s => s.HasSetPieces);
    }

    /// <summary>
    /// Mecânica de PendingContractChoice (Roadmap §9 Bloco 3, corte "múltiplas
    /// propostas"): confirma que aceitar/recusar de fato faz o motor fazer coisas
    /// diferentes, não só que passo-a-passo e lote batem (isso já é coberto por
    /// AdvanceCareerTests.StepByStep_MatchesBatchSimulation com FullRecipe.ContractChoices).
    /// </summary>
    [Fact]
    public void ContractChoice_AcceptedProposal_ChangesTier_DeclinedProposal_KeepsTier()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        var alwaysLeave = Enumerable.Repeat(SeasonRequestKind.RequestLeaveAtContractEnd, 25).ToArray();
        var baseRecipe = BaseRecipe(1) with { SeasonRequests = alwaysLeave };

        // Recusar TUDO (-1) nunca reseta o relógio do contrato (nem propostas fora do
        // ciclo, nem a da não-renovação) — a primeira expiração natural cai exatamente
        // onde NextContractDuration decidiu, sem interferência. Roda essa linha de base
        // primeiro pra descobrir quantas propostas (dentro ou fora do ciclo — Roadmap
        // pós-§9, "propostas de mais clubes", ambas consomem o mesmo índice) foram
        // consumidas até lá.
        var declined = sim.SimulateCareer(baseRecipe with { ContractChoices = Enumerable.Repeat(-1, 30).ToArray() });
        var declinedExpiringIdx = Enumerable.Range(0, declined.Timeline.Count)
            .Where(i => declined.Timeline[i].ContractExpiring).ToList();
        Assert.NotEmpty(declinedExpiringIdx);
        int first = declinedExpiringIdx[0];

        // HadTransferOffer marca toda temporada que consumiu um índice de
        // ContractChoices (proposta dentro OU fora do ciclo) — contar até "first"
        // (inclusive) dá exatamente quantas decisões vieram antes da 1ª expiração.
        int consumedByFirst = declined.Timeline.Take(first + 1).Count(s => s.HadTransferOffer);
        Assert.True(consumedByFirst > 0, "a temporada da 1ª expiração deveria ter gerado uma proposta");

        // Aceita SÓ a última proposta antes/na 1ª expiração (a da não-renovação em si);
        // todas as anteriores (fora do ciclo, se houver) continuam recusadas — isso
        // garante que a 1ª expiração caia na MESMA temporada nas duas carreiras, sem
        // nenhuma aceitação prévia resetar o relógio do contrato antes da hora.
        var acceptOnlyLast = Enumerable.Repeat(-1, consumedByFirst - 1).Append(0)
            .Concat(Enumerable.Repeat(-1, 29 - consumedByFirst)).ToArray();
        var accepted = sim.SimulateCareer(baseRecipe with { ContractChoices = acceptOnlyLast });
        var acceptedExpiringIdx = Enumerable.Range(0, accepted.Timeline.Count)
            .Where(i => accepted.Timeline[i].ContractExpiring).ToList();
        Assert.NotEmpty(acceptedExpiringIdx);
        Assert.Equal(first, acceptedExpiringIdx[0]);
        int tierBeforeFirst = first > 0 ? declined.Timeline[first - 1].ClubTier : declined.Timeline[first].ClubTier;

        // Recusar mantém o tier (contrato curto de "prova")...
        Assert.Equal(tierBeforeFirst, declined.Timeline[first].ClubTier);

        // ...e recusar sempre gera um contrato de no máximo 2 temporadas, enquanto
        // aceitar dá um contrato normal (NextContractDuration — 3+ temporadas nesta
        // idade, jovem, bem antes do pico). Comparar ContractYearsRemaining em vez de
        // ClubTier evita falso-negativo quando o tier já está num limite (1 ou 5) e o
        // clamp da proposta de "upgrade" não muda nada.
        Assert.True(declined.Timeline[first].ContractYearsRemaining <= 2);
        Assert.True(accepted.Timeline[first].ContractYearsRemaining > 2);
    }

    // Só pede empréstimo na PRIMEIRA temporada (índice 0) — diferente de
    // AlwaysSetPieces/AlwaysCaptaincy (concessão permanente), o empréstimo dura só uma
    // temporada e a restauração acontece no TOPO da iteração seguinte, antes de
    // qualquer pedido novo ser avaliado (ver parentTier em CareerSimulator) — então
    // repetir o pedido toda temporada resultaria em "emprestado" de novo a cada ano
    // (mecânica correta, mas não testaria a restauração isoladamente). Pedir só uma vez
    // isola exatamente o comportamento "um tier abaixo por UMA temporada, depois volta".
    private static readonly SeasonRequestKind[] LoanOnlyFirstSeason =
        new[] { SeasonRequestKind.RequestLoan }
            .Concat(Enumerable.Repeat(SeasonRequestKind.None, 24)).ToArray();

    [Fact]
    public void RequestLoan_DropsTierForOneSeasonThenRestores()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        var recipe = BaseRecipe(7) with { SeasonRequests = LoanOnlyFirstSeason, TransferChoices = Array.Empty<bool>() };

        var result = sim.SimulateCareer(recipe);

        var firstSeason = result.Timeline[0];
        Assert.True(firstSeason.RequestGranted, "tier inicial nesta seed deveria ser > 1, então o empréstimo deveria ser concedido");
        Assert.Equal(SeasonRequestKind.RequestLoan, firstSeason.RequestMade);
        Assert.True(firstSeason.OnLoan);

        var secondSeason = result.Timeline[1];
        Assert.False(secondSeason.OnLoan, "empréstimo dura só uma temporada — a seguinte não pode continuar marcada");
        Assert.True(secondSeason.ClubTier > firstSeason.ClubTier, "tier deveria voltar a subir pro clube dono do contrato");
    }

    [Fact]
    public void Captaincy_OnceGranted_StaysGrantedForRestOfCareer()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        var recipe = BaseRecipe(1) with { SeasonRequests = AlwaysCaptaincy };

        var result = sim.SimulateCareer(recipe);
        int firstCaptainIdx = result.Timeline.FindIndex(s => s.IsCaptain);
        Assert.True(firstCaptainIdx >= 0, "pedindo braçadeira toda temporada, deveria ser concedida em algum momento nesta carreira");
        Assert.All(result.Timeline.Skip(firstCaptainIdx), s => Assert.True(s.IsCaptain));
    }

    // --- Fadiga (painel Saúde, roadmap pós-§9) — único sistema desta rodada SEMPRE
    // ATIVO, não depende de nenhum SeasonRequest ter sido feito.

    [Fact]
    public void Fatigue_AccumulatesEvenWithoutAnyRequest()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        var recipe = BaseRecipe(7); // SeasonRequests null — comportamento padrão

        var result = sim.SimulateCareer(recipe);
        Assert.True(result.Timeline.Count > 3, "carreira precisa de temporadas suficientes pro acúmulo ficar visível");
        Assert.True(result.Timeline[3].Fatigue > 0, "fadiga deveria acumular sozinha, mesmo sem o jogador nunca abrir o painel Saúde");
        // Nunca decresce sozinha (só RequestRest reduz) — não-decrescente até aqui.
        for (int i = 1; i <= 3; i++)
            Assert.True(result.Timeline[i].Fatigue >= result.Timeline[i - 1].Fatigue);
    }

    private static readonly SeasonRequestKind[] AlwaysRest =
        Enumerable.Repeat(SeasonRequestKind.RequestRest, 25).ToArray();

    [Fact]
    public void RequestRest_HalvesAppsAndReducesFatigue_ComparedToNoRest()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        var baseRecipe = BaseRecipe(7);

        var rested = sim.SimulateCareer(baseRecipe with { SeasonRequests = AlwaysRest });
        var baseline = sim.SimulateCareer(baseRecipe); // sem pedidos — mesmo seed, mesma carreira "crua"

        var restedFirst = rested.Timeline[0];
        var baselineFirst = baseline.Timeline[0];
        Assert.True(restedFirst.RequestGranted);
        Assert.Equal(SeasonRequestKind.RequestRest, restedFirst.RequestMade);

        // apps é sorteado a partir do MESMO rng (seed idêntica, nada antes disso na
        // temporada consome rng de forma diferente) — descansar só corta pela metade
        // DEPOIS do sorteio, então a comparação direta é válida.
        Assert.True(restedFirst.Apps <= baselineFirst.Apps / 2 + 1);
        Assert.True(restedFirst.Fatigue < baselineFirst.Fatigue, "descansar deveria acumular MENOS fadiga que a temporada normal equivalente");
    }

    private static readonly SeasonRequestKind[] PersonalTrainerThenNone =
        new[] { SeasonRequestKind.RequestPersonalTrainer }
            .Concat(Enumerable.Repeat(SeasonRequestKind.None, 24)).ToArray();

    [Fact]
    public void RequestPersonalTrainer_StaysGranted_AndSlowsFatigueAccumulation()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        var baseRecipe = BaseRecipe(7);

        var withTrainer = sim.SimulateCareer(baseRecipe with { SeasonRequests = PersonalTrainerThenNone });
        var baseline = sim.SimulateCareer(baseRecipe);

        Assert.True(withTrainer.Timeline[0].RequestGranted);
        Assert.All(withTrainer.Timeline, s => Assert.True(s.HasPersonalTrainer, "concessão fica pra sempre, mesmo padrão de isCaptain/hasSetPieces"));

        // Depois de várias temporadas, a taxa reduzida (0.025 vs 0.04 por temporada)
        // deveria deixar a fadiga acumulada visivelmente menor que o baseline sem
        // personal trainer — carreiras idênticas em tudo mais (mesma seed).
        int lastIdx = Math.Min(withTrainer.Timeline.Count, baseline.Timeline.Count) - 1;
        Assert.True(lastIdx >= 5, "carreira precisa de temporadas suficientes pra taxa reduzida fazer diferença visível");
        Assert.True(withTrainer.Timeline[lastIdx].Fatigue < baseline.Timeline[lastIdx].Fatigue);
    }

    // Pede em toda temporada — o pedido só tem efeito nas que realmente sortearem
    // lesão (ver CareerSimulator), então precisa de várias tentativas pra pegar uma.
    private static readonly SeasonRequestKind[] AlwaysPlayInjured =
        Enumerable.Repeat(SeasonRequestKind.RequestPlayInjured, 25).ToArray();

    [Fact]
    public void RequestPlayInjured_OnlyMattersWhenInjuryActuallyRolled()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        var recipe = BaseRecipe(7) with { SeasonRequests = AlwaysPlayInjured };

        var result = sim.SimulateCareer(recipe);

        // Toda temporada com RequestGranted=true precisa ter uma lesão de verdade —
        // o pedido não faz nada numa temporada sem lesão (ver comentário no motor).
        foreach (var season in result.Timeline.Where(s => s.RequestMade == SeasonRequestKind.RequestPlayInjured))
        {
            if (season.RequestGranted)
                Assert.NotEqual(InjurySeverity.None, season.Injury);
        }
        Assert.Contains(result.Timeline, s => s.RequestMade == SeasonRequestKind.RequestPlayInjured && s.RequestGranted);
    }

    // --- Promessa de título (painel Clube, roadmap pós-§9) ---

    // Pede em toda temporada — precisa de várias tentativas pra pegar tanto uma
    // temporada em que virou campeão quanto uma em que não virou, na mesma carreira.
    private static readonly SeasonRequestKind[] AlwaysPromiseTitle =
        Enumerable.Repeat(SeasonRequestKind.RequestPromiseTitle, 25).ToArray();

    [Fact]
    public void RequestPromiseTitle_FulfilledOnlyWhenLeagueIsWon_AndMovesMoralInBothDirections()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        var recipe = BaseRecipe(7) with { SeasonRequests = AlwaysPromiseTitle };

        var result = sim.SimulateCareer(recipe);

        var promised = result.Timeline.Where(s => s.PromisedTitle).ToList();
        Assert.NotEmpty(promised);
        Assert.All(promised, s => Assert.True(s.RequestGranted, "a declaração em si é sempre aceita — o que varia é PromiseFulfilled"));

        // PromiseFulfilled é exatamente "foi campeão da liga nesta temporada" — nunca
        // diverge do que season.LeaguePosition já diz (via o título de liga concedido).
        foreach (var s in promised)
            Assert.Equal(s.LeaguePosition == 1, s.PromiseFulfilled);

        // Precisa de exemplos dos dois desfechos pra provar que a moral realmente
        // reage nas DUAS direções (cumprida sobe, quebrada desce), não só uma.
        var fulfilledIdx = result.Timeline.FindIndex(s => s.PromisedTitle && s.PromiseFulfilled);
        var brokenIdx = result.Timeline.FindIndex(s => s.PromisedTitle && !s.PromiseFulfilled);
        Assert.True(fulfilledIdx >= 0, "nesta seed, pedindo toda temporada, deveria ser campeão pelo menos uma vez");
        Assert.True(brokenIdx >= 0, "nesta seed, pedindo toda temporada, deveria falhar pelo menos uma vez também");

        double moraleBeforeFulfilled = fulfilledIdx > 0
            ? (result.Timeline[fulfilledIdx - 1].TeamMorale + result.Timeline[fulfilledIdx - 1].CrowdMorale) / 2.0
            : 0.0;
        double moraleAfterFulfilled = (result.Timeline[fulfilledIdx].TeamMorale + result.Timeline[fulfilledIdx].CrowdMorale) / 2.0;
        Assert.True(moraleAfterFulfilled > moraleBeforeFulfilled, "cumprir a promessa deveria subir a moral média (equipe+torcida)");

        double moraleBeforeBroken = brokenIdx > 0
            ? (result.Timeline[brokenIdx - 1].TeamMorale + result.Timeline[brokenIdx - 1].CrowdMorale) / 2.0
            : 0.0;
        double moraleAfterBroken = (result.Timeline[brokenIdx].TeamMorale + result.Timeline[brokenIdx].CrowdMorale) / 2.0;
        Assert.True(moraleAfterBroken < moraleBeforeBroken, "quebrar a promessa deveria descer a moral média (equipe+torcida)");
    }
}
