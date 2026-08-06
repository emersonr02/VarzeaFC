using System.Text.Json;
using System.Text.Json.Serialization;
using Varzea.Engine.Model;

namespace Varzea.Engine.Ruleset;

/// <summary>
/// Todo o balanceamento vive aqui, carregado de balance.json.
/// Tunar números NÃO pode exigir recompilar o motor, e cada carreira salva grava
/// a Version que usou — senão o primeiro rebalanceamento quebra todo o acervo.
/// </summary>
public sealed class GameRuleset
{
    public string Version { get; set; } = "0.0.0";
    public List<LegendDef> Legends { get; set; } = new();
    public Dictionary<string, double[]> PositionWeights { get; set; } = new();
    public Dictionary<string, OutputFactors> OutputFactors { get; set; } = new();
    public Dictionary<string, List<RoleDef>> Roles { get; set; } = new();
    public Dictionary<string, CountryDef> Countries { get; set; } = new();
    public List<TierDef> Tiers { get; set; } = new();
    public CareerCurve Curve { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static GameRuleset LoadFromFile(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<GameRuleset>(json, JsonOpts)
               ?? throw new InvalidOperationException("balance.json inválido");
    }

    public double[] WeightsFor(Pos p) => PositionWeights[p.ToString()];
    public OutputFactors FactorsFor(Pos p) => OutputFactors[p.ToString()];
    public List<RoleDef> RolesFor(Pos p) => Roles.TryGetValue(p.ToString(), out var r) ? r : new();

    public List<Legend> BuildLegends() =>
        Legends.Select(l => new Legend(l.Name, l.Ratings)).ToList();
}

public sealed class LegendDef
{
    public string Name { get; set; } = "";
    /// <summary>Ordem fixa: PAC, SHO, PAS, DRI, DEF, PHY, SKI, WEA.</summary>
    public int[] Ratings { get; set; } = new int[8];
}

public sealed class OutputFactors
{
    public double Attack { get; set; }
    public double Passing { get; set; }
    public double Defending { get; set; }
}

/// <summary>
/// Role dinâmica. A primeira regra satisfeita, na ordem do arquivo, vence —
/// por isso as "elite" vêm antes das "secondary" no JSON.
/// </summary>
public sealed class RoleDef
{
    public string Name { get; set; } = "";
    public string Attr { get; set; } = "Pac";
    public int Min { get; set; }
    public double GoalsMod { get; set; }
    public double AssistsMod { get; set; }
    public double DefenseMod { get; set; }
    public double TitleMod { get; set; }

    [JsonIgnore]
    public Attr AttrEnum => Enum.Parse<Attr>(Attr, ignoreCase: true);
}

public sealed class CountryDef
{
    public int Strength { get; set; }
    /// <summary>Qualidade da liga doméstica: 3=top5, 2=média, 1=menor.</summary>
    public int LeagueGrade { get; set; }
    public bool SouthAmerican { get; set; }
    /// <summary>
    /// Multiplicador autoral (não derivado por frequência — exceção deliberada, mesma
    /// lógica da seed fixa da Bola de Ouro anual). Aplicado só aos títulos domésticos
    /// (liga + copa nacional) no CareerScorer: ser campeão inglês vale mais que ser
    /// campeão uruguaio, mesmo quando as duas ligas caem no mesmo LeagueGrade.
    /// </summary>
    public double LeaguePrestige { get; set; } = 1.0;
    public List<string> Places { get; set; } = new();
}

public sealed class TierDef
{
    public int Tier { get; set; }
    public int Target { get; set; }
    public double ContinentalChance { get; set; }
}

public sealed class CareerCurve
{
    public int StartAge { get; set; } = 16;
    public int MinRetireAge { get; set; } = 32;
    public int MaxRetireAge { get; set; } = 38;
    public int MinPeakAge { get; set; } = 26;
    public int MaxPeakAge { get; set; } = 29;
    public double GrowthRate { get; set; } = 0.28;
    public int MinPotentialGap { get; set; } = 14;
    public int MaxPotentialGap { get; set; } = 27;
}
