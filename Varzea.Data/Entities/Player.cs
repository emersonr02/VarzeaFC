namespace Varzea.Data.Entities;

/// <summary>
/// Âncora mínima pra pendurar slots e conquistas. Não existe autenticação ainda
/// (HANDOFF não decidiu isso) — quando existir, Id deve virar o Id real do usuário
/// autenticado. Até lá é só uma FK, não um sistema de contas.
/// </summary>
public sealed class Player
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
