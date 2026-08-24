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
            // Temporada final por lesão CareerEnding não chega a jogar a liga (ver
            // CareerSimulator: "if (injury == InjurySeverity.CareerEnding)" retorna
            // antes da tabela ser calculada, com LeaguePosition=20 de placeholder) —
            // LeagueTable fica vazia por design só nesse caso, não é bug.
            if (season.LeagueTable.Count == 0) continue;
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

    /// <summary>Transferência internacional (roadmap pós-§9, "não vi uma partida com
    /// transferências para outra liga"): ClubCountry pode divergir de Recipe.Country
    /// (nacionalidade, nunca muda) depois de uma proposta primária internacional
    /// aceita — sempre com ClubTier=5 (só cogitada saindo de um clube grande pra
    /// outro). Aceita sempre a proposta 0 (a primária) pra maximizar a chance de pegar
    /// uma internacional quando ela é gerada.</summary>
    [Fact]
    public void InternationalTransfer_ChangesClubCountry_ButNeverNationality_AndTakesEffectNextSeason()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        bool sawInternational = false;

        foreach (var country in new[] { "Brasil", "Alemanha", "Inglaterra", "Espanha", "Argentina" })
        {
            for (ulong seed = 1; seed <= 100 && !sawInternational; seed++)
            {
                var recipe = BaseRecipe(seed, country) with
                {
                    ContractChoices = Enumerable.Repeat(0, 30).ToArray()
                };
                var result = sim.SimulateCareer(recipe);
                var timeline = result.Timeline;

                for (int i = 0; i < timeline.Count; i++)
                {
                    var s = timeline[i];
                    if (s.ClubCountry != country)
                    {
                        sawInternational = true;
                        Assert.Equal(5, s.ClubTier);
                        // A temporada ANTERIOR (se existir) ainda tinha que estar no
                        // país de origem — a mudança leva 1 temporada pra valer, mesmo
                        // padrão de qualquer outra troca de clube no motor.
                        if (i > 0) Assert.Equal(country, timeline[i - 1].ClubCountry);
                        break;
                    }
                }
            }
        }

        Assert.True(sawInternational, "nenhuma transferência internacional observada nas seeds/países testados");
    }

    /// <summary>Modo "jogo a jogo": detalhar as partidas usa um RNG DERIVADO, então
    /// ligar/desligar não pode mudar NENHUM outro número da carreira — é essa a
    /// garantia que deixa o Monte Carlo rodar sem partidas e a API rodar com elas, sem
    /// as duas divergirem (e sem precisar recalibrar o placar).</summary>
    [Fact]
    public void IncludeMatches_NeverChangesAnythingElseInTheCareer()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        foreach (var country in new[] { "Brasil", "Alemanha", "Espanha" })
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var recipe = BaseRecipe(seed, country) with { ContractChoices = Enumerable.Repeat(0, 30).ToArray() };
                var without = sim.SimulateCareer(recipe, includeMatches: false);
                var with = sim.SimulateCareer(recipe, includeMatches: true);

                Assert.Equal(without.Timeline.Count, with.Timeline.Count);
                for (int i = 0; i < without.Timeline.Count; i++)
                {
                    var a = without.Timeline[i];
                    var b = with.Timeline[i];
                    Assert.Equal(a.Age, b.Age);
                    Assert.Equal(a.Overall, b.Overall);
                    Assert.Equal(a.ClubTier, b.ClubTier);
                    Assert.Equal(a.ClubName, b.ClubName);
                    Assert.Equal(a.ClubCountry, b.ClubCountry);
                    Assert.Equal(a.Apps, b.Apps);
                    Assert.Equal(a.Goals, b.Goals);
                    Assert.Equal(a.Assists, b.Assists);
                    Assert.Equal(a.LeaguePosition, b.LeaguePosition);
                    Assert.Equal(a.Titles, b.Titles);
                    Assert.Empty(a.Matches);
                }
                Assert.Equal(without.PeakOverall, with.PeakOverall);
                Assert.Equal(without.TotalGoals, with.TotalGoals);
            }
        }
    }

    /// <summary>As partidas detalhadas precisam BATER com a tabela: mesma quantidade de
    /// jogos que o turno-returno da divisão, e os pontos que o jogador soma nelas têm
    /// que ser exatamente os pontos da linha dele na tabela — senão placar e
    /// classificação se contradizem na tela.</summary>
    [Fact]
    public void Matches_AreConsistentWithTheLeagueTable()
    {
        var sim = new CareerSimulator(Rules, Clubs);
        int checkedSeasons = 0;

        foreach (var country in new[] { "Brasil", "Inglaterra", "Itália" })
        {
            for (ulong seed = 1; seed <= 15; seed++)
            {
                var result = sim.SimulateCareer(BaseRecipe(seed, country), includeMatches: true);
                foreach (var s in result.Timeline)
                {
                    if (s.LeagueTable.Count == 0) continue;
                    checkedSeasons++;

                    // Turno e returno contra cada rival.
                    Assert.Equal((s.LeagueTable.Count - 1) * 2, s.Matches.Count);

                    // Pontos somados nas partidas == pontos na tabela.
                    int pts = s.Matches.Sum(m => m.GoalsFor > m.GoalsAgainst ? 3 : m.GoalsFor == m.GoalsAgainst ? 1 : 0);
                    var ownRow = s.LeagueTable.Single(r => r.IsPlayerClub);
                    Assert.Equal(ownRow.Points, pts);

                    // Metade em casa, metade fora; nunca enfrenta o próprio clube.
                    Assert.Equal(s.Matches.Count / 2, s.Matches.Count(m => m.Home));
                    Assert.DoesNotContain(s.Matches, m => m.Opponent == s.ClubName);

                    // Gols do jogador nunca passam do total da temporada nem do placar.
                    Assert.True(s.Matches.Sum(m => m.PlayerGoals) <= s.Goals);
                    Assert.All(s.Matches, m => Assert.True(m.PlayerGoals <= m.GoalsFor));
                    Assert.All(s.Matches, m => Assert.True(m.Played || (m.PlayerGoals == 0 && m.Rating == 0)));
                }
            }
        }

        Assert.True(checkedSeasons > 100, $"amostra pequena demais ({checkedSeasons} temporadas)");
    }
}
