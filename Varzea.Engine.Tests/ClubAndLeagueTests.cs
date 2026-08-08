using Varzea.Engine.Model;
using Varzea.Engine.Ruleset;
using Varzea.Engine.Simulation;
using Xunit;

namespace Varzea.Engine.Tests;

/// <summary>
/// Clubes reais (Roadmap pós-§9, painel Clube): 3 opções iniciais reais do próprio
/// país (não fictícias) e uma tabela de classificação real de pontos corridos por
/// temporada, em vez do número abstrato de posição que existia antes.
/// </summary>
public class ClubAndLeagueTests
{
    private static readonly GameRuleset Rules =
        GameRuleset.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "Ruleset", "balance.json"));
    private static readonly ClubDirectory Clubs =
        ClubDirectory.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "Ruleset", "clubs.json"));

    private static CareerRecipe BaseRecipe(ulong seed, string country) => new(
        Seed: seed,
        Country: country,
        DraftPicks: new[] { 0, 1, 2, 0, 1, 2, 0, 1 },
        Position: Pos.ST,
        TransferChoices: Enumerable.Range(0, 40).Select(i => i % 3 != 0).ToArray(),
        RulesetVersion: Rules.Version
    );

    [Fact]
    public void StartingClubOptions_AreThreeDistinctRealClubsFromThePlayersCountry()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        string country = "Brasil";
        var recipe = BaseRecipe(1, country);

        var options = sim.StartingClubOptions(recipe.Seed, country, recipe.DraftPicks, recipe.Position);

        Assert.Equal(3, options.Count);
        Assert.Equal(options.Count, options.Distinct().Count());
        var allBrazilianClubs = Clubs.Countries[country].Divisao1.Concat(Clubs.Countries[country].Divisao2);
        Assert.All(options, o => Assert.Contains(o, allBrazilianClubs));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void StartingClubChoice_PicksExactlyThatOptionAsFirstSeasonClub(int choiceIndex)
    {
        var sim = new CareerSimulator(Rules, Clubs);
        string country = "Alemanha";
        var recipe = BaseRecipe(3, country);

        var options = sim.StartingClubOptions(recipe.Seed, country, recipe.DraftPicks, recipe.Position);
        var result = sim.SimulateCareer(recipe with { StartingClubChoice = choiceIndex });

        Assert.Equal(options[choiceIndex], result.Timeline[0].ClubName);
    }

    [Fact]
    public void StartingClubChoice_NullOrOutOfRange_DefaultsToFirstOption()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        string country = "Espanha";
        var recipe = BaseRecipe(5, country);

        var options = sim.StartingClubOptions(recipe.Seed, country, recipe.DraftPicks, recipe.Position);
        var withNull = sim.SimulateCareer(recipe with { StartingClubChoice = null });
        var withOutOfRange = sim.SimulateCareer(recipe with { StartingClubChoice = 99 });

        Assert.Equal(options[0], withNull.Timeline[0].ClubName);
        Assert.Equal(options[0], withOutOfRange.Timeline[0].ClubName);
    }

    [Theory]
    [InlineData(1UL, "Brasil")]
    [InlineData(7UL, "Inglaterra")]
    [InlineData(42UL, "Argentina")]
    public void LeagueTable_ContainsPlayerClubAtTheReportedPosition_AndIsSortedByPoints(ulong seed, string country)
    {
        var sim = new CareerSimulator(Rules, Clubs);
        var recipe = BaseRecipe(seed, country);

        var result = sim.SimulateCareer(recipe);

        Assert.NotEmpty(result.Timeline);
        foreach (var season in result.Timeline)
        {
            Assert.NotEmpty(season.LeagueTable);
            // A linha na posição reportada (1-based) é exatamente o clube do jogador.
            var row = season.LeagueTable[season.LeaguePosition - 1];
            Assert.True(row.IsPlayerClub);
            Assert.Equal(season.ClubName, row.ClubName);

            // Só uma linha marcada como "do jogador".
            Assert.Single(season.LeagueTable, r => r.IsPlayerClub);

            // Pontos não-crescentes ao longo da tabela (ordenada por posição).
            for (int i = 1; i < season.LeagueTable.Count; i++)
                Assert.True(season.LeagueTable[i].Points <= season.LeagueTable[i - 1].Points);
        }
    }

    [Fact]
    public void LeagueTable_NeverIncludesThePlayersOwnClubTwice()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        var recipe = BaseRecipe(9, "Portugal");

        var result = sim.SimulateCareer(recipe);

        foreach (var season in result.Timeline)
        {
            int occurrences = season.LeagueTable.Count(r => r.ClubName == season.ClubName);
            Assert.Equal(1, occurrences);
        }
    }

    /// <summary>Acesso/rebaixamento (roadmap pós-§9): o efeito (tier mudou) só aparece
    /// na temporada SEGUINTE à marcada Promoted/Relegated — mesmo "delay de 1 temporada"
    /// de qualquer outra mudança de clube no motor. Varre várias seeds/países pra achar
    /// pelo menos uma ocorrência de cada em carreiras longas o bastante.</summary>
    [Fact]
    public void PromotionAndRelegation_TakeEffectOnlyTheFollowingSeason_AndNeverTouchGrandes()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        bool sawPromotion = false, sawRelegation = false;

        foreach (var country in new[] { "Brasil", "Alemanha", "Inglaterra", "Espanha", "Itália" })
        {
            for (ulong seed = 1; seed <= 15; seed++)
            {
                var result = sim.SimulateCareer(BaseRecipe(seed, country));
                for (int i = 0; i < result.Timeline.Count - 1; i++)
                {
                    var season = result.Timeline[i];
                    var next = result.Timeline[i + 1];
                    // Nunca os dois ao mesmo tempo, e nunca fora do intervalo de tier
                    // coberto por acesso/rebaixamento (grandes/tier 5 ficam de fora).
                    Assert.False(season.Promoted && season.Relegated);
                    Assert.True(season.ClubTier is >= 1 and <= 4 || !(season.Promoted || season.Relegated));

                    if (season.Promoted && !next.OnLoan)
                    {
                        sawPromotion = true;
                        Assert.True(next.ClubTier is 3 or 4 or 5, "promoção devia levar a um tier de 1ª divisão ou acima");
                    }
                    if (season.Relegated && !next.OnLoan)
                    {
                        sawRelegation = true;
                        Assert.True(next.ClubTier is 1 or 2, "rebaixamento devia levar a um tier de 2ª divisão");
                    }
                }
            }
        }

        Assert.True(sawPromotion, "nenhuma promoção observada nas seeds/países testados");
        Assert.True(sawRelegation, "nenhum rebaixamento observado nas seeds/países testados");
    }
}
