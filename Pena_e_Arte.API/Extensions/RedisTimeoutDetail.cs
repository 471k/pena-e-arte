using System.Text.RegularExpressions;

namespace Pena_e_Arte.API.Extensions;

// Picks the diagnostic counters out of a StackExchange.Redis timeout message. The message
// itself can't be logged: it names the command's key, and rate-limiter keys hold the client
// IP. Only an allow-list of counters is kept: bytes waiting to be read (in, in-pipe, inbound),
// reader/writer state (rs, ws), queue sizes and the thread-pool snapshot (WORKER, IOCP, POOL).
// "in" > 0 with a timeout means Redis answered but the pod didn't read the reply in time;
// "in" = 0 means the reply never arrived.
internal static partial class RedisTimeoutDetail
{
    private const int MaxValueLength = 80;

    [GeneratedRegex(@"(?<name>outbound|inbound)=(?<value>[0-9.]+[KMG]?i?B)")]
    private static partial Regex SizeCounters();

    [GeneratedRegex(@"(?<value>\d+)ms elapsed, timeout is (?<timeout>\d+)ms")]
    private static partial Regex Elapsed();

    [GeneratedRegex(@"(?<=, )(?<name>qu|qs|aw|bw|rs|ws|in|in-pipe|out-pipe|last-in|cur-in|sync-ops|async-ops): (?<value>[^,()]+)")]
    private static partial Regex Counters();

    [GeneratedRegex(@"(?<=, )(?<name>IOCP|WORKER|POOL): \((?<value>[^()]*)\)")]
    private static partial Regex ThreadPools();

    public static string? From(string? message)
    {
        if (string.IsNullOrEmpty(message)) return null;

        List<string> parts = [];
        Match elapsed = Elapsed().Match(message);
        if (elapsed.Success)
            parts.Add($"elapsed={elapsed.Groups["value"].Value}ms/{elapsed.Groups["timeout"].Value}ms");

        foreach (Match match in SizeCounters().Matches(message))
            parts.Add($"{match.Groups["name"].Value}={match.Groups["value"].Value}");
        foreach (Match match in Counters().Matches(message))
            parts.Add($"{match.Groups["name"].Value}={Trim(match.Groups["value"].Value)}");
        foreach (Match match in ThreadPools().Matches(message))
            parts.Add($"{match.Groups["name"].Value}=({Trim(match.Groups["value"].Value)})");

        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    private static string Trim(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= MaxValueLength ? trimmed : trimmed[..MaxValueLength];
    }
}
