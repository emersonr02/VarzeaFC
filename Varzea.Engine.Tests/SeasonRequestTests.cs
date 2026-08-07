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
        var sim = new CareerSimulator(Rules);
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
        var sim = new CareerSimulator(Rules);
        // Igual a AdvanceCareerTests.AdvanceToCompletion: a chamada real de /careers/advance
        // começa com TransferChoices VAZIO (nenhuma decisão feita ainda) — passar o array
        // cheio de baseRecipe direto faria a oferta resolver na hora em vez de pausar.
        var baseRecipe = BaseRecipe(1) with { TransferChoices = Array.Empty<bool>() };

        // Chamada 1: espelha POST /careers/advance {decision:null, request:"RequestLeaveNow"}
        var call1Requests = new[] { SeasonRequestKind.RequestLeaveNow };
        var progress1 = sim.AdvanceCareer(baseRecipe with { SeasonRequests = call1Requests });
        Assert.True(progress1.AwaitingDecision, "RequestLeaveNow deveria forçar uma oferta garantida na primeira temporada");

        int consumedSlots = progress1.Result.Timeline.Count + (progress1.PendingOffer is not null ? 1 : 0);
        var afterCall1 = call1Requests.Length < consumedSlots
            ? call1Requests.Concat(Enumerable.Repeat(SeasonRequestKind.None, consumedSlots - call1Requests.Length)).ToArray()
            : call1Requests;
        Assert.Equal(SeasonRequestKind.RequestLeaveNow, Assert.Single(afterCall1));

        // Chamada 2: espelha POST /careers/advance {decision:false} — SeasonRequests não muda.
        var progress2 = sim.AdvanceCareer(baseRecipe with { TransferChoices = new[] { false }, SeasonRequests = afterCall1 });
        var firstSeason = progress2.Result.Timeline[0];
        Assert.Equal(SeasonRequestKind.RequestLeaveNow, firstSeason.RequestMade);
        Assert.True(firstSeason.RequestGranted);
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
        var sim = new CareerSimulator(Rules);
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
        var sim = new CareerSimulator(Rules);
        var alwaysLeave = Enumerable.Repeat(SeasonRequestKind.RequestLeaveAtContractEnd, 25).ToArray();
        // TransferChoices vazio (nunca aceita nenhuma oferta fora do ciclo) — aceitar
        // uma dessas TAMBÉM reseta o relógio do contrato (mesmo bloco de reset do
        // contrato natural, ver CareerSimulator), o que empurraria a primeira expiração
        // pra muito mais tarde na carreira (jogador já veterano) e faria as durações de
        // aceitar/recusar coincidirem por coincidência de faixa etária, não pela
        // mecânica que este teste quer provar.
        var baseRecipe = BaseRecipe(1) with { SeasonRequests = alwaysLeave, TransferChoices = Array.Empty<bool>() };

        var accepted = sim.SimulateCareer(baseRecipe with { ContractChoices = Enumerable.Repeat(0, 25).ToArray() });
        var declined = sim.SimulateCareer(baseRecipe with { ContractChoices = Enumerable.Repeat(-1, 25).ToArray() });

        // A duração do PRIMEIRO contrato (NextContractDuration, sorteado antes de
        // qualquer escolha) depende só de idade/pico/aposentadoria — então a primeira
        // expiração cai na mesma temporada nas duas carreiras. Dali em diante as
        // durações divergem (recusar sempre gera contrato curto de "prova", ver
        // CareerSimulator), então só a primeira expiração é comparável 1:1.
        var declinedExpiringIdx = Enumerable.Range(0, declined.Timeline.Count)
            .Where(i => declined.Timeline[i].ContractExpiring).ToList();
        var acceptedExpiringIdx = Enumerable.Range(0, accepted.Timeline.Count)
            .Where(i => accepted.Timeline[i].ContractExpiring).ToList();
        Assert.NotEmpty(declinedExpiringIdx);
        Assert.NotEmpty(acceptedExpiringIdx);
        Assert.Equal(declinedExpiringIdx[0], acceptedExpiringIdx[0]);

        int first = declinedExpiringIdx[0];
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
        var sim = new CareerSimulator(Rules);
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
        var sim = new CareerSimulator(Rules);
        var recipe = BaseRecipe(1) with { SeasonRequests = AlwaysCaptaincy };

        var result = sim.SimulateCareer(recipe);
        int firstCaptainIdx = result.Timeline.FindIndex(s => s.IsCaptain);
        Assert.True(firstCaptainIdx >= 0, "pedindo braçadeira toda temporada, deveria ser concedida em algum momento nesta carreira");
        Assert.All(result.Timeline.Skip(firstCaptainIdx), s => Assert.True(s.IsCaptain));
    }
}
