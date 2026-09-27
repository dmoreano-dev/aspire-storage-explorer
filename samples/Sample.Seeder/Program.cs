using System.Text;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;

// Seeds the storage emulator with sample data so the explorer has something to show.

// ---- Blobs ----

var blobConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__blobs")
    ?? throw new InvalidOperationException("ConnectionStrings__blobs is not set. Run this project through the AppHost.");

var blobService = new BlobServiceClient(blobConnectionString);

// 1x1 transparent PNG.
var pixel = Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

var blobs = new List<(string Container, string Path, string ContentType, byte[] Content)>
{
    ("documents", "readme.md", "text/markdown", Text("# Sample documents")),
    ("documents", "reports/2025/q1.txt", "text/plain", Text("Q1 2025 report")),
    ("documents", "reports/2025/q2.txt", "text/plain", Text("Q2 2025 report")),
    ("documents", "reports/2026/q1.csv", "text/csv", Text("id,total\n1,100\n2,250\n")),
    ("documents", "invoices/invoice #001 (final).json", "application/json", Text("""{"id":1,"total":100}""")),
    ("images", "logos/pixel.png", "image/png", pixel),
    ("images", "pixel-at-root.png", "image/png", pixel),
};

for (var i = 1; i <= 25; i++)
    blobs.Add(("logs", $"2026/09/26/app-{i:00}.log", "text/plain", Text($"log file {i}")));

foreach (var group in blobs.GroupBy(blob => blob.Container))
{
    var container = blobService.GetBlobContainerClient(group.Key);
    await container.CreateIfNotExistsAsync();

    foreach (var (_, path, contentType, content) in group)
    {
        await container.GetBlobClient(path).UploadAsync(
            new BinaryData(content),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } });
    }
}

Console.WriteLine($"Seeded {blobs.Count} blobs in {blobs.Select(blob => blob.Container).Distinct().Count()} containers.");

// ---- Queues ----

var queueConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__queues")
    ?? throw new InvalidOperationException("ConnectionStrings__queues is not set. Run this project through the AppHost.");

var queueService = new QueueServiceClient(queueConnectionString);

// A mix of plain text and Base64-encoded messages (what a Functions trigger or another SDK typically leaves
// behind), so the explorer's decode heuristic has both kinds to show.
var orderEvents = queueService.GetQueueClient("order-events");
await orderEvents.CreateIfNotExistsAsync();
await orderEvents.SendMessageAsync("Plain text order #1042 shipped");
await orderEvents.SendMessageAsync(Base64("""{"type":"order.created","orderId":1043,"total":250}"""));
await orderEvents.SendMessageAsync(Base64("""{"type":"order.paid","orderId":1043,"method":"card"}"""));

var emailOutbox = queueService.GetQueueClient("email-outbox");
await emailOutbox.CreateIfNotExistsAsync();
await emailOutbox.SendMessageAsync(Base64("""{"to":"ada.torres@example.com","template":"welcome"}"""));

// Created empty, so the explorer also has a queue with nothing to peek to show.
await queueService.GetQueueClient("poison-messages").CreateIfNotExistsAsync();

Console.WriteLine("Seeded 4 messages across order-events and email-outbox (poison-messages is empty).");

// ---- Tables ----

var tableConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__tables")
    ?? throw new InvalidOperationException("ConnectionStrings__tables is not set. Run this project through the AppHost.");

var tableService = new TableServiceClient(tableConnectionString);

var customers = tableService.GetTableClient("customers");
await customers.CreateIfNotExistsAsync();

// Varied on purpose: a missing Email shows the explorer's "—" for a column another row has, Active exercises the
// boolean chip, and two partitions (eu/us) give the OData filter example (Country eq 'ES' and Total ge 100) real
// rows to find.
var customerEntities = new List<TableEntity>
{
    new("eu", "c-0001") { { "Name", "Ada Torres" }, { "Email", "ada.torres@example.com" }, { "Country", "ES" }, { "Total", 250.0 }, { "Active", true } },
    new("eu", "c-0002") { { "Name", "Marco Bianchi" }, { "Email", "marco.bianchi@example.com" }, { "Country", "IT" }, { "Total", 100.0 }, { "Active", true } },
    new("eu", "c-0003") { { "Name", "Lena Fischer" }, { "Email", "lena.fischer@example.com" }, { "Country", "DE" }, { "Total", 175.5 }, { "Active", false } },
    new("eu", "c-0004") { { "Name", "Jonas Berg" }, { "Country", "SE" }, { "Total", 90.0 }, { "Active", true } },
    new("us", "c-0101") { { "Name", "Priya Nair" }, { "Email", "priya.nair@example.com" }, { "Country", "US" }, { "Total", 410.0 }, { "Active", true } },
    new("us", "c-0102") { { "Name", "Sam Whitaker" }, { "Email", "sam.whitaker@example.com" }, { "Total", 265.0 }, { "Active", false } },
};

foreach (var entity in customerEntities)
    await customers.AddEntityAsync(entity);

// Created empty, so the explorer also has a table with nothing in it to show.
await tableService.GetTableClient("orders").CreateIfNotExistsAsync();

Console.WriteLine($"Seeded {customerEntities.Count} entities in customers (orders is empty).");

static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);

static string Base64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
