// Only what the integration tests need: an Azurite to read and write. The explorer itself runs inside the test
// process, so the tests exercise the code in the working tree and not an image that was built earlier.
var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureStorage("storage")
    .RunAsEmulator();

builder.Build().Run();
