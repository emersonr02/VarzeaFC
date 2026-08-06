using Varzea.Engine.Model;
using Varzea.Engine.Rng;
using Varzea.Engine.Ruleset;

namespace Varzea.Engine.Simulation;

/// <summary>
/// Motor puro: sem I/O, sem relógio, sem estado global.
/// SimulateCareer(recipe) é uma função — mesma entrada, mesma saída, sempre.
/// </summary>
public sealed class CareerSimulator
{
    private readonly GameRuleset _rules;
    private readonly List<Legend> _legends;

    public CareerSimulator(GameRuleset rules)
    {
        _rules = rules;
        _legends = rules.BuildLegends();
    }

    // ---------- DRAFT ----------

    /// <summary>
    /// Reconstrói as 8 rodadas do draft. Cada rodada oferece 3 lendas para um atributo;
    /// a lenda escolhida sai do pool (é o "roubo").
    /// </summary>
    public int[] ResolveDraft(ulong seed, int[] picks)
    {
        if (picks.Length != 8) throw new ArgumentException("draft precisa de 8 escolhas");

        var (rng, pool) = ReplayDraft(seed, Array.Empty<int>());
        var attrs = new int[8];
        for (int round = 0; round < 8; round++)
        {
            var candidates = DrawCandidates(rng, pool);
            int choice = Math.Clamp(picks[round], 0, candidates.Count - 1);
            var chosen = candidates[choice];
            attrs[round] = chosen.Get((Attr)round);
            pool.Remove(chosen);
        }
        return attrs;
    }

    /// <summary>
    /// As 3 lendas da PRÓXIMA rodada (índice = picksSoFar.Length), dado o que já foi
    /// escolhido. Existe porque o pool depende das escolhas reais: a lenda tirada sai
    /// do pool, as outras duas voltam — então a API não pode revelar as 8 rodadas de
    /// uma vez, só uma rodada por chamada (ver HANDOFF §7.5).
    /// </summary>
    public IReadOnlyList<Legend> PreviewNextDraftRound(ulong seed, int[] picksSoFar)
    {
        if (picksSoFar.Length >= 8) throw new ArgumentException("draft já tem as 8 escolhas");
        var (rng, pool) = ReplayDraft(seed, picksSoFar);
        return DrawCandidates(rng, pool);
    }

    /// <summary>Rebobina o RNG e o pool até o ponto imediatamente após as escolhas dadas.</summary>
    private (Pcg32 Rng, List<Legend> Pool) ReplayDraft(ulong seed, int[] picksSoFar)
    {
        var rng = Pcg32.Derive(seed, "draft");
        var pool = new List<Legend>(_legends);
        foreach (var pick in picksSoFar)
        {
            var candidates = DrawCandidates(rng, pool);
            int choice = Math.Clamp(pick, 0, candidates.Count - 1);
            pool.Remove(candidates[choice]);
        }
        return (rng, pool);
    }

    /// <summary>Candidatos por rodada de draft. 2, não 3 (Roadmap §9 Bloco 1) — decisão
    /// de produto, escolha mais tensa e rápida.</summary>
    private const int DraftCandidatesPerRound = 2;

    private static List<Legend> DrawCandidates(Pcg32 rng, List<Legend> pool)
    {
        var working = new List<Legend>(pool);
        var candidates = new List<Legend>();
        for (int i = 0; i < DraftCandidatesPerRound && working.Count > 0; i++)
        {
            int idx = rng.NextInt(0, working.Count - 1);
            candidates.Add(working[idx]);
            working.RemoveAt(idx);
        }
        return candidates;
    }

    /// <summary>
    /// Over ponderado por posição, mesma base do FIFA: soma dos atributos
    /// multiplicados por pesos que somam 1.0.
    /// </summary>
    public int OverallFor(int[] attrs, Pos pos)
    {
        var w = _rules.WeightsFor(pos);
        double sum = 0;
        for (int i = 0; i < 8; i++) sum += attrs[i] * w[i];
        return (int)Math.Round(sum);
    }

    /// <summary>Primeira role cujo atributo bate o mínimo vence (ordem do JSON = prioridade).</summary>
    public RoleDef ResolveRole(int[] attrs, Pos pos)
    {
        foreach (var role in _rules.RolesFor(pos))
            if (attrs[(int)role.AttrEnum] >= role.Min)
                return role;
        return new RoleDef { Name = "Indefinido", Min = 0 };
    }

    // ---------- CARREIRA ----------

    /// <summary>Roda a carreira inteira. Exige TransferChoices já completo — usado pelo
    /// Monte Carlo e por /careers/save (que já recebe a receita fechada).</summary>
    public CareerResult SimulateCareer(CareerRecipe recipe) => RunCareer(recipe, interactive: false).Result;

    /// <summary>
    /// Roda a carreira até a próxima oferta de transferência sem decisão em
    /// recipe.TransferChoices, ou até o fim. Não precisa serializar RNG entre chamadas:
    /// como a carreira inteira custa microssegundos, cada chamada re-simula do zero com
    /// a receita um pouco mais completa (uma decisão a mais) — determinístico por
    /// construção, e mantém a API sem sessão de servidor (ver Varzea.Api.CareerState).
    /// </summary>
    public CareerProgress AdvanceCareer(CareerRecipe recipe) => RunCareer(recipe, interactive: true);

    private CareerProgress RunCareer(CareerRecipe recipe, bool interactive)
    {
        var attrs = ResolveDraft(recipe.Seed, recipe.DraftPicks);
        var pos = recipe.Position;
        int potential = OverallFor(attrs, pos);
        var role = ResolveRole(attrs, pos);
        var factors = _rules.FactorsFor(pos);
        var country = _rules.Countries[recipe.Country];
        var curve = _rules.Curve;

        var rng = Pcg32.Derive(recipe.Seed, "career");

        int retireAge = rng.NextInt(curve.MinRetireAge, curve.MaxRetireAge);
        int peakAge = rng.NextInt(curve.MinPeakAge, curve.MaxPeakAge);
        int gap = rng.NextInt(curve.MinPotentialGap, curve.MaxPotentialGap);
        int overall = Math.Clamp(potential - gap, 40, Math.Max(41, potential - 4));

        int tier = StartingTier(potential, rng);
        int transferIdx = 0;

        var result = new CareerResult
        {
            Recipe = recipe,
            Position = pos,
            RoleName = role.Name,
            Potential = potential
        };

        int peak = overall, goals = 0, assists = 0, tackles = 0, cs = 0, caps = 0, seasons = 0;

        for (int age = curve.StartAge; age <= retireAge; age++)
        {
            // curva de evolução: cresce em direção ao potencial, estabiliza, decai
            if (age < peakAge)
                overall += Math.Max(1, (int)Math.Round((potential - overall) * curve.GrowthRate))
                           + (rng.Chance(0.30) ? 1 : 0);
            else if (age <= peakAge + 3)
                overall += rng.NextInt(-1, 2);
            else
                overall -= rng.NextInt(1, 4);

            overall = Math.Clamp(overall, 35, 99);
            peak = Math.Max(peak, overall);

            int target = _rules.Tiers.First(t => t.Tier == tier).Target;
            double perf = overall - target + (rng.NextDouble() * 16 - 8);

            var injury = RollInjury(rng, age);
            if (injury == InjurySeverity.CareerEnding)
            {
                result.Timeline.Add(new SeasonResult
                {
                    Age = age, Overall = overall, ClubTier = tier,
                    Injury = injury, LeaguePosition = 20
                });
                break;
            }

            int apps = Math.Clamp(rng.NextInt(24, 36) - InjuryCost(injury, rng), 4, 38);

            int sg = Output(rng, overall, factors.Attack, 30, perf, apps, role.GoalsMod);
            int sa = Output(rng, overall, factors.Passing, 22, perf, apps, role.AssistsMod);
            int st = Output(rng, overall, factors.Defending, 95, perf, apps, role.DefenseMod);
            int scs = (int)Math.Round(apps * Math.Clamp(0.10 + factors.Defending * (0.22 + perf / 160.0), 0, 0.60));

            var season = new SeasonResult
            {
                Age = age, Overall = overall, ClubTier = tier, Apps = apps,
                Goals = sg, Assists = sa, Tackles = st, CleanSheets = scs,
                Injury = injury,
                LeaguePosition = Math.Clamp((int)Math.Round(11 - perf / 2.0 + (rng.NextDouble() * 6 - 3)), 1, 20)
            };

            // --- LIGA ---
            if (season.LeaguePosition == 1)
            {
                var kind = country.LeagueGrade switch
                {
                    3 => TitleKind.LeagueTop5,
                    2 => TitleKind.LeagueMid,
                    _ => TitleKind.LeagueMinor
                };
                season.Titles.Add(kind);
                result.AddTitle(kind);
            }

            // --- COPA NACIONAL ---
            if (rng.Chance(Math.Clamp(0.10 + perf / 260.0, 0.03, 0.30)) && rng.Chance(0.40))
            {
                season.Titles.Add(TitleKind.DomesticCup);
                result.AddTitle(TitleKind.DomesticCup);
            }

            // --- CONTINENTAL ---
            double contChance = _rules.Tiers.First(t => t.Tier == tier).ContinentalChance;
            if (contChance > 0 && rng.Chance(contChance))
            {
                bool primary = tier >= 4;
                if (rng.Chance(Math.Clamp(0.35 + perf / 200.0, 0.10, 0.65)))
                {
                    var k = primary ? TitleKind.ContinentalPrimary : TitleKind.ContinentalSecondary;
                    season.Titles.Add(k);
                    result.AddTitle(k);
                }
            }

            // --- SELEÇÃO / COPA DO MUNDO (ciclo de 4 anos) ---
            int seasonCaps = 0;
            if (overall >= 76 && age >= 18 && age <= 35 && rng.Chance(0.35))
                seasonCaps += rng.NextInt(1, 8);

            bool wcYear = (age - curve.StartAge) % 4 == 2;
            if (wcYear && overall >= 74 && age >= 18)
            {
                double callUp = Math.Clamp((overall - 70) / 60.0 + country.Strength / 40.0, 0.05, 0.60);
                if (rng.Chance(callUp))
                {
                    seasonCaps += rng.NextInt(3, 7);
                    double winChance = Math.Clamp(
                        0.05 + (overall - 78) / 260.0 + (country.Strength - 5) / 90.0, 0.005, 0.18);
                    if (rng.Chance(winChance))
                    {
                        season.Titles.Add(TitleKind.WorldCup);
                        result.AddTitle(TitleKind.WorldCup);
                    }
                }
            }

            // --- PRÊMIOS INDIVIDUAIS ---
            // Equipe do Ano: melhor de cada posição. A chance NÃO depende de gols nem
            // de desarmes, só de quão acima do nível mundial o jogador está — por isso
            // goleiro e zagueiro competem em pé de igualdade com atacante.
            //
            // Gated por força da liga (Roadmap §9 Bloco 1): fora do top-5 europeu a
            // chance é multiplicada por um fator << 1. Como a Bola de Ouro só concorre
            // quem entrou na Equipe do Ano, o efeito composto já deixa a Bola de Ouro
            // "quase nunca" pra ligas de grade 1 sem precisar de outro gate separado.
            double leagueGradeAwardFactor = country.LeagueGrade switch
            {
                3 => 1.00,
                2 => 0.35,
                _ => 0.10
            };

            bool toty = false;
            double totyChance = Math.Clamp(((overall - 84) / 38.0
                + (season.LeaguePosition == 1 ? 0.06 : 0)
                + (season.Titles.Contains(TitleKind.ContinentalPrimary) ? 0.05 : 0)
                + (season.Titles.Contains(TitleKind.WorldCup) ? 0.04 : 0)) * leagueGradeAwardFactor, 0, 0.40);
            if (rng.Chance(totyChance))
            {
                toty = true;
                season.Titles.Add(TitleKind.TeamOfTheYear);
                result.AddTitle(TitleKind.TeamOfTheYear);
            }

            // Bola de Ouro: só concorre quem entrou na Equipe do Ano.
            // Sem esse gate o prêmio volta a ser refém de quem faz gol.
            if (toty)
            {
                double wc = Math.Clamp(((overall - 88) / 50.0
                    + (season.LeaguePosition == 1 ? 0.10 : 0)
                    + (season.Titles.Contains(TitleKind.ContinentalPrimary) ? 0.14 : 0)
                    + (season.Titles.Contains(TitleKind.WorldCup) ? 0.16 : 0)
                    + role.TitleMod) * leagueGradeAwardFactor, 0.00, 0.42);
                if (rng.Chance(wc))
                {
                    season.Titles.Add(TitleKind.BallonDOr);
                    result.AddTitle(TitleKind.BallonDOr);
                }
            }

            // --- EQUIPE DO ANO DA AMÉRICA / REI DA AMÉRICA ---
            // Análogo aos prêmios globais acima, mas escopado a ligas southAmerican
            // (Roadmap §9 Bloco 1 — reabre a decisão travada da secção 2 sobre só dois
            // prêmios individuais). O objetivo NÃO é competir com os prêmios globais —
            // um jogador de liga fora do top-5 europeu dificilmente ganha a Equipe do
            // Ano global, mas ainda deve ter uma vitrine regional alcançável.
            if (country.SouthAmerican)
            {
                bool saToty = false;
                double saTotyChance = Math.Clamp((overall - 83) / 38.0
                    + (season.LeaguePosition == 1 ? 0.04 : 0)
                    + (season.Titles.Contains(TitleKind.ContinentalPrimary) ? 0.04 : 0), 0, 0.33);
                if (rng.Chance(saTotyChance))
                {
                    saToty = true;
                    season.Titles.Add(TitleKind.SouthAmericanTeamOfTheYear);
                    result.AddTitle(TitleKind.SouthAmericanTeamOfTheYear);
                }

                if (saToty)
                {
                    double koa = Math.Clamp((overall - 89) / 45.0
                        + (season.LeaguePosition == 1 ? 0.06 : 0)
                        + (season.Titles.Contains(TitleKind.ContinentalPrimary) ? 0.10 : 0)
                        + role.TitleMod, 0.00, 0.26);
                    if (rng.Chance(koa))
                    {
                        season.Titles.Add(TitleKind.KingOfAmerica);
                        result.AddTitle(TitleKind.KingOfAmerica);
                    }
                }
            }

            // --- TRANSFERÊNCIA (decisão do jogador, vinda da receita) ---
            bool offer = false, upgrade = false;
            if (perf > 14 && tier < 5 && rng.Chance(0.45)) { offer = true; upgrade = true; }
            else if (perf < -16 && tier > 1 && rng.Chance(0.30)) { offer = true; upgrade = false; }

            bool accepted = false;
            if (offer)
            {
                if (interactive && transferIdx >= recipe.TransferChoices.Length)
                {
                    // Sem decisão ainda pra essa oferta — pausa aqui. Esta temporada NÃO
                    // entra no Timeline (só temporadas fechadas entram); os números já
                    // calculados voltam via PendingOffer pro cliente decidir.
                    return new CareerProgress
                    {
                        Result = Finalize(result, peak, seasons, goals, assists, tackles, cs, caps),
                        PendingOffer = new PendingTransferOffer(
                            age, overall, tier, upgrade,
                            sg, sa, st, scs, season.LeaguePosition)
                    };
                }
                accepted = transferIdx < recipe.TransferChoices.Length && recipe.TransferChoices[transferIdx];
                transferIdx++;
                tier += accepted ? (upgrade ? 1 : -1) : 0;
            }

            goals += sg; assists += sa; tackles += st; cs += scs; seasons++; caps += seasonCaps;

            var finished = new SeasonResult
            {
                Age = season.Age, Overall = season.Overall, ClubTier = season.ClubTier,
                Apps = season.Apps, Goals = season.Goals, Assists = season.Assists,
                Tackles = season.Tackles, CleanSheets = season.CleanSheets,
                LeaguePosition = season.LeaguePosition, Injury = season.Injury,
                Caps = seasonCaps, InTeamOfTheYear = toty,
                HadTransferOffer = offer, AcceptedTransfer = accepted
            };
            finished.Titles.AddRange(season.Titles);
            result.Timeline.Add(finished);
        }

        return new CareerProgress { Result = Finalize(result, peak, seasons, goals, assists, tackles, cs, caps) };
    }

    private static CareerResult Finalize(CareerResult r, int peak, int seasons,
        int g, int a, int t, int cs, int caps)
    {
        var final = new CareerResult
        {
            Recipe = r.Recipe, Position = r.Position, RoleName = r.RoleName,
            Potential = r.Potential, PeakOverall = peak, Seasons = seasons,
            TotalGoals = g, TotalAssists = a, TotalTackles = t,
            TotalCleanSheets = cs, TotalCaps = caps
        };
        final.Timeline.AddRange(r.Timeline);
        foreach (var kv in r.TitleCounts) final.TitleCounts[kv.Key] = kv.Value;
        return final;
    }

    private int StartingTier(int potential, Pcg32 rng)
    {
        if (potential >= 90) return rng.Chance(0.5) ? 2 : 3;
        if (potential >= 80) return 2;
        if (potential >= 65) return rng.Chance(0.7) ? 1 : 2;
        return 1;
    }

    private static InjurySeverity RollInjury(Pcg32 rng, int age)
    {
        if (age >= 30 && rng.Chance(0.006)) return InjurySeverity.CareerEnding;
        double roll = rng.NextDouble();
        if (roll < 0.02) return InjurySeverity.Severe;
        if (roll < 0.07) return InjurySeverity.Moderate;
        if (roll < 0.17) return InjurySeverity.Minor;
        return InjurySeverity.None;
    }

    private static int InjuryCost(InjurySeverity s, Pcg32 rng) => s switch
    {
        InjurySeverity.Minor => rng.NextInt(3, 8),
        InjurySeverity.Moderate => rng.NextInt(8, 16),
        InjurySeverity.Severe => rng.NextInt(16, 26),
        _ => 0
    };

    private static int Output(Pcg32 rng, int overall, double factor, double baseline,
        double perf, int apps, double roleMod)
    {
        double baseVal = overall / 99.0 * baseline * factor;
        double v = baseVal * (1 + perf / 70.0) * (apps / 34.0)
                   * (0.70 + rng.NextDouble() * 0.60) * (1 + roleMod);
        return Math.Max(0, (int)Math.Round(v));
    }
}
