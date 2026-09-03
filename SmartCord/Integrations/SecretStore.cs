using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SmartCord.Integrations;

/// <summary>
/// A tiny key/value secret bag DPAPI-encrypted to the current Windows user,
/// stored at <see cref="AppPaths.SecretsFile"/>. Holds OAuth client credentials,
/// access / refresh tokens and pasted API keys — anything that must not sit in
/// plaintext next to the exe or in appsettings.
/// </summary>
public sealed class SecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SmartCord.secrets.v1");

    private readonly ILogger<SecretStore>? _logger;
    private readonly object _gate = new();
    private Dictionary<string, string> _values;

    public SecretStore(ILogger<SecretStore>? logger = null)
    {
        _logger = logger;
        _values = Load();
    }

    public bool Has(string key)
    {
        lock (_gate)
        {
            return _values.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v);
        }
    }

    public string? Get(string key)
    {
        lock (_gate)
        {
            return _values.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : null;
        }
    }

    public void Set(string key, string? value)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(value))
            {
                _values.Remove(key);
            }
            else
            {
                _values[key] = value;
            }
            Save();
        }
    }

    public void RemoveByPrefix(string prefix)
    {
        lock (_gate)
        {
            foreach (var key in _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                _values.Remove(key);
            }
            Save();
        }
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SecretsFile))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var cipher = File.ReadAllBytes(AppPaths.SecretsFile);
            var plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plain);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not read secrets file; starting empty");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void Save()
    {
        try
        {
            AppPaths.EnsureCreated();
            var json = JsonSerializer.Serialize(_values);
            var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.CurrentUser);
            var tmp = AppPaths.SecretsFile + ".tmp";
            File.WriteAllBytes(tmp, cipher);
            File.Move(tmp, AppPaths.SecretsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not persist secrets file");
        }
    }
}
