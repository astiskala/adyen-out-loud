using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

public interface IInstanceIdentityService
{
    Task<InstanceIdentity> GetAsync(CancellationToken cancellationToken = default);
}
