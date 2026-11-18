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
    // numbers (GetPlanUsageReportQuery storage GB, GetPlatformStatsQuery percentages); those are the same
    // class of issue and deliberately out of scope here.

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
        // A line that rounds and mentions a rating or an average, or any Rating cast to double for an
        // aggregate, is a second copy of the rule. RatingDisplay.cs itself is the one allowed place.
        Regex rounds = new(@"Math\.Round\(");
        Regex mentionsARating = new(@"rating|avg|average", RegexOptions.IgnoreCase);
        Regex ratingAsDouble = new(@"\(double\)\s*\w+\.Rating\b");

        string repoRoot = FindRepoRoot();
        List<string> offenders = [];
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
                {
                    if ((rounds.IsMatch(lines[i]) && mentionsARating.IsMatch(lines[i])) || ratingAsDouble.IsMatch(lines[i]))
                        offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        offenders.Should().BeEmpty(
            because: "every displayed rating average goes through RatingDisplay.Average (exact decimal mean, "
                   + "one decimal, half up); rounding a double elsewhere lets two pages show two numbers");
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
