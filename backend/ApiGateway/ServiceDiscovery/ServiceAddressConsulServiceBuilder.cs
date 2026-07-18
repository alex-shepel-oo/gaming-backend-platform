using Consul;
using Ocelot.Logging;
using Ocelot.Provider.Consul;
using Ocelot.Provider.Consul.Interfaces;

namespace ApiGateway.ServiceDiscovery;

public sealed class ServiceAddressConsulServiceBuilder(
    IHttpContextAccessor contextAccessor, IConsulClientFactory clientFactory, IOcelotLoggerFactory loggerFactory)
    : DefaultConsulServiceBuilder(contextAccessor, clientFactory, loggerFactory)
{
    protected override string GetDownstreamHost(ServiceEntry entry, Node node) => entry.Service.Address;
}
