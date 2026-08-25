using System.Text.Json;
using Varzea.Engine.Model;

namespace Varzea.Engine.Scoring;

/// <summary>
/// Tabela de pesos derivada de Monte Carlo. NÃO é escrita à mão:
/// peso = log(1/frequência), normalizado. Se um rebalanceamento tornar a Bola de Ouro
/// mais fácil, o peso dela cai sozinho no próximo recálculo.
/// </summary>
public sealed class RarityWeights
{
    public string RulesetVersion { get; set; } = "";
    public int SampleSize { get; set; }
    public Dictionary<string, double> Frequency { get; set; } = new();
    public Dictionary<string, double> Weight { get; set; } = new();
    /// <summary>Percentis de produção por posição, para normalizar zagueiro vs atacante.</summary>
    public Dictionary<string, double> ProductionP50 { get; set; } = new();
    public Dictionary<string, double> ProductionP95 { get; set; } = new();

    public double WeightOf(TitleKind k) => Weight.TryGetValue(k.ToString(), out var v) ? v : 0;

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    public static RarityWeights FromJson(string json) =>
        JsonSerializer.Deserialize<RarityWeights>(json) ?? throw new InvalidOperationException("json inválido");
}

public static class RarityCalibrator
{
    /// <summary>
    /// Compressão logarítmica. Sem isso, um evento de 0,4% valeria 250x um de 100% —
    /// uma Copa do Mundo sozinha passaria a carreira inteira de outra pessoa,
    /// e o ranking viraria loteria de cauda longa.
    /// </summary>
    private static double Compress(double frequency)
    {
        double f = Math.Clamp(frequency, 0.0005, 1.0);
        return Math.Log(1.0 / f);
    }

    public static RarityWeights Calibrate(IReadOnlyList<CareerResult> sample, string rulesetVersion)
    {
        var kinds = Enum.GetValues<TitleKind>();
        var w = new RarityWeights { RulesetVersion = rulesetVersion, SampleSize = sample.Count };

        foreach (var k in kinds)
        {
            int careersWith = sample.Count(c => c.CountOf(k) > 0);
            double freq = sample.Count == 0 ? 0 : (double)careersWith / sample.Count;
            w.Frequency[k.ToString()] = freq;
            w.Weight[k.ToString()] = Compress(freq);
        }

        // normaliza para a liga menor valer 10 pontos — só uma referência de escala,
        // não implica mais que ela seja "o título mais comum" (não é, ver clamp abaixo).
        double baseW = w.Weight[TitleKind.LeagueMinor.ToString()];
        if (baseW > 0)
        {
            double scale = 10.0 / baseW;
            foreach (var key in w.Weight.Keys.ToList())
                w.Weight[key] = Math.Round(w.Weight[key] * scale, 2);
        }

        // Raridade pura (log(1/frequência)) quebra quando o GATE de um título é mais
        // estreito que o do título "pai" que ele imita — o título fica raro por causa
        // do gate (poucos jogadores nem competem), não porque seja mais difícil de
        // ganhar uma vez lá dentro. Rei da América só existe pra ligas sul-americanas
        // (universo pequeno) e saía mais valioso que Bola de Ouro; Liga Menor tornava-se
        // mais valiosa que Liga Top-5 pelo mesmo motivo. Trava aqui uma hierarquia de
        // prestígio conhecida (não derivada): cada item nunca pode valer mais que o seu
        // "pai" na cadeia. Ordem importa — de cima pra baixo, pra propagar o teto.
        ClampAtMost(w, TitleKind.LeagueMid, TitleKind.LeagueTop5);
        ClampAtMost(w, TitleKind.LeagueMinor, TitleKind.LeagueMid);
        ClampAtMost(w, TitleKind.ContinentalSecondary, TitleKind.ContinentalPrimary);
        ClampAtMost(w, TitleKind.KingOfAmerica, TitleKind.BallonDOr);
        ClampAtMost(w, TitleKind.SouthAmericanTeamOfTheYear, TitleKind.TeamOfTheYear);

        // percentis de produção POR POSIÇÃO — senão zagueiro nunca ranqueia
        foreach (var grp in sample.GroupBy(c => c.Position))
        {
            var vals = grp.Select(RawProduction).OrderBy(x => x).ToList();
            if (vals.Count == 0) continue;
            w.ProductionP50[grp.Key.ToString()] = Percentile(vals, 0.50);
            w.ProductionP95[grp.Key.ToString()] = Percentile(vals, 0.95);
        }
        return w;
    }

    /// <summary>Garante que o peso de <paramref name="child"/> nunca supere o de
    /// <paramref name="parent"/> — usado pra corrigir a inversão de hierarquia que a
    /// raridade pura produz entre título e seu análogo/subordinado.</summary>
    private static void ClampAtMost(RarityWeights w, TitleKind child, TitleKind parent)
    {
        string c = child.ToString(), p = parent.ToString();
        if (w.Weight.TryGetValue(c, out var cv) && w.Weight.TryGetValue(p, out var pv) && cv > pv)
            w.Weight[c] = pv;
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        int idx = Math.Clamp((int)Math.Round(p * (sorted.Count - 1)), 0, sorted.Count - 1);
        return sorted[idx];
    }

    /// <summary>
    /// Produção bruta com retorno decrescente (raiz): o gol 300 não pode valer
    /// o mesmo que o gol 30, senão o ranking vira "quem jogou mais temporadas".
    /// </summary>
    public static double RawProduction(CareerResult c) =>
        Math.Sqrt(c.TotalGoals) * 3.0
        + Math.Sqrt(c.TotalAssists) * 2.2
        + Math.Sqrt(c.TotalTackles) * 0.9
        + Math.Sqrt(c.TotalCleanSheets) * 1.6;
}

public sealed class CareerScorer
{
    private readonly RarityWeights _w;
    private readonly IReadOnlyDictionary<string, double> _leaguePrestige;

    // teto por categoria: impede empilhar 12 ligas médias e passar quem ganhou 3 Champions
    // valores calibrados em Monte Carlo (10k carreiras) para bater 60/25/10/5.
    // AwardScale/AwardCap recalibrados no Roadmap §9 Bloco 1: dois prêmios individuais
    // novos (Rei da América, Equipe do Ano da América) empilham na mesma carreira de
    // elite sul-americana junto com BallonDOr/TeamOfTheYear — sem essa redução o bloco
    // de prêmios passava a dominar o top 10% (~50% medido, alvo ~25%).
    private const double TitleScale = 3.9;
    private const double AwardScale = 5.1;
    private const double TitleCap = 420;
    private const double AwardCap = 220;
    private const double ProductionCap = 28;
    private const double PeakCap = 7.6;

    /// <param name="leaguePrestige">País → multiplicador autoral (GameRuleset.Countries[x].LeaguePrestige),
    /// aplicado só aos títulos domésticos. Ver HANDOFF §9 Bloco 1.</param>
    public CareerScorer(RarityWeights w, IReadOnlyDictionary<string, double>? leaguePrestige = null)
    {
        _w = w;
        _leaguePrestige = leaguePrestige ?? new Dictionary<string, double>();
    }

    public ScoreBreakdown Score(CareerResult c)
    {
        double titles = 0, awards = 0;
        double prestige = _leaguePrestige.GetValueOrDefault(c.Recipe.Country, 1.0);

        foreach (var kv in c.TitleCounts)
        {
            double v = _w.WeightOf(kv.Key) * kv.Value;
            if (IsIndividualAward(kv.Key)) awards += v;
            else if (IsDomesticTitle(kv.Key)) titles += v * prestige;
            else titles += v;
        }

        titles = Math.Min(titles * TitleScale, TitleCap);
        awards = Math.Min(awards * AwardScale, AwardCap);

        // produção normalizada pelo percentil da PRÓPRIA posição
        string pos = c.Position.ToString();
        double p50 = _w.ProductionP50.GetValueOrDefault(pos, 1);
        double p95 = _w.ProductionP95.GetValueOrDefault(pos, 2);
        double raw = RarityCalibrator.RawProduction(c);
        double norm = p95 - p50 > 0.0001 ? (raw - p50) / (p95 - p50) : 0;
        // +1 e /3 mantêm a produção sempre não-negativa: valores negativos puxavam o
        // total para baixo e explodiam as razões do critério 3
        double production = Math.Clamp(norm + 1, 0, 3) / 3.0 * ProductionCap;

        double peak = Math.Clamp((c.PeakOverall - 60) / 39.0, 0, 1) * PeakCap;

        return new ScoreBreakdown
        {
            Titles = Math.Round(titles, 2),
            Awards = Math.Round(awards, 2),
            Production = Math.Round(production, 2),
            Peak = Math.Round(peak, 2)
        };
    }

    private static bool IsIndividualAward(TitleKind k) =>
        k is TitleKind.BallonDOr or TitleKind.TeamOfTheYear
          or TitleKind.KingOfAmerica or TitleKind.SouthAmericanTeamOfTheYear;

    /// <summary>Títulos que vêm da liga doméstica do jogador — únicos afetados pelo
    /// multiplicador de prestígio de país (continental/seleção já são globais por
    /// natureza, e os prêmios individuais já são gated por LeagueGrade no motor).</summary>
    private static bool IsDomesticTitle(TitleKind k) =>
        k is TitleKind.LeagueTop5 or TitleKind.LeagueMid or TitleKind.LeagueMinor or TitleKind.DomesticCup;
}

public sealed class ScoreBreakdown
{
    public double Titles { get; init; }
    public double Awards { get; init; }
    public double Production { get; init; }
    public double Peak { get; init; }
    public double Total => Math.Round(Titles + Awards + Production + Peak, 2);
}
