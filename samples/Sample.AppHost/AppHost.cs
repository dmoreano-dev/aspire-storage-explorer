using Microsoft.Extensions.Configuration;


var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();

var blobs = storage.AddBlobs("blobs");
var queues = storage.AddQueues("queues");
var tables = storage.AddTables("tables");

// Adds the "storage-explorer" resource; open it from the dashboard.
storage.WithStorageExplorer();

// Fills the emulator with sample containers, folders, blobs, queues, messages, tables and entities, then exits.
if (builder.Configuration.GetValue("Seed:Enabled", true))
{
    builder.AddProject<Projects.Sample_Seeder>("seeder")
        .WithReference(blobs)
        .WithReference(queues)
        .WithReference(tables)
        .WaitFor(blobs)
        .WaitFor(queues)
        .WaitFor(tables);
}

builder.Build().Run();
