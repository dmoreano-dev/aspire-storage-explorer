using Microsoft.Extensions.Configuration;


var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();

var blobs = storage.AddBlobs("blobs");

// Adds the "storage-explorer" resource; open it from the dashboard.
storage.WithStorageExplorer();

// Fills the emulator with sample containers, folders and blobs, then exits.
if (builder.Configuration.GetValue("Seed:Enabled", true))
{
    builder.AddProject<Projects.Sample_Seeder>("seeder")
        .WithReference(blobs)
        .WaitFor(blobs);
}

builder.Build().Run();
