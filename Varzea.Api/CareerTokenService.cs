using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Varzea.Api;

/// <summary>
/// Assina o CareerState em vez de guardá-lo em sessão de servidor. O cliente carrega
/// o token de ida e volta a cada chamada do draft; o HMAC garante que ele não foi
/// adulterado (ex.: reescrever DraftPicks para forçar uma lenda específica).
/// Não é uma verdade absoluta contra cheat — isso vem da re-simulação em /careers/save —
/// só impede que o cliente forje estado intermediário sem passar pelo servidor.
/// </summary>
public sealed class CareerTokenService
{
    private readonly byte[] _key;

    public CareerTokenService(string secret) => _key = Encoding.UTF8.GetBytes(secret);

    public string Issue(CareerState state)
    {
        string payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(state));
        return $"{payload}.{Sign(payload)}";
    }

    public CareerState? TryVerify(string token)
    {
        var parts = token.Split('.', 2);
        if (parts.Length != 2) return null;

        string expected = Sign(parts[0]);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[1])))
            return null;

        try
        {
            return JsonSerializer.Deserialize<CareerState>(Base64UrlDecode(parts[0]));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string Sign(string payload)
    {
        byte[] hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        string padded = s.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
