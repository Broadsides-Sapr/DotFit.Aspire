// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Aspire.Hosting.Dcp.Model;
using k8s;
using k8s.Exceptions;
using k8s.Models;

namespace Aspire.Hosting.Dcp;

public enum DcpApiOperationType
{
    Create = 1,
    List = 2,
    Delete = 3,
    Watch = 4
}

public class KubernetesService : IDisposable
{
    private static readonly TimeSpan s_initialRetryDelay = TimeSpan.FromMilliseconds(100);
    private static GroupVersion GroupVersion => Model.Dcp.GroupVersion;

    private IKubernetes? _kubernetes;
    private TimeSpan _maxRetryDuration = TimeSpan.FromMilliseconds(1000);

    public KubernetesService(TimeSpan maxRetryDuration)
    {
        this.MaxRetryDuration = maxRetryDuration;
    }

    public KubernetesService() { }

    public TimeSpan MaxRetryDuration
    {
        get
        {
            return _maxRetryDuration;
        }
        set
        {
            _maxRetryDuration = value;
        }
    }

    public Task<T> CreateAsync<T>(T obj, CancellationToken cancellationToken = default)
        where T : CustomResource
    {
        var resourceType = GetResourceFor<T>();
        var namespaceParameter = obj.Namespace();

        return ExecuteWithRetry(
           DcpApiOperationType.Create,
           resourceType,
           async (kubernetes) =>
           {
               var response = string.IsNullOrEmpty(namespaceParameter)
                ? await kubernetes.CustomObjects.CreateClusterCustomObjectWithHttpMessagesAsync(
                    obj,
                    GroupVersion.Group,
                    GroupVersion.Version,
                    resourceType,
                    cancellationToken: cancellationToken).ConfigureAwait(false)
                : await kubernetes.CustomObjects.CreateNamespacedCustomObjectWithHttpMessagesAsync(
                    obj,
                    GroupVersion.Group,
                    GroupVersion.Version,
                    namespaceParameter,
                    resourceType,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

               return KubernetesJson.Deserialize<T>(response.Body.ToString());
           },
           cancellationToken);
    }

    public Task<List<T>> ListAsync<T>(string? namespaceParameter = null, CancellationToken cancellationToken = default)
        where T : CustomResource
    {
        var resourceType = GetResourceFor<T>();

        return ExecuteWithRetry(
            DcpApiOperationType.List,
            resourceType,
            async (kubernetes) =>
            {
                var response = string.IsNullOrEmpty(namespaceParameter)
                    ? await kubernetes.CustomObjects.ListClusterCustomObjectWithHttpMessagesAsync(
                        GroupVersion.Group,
                        GroupVersion.Version,
                        resourceType,
                        cancellationToken: cancellationToken).ConfigureAwait(false)
                    : await kubernetes.CustomObjects.ListNamespacedCustomObjectWithHttpMessagesAsync(
                        GroupVersion.Group,
                        GroupVersion.Version,
                        namespaceParameter,
                        resourceType,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                return KubernetesJson.Deserialize<CustomResourceList<T>>(response.Body.ToString()).Items;
            },
            cancellationToken);
    }

    public Task<T> DeleteAsync<T>(string name, string? namespaceParameter = null, CancellationToken cancellationToken = default)
        where T : CustomResource
    {
        var resourceType = GetResourceFor<T>();

        return ExecuteWithRetry(
            DcpApiOperationType.Delete,
            resourceType,
            async (kubernetes) =>
            {
                var response = string.IsNullOrEmpty(namespaceParameter)
                ? await kubernetes.CustomObjects.DeleteClusterCustomObjectWithHttpMessagesAsync(
                    GroupVersion.Group,
                    GroupVersion.Version,
                    resourceType,
                    name,
                    cancellationToken: cancellationToken).ConfigureAwait(false)
                : await kubernetes.CustomObjects.DeleteNamespacedCustomObjectWithHttpMessagesAsync(
                    GroupVersion.Group,
                    GroupVersion.Version,
                    namespaceParameter,
                    resourceType,
                    name,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                return KubernetesJson.Deserialize<T>(response.Body.ToString());
            },
            cancellationToken);
    }

    public async IAsyncEnumerable<(WatchEventType, T)> WatchAsync<T>(
        string? namespaceParameter = null,
        IEnumerable<NamespacedName>? existingObjects = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : CustomResource
    {
        var resourceType = GetResourceFor<T>();
        var result = await ExecuteWithRetry(
            DcpApiOperationType.Watch,
            resourceType,
            (kubernetes) =>
            {
                var responseTask = string.IsNullOrEmpty(namespaceParameter)
                    ? kubernetes.CustomObjects.ListClusterCustomObjectWithHttpMessagesAsync(
                        GroupVersion.Group,
                        GroupVersion.Version,
                        resourceType,
                        watch: true,
                        cancellationToken: cancellationToken)
                    : kubernetes.CustomObjects.ListNamespacedCustomObjectWithHttpMessagesAsync(
                        GroupVersion.Group,
                        GroupVersion.Version,
                        namespaceParameter,
                        resourceType,
                        watch: true,
                        cancellationToken: cancellationToken);

                return responseTask.WatchAsync<T, object>(null, cancellationToken);
            },
            cancellationToken).ConfigureAwait(false);

        await foreach (var item in result)
        {
            var (watchEventType, obj) = item;

            // Until the underlying API server starts returning resource version with the List calls, we need to
            // check against the existing set to make sure we don't send Added events for objects we already
            // know about.
            if (watchEventType != WatchEventType.Added ||
                existingObjects?.Any(o => string.Equals(obj.Metadata.Name, o.Name, StringComparison.Ordinal) &&
                                          string.Equals(obj.Metadata.NamespaceProperty, o.Namespace, StringComparison.Ordinal)) != true)
            {
                yield return item;
            }
        }
    }

    public void Dispose()
    {
        _kubernetes?.Dispose();
    }

    private static string GetResourceFor<T>() where T : CustomResource
    {
        if (!Model.Dcp.Schema.TryGet<T>(out var kindWithResource))
        {
            throw new InvalidOperationException($"Unknown custom resource type: {typeof(T).Name}");
        }

        return kindWithResource.Resource;
    }

    private Task<TResult> ExecuteWithRetry<TResult>(DcpApiOperationType operationType, string resourceType, Func<IKubernetes, TResult> operation, CancellationToken cancellationToken)
    {
        return ExecuteWithRetry<TResult>(operationType, resourceType, (IKubernetes kubernetes) => Task.FromResult(operation(kubernetes)), cancellationToken);
    }

    private async Task<TResult> ExecuteWithRetry<TResult>(DcpApiOperationType operationType, string resourceType, Func<IKubernetes, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        var currentTimestamp = DateTime.UtcNow;
        var delay = s_initialRetryDelay;
        AspireEventSource.Instance.DcpApiCallStart(operationType, resourceType);

        try
        {
            while (true)
            {
                try
                {
                    EnsureKubernetes();
                    return await operation(_kubernetes!).ConfigureAwait(false);
                }
                catch (Exception e) when (IsRetryable(e))
                {
                    if (DateTime.UtcNow.Subtract(currentTimestamp) > MaxRetryDuration)
                    {
                        AspireEventSource.Instance.DcpApiCallTimeout(operationType, resourceType);
                        throw;
                    }

                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    delay *= 2;
                    AspireEventSource.Instance.DcpApiCallRetry(operationType, resourceType);
                }
            }
        }
        finally
        {
            AspireEventSource.Instance.DcpApiCallStop(operationType, resourceType);
        }

    }

    private static bool IsRetryable(Exception ex) => ex is HttpRequestException || ex is KubeConfigException;

    private void EnsureKubernetes()
    {
        if (_kubernetes != null) { return; }

        lock (Model.Dcp.Schema)
        {
            if (_kubernetes != null) { return; }

            var config = KubernetesClientConfiguration.BuildConfigFromConfigFile(kubeconfigPath: Locations.DcpKubeconfigPath, useRelativePaths: false);
            _kubernetes = new Kubernetes(config);
        }
    }
}