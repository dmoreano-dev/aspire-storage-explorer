namespace StorageExplorer.Web.Tables;

internal sealed record TableSummary(string Name);

/// <param name="Columns">
/// The column names to show, in display order: <c>PartitionKey</c> and <c>RowKey</c> first, then every other key found
/// in <paramref name="Entities"/> (alphabetically), then <c>Timestamp</c> last. Entities have no fixed schema, so this
/// is only the union of the keys of the rows in this page, not of the whole table.
/// </param>
/// <param name="Entities">
/// Each entity as its property bag. A row can be missing some of <paramref name="Columns"/> when another entity in
/// the page has properties this one does not.
/// </param>
/// <param name="ContinuationToken">
/// Pass this back as the next request's <c>continuationToken</c> to get the next page, or <c>null</c> when this was
/// the last one.
/// </param>
internal sealed record EntityPage(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Entities,
    string? ContinuationToken);

/// <param name="Name">The property's name, unique within the entity, not one of PartitionKey/RowKey/Timestamp/odata.etag.</param>
/// <param name="Type">One of "String", "Number", "Boolean", "DateTime" or "Guid" (see <see cref="EntityPropertyParser"/>).</param>
/// <param name="Value">
/// The value as typed in the UI, still a string: a number or GUID as its literal text, a boolean as "true"/"false", a
/// date as whatever <c>Date.prototype.toISOString</c> produced client-side.
/// </param>
internal sealed record EntityPropertyInput(string Name, string Type, string? Value);

internal sealed record CreateEntityRequest(string? PartitionKey, string? RowKey, IReadOnlyList<EntityPropertyInput>? Properties);
