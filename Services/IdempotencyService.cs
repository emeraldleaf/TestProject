using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TestProject.Services;

/// <summary>
/// In-memory implementation of idempotency service using IMemoryCache
/// For production, consider using distributed cache (Redis) for multi-instance deployments
/// </summary>
public class IdempotencyService : IIdempotencyService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<IdempotencyService> _logger;
    private const string CACHE_PREFIX = "idempotency:";
    private static readonly TimeSpan DEFAULT_EXPIRY = TimeSpan.FromHours(1);

    public IdempotencyService(IMemoryCache cache, ILogger<IdempotencyService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task<IActionResult?> GetCachedResultAsync(string idempotencyKey)
    {
        if (!IsValidKey(idempotencyKey))
        {
            return Task.FromResult<IActionResult?>(null);
        }

        var cacheKey = CACHE_PREFIX + idempotencyKey;

        if (_cache.TryGetValue(cacheKey, out var cachedData))
        {
            _logger.LogInformation("Idempotency cache hit for key: {Key}", idempotencyKey);

            if (cachedData is IdempotencyResult result)
            {
                return Task.FromResult<IActionResult?>(DeserializeResult(result));
            }
        }

        return Task.FromResult<IActionResult?>(null);
    }

    public Task StoreCachedResultAsync(string idempotencyKey, IActionResult result, TimeSpan? expiry = null)
    {
        if (!IsValidKey(idempotencyKey))
        {
            _logger.LogWarning("Attempted to store result with invalid idempotency key: {Key}", idempotencyKey);
            return Task.CompletedTask;
        }

        var cacheKey = CACHE_PREFIX + idempotencyKey;
        var expiryTime = expiry ?? DEFAULT_EXPIRY;

        var serializedResult = SerializeResult(result);

        _cache.Set(cacheKey, serializedResult, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiryTime,
            Priority = CacheItemPriority.Normal
        });

        _logger.LogInformation("Stored idempotency result for key: {Key}, expires in: {Expiry}",
            idempotencyKey, expiryTime);

        return Task.CompletedTask;
    }

    public string GenerateKey(string operation, params object[] parameters)
    {
        var keyData = new { operation, parameters, timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd") };
        var json = JsonSerializer.Serialize(keyData);

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(hashBytes)[..16]; // Use first 16 chars for shorter keys
    }

    public bool IsValidKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        // Key should be 8-128 characters, alphanumeric plus common safe characters
        if (key.Length < 8 || key.Length > 128)
            return false;

        return key.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '+' || c == '/' || c == '=');
    }

    private IdempotencyResult SerializeResult(IActionResult result)
    {
        return result switch
        {
            OkObjectResult okResult => new IdempotencyResult
            {
                Type = "OkObjectResult",
                StatusCode = 200,
                Data = JsonSerializer.Serialize(okResult.Value)
            },
            BadRequestObjectResult badResult => new IdempotencyResult
            {
                Type = "BadRequestObjectResult",
                StatusCode = 400,
                Data = JsonSerializer.Serialize(badResult.Value)
            },
            ObjectResult objResult => new IdempotencyResult
            {
                Type = "ObjectResult",
                StatusCode = objResult.StatusCode ?? 200,
                Data = JsonSerializer.Serialize(objResult.Value)
            },
            StatusCodeResult statusResult => new IdempotencyResult
            {
                Type = "StatusCodeResult",
                StatusCode = statusResult.StatusCode,
                Data = null
            },
            _ => new IdempotencyResult
            {
                Type = "Unknown",
                StatusCode = 200,
                Data = JsonSerializer.Serialize(new { message = "Cached result" })
            }
        };
    }

    private IActionResult DeserializeResult(IdempotencyResult cached)
    {
        return cached.Type switch
        {
            "OkObjectResult" => new OkObjectResult(
                cached.Data != null ? JsonSerializer.Deserialize<object>(cached.Data) : null),
            "BadRequestObjectResult" => new BadRequestObjectResult(
                cached.Data != null ? JsonSerializer.Deserialize<object>(cached.Data) : null),
            "ObjectResult" => new ObjectResult(
                cached.Data != null ? JsonSerializer.Deserialize<object>(cached.Data) : null)
            {
                StatusCode = cached.StatusCode
            },
            "StatusCodeResult" => new StatusCodeResult(cached.StatusCode),
            _ => new OkObjectResult(
                cached.Data != null ? JsonSerializer.Deserialize<object>(cached.Data) : new { message = "Cached result" })
        };
    }

    private class IdempotencyResult
    {
        public string Type { get; set; } = "";
        public int StatusCode { get; set; }
        public string? Data { get; set; }
    }
}