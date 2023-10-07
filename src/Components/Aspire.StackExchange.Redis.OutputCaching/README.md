# Aspire.StackExchange.Redis.OutputCaching library

Registers an [ASP.NET Core Output Caching](https://learn.microsoft.com/aspnet/core/performance/caching/output) provider backed by a [Redis](https://redis.io/) server. Enables corresponding health check, logging, and telemetry.

## Getting started

### Prerequisites

- Redis server and the server hostname for connecting a client.

### Install the package

Install the Aspire StackExchange Redis OutputCache library with [NuGet][nuget]:

```dotnetcli
dotnet add package Aspire.StackExchange.Redis.OutputCaching
```

## Usage Example

In the `Program.cs` file of your project, call the `AddRedisOutputCache` extension to register the Redis output cache provider in the dependency injection container.

```cs
builder.AddRedisOutputCache();
```

After the `WebApplication` has been built, add the middleware to the request processing pipeline by calling [UseOutputCache](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.builder.outputcacheapplicationbuilderextensions.useoutputcache).

```cs
app.UseOutputCache();
```

For minimal API apps, configure an endpoint to do caching by calling [CacheOutput](https://learn.microsoft.com/dotnet/api/microsoft.extensions.dependencyinjection.outputcacheconventionbuilderextensions.cacheoutput), or by applying the [`[OutputCache]`](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.outputcaching.outputcacheattribute) attribute, as shown in the following examples:

```cs
app.MapGet("/cached", Gravatar.WriteGravatar).CacheOutput();
app.MapGet("/attribute", [OutputCache] (context) => 
    Gravatar.WriteGravatar(context));
```

For apps with controllers, apply the `[OutputCache]` attribute to the action method. For Razor Pages apps, apply the attribute to the Razor page class.

## Configuration

The Aspire StackExchange Redis OutputCache component provides multiple options to configure the Redis connection based on the requirements and conventions of your project. Note that at least one host name is required to connect.

### Use a connection string

When using a connection string from the `ConnectionStrings` configuration section, you can provide the name of the connection string when calling `builder.AddRedisOutputCache()`:

```cs
builder.AddRedisOutputCache("myRedisConnectionName");
```

And then the connection string will be retrieved from the `ConnectionStrings` configuration section:

```json
{
  "ConnectionStrings": {
    "myRedisConnectionName": "localhost:6379"
  }
}
```

See the [Basic Configuration Settings](https://stackexchange.github.io/StackExchange.Redis/Configuration.html#basic-configuration-strings) of the StackExchange.Redis docs for more information on how to format this connection string.

### Use configuration providers

The Redis OutputCache component supports [Microsoft.Extensions.Configuration](https://learn.microsoft.com/dotnet/api/microsoft.extensions.configuration). It loads the `StackExchangeRedisSettings` and `ConfigurationOptions` from configuration by using the `Aspire:StackExchange:Redis` key. Example `appsettings.json` that configures some of the options:

```json
{
  "Aspire": {
    "StackExchange": {
      "Redis": {
        "ConnectionString": "localhost:6379",
        "ConfigurationOptions": {
          "ConnectTimeout": 3000,
          "ConnectRetry": 2
        },
        "Tracing": false
      }
    }
  }
}
```

### Use inline delegates

You can also pass the `Action<StackExchangeRedisSettings> configureSettings` delegate to set up some or all the options inline, for example to use a connection string from code:

```cs
builder.AddRedisOutputCache(configureSettings: settings => settings.ConnectionString = "localhost:6379");
```

You can also setup the [ConfigurationOptions](https://stackexchange.github.io/StackExchange.Redis/Configuration.html#configuration-options) using the `Action<ConfigurationOptions> configureOptions` delegate parameter of the `AddRedisOutputCache` method. For example to set the connection timeout:

```cs
builder.AddRedisOutputCache(configureOptions: options => options.ConnectTimeout = 3000);
```

## App Extensions

In your App project, register a Redis container and consume the connection using the following methods:

```cs
var redis = builder.AddRedisContainer("cache");

var myService = builder.AddProject<YourApp.Projects.MyService>()
                       .WithRedis(redis);
```

`.WithRedis` configures a connection in the `MyService` project named `cache`. In the `Program.cs` file of `MyService`, the redis connection can be consumed using:

```cs
builder.AddRedisOutputCache("cache");
```

## Additional documentation

* https://learn.microsoft.com/aspnet/core/performance/caching/output
* https://github.com/dotnet/astra/tree/main/src/Components/README.md

## Feedback & Contributing

https://github.com/dotnet/astra