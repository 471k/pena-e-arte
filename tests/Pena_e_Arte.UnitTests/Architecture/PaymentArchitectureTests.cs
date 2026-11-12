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

    /// <summary>
    /// ADR-0001 Amendment B: Flow A carries no platform commission, permanently — the field and
    /// capability that existed to support one (<c>Payment.PlatformFeeAmount</c>,
    /// <c>PaymentProviderCapabilities.SupportsSplit</c>) were deliberately removed rather than
    /// left at a permanent zero. This guards against either reappearing, under the same name,
    /// anywhere in Domain or Application — the two layers where a commission field would be
    /// modelled. A comment saying "don't add this back" erodes; a failing build doesn't.
    /// </summary>
    [Fact]
    public void NoMember_IsNamedPlatformFeeAmountOrSupportsSplit()
    {
        Assembly[] assemblies =
        [
            typeof(Payment).Assembly,                                                  // Domain
            typeof(global::Pena_e_Arte.Application.Persistence.IAppDbContext).Assembly, // Application
        ];

        string[] forbiddenNames = ["PlatformFeeAmount", "SupportsSplit"];
        const BindingFlags AllDeclared = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        List<string> offenders = [];

        foreach (Assembly assembly in assemblies)
        {
            foreach (Type type in assembly.GetTypes())
            {
                foreach (PropertyInfo prop in type.GetProperties(AllDeclared))
                    if (forbiddenNames.Contains(prop.Name))
                        offenders.Add($"{type.FullName}.{prop.Name}");

                foreach (FieldInfo field in type.GetFields(AllDeclared))
                    if (forbiddenNames.Contains(field.Name))
                        offenders.Add($"{type.FullName}.{field.Name}");
            }
        }

        offenders.Should().BeEmpty(
            because: "ADR-0001 Amendment B closed the decision that Flow A never takes a "
                   + "platform commission — PlatformFeeAmount and SupportsSplit were removed, "
                   + "not zeroed, and must not be reintroduced under either name.");
    }

    /// <summary>
    /// The frontend's describeDeposit (frontend/src/features/payments/depositOutcome.ts) shows a Paid
    /// deposit on a cancelled appointment as "Kept (cancellation policy)" without checking anything.
    /// That label is only honest while every route that sets a payment to Paid refuses a deposit that
    /// isn't forfeited on a cancelled appointment. PR #356 closed four routes that didn't; a fifth would
    /// bring the mislabel back silently, because describeDeposit's own tests still pass.
    ///
    /// This is a tripwire, not a proof: it lists every direct assignment of PaymentStatus.Paid in
    /// production code and fails when one is added, moved to another file or removed, so the author has
    /// to check the route against that rule and then update the list. It can't see an indirect write
    /// (e.g. <c>payment.Status = outcome.PaymentStatus</c> from CancellationOutcome, which only
    /// gives Paid together with DepositStatus.Forfeited).
    /// </summary>
    [Fact]
    public void PaymentStatusPaid_IsWrittenOnlyByTheKnownRoutes()
    {
        // File (repo-relative, '/' separators) -> number of direct "Status = PaymentStatus.Paid" writes,
        // with why each one cannot leave a non-forfeited deposit Paid on a cancelled appointment.
        Dictionary<string, int> known = new()
        {
            // Refuses a Cancelled or NoShow appointment; the appointment ends Completed.
            ["Pena_e_Arte.Application/Appointments/Commands/CompleteAppointmentCommand.cs"] = 1,
            // The appointment ends NoShow with DepositStatus.Forfeited.
            ["Pena_e_Arte.Application/Appointments/Commands/MarkNoShowCommand.cs"] = 1,
            // Refuses a hold that isn't forfeited on a cancelled appointment (PR #356).
            ["Pena_e_Arte.Application/Payments/Commands/CaptureDepositCommand.cs"] = 1,
            // The single owedBack predicate gates both heal cases (PR #356).
            ["Pena_e_Arte.Application/Payments/Commands/ConfirmCardPaymentCommand.cs"] = 1,
            // Needs CashPending; cancelling turns CashPending into Refunded (CancellationOutcome).
            ["Pena_e_Arte.Application/Payments/Commands/ConfirmCashDepositCommand.cs"] = 1,
            // LANDMINE, unreachable today. Legacy Stripe webhook (payment_intent.succeeded, BillingEndpoints),
            // keyed by a Stripe PaymentIntent id; deposits carry POK order ids, so no deposit row matches
            // (Stripe deposits are foreclosed by ADR-0001 Amendment B). If it ever matches a deposit it does
            // the worst thing a route to Paid can do: it sets ANY payment status other than Paid to Paid
            // (Refunded and Failed included), and sets the appointment's DepositStatus to Paid whatever it
            // was (Forfeited, Refunded and PrePaid included). That erases why the money moved (a refund or
            // a kept deposit then reads as a normal payment) and on a cancelled appointment it makes
            // describeDeposit show money owed back as "Kept".
            // Before anything makes it reachable for deposits (a Stripe deposit path, or matching by another
            // id), it must: refuse to change a Refunded or Failed payment; refuse, or refund like
            // PaymentReconciliationJob does, a non-forfeited deposit on a cancelled appointment (the owedBack
            // rule in ConfirmCardPaymentCommand, PR #356); and change DepositStatus only from Pending.
            ["Pena_e_Arte.Application/Payments/Commands/ConfirmPaymentCommand.cs"] = 1,
            // Refuses a cancelled appointment before reaching this heal.
            ["Pena_e_Arte.Application/Payments/Commands/CreateDepositPaymentCommand.cs"] = 1,
            // Same guard as CreateDepositPaymentCommand (PR #356, the fourth route).
            ["Pena_e_Arte.Application/Payments/Commands/DeclareCashDepositCommand.cs"] = 1,
            // MarkPaid; a held deposit on a cancelled appointment is refunded, not marked Paid (PR #356).
            ["Pena_e_Arte.Infrastructure/Jobs/PaymentReconciliationJob.cs"] = 1,
        };

        string apiDir = FindRepoFile("Pena_e_Arte.API");
        string repoRoot = Directory.GetParent(apiDir)!.FullName;
        string[] productionProjects =
            ["Pena_e_Arte.API", "Pena_e_Arte.Application", "Pena_e_Arte.Domain", "Pena_e_Arte.Infrastructure"];
        System.Text.RegularExpressions.Regex paidWrite =
            new(@"\bStatus\s*=\s*PaymentStatus\.Paid\b");

        Dictionary<string, int> found = [];
        foreach (string project in productionProjects)
        {
            foreach (string path in Directory.EnumerateFiles(Path.Combine(repoRoot, project), "*.cs", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
                if (relative.Contains("/bin/") || relative.Contains("/obj/")
                    || relative.Contains("/Migrations/") || relative.Contains("/Persistence/Seed/"))
                    continue;

                int count = paidWrite.Matches(File.ReadAllText(path)).Count;
                if (count > 0) found[relative] = count;
            }
        }

        found.Should().BeEquivalentTo(known,
            because: "describeDeposit labels Paid on a cancelled appointment as kept, which is only true "
                   + "while every route to Paid refuses a deposit that isn't forfeited on a cancelled "
                   + "appointment. Check the new or changed route against that rule (see PR #356), then "
                   + "update this list with the reason it holds.");
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
