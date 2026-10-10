namespace DocFlow.Api.Common;

/// <summary>Maps to HTTP 404.</summary>
public class NotFoundException(string message) : Exception(message);

/// <summary>Maps to HTTP 403 — the user is authenticated but not allowed to do this.</summary>
public class ForbiddenException(string message) : Exception(message);

/// <summary>Maps to HTTP 409 — the request conflicts with the current state (e.g. invalid workflow transition).</summary>
public class BusinessRuleException(string message) : Exception(message);

/// <summary>Maps to HTTP 400.</summary>
public class BadRequestException(string message) : Exception(message);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public class JwtSettings
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 60;
}
