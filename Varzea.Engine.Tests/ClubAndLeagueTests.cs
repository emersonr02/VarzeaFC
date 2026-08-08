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
}
