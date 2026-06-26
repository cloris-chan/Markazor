using System.Diagnostics.CodeAnalysis;
using Markazor.Api.Functions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace MarkazorSite.Functions.Functions;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Azure Functions worker activates function classes through metadata and dependency injection.")]
internal sealed class SetupStatusFunction(MarkazorFunctionsEndpointService endpoints)
{
    [Function("SetupStatus")]
    public HttpResponseData Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "setup/status")] HttpRequestData request)
    {
        return endpoints.GetSetupStatus(request);
    }
}
