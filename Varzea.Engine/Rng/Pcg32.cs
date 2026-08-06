namespace Varzea.Engine.Rng;

/// <summary>
/// PCG32 — gerador determinístico, portável e rápido.
/// REGRA DE OURO DO MOTOR: nenhuma classe de simulação pode usar Random.Shared,
/// DateTime.Now ou Guid.NewGuid(). Toda aleatoriedade passa por aqui.
/// Mesma seed + mesmas decisões => sempre o mesmo resultado, hoje e daqui a 3 anos.
/// </summary>
public sealed class Pcg32
{
    private ulong _state;
    private readonly ulong _inc;

    private const ulong Multiplier = 6364136223846793005UL;

    public Pcg32(ulong seed, ulong sequence = 1UL)
    {
        _inc = (sequence << 1) | 1UL;
        _state = 0UL;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    public uint NextUInt()
    {
        ulong old = _state;
        _state = unchecked(old * Multiplier + _inc);
        uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
    }

    /// <summary>Double uniforme em [0,1).</summary>
    public double NextDouble() => NextUInt() * (1.0 / 4294967296.0);

    /// <summary>Inteiro em [minInclusive, maxInclusive].</summary>
    public int NextInt(int minInclusive, int maxInclusive)
    {
        if (maxInclusive < minInclusive)
            throw new ArgumentException("max < min");
        long range = (long)maxInclusive - minInclusive + 1;
        return (int)(minInclusive + (long)(NextDouble() * range));
    }

    public bool Chance(double probability) => NextDouble() < probability;

    public T Pick<T>(IReadOnlyList<T> items)
    {
        if (items.Count == 0) throw new ArgumentException("lista vazia");
        return items[NextInt(0, items.Count - 1)];
    }

    /// <summary>
    /// Deriva um sub-gerador para um domínio isolado (ex.: draft vs temporada 7).
    /// Evita que mudar o consumo de números num sistema desloque todos os outros —
    /// sem isso, mexer no gerador de nomes mudaria os resultados das partidas.
    /// </summary>
    public static Pcg32 Derive(ulong seed, string domain, int index = 0)
    {
        unchecked
        {
            ulong h = 1469598103934665603UL;
            foreach (char c in domain)
            {
                h ^= c;
                h *= 1099511628211UL;
            }
            h ^= (ulong)index * 0x9E3779B97F4A7C15UL;
            return new Pcg32(seed, h | 1UL);
        }
    }
}
