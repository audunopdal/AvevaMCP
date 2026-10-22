using System;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Newtonsoft.Json.Linq;

namespace AvevaMcp.CafAddin
{
    internal static partial class CafOperations
    {
        private const double MaximumWeldTolerance = 10.0;
        private const double MinimumPlacementTolerance = 1.0;

        // Revit shared E/N/Elev millimetres are treated as UE world X/Y/Z in database millimetres.
        private static JObject ImportRevitMesh(JObject parameters)
        {
            string zonePath = RequiredString(parameters, "zonePath");
            string equipmentName = RequiredFullName(parameters, "equipmentName");
            string meshName = RequiredFullName(parameters, "meshName");
            if (string.Equals(equipmentName, meshName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("equipmentName and meshName must differ.");
            }

            double tolerance = RequiredDouble(parameters, "weldTolerance");
            if (tolerance <= 0 || tolerance > MaximumWeldTolerance)
            {
                throw new ArgumentException("weldTolerance must be greater than 0 and at most 10 mm.");
            }

            JArray rows = RequiredMeshArray(parameters, "triangles", 1, PolyhedronMesh.MaximumFaces);
            var triangles = new double[rows.Count][];
            for (int row = 0; row < triangles.Length; row++)
            {
                JArray values = rows[row] as JArray;
                if (values == null || values.Count != 9)
                {
                    throw new ArgumentException("Each triangle must be an array of nine numbers: a, b and c as x,y,z.");
                }

                triangles[row] = new double[9];
                for (int index = 0; index < 9; index++)
                {
                    if (values[index].Type != JTokenType.Float && values[index].Type != JTokenType.Integer)
                    {
                        throw new ArgumentException("Triangle coordinates must be numeric.");
                    }

                    triangles[row][index] = values[index].Value<double>();
                }
            }

            WeldedMesh welded = PolyhedronMesh.WeldTriangles(triangles, tolerance);
            if (welded.Faces.Length == 0)
            {
                throw new ArgumentException("No nondegenerate, unique triangles remain after welding.");
            }

            DbElement zone = GetElement(zonePath);
            if (!zone.GetElementType().Equals(DbElementTypeInstance.ZONE))
            {
                throw new ArgumentException("zonePath must reference a ZONE: " + zonePath);
            }

            if (DbElement.GetElement(equipmentName).IsValid || DbElement.GetElement(meshName).IsValid)
            {
                throw new InvalidOperationException("An element already exists at " + equipmentName + " or " + meshName);
            }

            if (!zone.IsCreatable(DbElementTypeInstance.EQUIPMENT))
            {
                throw new InvalidOperationException("This ZONE cannot own EQUIPMENT.");
            }

            DbElement world = GetElement("/*");
            double[] zoneOrigin = WorldPosition(zone, world);
            double[][] zoneAxes = ToAxes(WorldOrientation(zone, world));
            double[][] local = PolyhedronMesh.WorldToLocal(welded.Vertices, zoneOrigin, zoneAxes);
            double[] equipmentOrigin = local[0];
            var relative = new double[local.Length][];
            for (int index = 0; index < local.Length; index++)
            {
                relative[index] = new[]
                {
                    local[index][0] - equipmentOrigin[0],
                    local[index][1] - equipmentOrigin[1],
                    local[index][2] - equipmentOrigin[2]
                };
            }

            PolyhedronMesh.ValidateSurface(relative, welded.Faces);

            DbElement equipment = zone.CreateLast(DbElementTypeInstance.EQUIPMENT);
            JObject result;
            JObject verification;
            try
            {
                PlaceMesh(equipment, equipmentName, equipmentOrigin);
                string description = OptionalString(parameters, "description");
                if (description != null)
                {
                    equipment.SetAttribute(DbAttributeInstance.DESC, description);
                }

                result = CreateMesh(equipment, equipmentName, meshName, relative, welded.Faces);
                verification = VerifyPlacement(DbElement.GetElement(meshName), world,
                    welded.Vertices, welded.Faces, Math.Max(MinimumPlacementTolerance, tolerance * 10));
            }
            catch (Exception operationError)
            {
                try
                {
                    equipment.Delete();
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException(
                        "Revit mesh import failed and cleanup was incomplete; inspect unsaved work at " + equipmentName,
                        operationError, rollbackError);
                }

                throw;
            }

            result["equipmentPath"] = equipmentName;
            result["zonePath"] = zonePath;
            result["coordinateMapping"] = "Revit shared E/N/Elev mm = UE world X/Y/Z, converted into the ZONE frame";
            result["sourceTriangleCount"] = triangles.Length;
            result["degenerateTrianglesDropped"] = welded.DegenerateDropped;
            result["duplicateTrianglesDropped"] = welded.DuplicateDropped;
            result["weldTolerance"] = tolerance;
            result["firstVertexWorld"] = new JArray(welded.Vertices[0][0], welded.Vertices[0][1], welded.Vertices[0][2]);
            result["verification"] = verification;
            JToken source = parameters["source"];
            if (source != null)
            {
                result["source"] = source.DeepClone();
            }

            return result;
        }

        private static JObject VerifyPlacement(
            DbElement mesh, DbElement world, double[][] worldVertices, int[][] faces, double tolerance)
        {
            if (!mesh.IsValid)
            {
                throw new InvalidOperationException("The created mesh could not be found for verification.");
            }

            double originDeviation = PolyhedronMesh.Distance(WorldPosition(mesh, world), worldVertices[0]);
            if (originDeviation > tolerance)
            {
                throw new InvalidOperationException(
                    "Placement verification failed: mesh WPOS deviates " + originDeviation +
                    " mm from the Revit shared position. Check units and the ZONE transform.");
            }

            var verification = new JObject
            {
                ["meshOriginDeviation"] = originDeviation,
                ["toleranceMm"] = tolerance
            };

            int[] lastFace = faces[faces.Length - 1];
            int vertexIndex = lastFace[2];
            try
            {
                DbElement point = FindLastFacePoint(mesh, vertexIndex);
                double pointDeviation = PolyhedronMesh.Distance(WorldPosition(point, world), worldVertices[vertexIndex]);
                verification["checkedVertexIndex"] = vertexIndex;
                verification["pointDeviation"] = pointDeviation;
                verification["pointWithinTolerance"] = pointDeviation <= tolerance;
            }
            catch (Exception pointError)
            {
                verification["pointDeviation"] = JValue.CreateNull();
                verification["pointCheckError"] = pointError.Message;
            }

            return verification;
        }

        private static DbElement FindLastFacePoint(DbElement mesh, int vertexIndex)
        {
            if (mesh.GetElementType().Equals(DbElementTypeInstance.POLYHEDRON))
            {
                foreach (DbElement member in mesh.Members())
                {
                    if (member.GetElementType().Equals(DbElementTypeInstance.POLPTLIST))
                    {
                        return member.Members()[vertexIndex];
                    }
                }

                throw new ArgumentException("The POLYHEDRON has no POLPTLIST.");
            }

            DbElement[] polygons = mesh.Members();
            DbElement[] points = polygons[polygons.Length - 1].Members();
            return points[points.Length - 1];
        }

        // WORPOS/WORI are the world pseudo-attributes; POS/ORI WRT WORLD is the fallback when unavailable.
        private static double[] WorldPosition(DbElement element, DbElement world)
        {
            try
            {
                return ToArray(element.GetPosition(DbAttributeInstance.WORPOS));
            }
            catch (Exception worldError)
            {
                try
                {
                    var qualifier = new DbQualifier();
                    qualifier.wrtQualifier = world;
                    return ToArray(element.GetPosition(DbAttributeInstance.POS, qualifier));
                }
                catch (Exception qualifiedError)
                {
                    throw new AggregateException("Cannot read the world position of " +
                        element.GetString(DbAttributeInstance.NAME), worldError, qualifiedError);
                }
            }
        }

        private static Orientation WorldOrientation(DbElement element, DbElement world)
        {
            try
            {
                return element.GetOrientation(DbAttributeInstance.WORI);
            }
            catch (Exception worldError)
            {
                try
                {
                    var qualifier = new DbQualifier();
                    qualifier.wrtQualifier = world;
                    return element.GetOrientation(DbAttributeInstance.ORI, qualifier);
                }
                catch (Exception qualifiedError)
                {
                    throw new AggregateException("Cannot read the world orientation of " +
                        element.GetString(DbAttributeInstance.NAME), worldError, qualifiedError);
                }
            }
        }

        private static double[] ToArray(Position position)
        {
            return new[] { position.X, position.Y, position.Z };
        }

        private static double[][] ToAxes(Orientation orientation)
        {
            return new[]
            {
                ToArray(orientation.XDir()),
                ToArray(orientation.YDir()),
                ToArray(orientation.ZDir())
            };
        }

        private static double[] ToArray(Direction direction)
        {
            return new[] { direction.East, direction.North, direction.Up };
        }

        private static string RequiredFullName(JObject parameters, string name)
        {
            string value = RequiredString(parameters, name);
            if (!value.StartsWith("/", StringComparison.Ordinal) || value.Length < 2)
            {
                throw new ArgumentException(name + " must be a full AVEVA element name beginning with /.");
            }

            return value;
        }

        private static string OptionalString(JObject parameters, string name)
        {
            JToken value = parameters[name];
            if (value == null || value.Type == JTokenType.Null)
            {
                return null;
            }

            if (value.Type != JTokenType.String)
            {
                throw new ArgumentException("'" + name + "' must be a string.");
            }

            return value.Value<string>();
        }
    }
}
