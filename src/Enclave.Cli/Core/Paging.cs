using Enclave.Api.Scaffolding.Pagination.Models;

namespace Enclave.Cli.Core;

internal static class Paging
{
    // The most the API returns per page (portal PaginationDefaults.MaxPageSize).
    public const int PageSize = 200;

    // readPage takes the page number, then the page size. A failed page read throws, so a command
    // prints nothing from a list it could not read whole (spec/cli-surface.md "Output").
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
