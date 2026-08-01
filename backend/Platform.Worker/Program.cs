using Platform.Worker;
using Platform.Worker.Extensions;

var hostBuilder = WorkerHostBuilder.Create(args);
hostBuilder.ConfigureServices((context, services) => services.AddCleanupJob(context.Configuration));

// Comfortably below the Helm chart's terminationGracePeriodSeconds default (30s,
// infra/helm/gaming-backend-platform/values.yaml) so the host always finishes an
// in-flight Quartz job and exits on its own, instead of Kubernetes cutting it
// short with SIGKILL once the grace period runs out.
hostBuilder.ConfigureHostOptions((_, options) => options.ShutdownTimeout = TimeSpan.FromSeconds(15));

await hostBuilder.Build().RunAsync();

public partial class Program;
