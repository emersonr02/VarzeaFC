using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Varzea.Data;

/// <summary>
/// Só pra `dotnet ef migrations add` funcionar sem precisar de um projeto de
/// startup com DI configurado. A connection string aqui nunca é usada pra conectar de
/// verdade — gerar uma migration só precisa do modelo, não de um Postgres respondendo.
/// Em runtime, quem registra o DbContext é Varzea.Api (via appsettings/env var real).
/// </summary>
public sealed class VarzeaDbContextFactory : IDesignTimeDbContextFactory<VarzeaDbContext>
{
    public VarzeaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<VarzeaDbContext>()
            .UseNpgsql("Host=localhost;Database=varzea;Username=varzea;Password=design-time-only")
            .Options;
        return new VarzeaDbContext(options);
    }
}
