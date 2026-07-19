using Platform.Worker;
using Platform.Worker.Extensions;

var hostBuilder = WorkerHostBuilder.Create(args);
hostBuilder.ConfigureServices((context, services) => services.AddCleanupJob(context.Configuration));

await hostBuilder.Build().RunAsync();

public partial class Program;
