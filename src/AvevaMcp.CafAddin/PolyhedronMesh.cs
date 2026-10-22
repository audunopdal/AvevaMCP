using System;
using System.Collections.Generic;

namespace AvevaMcp.CafAddin
{
    internal static class PolyhedronMesh
    {
        internal const int MaximumVertices = 10000;
        internal const int MaximumFaces = 20000;

        internal static void ValidateSurface(double[][] vertices, int[][] faces)
        {
            if (vertices == null || vertices.Length < 3 || vertices.Length > MaximumVertices)
            {
                throw new ArgumentException("vertices must contain 3-10000 [x,y,z] positions.");
            }

            if (faces == null || faces.Length < 1 || faces.Length > MaximumFaces)
            {
                throw new ArgumentException("faces must contain 1-20000 triangular index arrays.");
            }

            var used = new bool[vertices.Length];
            var uniqueFaces = new HashSet<string>();
            foreach (double[] vertex in vertices)
            {
                if (vertex == null || vertex.Length != 3)
                {
                    throw new ArgumentException("Each vertex must contain exactly three coordinates.");
                }

                foreach (double coordinate in vertex)
                {
                    if (double.IsNaN(coordinate) || double.IsInfinity(coordinate))
                    {
                        throw new ArgumentException("Vertex coordinates must be finite.");
                    }
                }
            }

            foreach (int[] face in faces)
            {
                if (face == null || face.Length != 3)
                {
                    throw new ArgumentException("Each face must contain exactly three zero-based indices.");
                }

                foreach (int index in face)
                {
                    if (index < 0 || index >= vertices.Length)
                    {
                        throw new ArgumentException("A face index is outside the vertex array.");
                    }

                    used[index] = true;
                }

                if (face[0] == face[1] || face[1] == face[2] || face[2] == face[0])
                {
                    throw new ArgumentException("Each triangle must reference three different vertices.");
                }

                int[] orderedIndices = { face[0], face[1], face[2] };
                Array.Sort(orderedIndices);
                string faceKey = orderedIndices[0] + "," + orderedIndices[1] + "," + orderedIndices[2];
                if (!uniqueFaces.Add(faceKey))
                {
                    throw new ArgumentException("The mesh contains a duplicate triangle.");
                }

                double[] first = Subtract(vertices[face[1]], vertices[face[0]]);
                double[] second = Subtract(vertices[face[2]], vertices[face[0]]);
                double[] normal = Cross(first, second);
                double areaSquared = Dot(normal, normal);
                if (!IsFinite(areaSquared) || areaSquared <= 0)
                {
                    throw new ArgumentException("Faces must be nondegenerate with finite area.");
                }
            }

            if (Array.IndexOf(used, false) >= 0)
            {
                throw new ArgumentException("Every vertex must be referenced by a face.");
            }
        }

        internal static bool TryValidateClosed(double[][] vertices, int[][] faces, out double volume, out string reason)
        {
            var edges = new HashSet<long>();
            volume = 0;
            foreach (int[] face in faces)
            {
                double[] a = Subtract(vertices[face[0]], vertices[0]);
                double[] b = Subtract(vertices[face[1]], vertices[0]);
                double[] c = Subtract(vertices[face[2]], vertices[0]);
                volume += Dot(a, Cross(b, c)) / 6.0;
                for (int edge = 0; edge < 3; edge++)
                {
                    int from = face[edge];
                    int to = face[(edge + 1) % 3];
                    long key = (long)from * MaximumVertices + to;
                    if (!edges.Add(key))
                    {
                        reason = "A directed edge occurs more than once; the mesh is non-manifold or winding is inconsistent.";
                        return false;
                    }
                }
            }

            foreach (long key in edges)
            {
                long reverse = (key % MaximumVertices) * MaximumVertices + key / MaximumVertices;
                if (!edges.Contains(reverse))
                {
                    reason = "The mesh is open; an edge has no oppositely directed face edge.";
                    return false;
                }
            }

            if (!IsFinite(volume))
            {
                reason = "The signed volume is not finite.";
                return false;
            }

            if (volume <= 0)
            {
                reason = "The closed mesh does not have positive outward-facing signed volume.";
                return false;
            }

            reason = null;
            return true;
        }

        internal static WeldedMesh WeldTriangles(double[][] triangles, double tolerance)
        {
            if (!(tolerance > 0) || double.IsInfinity(tolerance))
            {
                throw new ArgumentException("The weld tolerance must be positive and finite.");
            }

            if (triangles == null || triangles.Length < 1 || triangles.Length > MaximumFaces)
            {
                throw new ArgumentException("triangles must contain 1-20000 entries.");
            }

            var cells = new Dictionary<string, int>();
            var welded = new List<double[]>();
            var faces = new List<int[]>();
            var uniqueFaces = new HashSet<string>();
            int degenerate = 0;
            int duplicate = 0;
            foreach (double[] triangle in triangles)
            {
                if (triangle == null || triangle.Length != 9)
                {
                    throw new ArgumentException("Each triangle must contain nine coordinates: a, b and c as x,y,z.");
                }

                var face = new int[3];
                for (int corner = 0; corner < 3; corner++)
                {
                    double x = triangle[corner * 3];
                    double y = triangle[corner * 3 + 1];
                    double z = triangle[corner * 3 + 2];
                    if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z))
                    {
                        throw new ArgumentException("Triangle coordinates must be finite.");
                    }

                    string key = Cell(x, tolerance) + "," + Cell(y, tolerance) + "," + Cell(z, tolerance);
                    int index;
                    if (!cells.TryGetValue(key, out index))
                    {
                        index = welded.Count;
                        cells.Add(key, index);
                        welded.Add(new[] { x, y, z });
                    }

                    face[corner] = index;
                }

                if (face[0] == face[1] || face[1] == face[2] || face[2] == face[0])
                {
                    degenerate++;
                    continue;
                }

                double[] normal = Cross(Subtract(welded[face[1]], welded[face[0]]), Subtract(welded[face[2]], welded[face[0]]));
                double areaSquared = Dot(normal, normal);
                if (!IsFinite(areaSquared) || areaSquared <= 0)
                {
                    degenerate++;
                    continue;
                }

                int[] ordered = { face[0], face[1], face[2] };
                Array.Sort(ordered);
                if (!uniqueFaces.Add(ordered[0] + "," + ordered[1] + "," + ordered[2]))
                {
                    duplicate++;
                    continue;
                }

                faces.Add(face);
            }

            // Vertices referenced only by dropped triangles must not remain, because every vertex must be used.
            var remap = new int[welded.Count];
            for (int index = 0; index < remap.Length; index++)
            {
                remap[index] = -1;
            }

            var vertices = new List<double[]>();
            foreach (int[] face in faces)
            {
                for (int corner = 0; corner < 3; corner++)
                {
                    int original = face[corner];
                    if (remap[original] < 0)
                    {
                        remap[original] = vertices.Count;
                        vertices.Add(welded[original]);
                    }

                    face[corner] = remap[original];
                }
            }

            return new WeldedMesh(vertices.ToArray(), faces.ToArray(), degenerate, duplicate);
        }

        // axes are the local X/Y/Z unit vectors expressed in world coordinates.
        internal static double[][] WorldToLocal(double[][] worldPoints, double[] origin, double[][] axes)
        {
            var local = new double[worldPoints.Length][];
            for (int index = 0; index < worldPoints.Length; index++)
            {
                double[] offset = Subtract(worldPoints[index], origin);
                local[index] = new[] { Dot(offset, axes[0]), Dot(offset, axes[1]), Dot(offset, axes[2]) };
            }

            return local;
        }

        internal static double[] LocalToWorld(double[] local, double[] origin, double[][] axes)
        {
            var world = new double[3];
            for (int axis = 0; axis < 3; axis++)
            {
                world[axis] = origin[axis] + local[0] * axes[0][axis] + local[1] * axes[1][axis] + local[2] * axes[2][axis];
            }

            return world;
        }

        internal static double Distance(double[] a, double[] b)
        {
            double[] difference = Subtract(a, b);
            return Math.Sqrt(Dot(difference, difference));
        }

        private static long Cell(double coordinate, double tolerance)
        {
            double cell = Math.Round(coordinate / tolerance);
            if (Math.Abs(cell) > 1e15)
            {
                throw new ArgumentException("A coordinate is too large for the weld tolerance.");
            }

            return (long)cell;
        }

        private static double[] Subtract(double[] a, double[] b)
        {
            return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        }

        private static double[] Cross(double[] a, double[] b)
        {
            return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        }

        private static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    internal sealed class WeldedMesh
    {
        internal WeldedMesh(double[][] vertices, int[][] faces, int degenerateDropped, int duplicateDropped)
        {
            Vertices = vertices;
            Faces = faces;
            DegenerateDropped = degenerateDropped;
            DuplicateDropped = duplicateDropped;
        }

        internal double[][] Vertices { get; }
        internal int[][] Faces { get; }
        internal int DegenerateDropped { get; }
        internal int DuplicateDropped { get; }
    }
}
