using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Varzea.Engine.Model;
using Varzea.Engine.Ruleset;
using Varzea.Engine.Simulation;
using Xunit;

namespace Varzea.Engine.Tests;

/// <summary>
/// A garantia que sustenta ranking público e replay: mesma receita, mesmo resultado,
/// sempre. Um único vazamento de aleatoriedade fora do Pcg32 injetado quebraria isto
/// silenciosamente, e só se descobriria com o acervo salvo já corrompido.
/// </summary>
public class DeterminismTests
{
    private static readonly GameRuleset Rules =
        GameRuleset.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "Ruleset", "balance.json"));
    private static readonly ClubDirectory Clubs =
        ClubDirectory.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "Ruleset", "clubs.json"));

    private static CareerRecipe SampleRecipe(ulong seed, Pos position = Pos.ST) => new(
        Seed: seed,
        Country: Rules.Countries.Keys.First(),
        DraftPicks: new[] { 0, 1, 2, 0, 1, 2, 0, 1 },
        Position: position,
        TransferChoices: Enumerable.Range(0, 12).Select(i => i % 2 == 0).ToArray(),
        RulesetVersion: Rules.Version
    );

    private static string Hash(CareerResult result)
    {
        string json = JsonSerializer.Serialize(result);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(42UL)]
    [InlineData(999_999UL)]
    public void SameRecipe_Produces1000IdenticalHashes(ulong seed)
    {
        var sim = new CareerSimulator(Rules, Clubs);
        var recipe = SampleRecipe(seed);
        string baseline = Hash(sim.SimulateCareer(recipe));

        for (int i = 0; i < 1000; i++)
            Assert.Equal(baseline, Hash(sim.SimulateCareer(recipe)));
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentCareers()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        string a = Hash(sim.SimulateCareer(SampleRecipe(1)));
        string b = Hash(sim.SimulateCareer(SampleRecipe(2)));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SameSeed_DifferentDraftPicks_ChangesResult()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        var recipeA = SampleRecipe(7) with { DraftPicks = new[] { 0, 0, 0, 0, 0, 0, 0, 0 } };
        var recipeB = SampleRecipe(7) with { DraftPicks = new[] { 2, 2, 2, 2, 2, 2, 2, 2 } };

        string a = Hash(sim.SimulateCareer(recipeA));
        string b = Hash(sim.SimulateCareer(recipeB));
        Assert.NotEqual(a, b);
    }
}
