namespace Pix.Processing.Application.Ports;

public interface ILocalCachePort
{
    bool TryGetValue<T>(string key, out T? value);
    void Set<T>(string key, T value, TimeSpan ttl);
    void Remove(string key);
}
