namespace MindSprint.Api;

public static class SecurityConfiguration
{
    public static string GetGeminiApiKey(IConfiguration configuration)
    {
        var path = configuration["Gemini:ApiKeyFile"];
        if (string.IsNullOrWhiteSpace(path)) return configuration["Gemini:ApiKey"] ?? "";
        try
        {
            if (new FileInfo(path).Length > 4096) throw new InvalidOperationException("Gemini secret file is invalid.");
            return File.ReadAllText(path).Trim();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("Cannot read Gemini secret file.");
        }
    }

    public static string GetConnectionString(IConfiguration configuration, bool isProduction)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Default must be configured.");
        if (isProduction && connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Set ConnectionStrings__Default to the production database connection string.");
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

    public static string[] GetCorsOrigins(IConfiguration configuration, bool isProduction)
    {
        var defaults = isProduction ? Array.Empty<string>() : configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        var origins = defaults
            .Concat((configuration["Cors:AllowedOrigins"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                (uri.Scheme is "http" or "https") && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (isProduction && origins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https"))
            throw new InvalidOperationException("Cors__AllowedOrigins must use HTTPS origins in Production.");
        // A bundled frontend calls the API on its own origin; no cross-origin access is needed.
        if (isProduction && origins.Length == 0 && !configuration.GetValue<bool>("Hosting:ServeFrontend"))
            throw new InvalidOperationException("Set Cors__AllowedOrigins to the public frontend origin(s), separated by commas.");
        return origins;
    }
}
