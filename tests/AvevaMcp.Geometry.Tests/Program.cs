using System;
using AvevaMcp.CafAddin;

internal static class Program
{
    private static readonly double[][] Vertices =
    {
        new[] { 0.0, 0, 0 }, new[] { 100.0, 0, 0 },
        new[] { 0.0, 100, 0 }, new[] { 0.0, 0, 100 }
    };

    private static readonly int[][] Faces =
    {
        new[] { 0, 2, 1 }, new[] { 0, 1, 3 },
        new[] { 0, 3, 2 }, new[] { 1, 2, 3 }
    };

    private static int _checks;

    private static void Main()
    {
        PolyhedronMesh.ValidateSurface(Vertices, Faces);
        double closedVolume;
        string reason;
        if (!PolyhedronMesh.TryValidateClosed(Vertices, Faces, out closedVolume, out reason))
        {
            throw new Exception("Outward tetrahedron was rejected: " + reason);
        }
        Near(closedVolume, 1000000.0 / 6, 1e-8);
        var translated = new double[4][];
        for (int i = 0; i < 4; i++)
        {
            translated[i] = new[] { Vertices[i][0] + 3263493.2, Vertices[i][1] + 1634139.99, Vertices[i][2] + 104330.0 };
        }
        if (!PolyhedronMesh.TryValidateClosed(translated, Faces, out closedVolume, out reason))
        {
            throw new Exception("Translated tetrahedron was rejected: " + reason);
        }
        Near(closedVolume, 1000000.0 / 6, 1e-6);

        int[][] openTriangle = { new[] { 0, 2, 1 } };
        double[][] triangleVertices = { Vertices[0], Vertices[1], Vertices[2] };
        PolyhedronMesh.ValidateSurface(triangleVertices, openTriangle);
        if (PolyhedronMesh.TryValidateClosed(triangleVertices, openTriangle, out closedVolume, out reason) ||
            reason.IndexOf("open", StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new Exception("Open surface was not correctly routed to POHE.");
        }
        _checks++;

        Reject(null, Faces);
        Reject(Vertices, null);
        Reject(new double[2][], Faces);
        Reject(Vertices, new int[0][]);
        Reject(new double[10001][], Faces);
        Reject(Vertices, new int[20001][]);
        Reject(ChangeVertex(new[] { double.NaN, 0, 0 }), Faces);
        Reject(ChangeVertex(new[] { double.PositiveInfinity, 0, 0 }), Faces);
        Reject(ChangeVertex(new[] { double.MaxValue, 0, 0 }), Faces);
        Reject(ChangeVertex(new[] { 0.0, 0 }), Faces);
        Reject(ChangeVertex(null), Faces);
        Reject(Vertices, ChangeFace(new[] { 0, 1, 4 }));
        Reject(Vertices, ChangeFace(new[] { -1, 1, 2 }));
        Reject(Vertices, ChangeFace(new[] { 0, 1, 1 }));
        Reject(Vertices, ChangeFace(new[] { 0, 1 }));
        Reject(Vertices, ChangeFace(null));
        Reject(triangleVertices, new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 } });
        Reject(Vertices, ChangeFace(Faces[1]));
        Reject(new[] { Vertices[0], Vertices[1], Vertices[2], new[] { 50.0, 50, 0 } }, Faces);
        Reject(new[] { Vertices[0], Vertices[1], Vertices[2], Vertices[3], new[] { 1.0, 1, 1 } }, Faces);

        var reversed = new int[Faces.Length][];
        for (int i = 0; i < Faces.Length; i++)
        {
            reversed[i] = new[] { Faces[i][2], Faces[i][1], Faces[i][0] };
        }
        PolyhedronMesh.ValidateSurface(Vertices, reversed);
        if (PolyhedronMesh.TryValidateClosed(Vertices, reversed, out closedVolume, out reason) ||
            reason.IndexOf("outward", StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new Exception("Inward-wound closed mesh was not correctly routed to POHE.");
        }
        _checks++;

        var openVertices = new double[8][];
        var openFaces = new int[7][];
        for (int i = 0; i < 4; i++)
        {
            openVertices[i] = Vertices[i];
            openVertices[i + 4] = new[] { Vertices[i][0] + 200, Vertices[i][1], Vertices[i][2] };
            openFaces[i] = Faces[i];
            if (i < 3)
            {
                openFaces[i + 4] = new[] { Faces[i][0] + 4, Faces[i][1] + 4, Faces[i][2] + 4 };
            }
        }
        PolyhedronMesh.ValidateSurface(openVertices, openFaces);
        if (PolyhedronMesh.TryValidateClosed(openVertices, openFaces, out closedVolume, out reason))
        {
            throw new Exception("Disconnected open shells were incorrectly accepted as one POLYHE.");
        }
        _checks++;

        // A periodic triangulated torus reaches both configured limits exactly.
        const int n = 100;
        var torusVertices = new double[n * n][];
        var torusFaces = new int[2 * n * n][];
        for (int u = 0; u < n; u++)
        {
            for (int v = 0; v < n; v++)
            {
                double angleU = 2 * Math.PI * u / n;
                double angleV = 2 * Math.PI * v / n;
                int a = u * n + v;
                int b = ((u + 1) % n) * n + v;
                int c = ((u + 1) % n) * n + (v + 1) % n;
                int d = u * n + (v + 1) % n;
                torusVertices[a] = new[]
                {
                    (10 + Math.Cos(angleV)) * Math.Cos(angleU),
                    (10 + Math.Cos(angleV)) * Math.Sin(angleU), Math.Sin(angleV)
                };
                torusFaces[2 * a] = new[] { a, b, c };
                torusFaces[2 * a + 1] = new[] { a, c, d };
            }
        }
        PolyhedronMesh.ValidateSurface(torusVertices, torusFaces);
        if (!PolyhedronMesh.TryValidateClosed(torusVertices, torusFaces, out closedVolume, out reason))
        {
            throw new Exception("Closed torus was rejected: " + reason);
        }
        Near(closedVolume, 20 * Math.PI * Math.PI, 0.3);
        CheckWeldAndTransforms();
        Console.WriteLine("Passed " + _checks + " geometry checks.");
    }

    private static void CheckWeldAndTransforms()
    {
        // Revit-style corner-per-triangle tetrahedron at shared coordinates, with 0.03 mm noise, a duplicate and a sliver.
        double[] o = { 3263493.1998, 1634139.994, 104329.9999 };
        var corners = new double[Faces.Length + 2][];
        for (int f = 0; f < Faces.Length; f++)
        {
            corners[f] = new double[9];
            for (int k = 0; k < 3; k++)
            {
                double noise = (f + k) % 2 == 0 ? 0.03 : -0.03;
                for (int axis = 0; axis < 3; axis++)
                {
                    corners[f][k * 3 + axis] = o[axis] + Vertices[Faces[f][k]][axis] + noise * 0.1;
                }
            }
        }
        corners[Faces.Length] = (double[])corners[0].Clone();
        corners[Faces.Length + 1] = new[] { o[0], o[1], o[2], o[0] + 0.01, o[1], o[2], o[0] + 100, o[1], o[2] };
        WeldedMesh welded = PolyhedronMesh.WeldTriangles(corners, 0.1);
        Expect(welded.Vertices.Length == 4 && welded.Faces.Length == 4, "weld should yield 4 vertices and 4 faces");
        Expect(welded.DuplicateDropped == 1 && welded.DegenerateDropped == 1, "weld should drop one duplicate and one degenerate");
        PolyhedronMesh.ValidateSurface(welded.Vertices, welded.Faces);
        double volume;
        string reason;
        Expect(PolyhedronMesh.TryValidateClosed(welded.Vertices, welded.Faces, out volume, out reason),
            "welded shared-coordinate tetrahedron should be closed: " + reason);

        WeldedMesh negativeZero = PolyhedronMesh.WeldTriangles(new[]
        {
            new[] { -0.01, 0, 0, 10, 0, 0, 0, 10, 0 },
            new[] { 0.01, 0, 0, 0, 10, 0, 0, 0, 10 }
        }, 0.1);
        Expect(negativeZero.Vertices.Length == 4, "-0 and +0 cells must weld together");

        ExpectThrows(() => PolyhedronMesh.WeldTriangles(new[] { new double[8] }, 0.1), "8 coordinates");
        ExpectThrows(() => PolyhedronMesh.WeldTriangles(new[] { new[] { double.NaN, 0, 0, 1, 0, 0, 0, 1, 0 } }, 0.1), "NaN");
        ExpectThrows(() => PolyhedronMesh.WeldTriangles(corners, 0), "zero tolerance");
        ExpectThrows(() => PolyhedronMesh.WeldTriangles(new double[0][], 0.1), "empty");

        // Zone rotated 90 degrees about Z and translated: local X = world Y, local Y = -world X.
        double[] zoneOrigin = { 1000, 2000, 300 };
        double[][] axes = { new[] { 0.0, 1, 0 }, new[] { -1.0, 0, 0 }, new[] { 0.0, 0, 1 } };
        double[][] world = { new[] { 1000.0, 2500, 400 }, new[] { 1200.0, 2000, 300 } };
        double[][] local = PolyhedronMesh.WorldToLocal(world, zoneOrigin, axes);
        Expect(PolyhedronMesh.Distance(local[0], new[] { 500.0, 0, 100 }) < 1e-9, "world->local first point");
        Expect(PolyhedronMesh.Distance(local[1], new[] { 0.0, -200, 0 }) < 1e-9, "world->local second point");
        Expect(PolyhedronMesh.Distance(PolyhedronMesh.LocalToWorld(local[1], zoneOrigin, axes), world[1]) < 1e-9,
            "local->world round trip");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
        _checks++;
    }

    private static void ExpectThrows(Action action, string message)
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            _checks++;
            return;
        }
        throw new Exception("Expected rejection: " + message);
    }

    private static double[][] ChangeVertex(double[] vertex)
    {
        var result = (double[][])Vertices.Clone();
        result[0] = vertex;
        return result;
    }

    private static int[][] ChangeFace(int[] face)
    {
        var result = (int[][])Faces.Clone();
        result[0] = face;
        return result;
    }

    private static void Reject(double[][] vertices, int[][] faces)
    {
        try
        {
            PolyhedronMesh.ValidateSurface(vertices, faces);
        }
        catch (ArgumentException)
        {
            _checks++;
            return;
        }
        throw new Exception("Invalid mesh was accepted.");
    }

    private static void Near(double actual, double expected, double tolerance)
    {
        if (double.IsNaN(actual) || Math.Abs(actual - expected) > tolerance)
        {
            throw new Exception("Expected volume " + expected + ", got " + actual);
        }
        _checks++;
    }
}
