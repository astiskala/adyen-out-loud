using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

public sealed class InstanceIdentityService(Uri relayBaseUrl, IInstanceTokenStore tokenStore) : IInstanceIdentityService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private InstanceIdentity? _identity;

    public async Task<InstanceIdentity> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_identity is not null) return _identity;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_identity is not null) return _identity;

            string? token = null;
            try
            {
                token = await tokenStore.GetAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                tokenStore.Remove();
            }

            if (!DeviceIdentityGenerator.IsValid(token))
            {
                if (token is not null) tokenStore.Remove();
                token = DeviceIdentityGenerator.Create();
                await tokenStore.SetAsync(token).ConfigureAwait(false);
            }

            _identity = RelayEndpointFactory.Create(relayBaseUrl, token);
            return _identity;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
