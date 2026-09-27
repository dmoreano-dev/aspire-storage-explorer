using Azure.Data.Tables;

namespace StorageExplorer.Web.Tables;

internal interface ITableExplorerService
{
    Task<IReadOnlyList<TableSummary>> ListTablesAsync(CancellationToken cancellationToken);

    /// <summary>Reads one page of up to <see cref="TableExplorerService.PageSize"/> entities.</summary>
    /// <param name="filter">An OData filter (for example <c>PartitionKey eq 'foo'</c>), or <c>null</c> for every entity.</param>
    /// <param name="continuationToken">
    /// From a previous <see cref="EntityPage.ContinuationToken"/>, to continue after it; <c>null</c> for the first page.
    /// </param>
    Task<EntityPage> QueryEntitiesAsync(string table, string? filter, string? continuationToken, CancellationToken cancellationToken);
}

internal sealed class TableExplorerService(IStorageConnection connection) : ITableExplorerService
{
    internal const int PageSize = 100;

    // Every entity carries these; PartitionKey and RowKey get a fixed place ahead of the entity's own properties, and
    // Timestamp a fixed place after them. odata.etag is dropped: it is metadata about the row, not a property of it,
    // and it duplicates Timestamp for anything this explorer needs.
    private static readonly string[] LeadingColumns = ["PartitionKey", "RowKey"];
    private const string TimestampColumn = "Timestamp";
    private const string ETagKey = "odata.etag";

    public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(CancellationToken cancellationToken)
    {
        var tables = new List<TableSummary>();

        await foreach (var table in connection.Table!.QueryAsync(cancellationToken: cancellationToken))
            tables.Add(new TableSummary(table.Name));

        return tables;
    }

    public async Task<EntityPage> QueryEntitiesAsync(
        string table,
        string? filter,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var pageable = connection.Table!.GetTableClient(table)
            .QueryAsync<TableEntity>(filter: string.IsNullOrEmpty(filter) ? null : filter, maxPerPage: PageSize, cancellationToken: cancellationToken);

        await using var pages = pageable.AsPages(continuationToken, PageSize).GetAsyncEnumerator(cancellationToken);

        if (!await pages.MoveNextAsync())
            return new EntityPage(LeadingColumns.Append(TimestampColumn).ToArray(), [], null);

        var page = pages.Current;
        var rows = page.Values.Select(ToRow).ToList();

        return new EntityPage(ColumnsOf(rows), rows, string.IsNullOrEmpty(page.ContinuationToken) ? null : page.ContinuationToken);
    }

    private static IReadOnlyDictionary<string, object?> ToRow(TableEntity entity)
    {
        var row = new Dictionary<string, object?>(entity.Count);

        foreach (var (key, value) in entity)
        {
            if (key != ETagKey)
                row[key] = value;
        }

        return row;
    }

    private static IReadOnlyList<string> ColumnsOf(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var custom = rows
            .SelectMany(row => row.Keys)
            .Where(key => Array.IndexOf(LeadingColumns, key) < 0 && key != TimestampColumn)
            .Distinct()
            .Order(StringComparer.Ordinal);

        return [.. LeadingColumns, .. custom, TimestampColumn];
    }
}
