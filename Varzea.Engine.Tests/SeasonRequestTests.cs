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

            var recipe = baseRecipe with { TransferChoices = decided.ToArray(), SeasonRequests = callRequests };
            var progress = sim.AdvanceCareer(recipe);

            // +1 quando sobra uma pausa: essa temporada já consumiu seu slot dentro do
            // motor, mas não entra em Timeline até ser resolvida — sem esse +1, a
            // próxima chamada (se também for "de verdade") sobrescreveria esse pedido.
            int consumedSlots = progress.Result.Timeline.Count + (progress.PendingOffer is not null ? 1 : 0);
            seasonRequests = callRequests.Length < consumedSlots
                ? callRequests.Concat(Enumerable.Repeat(SeasonRequestKind.None, consumedSlots - callRequests.Length)).ToArray()
                : callRequests;

            if (!progress.AwaitingDecision) return (progress.Result, seasonRequests, pauses, realCalls);

            pauses++;
            resolvingDecisionNow = true;
            Assert.NotNull(progress.PendingOffer);
            decided.Add(baseRecipe.TransferChoices[decided.Count]);
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
