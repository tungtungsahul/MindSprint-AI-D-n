using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Data.SqlClient;

namespace MindSprint.Api.Data;

// https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-8.0
public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        // Bound SQL Client reconnection as well as the health middleware timeout.
        if (connection is SqlConnection)
            connection.ConnectionString = new SqlConnectionStringBuilder(connection.ConnectionString)
                { ConnectTimeout = 2, ConnectRetryCount = 0 }.ConnectionString;
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        command.CommandTimeout = 2;
        await command.ExecuteScalarAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
