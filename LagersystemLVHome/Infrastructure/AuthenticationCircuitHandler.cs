using Microsoft.AspNetCore.Components.Server.Circuits;
using LagersystemLVHome.Application.Services;

namespace LagersystemLVHome.Infrastructure;

public class AuthenticationCircuitHandler : CircuitHandler
{
    private readonly IServiceProvider _serviceProvider;

    public AuthenticationCircuitHandler(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        // Circuit opened - authentication state will be initialized
        return base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        // Connection restored
        return base.OnConnectionUpAsync(circuit, cancellationToken);
    }
}
