using Varzea.Engine.Model;
using Varzea.Engine.Scoring;
using Xunit;

namespace Varzea.Engine.Tests;

/// <summary>
/// Peso puro por raridade (log(1/frequência)) inverte a hierarquia sempre que o GATE de
/// um título é mais estreito que o do "pai" que ele imita — o título fica raro porque
/// poucos jogadores competem por ele, não porque seja mais difícil de ganhar uma vez lá
/// dentro. Bug real relatado pelo usuário: Rei da América (só ligas sul-americanas, gate
/// estreito) saía valendo mais que Bola de Ouro; Liga Menor saía valendo mais que Liga
/// Top-5. Este teste prova que RarityCalibrator.Calibrate trava essa hierarquia mesmo
/// quando a amostra é construída pra favorecer exatamente a inversão.
/// </summary>
public class RarityCalibratorTests
{
    private static CareerResult MakeCareer(params TitleKind[] titles)
    {
        var c = new CareerResult { Position = Pos.ST, RoleName = "Test", Potential = 80, PeakOverall = 80, Seasons = 10 };
        foreach (var t in titles) c.AddTitle(t);
        return c;
    }

    [Fact]
    public void HierarchyClamps_HoldEvenWhenSampleFavorsInversion()
    {
        var sample = new List<CareerResult>();

        // Rei da América: gate estreito, MUITO raro na amostra (poucas carreiras).
        for (int i = 0; i < 5; i++) sample.Add(MakeCareer(TitleKind.KingOfAmerica));
        // Bola de Ouro: gate global, mais comum na amostra — favorece a inversão.
        for (int i = 0; i < 50; i++) sample.Add(MakeCareer(TitleKind.BallonDOr));

        // Liga Menor: rara na amostra.
        for (int i = 0; i < 5; i++) sample.Add(MakeCareer(TitleKind.LeagueMinor));
        // Liga Top-5: comum na amostra — favorece a inversão (Top-5 "mais fácil" que Menor).
        for (int i = 0; i < 80; i++) sample.Add(MakeCareer(TitleKind.LeagueTop5));
        for (int i = 0; i < 40; i++) sample.Add(MakeCareer(TitleKind.LeagueMid));

        // Continental: mesma lógica (Primary mais comum que Secondary na amostra).
        for (int i = 0; i < 60; i++) sample.Add(MakeCareer(TitleKind.ContinentalPrimary));
        for (int i = 0; i < 20; i++) sample.Add(MakeCareer(TitleKind.ContinentalSecondary));

        // Equipe do Ano da América: rara; Equipe do Ano global, mais comum.
        for (int i = 0; i < 8; i++) sample.Add(MakeCareer(TitleKind.SouthAmericanTeamOfTheYear));
        for (int i = 0; i < 45; i++) sample.Add(MakeCareer(TitleKind.TeamOfTheYear));

        // Preenche o resto da amostra sem títulos, pra frequência fazer sentido.
        for (int i = 0; i < 200; i++) sample.Add(MakeCareer());

        var w = RarityCalibrator.Calibrate(sample, "test");

        Assert.True(w.WeightOf(TitleKind.KingOfAmerica) <= w.WeightOf(TitleKind.BallonDOr),
            "Rei da América nunca pode valer mais que Bola de Ouro");
        Assert.True(w.WeightOf(TitleKind.LeagueMinor) <= w.WeightOf(TitleKind.LeagueMid),
            "Liga Menor nunca pode valer mais que Liga Média");
        Assert.True(w.WeightOf(TitleKind.LeagueMid) <= w.WeightOf(TitleKind.LeagueTop5),
            "Liga Média nunca pode valer mais que Liga Top-5");
        Assert.True(w.WeightOf(TitleKind.ContinentalSecondary) <= w.WeightOf(TitleKind.ContinentalPrimary),
            "Continental secundária nunca pode valer mais que a primária");
        Assert.True(w.WeightOf(TitleKind.SouthAmericanTeamOfTheYear) <= w.WeightOf(TitleKind.TeamOfTheYear),
            "Equipe do Ano da América nunca pode valer mais que a Equipe do Ano global");
    }
}
