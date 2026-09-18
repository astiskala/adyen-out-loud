using AdyenOutLoud.Abstractions;
using Microsoft.Maui.Storage;

namespace AdyenOutLoud.Services;

public sealed class SecureStorageTokenStore : IInstanceTokenStore
{
    private const string TokenKey = "instance-token-v1";

    public Task<string?> GetAsync() => SecureStorage.Default.GetAsync(TokenKey);
    public Task SetAsync(string token) => SecureStorage.Default.SetAsync(TokenKey, token);
    public void Remove() => SecureStorage.Default.Remove(TokenKey);
}
