using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pena_e_Arte.UnitTests.Architecture;

/// <summary>
/// The clock ratchet (step 3 of the clock work; backend.md, "Clocks"). Production code reads the real wall
/// clock directly in a few hundred places. Banning that with every existing use grandfathered would be
/// decoration, so instead each production file's count of direct reads is recorded in
/// clock-reads-baseline.txt and may only go DOWN:
///  - a file whose count rises, or a new file with any direct read, fails: inject TimeProvider instead;
///  - a file whose count falls fails too, asking for the baseline to be lowered, so the room freed by a
///    migration can't be silently reused later. To lower it without hand-editing, run the test once with
///    UPDATE_CLOCK_BASELINE=1: it rewrites only the lines that FELL. It never raises a line, so a rise still
///    fails, and it is ignored when CI is set, so CI can never rewrite the file.
///
/// Production code only, by decision (Desktop, 19 Nov): most clock reads in tests are harmless fixture
/// style, so a test ratchet would mostly measure noise and fire on good new tests. The nightly Clock
/// sweep (.github/workflows/clock-sweep.yml) finds test defects by behaviour instead.
///
/// It counts syntax, not text: comments, strings and XML docs that mention DateTime.UtcNow never count.
/// Counted: DateTime/DateTimeOffset .UtcNow/.Now/.Today, and TimeProvider.System.GetUtcNow()/GetLocalNow()
/// (the same read with an extra hop). TimeProvider.System on its own is NOT counted: passing it as the
/// default for an injectable provider is the pattern we want.
/// </summary>
public class ClockReadRatchetTests
{
    private const string BaselineFile = "tests/Pena_e_Arte.UnitTests/Architecture/clock-reads-baseline.txt";
    private const string UpdateSwitch = "UPDATE_CLOCK_BASELINE";

    private static readonly string[] ProductionProjects =
    [
        "Pena_e_Arte.API", "Pena_e_Arte.Application", "Pena_e_Arte.Contracts",
        "Pena_e_Arte.Domain", "Pena_e_Arte.Infrastructure",
    ];

    [Fact]
    public void DirectClockReadsInProductionCode_OnlyGoDown()
    {
        string repoRoot = FindRepoRoot();
        string baselinePath = Path.Combine(repoRoot, BaselineFile);
        SortedDictionary<string, int> actual = CountProductionClockReads(repoRoot);
        Dictionary<string, int> baseline = ReadBaseline(baselinePath);

        List<string> rose = [];
        List<string> fell = [];
        foreach ((string file, int count) in actual)
        {
            int allowed = baseline.GetValueOrDefault(file);
            if (count > allowed) rose.Add($"{file}: {count} direct clock reads, baseline {allowed}");
            else if (count < allowed) fell.Add($"{count}\t{file}");
        }
        foreach ((string file, int allowed) in baseline)
        {
            if (!actual.ContainsKey(file)) fell.Add($"(remove the line)\t{file} (was {allowed}; file gone or now 0)");
        }

        bool update = Environment.GetEnvironmentVariable(UpdateSwitch) == "1"
                      && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));
        if (update && fell.Count > 0)
        {
            File.WriteAllLines(baselinePath, LoweredBaseline(File.ReadAllLines(baselinePath), baseline, actual));
            fell.Clear();
        }

        rose.Should().BeEmpty(
            "production code must not add direct wall-clock reads. Inject TimeProvider and read GetUtcNow() for "
            + "time of day, or GetTimestamp()/GetElapsedTime() for a duration (backend.md, \"Clocks\"). Files over "
            + "their baseline:\n  " + string.Join("\n  ", rose));
        fell.Should().BeEmpty(
            "these files now read the clock directly fewer times than recorded. Good. Lock it in by setting their "
            + $"lines in {BaselineFile} to the values below, or run this test once with {UpdateSwitch}=1 to "
            + "rewrite them:\n  " + string.Join("\n  ", fell));
    }

    [Fact]
    public void TheUpdateSwitch_OnlyEverLowers()
    {
        string[] lines = ["# header kept", "3\tA.cs", "2\tB.cs", "1\tGone.cs"];
        Dictionary<string, int> baseline = new() { ["A.cs"] = 3, ["B.cs"] = 2, ["Gone.cs"] = 1 };
        // A fell to 1, B rose to 5, Gone is gone, New appeared: only A and Gone may change.
        SortedDictionary<string, int> actual = new(StringComparer.Ordinal) { ["A.cs"] = 1, ["B.cs"] = 5, ["New.cs"] = 2 };

        LoweredBaseline(lines, baseline, actual).Should().Equal("# header kept", "1\tA.cs", "2\tB.cs");
    }

    // Line by line, in the file's own order so the diff shows only what changed: comments are kept, each
    // entry becomes min(baseline, actual) and is dropped at 0. A rise or a new file is never written, so the
    // test still fails on it after an update run.
    internal static List<string> LoweredBaseline(
        IEnumerable<string> lines, Dictionary<string, int> baseline, SortedDictionary<string, int> actual)
    {
        List<string> result = [];
        foreach (string line in lines)
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                result.Add(line);
                continue;
            }
            string file = line.Split('\t')[1];
            int lowered = Math.Min(baseline[file], actual.GetValueOrDefault(file));
            if (lowered > 0) result.Add($"{lowered}\t{file}");
        }
        return result;
    }

    [Fact]
    public void TheCounter_CountsSyntaxNotText()
    {
        const string source = """
            // DateTime.UtcNow in a comment does not count
            /// <summary>Nor DateTime.UtcNow in an XML doc.</summary>
            class C
            {
                string s = "DateTime.UtcNow in a string does not count";
                DateTime a = DateTime.UtcNow;                       // 1
                DateTime b = System.DateTime.Now;                   // 2
                DateTime c = DateTime.Today;                        // 3
                DateTimeOffset d = DateTimeOffset.UtcNow;           // 4
                DateTimeOffset e = TimeProvider.System.GetUtcNow(); // 5
                TimeProvider f = TimeProvider.System;               // an injection default: not counted
                DateTime g = DateTime.MinValue;                     // not a clock read
                long h = Stopwatch.GetTimestamp();                  // monotonic: not counted
            }
            """;

        CountClockReads(source).Should().Be(5);
    }

    internal static int CountClockReads(string source)
    {
        SyntaxNode root = CSharpSyntaxTree.ParseText(source).GetRoot();
        int count = 0;
        foreach (MemberAccessExpressionSyntax access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            string member = access.Name.Identifier.Text;
            string target = RightmostName(access.Expression);
            if (member is "UtcNow" or "Now" or "Today" && target is "DateTime" or "DateTimeOffset")
                count++;
            else if (member is "GetUtcNow" or "GetLocalNow"
                     && access.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "System" } inner
                     && RightmostName(inner.Expression) == "TimeProvider")
                count++;
        }
        return count;
    }

    private static string RightmostName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
        _ => string.Empty,
    };

    private static SortedDictionary<string, int> CountProductionClockReads(string repoRoot)
    {
        SortedDictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (string project in ProductionProjects)
        {
            foreach (string path in Directory.EnumerateFiles(Path.Combine(repoRoot, project), "*.cs", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
                if (relative.Contains("/bin/") || relative.Contains("/obj/")) continue;
                int count = CountClockReads(File.ReadAllText(path));
                if (count > 0) counts[relative] = count;
            }
        }
        return counts;
    }

    private static Dictionary<string, int> ReadBaseline(string path)
    {
        Dictionary<string, int> baseline = new(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] parts = line.Split('\t');
            baseline[parts[1]] = int.Parse(parts[0]);
        }
        return baseline;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Pena_e_Arte.Application")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
