using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

// Seeds the storage emulator with sample data so the explorer has something to show.
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__blobs")
    ?? throw new InvalidOperationException("ConnectionStrings__blobs is not set. Run this project through the AppHost.");

var service = new BlobServiceClient(connectionString);

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
    var container = service.GetBlobContainerClient(group.Key);
    await container.CreateIfNotExistsAsync();

    foreach (var (_, path, contentType, content) in group)
    {
        await container.GetBlobClient(path).UploadAsync(
            new BinaryData(content),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } });
    }
}

Console.WriteLine($"Seeded {blobs.Count} blobs in {blobs.Select(blob => blob.Container).Distinct().Count()} containers.");

static byte[] Text(string value) => System.Text.Encoding.UTF8.GetBytes(value);
