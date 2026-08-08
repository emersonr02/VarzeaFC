using Varzea.Engine.Model;
using Varzea.Engine.Rng;
using Varzea.Engine.Ruleset;
using Varzea.Engine.Scoring;
using Varzea.Engine.Simulation;

namespace Varzea.MonteCarlo;

public static class Program
{
    public static void Main(string[] args)
    {
        int n = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 10_000;
        string balancePath = args.Length > 1 ? args[1] : "balance.json";
        string clubsPath = Path.Combine(Path.GetDirectoryName(balancePath) ?? ".", "clubs.json");

        var rules = GameRuleset.LoadFromFile(balancePath);
        var clubs = ClubDirectory.LoadFromFile(clubsPath);
        var sim = new CareerSimulator(rules, clubs);

        Console.WriteLine($"Ruleset v{rules.Version} · rodando {n:N0} carreiras...\n");

        var sample = new List<CareerResult>(n);
        var countries = rules.Countries.Keys.ToList();
        var positions = Enum.GetValues<Pos>();

        for (int i = 0; i < n; i++)
        {
            ulong seed = (ulong)i * 6364136223846793005UL + 1442695040888963407UL;
            var pick = Pcg32.Derive(seed, "policy");

            var picks = new int[8];
            for (int r = 0; r < 8; r++) picks[r] = pick.NextInt(0, 2);

            var recipe = new CareerRecipe(
                Seed: seed,
                Country: countries[pick.NextInt(0, countries.Count - 1)],
                DraftPicks: picks,
                Position: positions[pick.NextInt(0, positions.Length - 1)],
                TransferChoices: Enumerable.Range(0, 12).Select(_ => pick.Chance(0.8)).ToArray(),
                RulesetVersion: rules.Version,
                // Roadmap pós-§9, "propostas de mais clubes": toda proposta (dentro ou
                // fora do ciclo de contrato) agora usa ContractChoices, não mais
                // TransferChoices — sem isso, a amostra recusaria 100% das propostas
                // (índice sempre fora dos limites de um array vazio), o que já derrubou
                // a progressão de tier inteira uma vez (ContinentalPrimary foi a 0%).
                ContractChoices: Enumerable.Range(0, 20).Select(_ => pick.Chance(0.8) ? 0 : -1).ToArray()
            );

            sample.Add(sim.SimulateCareer(recipe));
        }

        var weights = RarityCalibrator.Calibrate(sample, rules.Version);
        var leaguePrestige = rules.Countries.ToDictionary(kv => kv.Key, kv => kv.Value.LeaguePrestige);
        var scorer = new CareerScorer(weights, leaguePrestige);
        var scored = sample.Select(c => (Career: c, Score: scorer.Score(c))).ToList();

        PrintRarity(weights);
        PrintCriterion1(scored);
        PrintCriterion2(scored);
        PrintCriterion3(scored);

        File.WriteAllText("rarity-weights.json", weights.ToJson());
        Console.WriteLine("\n→ pesos gravados em rarity-weights.json");
    }

    private static void PrintRarity(RarityWeights w)
    {
        Console.WriteLine("── PESOS DERIVADOS (não escritos à mão) ──");
        Console.WriteLine($"{"TÍTULO",-24}{"FREQUÊNCIA",14}{"PESO",10}");
        foreach (var kv in w.Weight.OrderByDescending(x => x.Value))
            Console.WriteLine($"{kv.Key,-24}{w.Frequency[kv.Key],13:P2}{kv.Value,10:F1}");
    }

    /// <summary>Critério 1: distribuição espalhada. Topo empatado mata o ranking.</summary>
    private static void PrintCriterion1(List<(CareerResult Career, ScoreBreakdown Score)> scored)
    {
        Console.WriteLine("\n── CRITÉRIO 1: distribuição do score ──");
        var totals = scored.Select(s => s.Score.Total).OrderBy(x => x).ToList();
        double p50 = totals[totals.Count / 2];
        double p99 = totals[(int)(totals.Count * 0.99)];
        double max = totals[^1];

        Console.WriteLine($"mediana={p50:F0}  p99={p99:F0}  máx={max:F0}");

        // quantas carreiras distintas ocupam o top 1%? se poucas, houve empate.
        // Arredondar a 2 casas (não a inteiro) — senão o próprio teste achata o score.
        var top1 = scored.OrderByDescending(s => s.Score.Total)
                         .Take(Math.Max(1, scored.Count / 100)).ToList();
        int distinct = top1.Select(s => Math.Round(s.Score.Total, 2)).Distinct().Count();
        double ratio = (double)distinct / top1.Count;
        double hi = top1[0].Score.Total, lo = top1[^1].Score.Total;
        double spread = hi > 0 ? (hi - lo) / hi : 0;
        Console.WriteLine($"top 1%: {distinct}/{top1.Count} scores distintos ({ratio:P0}), " +
                          $"dispersão {spread:P0} " +
                          (ratio > 0.95 && spread > 0.15 ? "✔ OK" : "✘ ACHATADO — desempate viraria aleatório"));
    }

    /// <summary>Critério 2: toda posição precisa conseguir chegar ao top 10%.</summary>
    private static void PrintCriterion2(List<(CareerResult Career, ScoreBreakdown Score)> scored)
    {
        Console.WriteLine("\n── CRITÉRIO 2: todas as posições alcançam o top 10% ──");
        var cut = scored.OrderByDescending(s => s.Score.Total)
                        .Take(Math.Max(1, scored.Count / 10)).ToList();

        bool ok = true;
        foreach (var pos in Enum.GetValues<Pos>())
        {
            int inTop = cut.Count(s => s.Career.Position == pos);
            int total = scored.Count(s => s.Career.Position == pos);
            double rate = total == 0 ? 0 : (double)inTop / total;
            if (rate < 0.02) ok = false;
            Console.WriteLine($"  {pos,-4} {inTop,5} no top 10%  ({rate:P1}) {(rate < 0.02 ? "✘" : "")}");
        }
        Console.WriteLine(ok ? "✔ OK" : "✘ posição sub-representada — revisar normalização por posição");
    }

    /// <summary>
    /// Critério 3: nenhum bloco pode dominar a carreira mediana.
    /// Medir no top 10%, não na média global: prêmios raros zeram na maioria
    /// das carreiras e distorcem a média geral.
    /// </summary>
    private static void PrintCriterion3(List<(CareerResult Career, ScoreBreakdown Score)> scored)
    {
        Console.WriteLine("\n── CRITÉRIO 3: contribuição por bloco ──");
        var valid = scored.Where(s => s.Score.Total > 0).ToList();
        var top10 = scored.OrderByDescending(s => s.Score.Total)
                          .Take(Math.Max(1, scored.Count / 10)).ToList();

        Console.WriteLine($"  {"BLOCO",-10}{"GERAL",8}{"TOP 10%",10}   alvo");
        void Row(string label, Func<ScoreBreakdown, double> block, string alvo)
        {
            double geral = valid.Average(s => block(s.Score) / s.Score.Total);
            double top = top10.Average(s => block(s.Score) / s.Score.Total);
            Console.WriteLine($"  {label,-10}{geral,7:P1}{top,9:P1}   {alvo}");
        }
        Row("títulos", s => s.Titles, "~60%");
        Row("prêmios", s => s.Awards, "~25%");
        Row("produção", s => s.Production, "~10%");
        Row("pico", s => s.Peak, "~5%");
        Console.WriteLine("  (prêmios raros zeram na maioria das carreiras — o TOP 10% é a leitura honesta)");
    }
}
