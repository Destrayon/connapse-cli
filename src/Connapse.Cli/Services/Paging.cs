namespace Connapse.Cli.Services;

/// <summary>Walks a skip/take list endpoint to the end.</summary>
internal static class Paging
{
    /// <summary>The server's largest accepted <c>take</c>, so a long list costs the fewest requests.</summary>
    public const int PageSize = 200;

    /// <summary>
    /// Every item across every page, in the server's order. Stops when the server says there is no
    /// more, or a page comes back empty, so a server that keeps answering <c>hasMore</c> can't loop forever.
    /// </summary>
    public static async Task<List<T>> ReadAllAsync<T>(Func<int, int, Task<(IReadOnlyList<T> Items, bool HasMore)>> page)
    {
        var all = new List<T>();
        for (int skip = 0; ; skip += PageSize)
        {
            var (items, hasMore) = await page(skip, PageSize);
            all.AddRange(items);
            if (!hasMore || items.Count == 0)
                return all;
        }
    }
}
