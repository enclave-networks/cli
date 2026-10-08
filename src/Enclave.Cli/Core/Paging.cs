using Enclave.Api.Scaffolding.Pagination.Models;

namespace Enclave.Cli.Core;

/// <summary>
/// Reads every page of an Enclave.Sdk.Api list.
/// </summary>
internal static class Paging
{
    /// <summary>
    /// The page size every list asks for: the most the API returns per page (portal
    /// PaginationDefaults.cs:11).
    /// </summary>
    public const int PageSize = 200;

    /// <summary>
    /// Reads pages 0, 1, 2 and on, <see cref="PageSize"/> items each, until a response's
    /// metadata.nextPage is null, and returns every item in order. A failed page read throws, so a
    /// command prints nothing from a list it could not read whole (proposed-cli-surface.md "Output").
    /// </summary>
    /// <typeparam name="T">The list's item model.</typeparam>
    /// <param name="readPage">Reads one page: the page number and page size go to the list
    /// method's pageNumber and perPage, as in
    /// <c>(page, perPage) => org.Client.Policies.GetPoliciesAsync(search, pageNumber: page, perPage: perPage)</c>.</param>
    /// <param name="cancellationToken">Stops reading.</param>
    public static async Task<List<T>> ReadAllAsync<T>(Func<int, int, Task<PaginatedResponseModel<T>>> readPage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readPage);

        var items = new List<T>();
        int? page = 0;

        while (page is { } current)
        {
            var response = await readPage(current, PageSize);

            await foreach (var item in response.Items.WithCancellation(cancellationToken))
            {
                items.Add(item);
            }

            page = response.Metadata.NextPage;

            // A next page that does not move forwards would read the same pages forever.
            if (page <= current)
            {
                throw new CliException(ErrorCode.ApiError, $"The Enclave API gave page {current} a next page of {page}, which does not move forwards.");
            }
        }

        return items;
    }
}
