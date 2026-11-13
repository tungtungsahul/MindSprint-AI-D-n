namespace MindSprint.Api;

public static class SecurityConfiguration
{
    public static string GetConnectionString(IConfiguration configuration, bool isProduction)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Default must be configured.");
        return connectionString;
    }

    public static string GetJwtKey(IConfiguration configuration, bool isProduction)
    {
        var jwtKey = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32 ||
            (isProduction && jwtKey.Contains("DOI_KHOA_NAY", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Set Jwt__Key to a secret value with at least 32 characters.");
        return jwtKey;
    }

    private static bool IsLoopbackOrigin(string origin)
    {
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
               (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("[::1]", StringComparison.OrdinalIgnoreCase));
    }

    public static string[] GetCorsOrigins(IConfiguration configuration, bool isProduction)
    {
        var defaults = isProduction ? Array.Empty<string>() : configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        var origins = defaults
            .Concat((configuration["Cors:AllowedOrigins"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                (uri.Scheme is "http" or "https") && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var nonLoopbackOrigins = origins.Where(origin => !IsLoopbackOrigin(origin)).ToArray();
        if (isProduction && nonLoopbackOrigins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https"))
            throw new InvalidOperationException("Cors__AllowedOrigins must use HTTPS origins in Production, except loopback localhost origins for the laptop same-origin deployment.");
        if (isProduction && origins.Length == 0)
            throw new InvalidOperationException("Set Cors__AllowedOrigins to the frontend origin(s), separated by commas.");
        return origins;
    }
}