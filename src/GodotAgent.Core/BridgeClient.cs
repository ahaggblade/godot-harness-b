using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace GodotAgent.Core;

public sealed class BridgeClient
{
    public async Task<JsonNode> SendAsync(SessionManifest manifest, string method, JsonObject? payload, CancellationToken cancellationToken)
    {
        if (manifest.Port is null || string.IsNullOrWhiteSpace(manifest.Token))
        {
            throw new InvalidOperationException("The session does not expose a live daemon bridge.");
        }

        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync("127.0.0.1", manifest.Port.Value, cancellationToken);
        await using var stream = tcpClient.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

        await WriteEnvelopeAsync(writer, new BridgeEnvelope
        {
            Type = "hello",
            Role = "client",
            SessionId = manifest.SessionId,
            Token = manifest.Token,
        });

        await ReadEnvelopeAsync(reader, cancellationToken);

        var requestId = Guid.NewGuid().ToString("N");
        await WriteEnvelopeAsync(writer, new BridgeEnvelope
        {
            Type = "request",
            Id = requestId,
            Method = method,
            SessionId = manifest.SessionId,
            Token = manifest.Token,
            Params = payload,
        });

        var response = await ReadEnvelopeAsync(reader, cancellationToken);
        if (response.Error is not null)
        {
            throw new InvalidOperationException($"{response.Error.Code}: {response.Error.Message}");
        }

        return response.Result ?? new JsonObject();
    }

    private static async Task WriteEnvelopeAsync(StreamWriter writer, BridgeEnvelope envelope)
    {
        await writer.WriteLineAsync(JsonDefaults.SerializeCompact(envelope));
    }

    private static async Task<BridgeEnvelope> ReadEnvelopeAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() => reader.Dispose());
        var line = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new IOException("The daemon closed the bridge connection unexpectedly.");
        }

        return JsonDefaults.Deserialize<BridgeEnvelope>(line) ?? throw new IOException("Failed to parse bridge envelope.");
    }
}
