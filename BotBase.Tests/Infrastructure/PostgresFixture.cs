using Testcontainers.PostgreSql;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace BotBase.Tests.Infrastructure;

// Один контейнер PostgreSQL на все тесты; каждый тест получает свою чистую базу.
public class PostgresFixture : IAsyncLifetime
{
    readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public string NewDatabase() =>
        new Npgsql.NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = "t_" + Guid.NewGuid().ToString("N")[..12] }.ToString();
}

[CollectionDefinition("db")]
public class DbCollection : ICollectionFixture<PostgresFixture>;
