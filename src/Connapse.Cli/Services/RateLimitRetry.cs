using System.Net;

namespace Connapse.Cli.Services;

/// <summary>
/// When the server says it's too busy — 429, or 503 with <c>Retry-After</c> — wait and send again.
/// The server's rate limiter is a fixed window (200 requests a minute per user by default) that names
/// the seconds left in <c>Retry-After</c>, so a long run such as uploading a big folder pauses until the
/// window resets instead of failing every request after the 200th.
///
/// The wait is <c>Retry-After</c> plus one second, because the server rounds the seconds left down, plus up
/// to a second of jitter, so several CLIs don't all fire the instant the window opens. Without the header it backs
/// off exponentially with "equal jitter" (half the step fixed, half random). A wait longer than
/// <see cref="LongestWait"/>, or a sixth 429 in a row, is returned to the caller as the error it is.
/// </summary>
public sealed class RateLimitRetry
{
    public const int MaxRetries = 5;
    public static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LongestBackoff = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RoundingMargin = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxJitter = TimeSpan.FromSeconds(1);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeProvider _time;
    private readonly Random _random;
    private readonly TextWriter? _notices;

    /// <param name="delay">How to wait; tests pass one that doesn't.</param>
    /// <param name="notices">Where to say the CLI is waiting; standard error when not given.</param>
    public RateLimitRetry(
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeProvider? time = null,
        Random? random = null,
        TextWriter? notices = null)
    {
        _time = time ?? TimeProvider.System;
        _delay = delay ?? ((wait, ct) => Task.Delay(wait, _time, ct));
        _random = random ?? Random.Shared;
        _notices = notices;
    }

    /// <summary>
    /// Sends with <paramref name="send"/>, and again after a wait each time the server says it's too
    /// busy. <paramref name="send"/> must build a fresh request each time; its content is sent again.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        for (var retry = 0; ; retry++)
        {
            var response = await send(ct);
            if (!IsTooBusy(response) || retry == MaxRetries)
                return response;

            var wait = WaitBefore(retry, response);
            if (wait is null)
                return response;

            response.Dispose();
            (_notices ?? Console.Error).WriteLine(
                $"The server is rate limiting requests; waiting {Math.Ceiling(wait.Value.TotalSeconds)}s before trying again (retry {retry + 1} of {MaxRetries}).");
            await _delay(wait.Value, ct);
        }
    }

    public static bool IsTooBusy(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.TooManyRequests
        || (response.StatusCode == HttpStatusCode.ServiceUnavailable && response.Headers.RetryAfter is not null);

    /// <summary>How long to wait before retry number <paramref name="retry"/> (from 0); null to give up.</summary>
    public TimeSpan? WaitBefore(int retry, HttpResponseMessage response)
    {
        var asked = RetryAfter(response);
        if (asked is { } seconds)
        {
            if (seconds > LongestWait)
                return null;
            return seconds + RoundingMargin + MaxJitter * _random.NextDouble();
        }

        var step = FirstBackoff * Math.Pow(2, retry);
        if (step > LongestBackoff)
            step = LongestBackoff;
        return step / 2 + step / 2 * _random.NextDouble();
    }

    /// <summary><c>Retry-After</c> as a wait: a number of seconds, or an HTTP date (RFC 9110 §10.2.3).</summary>
    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (header?.Date is { } date)
        {
            var left = date - _time.GetUtcNow();
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
        return null;
    }
}
