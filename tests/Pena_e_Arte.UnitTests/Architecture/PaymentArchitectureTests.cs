using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.UnitTests.Architecture;

/// <summary>
/// Architecture fitness function (ADR-0001 Consequence 3): fails the build if a type shaped like a
/// platform-balance ledger / payout queue is ever introduced. TattooOS is a technical service
/// provider (Law 55/2020 Art. 4(g)) — it must NOT hold, commingle, or queue studio funds on a
/// platform balance. POK's split-at-payment model means money never lands on a platform ledger; a
/// type modelling one would signal exactly the commingling this posture forbids.
///
/// Perfect static enforcement of "no commingling" is not achievable — a balance could be modelled
/// under an innocuous name. This guards the obvious/named shapes; the complementary runtime signal
/// is PaymentReconciliationJob's logging, which would surface an unexpected balance in a report.
/// </summary>
public class PaymentArchitectureTests
{
    // Assemblies where a ledger/payout-queue entity would plausibly be introduced.
    private static readonly Assembly[] SolutionAssemblies =
    [
        typeof(Payment).Assembly,                                                    // Domain
        typeof(global::Pena_e_Arte.Infrastructure.Persistence.AppDbContext).Assembly, // Infrastructure
        typeof(global::Pena_e_Arte.Application.Persistence.IAppDbContext).Assembly,    // Application
    ];

    private const string ForbiddenLedgerNames =
        "(?i).*(PlatformLedger|PayoutQueue|PlatformBalance|PlatformWallet|CommingledFunds|FloatAccount).*";

    [Fact]
    public void NoType_IsShapedLikeAPlatformBalanceLedgerOrPayoutQueue()
    {
        List<string> offenders = [];

        foreach (Assembly assembly in SolutionAssemblies)
        {
            IEnumerable<Type>? matches = Types.InAssembly(assembly)
                .That().HaveNameMatching(ForbiddenLedgerNames)
                .GetTypes();

            offenders.AddRange(matches.Select(t => t.FullName ?? t.Name));
        }

        offenders.Should().BeEmpty(
            because: "TattooOS must not hold or commingle studio funds on a platform balance "
                   + "(Law 55/2020 Art. 4(g)); a ledger/payout-queue type signals that exposure. "
                   + "If a match is legitimate, rename it or narrow the rule deliberately.");
    }
}
