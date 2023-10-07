# Aspire.Npgsql library

Registers [NpgsqlDataSource](https://www.npgsql.org/doc/api/Npgsql.NpgsqlDataSource.html) in the DI container for connecting PostgreSQL database. Enables corresponding health check, metrics, logging and telemetry..

## Getting started

### Prerequisites

- PostgreSQL database and connection string for accessing the database.

### Install the package

Install the Aspire PostgreSQL Npgsql library with [NuGet][nuget]:

```dotnetcli
dotnet add package Aspire.Npgsql
```

## Usage Example

In the `Program.cs` file of your project, call the `AddNpgsqlDataSource` extension method to register a `NpgsqlDataSource` for use via the dependency injection container.

```cs
builder.AddNpgsqlDataSource();
```

You can then retrieve the `NpgsqlDataSource` instance using dependency injection. For example, to retrieve the cache from a Web API controller:

```cs
private readonly NpgsqlDataSource _dataSource;

public ProductsController(NpgsqlDataSource dataSource)
{
    _dataSource = dataSource;
}
```

## Configuration

The Aspire PostgreSQL Npgsql component provides multiple options to configure the database connection based on the requirements and conventions of your project.

### Use a connection string

When using a connection string from the `ConnectionStrings` configuration section, you can provide the name of the connection string when calling `builder.AddNpgsqlDataSource()`:

```cs
builder.AddNpgsqlDataSource("myConnection");
```

And then the connection string will be retrieved from the `ConnectionStrings` configuration section:

```json
{
  "ConnectionStrings": {
    "myConnection": "Host=myserver;Database=test"
  }
}
```

See the [ConnectionString documentation](https://www.npgsql.org/doc/connection-string-parameters.html) for more information on how to format this connection string.

### Use configuration providers

The Aspire PostgreSQL Npgsql component supports [Microsoft.Extensions.Configuration](https://learn.microsoft.com/dotnet/api/microsoft.extensions.configuration). It loads the `NpgsqlSettings` from configuration by using the `Aspire:Npgsql` key. Example `appsettings.json` that configures some of the options:

```json
{
  "Aspire": {
    "Npgsql": {
      "ConnectionString": "Host=myserver;Database=test",
      "Metrics": false
    }
  }
}
```

### Use inline delegates

Also you can pass the `Action<NpgsqlSettings> configureSettings` delegate to set up some or all the options inline, for example to use a connection string from code:

```cs
    builder.AddNpgsqlDataSource(configureSettings: settings => settings.ConnectionString = "Host=myserver;Database=test");
```

## App Extensions

In your App project, register a Postgres container and consume the connection using the following methods:

```cs
var postgres = builder.AddPostgresContainer("postgresdb");

var myService = builder.AddProject<YourApp.Projects.MyService>()
                       .WithPostgresDatabase(postgres, databaseName: "test")
```

`.WithPostgresDatabase` configures a connection in the `MyService` project named `postgresdb`. In the `Program.cs` file of `MyService`, the database connection can be consumed using:

```cs
builder.AddNpgsqlDataSource("postgresdb");
```

## Additional documentation

* https://www.npgsql.org/doc/basic-usage.html
* https://github.com/dotnet/astra/tree/main/src/Components/README.md

## Feedback & Contributing

https://github.com/dotnet/astra