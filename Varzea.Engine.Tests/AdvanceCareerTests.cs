using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Varzea.Engine.Model;
using Varzea.Engine.Ruleset;
using Varzea.Engine.Simulation;
using Xunit;

namespace Varzea.Engine.Tests;

/// <summary>
/// AdvanceCareer existe pra alimentar as decisões de transferência uma a uma
/// (fluxo interativo de /careers/advance), sem RNG serializado entre chamadas —
/// cada chamada re-simula do zero com a receita um pouco mais completa. Esta
/// suíte prova que o atalho (re-simular tudo) é equivalente a rodar a carreira
/// inteira de uma vez: se divergir, o motor "interativo" e o motor "batch"
/// saíram de sincronia, e o jogo pausaria numa decisão pra depois dar um
/// resultado final diferente do que o jogador realmente viveu.
/// </summary>
public class AdvanceCareerTests
{
    private static readonly GameRuleset Rules =
        GameRuleset.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "Ruleset", "balance.json"));

    private static CareerRecipe FullRecipe(ulong seed) => new(
        Seed: seed,
        Country: Rules.Countries.Keys.First(),
        DraftPicks: new[] { 0, 1, 2, 0, 1, 2, 0, 1 },
        Position: Pos.ST,
        // Grande o bastante pra nunca faltar decisão numa carreira real (~20 temporadas,
        // no máximo uma oferta por temporada) — diferente do array de 12 usado noutros
        // testes, que existe só pra exercitar o "acabaram as decisões" do modo batch.
        TransferChoices: Enumerable.Range(0, 40).Select(i => i % 3 != 0).ToArray(),
        RulesetVersion: Rules.Version,
        SeasonRequests: null,
        // Cicla 0,1,2 — GenerateContractProposals gera 1-3 propostas, então às vezes o
        // índice fica fora dos limites (o motor já trata isso como "recusou todas", ver
        // CareerSimulator) — não precisa ser sempre válido, só determinístico.
        ContractChoices: Enumerable.Range(0, 40).Select(i => i % 3).ToArray()
    );

    /// <summary>
    /// Hash só do RESULTADO da simulação — não inclui CareerResult.Recipe.TransferChoices.
    /// A versão "direta" usa um array de 40 posições (folga de sobra); a "passo-a-passo"
    /// só acumula as decisões de fato consumidas (pode ter 5, por exemplo). Os arrays têm
    /// tamanhos diferentes por construção do teste, mas isso não é uma divergência real —
    /// incluir o Recipe no hash compararia a receita de entrada, não o que a carreira viveu.
    /// </summary>
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

    /// <summary>Simula exatamente o que a API faz: acumula decisões uma a uma, cada
    /// vez que AdvanceCareer pausa numa oferta pendente — agora em DOIS arrays
    /// separados, já que PendingOffer (bool) e PendingContractChoice (índice) são tipos
    /// de pausa mutuamente exclusivos, cada um com sua própria fonte de decisões
    /// pré-fabricadas (TransferChoices / ContractChoices de FullRecipe).</summary>
    private static (CareerResult Result, int PauseCount) AdvanceToCompletion(CareerSimulator sim, CareerRecipe full)
    {
        var decided = new List<bool>();
        var contractDecided = new List<int>();
        int pauses = 0;
        while (true)
        {
            var progress = sim.AdvanceCareer(full with
            {
                TransferChoices = decided.ToArray(),
                ContractChoices = contractDecided.ToArray()
            });
            if (!progress.AwaitingDecision) return (progress.Result, pauses);

            pauses++;
            if (progress.PendingOffer is not null)
            {
                decided.Add(full.TransferChoices[decided.Count]);
            }
            else
            {
                Assert.NotNull(progress.PendingContractChoice);
                contractDecided.Add(full.ContractChoices![contractDecided.Count]);
            }
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(42UL)]
    [InlineData(999_999UL)]
    [InlineData(123_456UL)]
    public void StepByStep_MatchesBatchSimulation(ulong seed)
    {
        var sim = new CareerSimulator(Rules);
        var recipe = FullRecipe(seed);

        string direct = Hash(sim.SimulateCareer(recipe));
        var (stepped, pauses) = AdvanceToCompletion(sim, recipe);

        Assert.Equal(direct, Hash(stepped));
        Assert.True(pauses > 0, "essa seed não gerou nenhuma oferta de transferência — trocar de seed pro teste exercitar o caminho interativo de verdade");
    }

    [Fact]
    public void PendingOffer_SeasonNeverAppearsInTimelineUntilResolved()
    {
        var sim = new CareerSimulator(Rules);
        var recipe = FullRecipe(42);

        var progress = sim.AdvanceCareer(recipe with { TransferChoices = Array.Empty<bool>(), ContractChoices = Array.Empty<int>() });
        Assert.True(progress.AwaitingDecision);

        // A primeira pausa desta seed pode ser PendingOffer OU PendingContractChoice
        // (não-renovação agora gera propostas múltiplas, ver Domain.cs) — o que este
        // teste garante vale pros dois: a temporada pendente não aparece no Timeline.
        int pendingAge = progress.PendingOffer?.Age ?? progress.PendingContractChoice!.Age;
        Assert.DoesNotContain(progress.Result.Timeline, s => s.Age == pendingAge);
    }
}
