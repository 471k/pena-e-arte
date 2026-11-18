using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Pena_e_Arte.Application.Public;
using Pena_e_Arte.Contracts.Responses.Public;

namespace Pena_e_Arte.UnitTests.Public;

public class RatingDisplayTests
{
    // The five one-decimal midpoints reachable with 4 or 20 reviews. Only the quarters are exact in binary;
    // on a double, 4.35 is 4.3499999999999996 and would round down under any mode.
    [Theory]
    [InlineData(17, 4, "4.3")]  // 4.25
    [InlineData(15, 4, "3.8")]  // 3.75
    [InlineData(87, 20, "4.4")] // 4.35
    [InlineData(89, 20, "4.5")] // 4.45
    [InlineData(83, 20, "4.2")] // 4.15
    [InlineData(14, 3, "4.7")]  // 4.666...
    [InlineData(101, 25, "4.0")] // 4.04: rounds to a whole number
    [InlineData(5, 1, "5")]
    [InlineData(1, 1, "1")]
    public void Average_IsTheExactMeanToOneDecimal_HalfUp(int sum, int count, string expected)
    {
        RatingDisplay.Average(sum, count).Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Average_IsNull_WhenThereAreNoReviews()
    {
        RatingDisplay.Average(0, 0).Should().BeNull();
    }

    // ── Guard: one rule, everywhere a rating average is shown ─────────────────────────────────────────
    // Scoped to ratings on purpose. Math.Round on doubles with the default mode also appears on admin-facing
    // numbers (GetPlanUsageReportQuery storage GB, GetPlatformStatsQuery percentages, PlanLimitService storage
    // GB); those are the same class of issue and deliberately out of scope here. PlanLimitService's value is
    // the odd one out: it goes into a PlanUsageSnapshot, which something may compare against a plan limit,
    // so check that one for a decision path rather than assuming it is only displayed.
    //
    // How much these scans carry: the first guard is the compiler. RatingDisplay.Average takes (int, int), so a
    // double mean cannot be handed to it, and any site that uses the helper refuses the mutation at build time.
    // The scans below cover the code that never reaches the helper at all. They read one line at a time, so an
    // aggregate whose lambda is split across lines is missed; dotnet format --verify-no-changes (blocking in
    // ci.yml) keeps lambdas this short on one line. If a second rule ever needs this kind of scan, reconsider
    // a Roslyn syntax walk then, not for one rule.

    [Fact]
    public void EveryAverageRatingField_OnThePublicContracts_IsADecimal()
    {
        List<string> offenders = typeof(ReviewListResponse).Assembly.GetTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name.EndsWith("AverageRating", StringComparison.Ordinal))
                .Where(p => p.PropertyType != typeof(decimal?))
                .Select(p => $"{t.Name}.{p.Name} is {p.PropertyType.Name}"))
            .ToList();

        offenders.Should().BeEmpty(
            because: "a displayed rating average is a fixed-precision quantity from RatingDisplay.Average, "
                   + "and a double field invites arithmetic that reintroduces representation error");
    }

    [Fact]
    public void NoRatingAverage_IsRoundedOrAveragedAsADouble_OutsideRatingDisplay()
    {
        // A line that rounds and mentions a rating or an average, any Rating cast to double for an aggregate,
        // or LINQ's Average over a Rating is a second copy of the rule. Review.Rating is an int, so
        // g.Average(r => r.Rating) is a double mean with no cast and no Math.Round on the line: the shortest
        // way to write it, and invisible to the first two patterns. Use .* not [^)]* after "Average(":
        // "(double)" contains a paren. RatingDisplay.cs itself is the one allowed place.
        Regex rounds = new(@"Math\.Round\(");
        Regex mentionsARating = new(@"rating|avg|average", RegexOptions.IgnoreCase);
        Regex ratingAsDouble = new(@"\(double\)\s*\w+\.Rating\b");
        Regex averagesARating = new(@"Average\(.*\.Rating\b");

        List<string> offenders = ServerSourceLines()
            .Where(l => (rounds.IsMatch(l.Text) && mentionsARating.IsMatch(l.Text))
                        || ratingAsDouble.IsMatch(l.Text) || averagesARating.IsMatch(l.Text))
            .Select(l => $"{l.Where}: {l.Text.Trim()}")
            .ToList();

        offenders.Should().BeEmpty(
            because: "every displayed rating average goes through RatingDisplay.Average (exact decimal mean, "
                   + "one decimal, half up); rounding a double elsewhere lets two pages show two numbers");
    }

    [Fact]
    public void EveryRatingAggregate_IsAnIntegerSum_InAFileThatUsesRatingDisplay()
    {
        // The positive half. A scan that only looks for the wrong shape passes a file that computes nothing,
        // and passes when it finds no files at all. So: rating aggregates exist, every one is
        // g.Sum(r => r.Rating) with no cast, and each sits in a file that hands the sum to RatingDisplay.
        // Sum and Average only: Review.Rating is an int, so Min and Max over it return an int and cannot carry a
        // double (a cast inside them is caught by the negative scan's ratingAsDouble). "Highest rated" is fine.
        // The \1 backreference is deliberate: the lambda's parameter must be the thing whose Rating is summed,
        // which also catches g.Sum(r => x.Rating), the wrong-variable bug. Do not simplify it to \w+.
        Regex aggregatesARating = new(@"\.(Sum|Average)\(.*\.Rating\b");
        Regex integerSum = new(@"\.Sum\(\s*(\w+)\s*=>\s*\1\.Rating\s*\)");

        List<SourceLine> lines = ServerSourceLines().ToList();
        List<SourceLine> aggregates = lines.Where(l => aggregatesARating.IsMatch(l.Text)).ToList();
        HashSet<string> filesUsingTheRule = lines
            .Where(l => l.Text.Contains("RatingDisplay.Average(", StringComparison.Ordinal))
            .Select(l => l.File)
            .ToHashSet();

        aggregates.Should().NotBeEmpty(
            because: "the public pages aggregate ratings; finding none means the scan is looking in the wrong place");

        List<string> offenders = aggregates
            .Where(l => !integerSum.IsMatch(l.Text) || !filesUsingTheRule.Contains(l.File))
            .Select(l => $"{l.Where}: {l.Text.Trim()}")
            .ToList();

        offenders.Should().BeEmpty(
            because: "a rating aggregate is an integer sum (with a count) handed to RatingDisplay.Average; "
                   + "an Average over Rating is a double-valued copy of the rule, and a Sum over anything but the "
                   + "lambda's own Rating sums the wrong thing");
    }

    private sealed record SourceLine(string File, int Number, string Text)
    {
        public string Where => $"{File}:{Number}";
    }

    // Production server code only. RatingDisplay.cs is left out because it is the rule itself.
    private static IEnumerable<SourceLine> ServerSourceLines()
    {
        string repoRoot = FindRepoRoot();
        foreach (string project in new[] { "Pena_e_Arte.Application", "Pena_e_Arte.API", "Pena_e_Arte.Infrastructure" })
        {
            foreach (string path in Directory.EnumerateFiles(Path.Combine(repoRoot, project), "*.cs", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
                if (relative.Contains("/bin/") || relative.Contains("/obj/") || relative.Contains("/Migrations/")
                    || relative.EndsWith("/Public/RatingDisplay.cs", StringComparison.Ordinal))
                    continue;

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                    yield return new SourceLine(relative, i + 1, lines[i]);
            }
        }
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Pena_e_Arte.Application")))
            dir = dir.Parent;

        dir.Should().NotBeNull(because: "the repo root must be findable from the test output directory");
        return dir!.FullName;
    }
}
