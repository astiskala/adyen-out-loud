namespace AdyenOutLoud.Abstractions;

public interface IInstanceTokenStore
{
    Task<string?> GetAsync();
    Task SetAsync(string token);
    void Remove();
}
