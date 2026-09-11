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

    /// <summary>
    /// ADR-0001 accepted risk: "POK webhooks have no documented signature — treat every webhook as
    /// an untrusted ping; always re-fetch GET /sdk-orders/{id}. Architecture test forbids reading
    /// payment state from a webhook body." A reflection-based check can't see into a method body
    /// reliably, so this reads the actual endpoint source and asserts the handler never assigns
    /// Payment.Status (or reads a request body at all) — it may only trigger a re-fetch.
    /// </summary>
    [Fact]
    public void PokWebhookHandler_NeverReadsRequestBodyOrAssignsPaymentState()
    {
        string sourcePath = FindRepoFile("Pena_e_Arte.API", "Endpoints", "PaymentEndpoints.cs");
        string source = File.ReadAllText(sourcePath);

        int methodStart = source.IndexOf("HandlePokWebhook", StringComparison.Ordinal);
        methodStart.Should().BeGreaterThan(-1, because: "the POK webhook handler must exist in PaymentEndpoints.cs");

        // Grab the method body between the first '{' after the signature and its matching '}',
        // tracked by brace depth rather than assuming no nested blocks.
        int braceStart = source.IndexOf('{', methodStart);
        int depth = 0;
        int i = braceStart;
        for (; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) break;
        }
        string methodBody = source[methodStart..(i + 1)];

        methodBody.Should().NotContain(".Status =",
            because: "the webhook handler must never assign payment state directly from the request");
        methodBody.Should().NotContain("HttpRequest",
            because: "the handler must not read the POK webhook's (unsigned, undocumented) body at all");
        methodBody.Should().Contain("TriggerPaymentReconciliationNow",
            because: "the only allowed effect is triggering a re-fetch from POK, never trusting the ping itself");
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, pathParts[0])))
            dir = dir.Parent;

        dir.Should().NotBeNull(because: $"the repo root containing '{pathParts[0]}' must be findable from the test output directory");
        return Path.Combine([dir!.FullName, .. pathParts]);
    }
}
