using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace AvevaMcp.Server;

internal static class BridgeClient
{
    private const string PipeName = "AvevaUE41Mcp";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static long _nextRequestId;

    public static async Task<string> CallAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            await pipe.ConnectAsync(3000, cancellationToken);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException(
                "Could not connect to the AVEVA UE 4.1 CAF add-in. Start UE and verify that AvevaMcp.CafAddin is loaded.",
                exception);
        }

        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true
        };

        var request = new BridgeRequest(
            Interlocked.Increment(ref _nextRequestId),
            method,
            parameters);

        await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
        var responseLine = await reader.ReadLineAsync(cancellationToken);
        if (responseLine is null)
        {
            throw new IOException("The AVEVA CAF add-in closed the named pipe without returning a response.");
        }

        using var response = JsonDocument.Parse(responseLine);
        var root = response.RootElement;
        if (!root.TryGetProperty("id", out var responseId) ||
            responseId.GetInt64() != request.Id)
        {
            throw new InvalidDataException("The AVEVA CAF add-in returned a mismatched request id.");
        }

        if (root.TryGetProperty("error", out var error) &&
            error.ValueKind != JsonValueKind.Null)
        {
            var message = error.TryGetProperty("message", out var errorMessage)
                ? errorMessage.GetString()
                : error.ToString();
            throw new InvalidOperationException(message ?? "The AVEVA CAF operation failed.");
        }

        if (!root.TryGetProperty("result", out var result))
        {
            throw new InvalidDataException("The AVEVA CAF add-in returned an invalid response.");
        }

        return result.GetRawText();
    }

    private sealed record BridgeRequest(long Id, string Method, object Parameters);
}
