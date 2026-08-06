using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Varzea.Data;

/// <summary>
/// Só pra `dotnet ef` (migrations add / database update) funcionar sem precisar de um
/// projeto de startup com DI configurado. Pra gerar uma migration, a connection string
/// nem precisa ser válida — só o modelo importa. Pra `database update` de verdade,
/// porém, a conexão precisa apontar pro Postgres real: lê de VARZEA_DB_CONNECTION se
/// definida (ex.: a do docker-compose.yml), senão cai no valor fake de design-time.
/// Em runtime, quem registra o DbContext é Varzea.Api (via appsettings/env var real).
/// </summary>
public sealed class VarzeaDbContextFactory : IDesignTimeDbContextFactory<VarzeaDbContext>
{
    public VarzeaDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("VARZEA_DB_CONNECTION")
            ?? "Host=localhost;Database=varzea;Username=varzea;Password=design-time-only";
        var options = new DbContextOptionsBuilder<VarzeaDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new VarzeaDbContext(options);
    }
}
