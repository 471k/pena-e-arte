using Microsoft.EntityFrameworkCore;

namespace Pena_e_Arte.Infrastructure.Persistence;

/// <summary>
/// The MySQL server version EF Core generates SQL for — declared once, read everywhere.
/// <para>
/// Pomelo emits different SQL for different declared versions (for example, a correlated
/// subquery becomes a LATERAL join only when the declared version is at least 8.0.14, and
/// cannot be translated at all below that). So every context that talks to MySQL must declare
/// the same version, or a test can pass on SQL production never runs. Until this constant
/// existed, production declared 8.4.0 in two places and the integration tests declared 8.0.0
/// in 31 places and 8.4.0 in 5: the suite could not translate the POK credential query that
/// runs on every card payment.
/// </para>
/// <para>
/// Read by: <see cref="Extensions.InfrastructureServiceExtensions"/> (the running app),
/// <see cref="AppDbContextFactory"/> (dotnet ef design time), and every integration-test
/// context. Never write a <c>new MySqlServerVersion(...)</c> literal anywhere else.
/// </para>
/// <para>
/// Must match the servers the repo runs against: <c>docker-compose.yml</c> and
/// <c>.github/workflows/ci.yml</c> both use <c>mysql:8.4</c>. Raising it lets EF emit SQL an
/// older server cannot run, so move those (and production) first.
/// </para>
/// </summary>
public static class DatabaseServerVersion
{
    public static readonly ServerVersion MySql = new MySqlServerVersion(new Version(8, 4, 0));
}
