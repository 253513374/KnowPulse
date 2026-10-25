using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace KnowPulse.RagAI;

public class QdrantProductionRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_Use_Configured_Qdrant_Client_When_Default_HttpClient_Is_Registered(bool maintenance)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        var keyVariable = $"KNOWPULSE_TEST_QDRANT_KEY_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(keyVariable, "test-qdrant-key");
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["RagAI:VectorStore:Endpoint"] = endpoint,
                    ["RagAI:VectorStore:KnowledgeCollectionName"] = "registration-test",
                    ["RagAI:VectorStore:VectorDimension"] = "2",
                    ["RagAI:VectorStore:ApiKeyEnvironmentVariable"] = keyVariable
                }).Build());
            services.AddHttpClient();
            new KnowPulseApplicationModule().ConfigureServices(new ServiceConfigurationContext(services));
            await using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<HttpClient>().BaseAddress.ShouldBeNull();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = RespondOnceAsync(listener, timeout.Token);
            if (maintenance)
            {
                var health = await provider.GetRequiredService<IKnowledgeVectorStoreMaintenance>()
                    .GetKnowledgeCollectionHealthAsync(timeout.Token);
                health.CollectionExists.ShouldBeTrue();
                health.VectorDimensionMatches.ShouldBeTrue();
            }
            else
            {
                await provider.GetRequiredService<IKnowledgeVectorStore>().UpsertChunksAsync(
                    [new KnowledgeChunkVectorRecord { ChunkId = Guid.NewGuid(), Embedding = new float[] { 0.1f, 0.2f } }],
                    timeout.Token);
            }

            var request = await response;
            request.RequestLine.ShouldBe(maintenance
                ? "GET /collections/registration-test HTTP/1.1"
                : "PUT /collections/registration-test/points?wait=true HTTP/1.1");
            request.ApiKey.ShouldBe("test-qdrant-key");
        }
        finally
        {
            Environment.SetEnvironmentVariable(keyVariable, null);
        }
    }

    private static async Task<(string? RequestLine, string? ApiKey)> RespondOnceAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        using var connection = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken);
        string? apiKey = null;
        var contentLength = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 } header)
        {
            if (header.StartsWith("api-key:", StringComparison.OrdinalIgnoreCase)) apiKey = header[8..].Trim();
            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) contentLength = int.Parse(header[15..].Trim());
        }
        if (contentLength > 0) await reader.ReadBlockAsync(new char[contentLength], cancellationToken);

        const string json = "{\"result\":{\"config\":{\"params\":{\"vectors\":{\"size\":2}}},\"payload_schema\":{}}}";
        var reply = Encoding.UTF8.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(json)}\r\nConnection: close\r\n\r\n{json}");
        await stream.WriteAsync(reply, cancellationToken);
        return (requestLine, apiKey);
    }
}
