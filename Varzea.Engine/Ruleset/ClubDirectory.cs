using System.Text.Json;
using Varzea.Engine.Rng;

namespace Varzea.Engine.Ruleset;

/// <summary>
/// Clubes reais de 1ª/2ª divisão por país (roadmap pós-§9), carregados de clubs.json.
/// Substitui os nomes de clube fictícios gerados no front — cada temporada agora tem um
/// clube real de verdade, não um template "{Prefixo} {Lugar}".
/// </summary>
public sealed class ClubDirectory
{
    public string Version { get; set; } = "0.0.0";
    public Dictionary<string, CountryClubs> Countries { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ClubDirectory LoadFromFile(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ClubDirectory>(json, JsonOpts)
               ?? throw new InvalidOperationException("clubs.json inválido");
    }

    /// <summary>
    /// Clubes disputando a MESMA divisão do clube do jogador nesta temporada (pra
    /// montar a tabela de pontos corridos) — tier 5 e o resto da divisão 1 jogam a
    /// mesma liga, tier 1-2 jogam a divisão 2. Nunca inclui o próprio clube do
    /// jogador (esse entra à parte, com força derivada de perf).
    /// </summary>
    public IReadOnlyList<string> LeagueRivals(string country, int tier, string ownClub)
    {
        var c = Countries.TryGetValue(country, out var cc) ? cc : Countries.Values.First();
        var division = tier >= 3 ? c.Divisao1 : c.Divisao2;
        var rivals = division.Where(name => name != ownClub).ToList();
        // Acesso/rebaixamento mantém a IDENTIDADE do clube (roadmap pós-§9) — um clube
        // que subiu/desceu pode não estar na lista curada desta divisão (ex: um clube
        // de 2ª divisão "promovido" continua não sendo um dos nomes de 1ª divisão
        // catalogados). Sem o Where acima removendo nada, o clube do jogador virava um
        // (N+1)-ésimo time extra — bug real relatado: "21º lugar num campeonato de 20".
        // Derruba o último da lista pra manter o tamanho real da divisão sempre em N.
        if (rivals.Count == division.Count && rivals.Count > 0)
            rivals.RemoveAt(rivals.Count - 1);
        return rivals;
    }

    /// <summary>Pool de clubes candidatos pro tier dado — tier 5 vem sempre de
    /// "grandes" (os poucos clubes tradicionalmente fortes de cada país); tier 3-4 vem
    /// do resto da divisão 1; tier 1-2 vem da divisão 2. Usado tanto pra sortear/
    /// escolher O PRÓPRIO clube quanto pra gerar as 3 opções iniciais — não confundir
    /// com LeagueRivals, que é a divisão INTEIRA (adversários da tabela).</summary>
    public IReadOnlyList<string> PoolFor(string country, int tier)
    {
        var c = Countries.TryGetValue(country, out var cc) ? cc : Countries.Values.First();
        var pool = tier switch
        {
            5 => c.Grandes,
            >= 3 => (IReadOnlyList<string>)c.Divisao1.Where(n => !c.Grandes.Contains(n)).ToList(),
            _ => c.Divisao2
        };
        return pool.Count > 0 ? pool : c.Divisao1;
    }

    /// <summary>Sorteia um clube real pro tier dado. Mesmo clube pode repetir entre
    /// temporadas (real: um jogador pode voltar a jogar num clube em que já jogou).</summary>
    public string PickClub(string country, int tier, Pcg32 rng)
    {
        var pool = PoolFor(country, tier);
        return pool[rng.NextInt(0, pool.Count - 1)];
    }

    /// <summary>Força de base (pra tabela de pontos corridos) por categoria do clube —
    /// escala arbitrária, só precisa manter a ordem grandes > resto div1 > div2;
    /// recalibrada junto com o resto do motor (ver CareerSimulator.SimulateLeagueTable).</summary>
    public double BaseStrength(string country, string clubName)
    {
        var c = Countries.TryGetValue(country, out var cc) ? cc : Countries.Values.First();
        if (c.Grandes.Contains(clubName)) return 90;
        if (c.Divisao1.Contains(clubName)) return 65;
        return 40;
    }
}

public sealed class CountryClubs
{
    public List<string> Grandes { get; set; } = new();
    public List<string> Divisao1 { get; set; } = new();
    public List<string> Divisao2 { get; set; } = new();
}
