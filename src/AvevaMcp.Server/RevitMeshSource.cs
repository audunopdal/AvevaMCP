using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace AvevaMcp.Server;

internal sealed record RevitMesh(
    long ElementId, string? UniqueId, string? Name, string? Category, string Status,
    double[] OriginShared, double[][] Triangles, int MaterialCount);

// Reads meshes directly from the RevitMCP add-in pipe so large triangle sets never pass through the agent.
internal static class RevitMeshSource
{
    public const int MaximumTriangles = 20000;
    private const int MaxMessageBytes = 1024 * 1024;
    private const int PageSize = 100;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<RevitMesh> ReadAsync(long elementId, int? revitProcessId, CancellationToken cancellationToken)
    {
        if (elementId <= 0)
            throw new ArgumentException("revitElementId must be a positive 64-bit integer.");

        int processId = ResolveProcessId(revitProcessId);
        var triangles = new List<double[]>();
        var materials = new HashSet<long>();
        double[]? origin = null;
        string? uniqueId = null, name = null, category = null, status = null;
        int offset = 0;
        while (true)
        {
            using JsonDocument page = await SendAsync(processId,
                new { operation = "get_element_mesh", elementId, offset, limit = PageSize }, cancellationToken);
            JsonElement root = page.RootElement;
            JsonElement element = root.GetProperty("element");
            status = element.GetProperty("status").GetString() ?? "";
            if (status != "mesh_fallback")
                throw new InvalidOperationException(
                    $"Revit element {elementId} has no exportable mesh (status '{status}': {element.GetProperty("reason").GetString()}).");
            JsonElement info = element.GetProperty("element");
            uniqueId = info.GetProperty("uniqueId").GetString();
            name = info.GetProperty("name").GetString();
            category = info.TryGetProperty("category", out JsonElement categoryValue) ? categoryValue.GetString() : null;

            string unit = root.GetProperty("unit").GetString() ?? "";
            double scale = unit switch
            {
                "mm" => 1.0,
                "ft" => 304.8,
                _ => throw new InvalidDataException($"Unsupported Revit mesh unit '{unit}'.")
            };
            double[] pageOrigin = ReadPoint(root.GetProperty("originShared"), scale);
            if (origin is null)
                origin = pageOrigin;
            else if (!pageOrigin.SequenceEqual(origin))
                throw new InvalidDataException("Revit returned different shared origins for pages of the same mesh.");

            JsonElement trianglePage = root.GetProperty("triangles");
            int total = trianglePage.GetProperty("total").GetInt32();
            if (total > MaximumTriangles)
                throw new InvalidOperationException($"Revit element {elementId} has {total} triangles; the import limit is {MaximumTriangles}.");
            foreach (JsonElement triangle in trianglePage.GetProperty("items").EnumerateArray())
            {
                double[] a = ReadPoint(triangle.GetProperty("a"), scale);
                double[] b = ReadPoint(triangle.GetProperty("b"), scale);
                double[] c = ReadPoint(triangle.GetProperty("c"), scale);
                triangles.Add(
                [
                    origin[0] + a[0], origin[1] + a[1], origin[2] + a[2],
                    origin[0] + b[0], origin[1] + b[1], origin[2] + b[2],
                    origin[0] + c[0], origin[1] + c[1], origin[2] + c[2]
                ]);
                if (triangle.TryGetProperty("materialId", out JsonElement material) && material.ValueKind == JsonValueKind.Number)
                    materials.Add(material.GetInt64());
            }

            JsonElement next = trianglePage.GetProperty("nextOffset");
            if (next.ValueKind == JsonValueKind.Null)
                break;
            int nextOffset = next.GetInt32();
            if (nextOffset <= offset)
                throw new InvalidDataException("Revit returned a non-advancing mesh page offset.");
            offset = nextOffset;
        }

        if (triangles.Count == 0)
            throw new InvalidOperationException($"Revit element {elementId} returned no triangles.");
        return new RevitMesh(elementId, uniqueId, name, category, status!, origin!, triangles.ToArray(), materials.Count);
    }

    private static int ResolveProcessId(int? requested)
    {
        Process[] processes = Process.GetProcessesByName("Revit");
        try
        {
            if (requested is { } id)
            {
                if (!processes.Any(process => process.Id == id))
                    throw new InvalidOperationException($"No running Revit process has PID {id}.");
                return id;
            }

            return processes.Length switch
            {
                0 => throw new InvalidOperationException("Revit is not running. Start Revit 2026 with the RevitMCP add-in and open a model."),
                1 => processes[0].Id,
                _ => throw new InvalidOperationException(
                    $"Multiple Revit processes are running ({string.Join(", ", processes.Select(process => process.Id))}); specify revitProcessId.")
            };
        }
        finally
        {
            foreach (Process process in processes)
                process.Dispose();
        }
    }

    private static async Task<JsonDocument> SendAsync(int processId, object request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        await using var pipe = new NamedPipeClientStream(".", $"RevitMCP-2026-{processId}", PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(3000, timeout.Token);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException(
                $"Could not connect to the RevitMCP add-in in Revit PID {processId}. Verify that the add-in is loaded and Revit is idle.", exception);
        }

        byte[] body = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        byte[] header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await pipe.WriteAsync(header, timeout.Token);
        await pipe.WriteAsync(body, timeout.Token);
        await pipe.FlushAsync(timeout.Token);

        await pipe.ReadExactlyAsync(header, timeout.Token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxMessageBytes)
            throw new InvalidDataException("The RevitMCP add-in returned an invalid message length.");
        byte[] response = new byte[length];
        await pipe.ReadExactlyAsync(response, timeout.Token);

        using JsonDocument envelope = JsonDocument.Parse(response);
        JsonElement root = envelope.RootElement;
        if (root.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.String)
            throw new InvalidOperationException("Revit: " + error.GetString());
        if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The RevitMCP add-in returned no data.");
        return JsonDocument.Parse(data.GetRawText());
    }

    private static double[] ReadPoint(JsonElement point, double scale)
    {
        double[] values = [point.GetProperty("x").GetDouble() * scale, point.GetProperty("y").GetDouble() * scale,
            point.GetProperty("z").GetDouble() * scale];
        if (values.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("Revit returned a non-finite mesh coordinate.");
        return values;
    }
}
