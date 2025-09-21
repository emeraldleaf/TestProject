using Microsoft.AspNetCore.Mvc;

namespace TestProject.Services;

/// <summary>
/// Service for handling idempotency keys to prevent duplicate operations
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Check if an operation with the given idempotency key has already been performed
    /// </summary>
    /// <param name="idempotencyKey">The unique key for the operation</param>
    /// <returns>The cached result if found, null otherwise</returns>
    Task<IActionResult?> GetCachedResultAsync(string idempotencyKey);

    /// <summary>
    /// Store the result of an operation with the given idempotency key
    /// </summary>
    /// <param name="idempotencyKey">The unique key for the operation</param>
    /// <param name="result">The result to cache</param>
    /// <param name="expiry">How long to cache the result (default: 1 hour)</param>
    Task StoreCachedResultAsync(string idempotencyKey, IActionResult result, TimeSpan? expiry = null);

    /// <summary>
    /// Generate a default idempotency key based on operation parameters
    /// </summary>
    /// <param name="operation">The operation name</param>
    /// <param name="parameters">The operation parameters</param>
    /// <returns>A deterministic idempotency key</returns>
    string GenerateKey(string operation, params object[] parameters);

    /// <summary>
    /// Check if an idempotency key is valid format
    /// </summary>
    /// <param name="key">The key to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    bool IsValidKey(string? key);
}