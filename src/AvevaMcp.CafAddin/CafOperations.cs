using System;
using System.Collections.Generic;
using System.Globalization;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Newtonsoft.Json.Linq;

namespace AvevaMcp.CafAddin
{
    internal static class CafOperations
    {
        private const int MaximumResults = 500;

        public static JToken Execute(string method, JObject parameters)
        {
            switch (method)
            {
                case "get_context":
                    return GetContext();
                case "list_sites":
                    return ListSites();
                case "get_element":
                    return DescribeElement(GetElement(RequiredString(parameters, "path")));
                case "list_children":
                    return ListChildren(
                        GetElement(RequiredString(parameters, "path")),
                        ReadLimit(parameters));
                case "search_elements":
                    return SearchElements(
                        GetElement(RequiredString(parameters, "rootPath")),
                        RequiredString(parameters, "nameContains"),
                        ReadLimit(parameters));
                case "read_attribute":
                    return ReadAttribute(
                        GetElement(RequiredString(parameters, "path")),
                        RequiredString(parameters, "attribute"),
                        RequiredString(parameters, "valueType"));
                case "set_attribute":
                    return SetAttribute(
                        GetElement(RequiredString(parameters, "path")),
                        RequiredString(parameters, "attribute"),
                        RequiredString(parameters, "valueType"),
                        RequiredString(parameters, "value"));
                case "create_element":
                    return CreateElement(
                        GetElement(RequiredString(parameters, "parentPath")),
                        RequiredString(parameters, "elementType"),
                        RequiredString(parameters, "name"));
                case "create_rectangular_extrusion":
                    return CreateRectangularExtrusion(
                        RequiredString(parameters, "path"),
                        RequiredDouble(parameters, "startX"),
                        RequiredDouble(parameters, "startY"),
                        RequiredDouble(parameters, "startZ"),
                        RequiredDouble(parameters, "endX"),
                        RequiredDouble(parameters, "endY"),
                        RequiredDouble(parameters, "endZ"),
                        RequiredDouble(parameters, "width"),
                        RequiredDouble(parameters, "height"),
                        OptionalBoolean(parameters, "updateExisting"));
                case "delete_element":
                    return DeleteElement(
                        RequiredString(parameters, "path"),
                        RequiredString(parameters, "confirmPath"));
                case "save_work":
                    return SaveWork(RequiredString(parameters, "comment"));
                case "discard_work":
                    return DiscardWork(RequiredString(parameters, "confirmation"));
                default:
                    throw new ArgumentException("Unsupported CAF operation: " + method);
            }
        }

        private static JObject GetContext()
        {
            return new JObject
            {
                ["project"] = Project.CurrentProject.Name,
                ["user"] = Project.CurrentProject.UserName,
                ["mdb"] = MDB.CurrentMDB.Name
            };
        }

        private static JObject DescribeElement(DbElement element)
        {
            return new JObject
            {
                ["path"] = element.GetString(DbAttributeInstance.NAME),
                ["type"] = element.GetElementType().ToString()
            };
        }

        private static JArray ListSites()
        {
            var results = new JArray();
            foreach (DbElement element in GetElement("/*").Members())
            {
                if (element.GetElementType().Equals(DbElementTypeInstance.SITE))
                {
                    results.Add(DescribeElement(element));
                }
            }

            return results;
        }

        private static JArray ListChildren(DbElement parent, int limit)
        {
            var results = new JArray();
            DbElement[] children = parent.Members();
            for (int index = 0; index < children.Length && results.Count < limit; index++)
            {
                results.Add(DescribeElement(children[index]));
            }

            return results;
        }

        private static JObject SearchElements(DbElement root, string text, int limit)
        {
            var results = new JArray();
            var pending = new Stack<DbElement>();
            pending.Push(root);
            int visited = 0;
            const int maximumVisited = 100000;

            while (pending.Count > 0 && results.Count < limit)
            {
                DbElement element = pending.Pop();
                if (++visited > maximumVisited)
                {
                    throw new InvalidOperationException(
                        "Search exceeded 100000 visited elements. Narrow the root path.");
                }

                string name = element.GetString(DbAttributeInstance.NAME);
                if (name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    results.Add(DescribeElement(element));
                }

                DbElement[] children = element.Members();
                for (int index = children.Length - 1; index >= 0; index--)
                {
                    pending.Push(children[index]);
                }
            }

            return new JObject
            {
                ["results"] = results,
                ["truncated"] = results.Count == limit && pending.Count > 0,
                ["visited"] = visited
            };
        }

        private static JObject ReadAttribute(DbElement element, string attributeName, string valueType)
        {
            DbAttribute attribute = GetAttribute(attributeName);
            JToken value;
            switch (NormalizeValueType(valueType))
            {
                case "string":
                    value = new JValue(element.GetString(attribute));
                    break;
                case "integer":
                    value = new JValue(element.GetInteger(attribute));
                    break;
                case "double":
                    value = new JValue(element.GetDouble(attribute));
                    break;
                case "boolean":
                    value = new JValue(element.GetBool(attribute));
                    break;
                default:
                    throw new ArgumentException("Unsupported valueType. Use string, integer, double, or boolean.");
            }

            return new JObject
            {
                ["path"] = element.GetString(DbAttributeInstance.NAME),
                ["attribute"] = attributeName,
                ["valueType"] = NormalizeValueType(valueType),
                ["value"] = value
            };
        }

        private static JObject SetAttribute(
            DbElement element,
            string attributeName,
            string valueType,
            string value)
        {
            DbAttribute attribute = GetAttribute(attributeName);
            JToken responseValue;
            switch (NormalizeValueType(valueType))
            {
                case "string":
                {
                    element.SetAttribute(attribute, value);
                    responseValue = new JValue(value);
                    break;
                }
                case "integer":
                {
                    int integerValue = int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    element.SetAttribute(attribute, integerValue);
                    responseValue = new JValue(integerValue);
                    break;
                }
                case "double":
                {
                    double doubleValue = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue))
                    {
                        throw new ArgumentException("Double attribute values must be finite.");
                    }

                    element.SetAttribute(attribute, doubleValue);
                    responseValue = new JValue(doubleValue);
                    break;
                }
                case "boolean":
                {
                    bool booleanValue = bool.Parse(value);
                    element.SetAttribute(attribute, booleanValue);
                    responseValue = new JValue(booleanValue);
                    break;
                }
                default:
                    throw new ArgumentException("Unsupported valueType. Use string, integer, double, or boolean.");
            }

            return new JObject
            {
                ["path"] = element.GetString(DbAttributeInstance.NAME),
                ["attribute"] = attributeName,
                ["valueType"] = NormalizeValueType(valueType),
                ["value"] = responseValue
            };
        }

        private static JObject CreateElement(DbElement parent, string elementType, string name)
        {
            if (!name.StartsWith("/", StringComparison.Ordinal) || name.Length < 2)
            {
                throw new ArgumentException("name must be a full AVEVA element path beginning with /.");
            }

            DbElementType type = DbElementType.GetElementType(elementType);
            if (type == null)
            {
                throw new ArgumentException("Unknown AVEVA element type: " + elementType);
            }

            DbElement created = parent.Create(0, type);
            try
            {
                created.SetAttribute(DbAttributeInstance.NAME, name);
            }
            catch
            {
                created.Delete();
                throw;
            }

            return DescribeElement(created);
        }

        private static JObject CreateRectangularExtrusion(
            string path,
            double startX,
            double startY,
            double startZ,
            double endX,
            double endY,
            double endZ,
            double width,
            double height,
            bool updateExisting)
        {
            EnsureFinite(startX, "startX");
            EnsureFinite(startY, "startY");
            EnsureFinite(startZ, "startZ");
            EnsureFinite(endX, "endX");
            EnsureFinite(endY, "endY");
            EnsureFinite(endZ, "endZ");
            EnsureFinite(width, "width");
            EnsureFinite(height, "height");

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException("width", "Wall width must be greater than zero.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException("height", "Wall height must be greater than zero.");
            }

            const double coordinateTolerance = 1e-6;
            if (Math.Abs(endZ - startZ) > coordinateTolerance)
            {
                throw new ArgumentException(
                    "The extrusion operation supports vertical walls only; startZ and endZ must match.");
            }

            double deltaX = endX - startX;
            double deltaY = endY - startY;
            double length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            EnsureFinite(length, "wall baseline length");
            if (length <= coordinateTolerance)
            {
                throw new ArgumentException("The wall baseline must have a non-zero horizontal length.");
            }

            DbElement extrusion = GetElement(path);
            if (!extrusion.GetElementType().Equals(DbElementTypeInstance.EXTRUSION))
            {
                throw new ArgumentException("The specified element is not an EXTRUSION: " + path);
            }

            DbElement[] members = extrusion.Members();
            DbElement loop = null;
            DbElement[] vertices = null;
            Position[] previousVertices = null;
            if (members.Length != 0)
            {
                if (!updateExisting || members.Length != 1 ||
                    !members[0].GetElementType().Equals(DbElementTypeInstance.LOOP))
                {
                    throw new InvalidOperationException(
                        "Updating geometry requires updateExisting=true and exactly one four-vertex LOOP: " + path);
                }

                loop = members[0];
                vertices = loop.Members();
                if (vertices.Length != 4)
                {
                    throw new InvalidOperationException("The existing LOOP must contain exactly four VERTEX elements.");
                }

                previousVertices = new Position[4];
                for (int index = 0; index < vertices.Length; index++)
                {
                    if (!vertices[index].GetElementType().Equals(DbElementTypeInstance.VERTEX))
                    {
                        throw new InvalidOperationException("The existing LOOP contains a non-VERTEX element.");
                    }

                    previousVertices[index] = vertices[index].GetPosition(DbAttributeInstance.POS);
                }
            }

            double directionX = deltaX / length;
            double directionY = deltaY / length;
            double originX = startX + directionY * width / 2.0;
            double originY = startY - directionX * width / 2.0;
            EnsureFinite(originX, "originX");
            EnsureFinite(originY, "originY");
            string loopPath = path.TrimEnd('/') + "/LOOP";
            Position previousPosition = extrusion.GetPosition(DbAttributeInstance.POS);
            Orientation previousOrientation = extrusion.GetOrientation(DbAttributeInstance.ORI);
            double previousHeight = extrusion.GetDouble(DbAttributeInstance.HEIG);
            bool creatingLoop = loop == null;

            try
            {
                if (creatingLoop)
                {
                    loop = extrusion.CreateLast(DbElementTypeInstance.LOOP);
                    loop.SetAttribute(DbAttributeInstance.NAME, loopPath);
                    vertices = new DbElement[4];
                    for (int index = 0; index < vertices.Length; index++)
                    {
                        vertices[index] = loop.CreateLast(DbElementTypeInstance.VERTEX);
                        vertices[index].SetAttribute(
                            DbAttributeInstance.NAME,
                            loopPath + "/VERTEX" + (index + 1).ToString(CultureInfo.InvariantCulture));
                    }
                }

                Position[] profileVertices =
                {
                    Position.Create(0, 0, 0),
                    Position.Create(length, 0, 0),
                    Position.Create(length, width, 0),
                    Position.Create(0, width, 0)
                };

                for (int index = 0; index < profileVertices.Length; index++)
                {
                    vertices[index].SetAttribute(DbAttributeInstance.POS, profileVertices[index]);
                }

                Direction profileX = Direction.Create(
                    Position.Create(directionX, directionY, 0));
                Direction profileY = Direction.Create(Position.Create(-directionY, directionX, 0));
                extrusion.SetAttribute(DbAttributeInstance.POS, Position.Create(originX, originY, startZ));
                extrusion.SetAttribute(
                    DbAttributeInstance.ORI,
                    Orientation.Create(Axis.X, profileX, Axis.Y, profileY));
                extrusion.SetAttribute(DbAttributeInstance.HEIG, height);
            }
            catch (Exception operationError)
            {
                try
                {
                    if (creatingLoop)
                    {
                        if (loop != null && loop.IsValid)
                        {
                            loop.Delete();
                        }
                    }
                    else
                    {
                        for (int index = 0; index < vertices.Length; index++)
                        {
                            vertices[index].SetAttribute(DbAttributeInstance.POS, previousVertices[index]);
                        }
                    }

                    extrusion.SetAttribute(DbAttributeInstance.POS, previousPosition);
                    extrusion.SetAttribute(DbAttributeInstance.ORI, previousOrientation);
                    extrusion.SetAttribute(DbAttributeInstance.HEIG, previousHeight);
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException(
                        "Geometry creation failed and rollback was incomplete; inspect unsaved work at " + path,
                        operationError,
                        rollbackError);
                }

                throw;
            }

            return new JObject
            {
                ["path"] = path,
                ["type"] = "EXTRUSION",
                ["length"] = length,
                ["width"] = width,
                ["wallHeight"] = height,
                ["extrusionHeight"] = extrusion.GetDouble(DbAttributeInstance.HEIG),
                ["position"] = new JObject
                {
                    ["x"] = originX,
                    ["y"] = originY,
                    ["z"] = startZ
                },
                ["profileVertices"] = new JArray
                {
                    new JObject { ["x"] = 0, ["y"] = 0 },
                    new JObject { ["x"] = length, ["y"] = 0 },
                    new JObject { ["x"] = length, ["y"] = width },
                    new JObject { ["x"] = 0, ["y"] = width }
                }
            };
        }

        private static JObject DeleteElement(string path, string confirmPath)
        {
            if (!string.Equals(path, confirmPath, StringComparison.Ordinal))
            {
                throw new ArgumentException("confirmPath must exactly match path.");
            }

            if (path == "/" || path == "/*")
            {
                throw new ArgumentException("The MDB root element cannot be deleted.");
            }

            DbElement element = GetElement(path);
            element.Delete();
            return new JObject { ["deletedPath"] = path };
        }

        private static JObject SaveWork(string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
            {
                throw new ArgumentException("A non-empty save comment is required.");
            }

            MDB.CurrentMDB.SaveWork(comment);
            return new JObject { ["saved"] = true, ["comment"] = comment };
        }

        private static JObject DiscardWork(string confirmation)
        {
            if (!string.Equals(confirmation, "DISCARD_CURRENT_WORK", StringComparison.Ordinal))
            {
                throw new ArgumentException("confirmation must be exactly DISCARD_CURRENT_WORK.");
            }

            MDB.CurrentMDB.QuitWork();
            return new JObject { ["discarded"] = true };
        }

        private static DbElement GetElement(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A non-empty element path is required.");
            }

            DbElement element = DbElement.GetElement(path);
            if (!element.IsValid)
            {
                throw new ArgumentException("No valid AVEVA element exists at path: " + path);
            }

            return element;
        }

        private static DbAttribute GetAttribute(string name)
        {
            DbAttribute attribute = DbAttribute.GetDbAttribute(name);
            if (attribute == null)
            {
                throw new ArgumentException("Unknown AVEVA attribute: " + name);
            }

            return attribute;
        }

        private static string RequiredString(JObject parameters, string name)
        {
            JToken value = parameters[name];
            if (value == null || value.Type != JTokenType.String ||
                string.IsNullOrWhiteSpace(value.Value<string>()))
            {
                throw new ArgumentException("A non-empty '" + name + "' string is required.");
            }

            return value.Value<string>();
        }

        private static bool OptionalBoolean(JObject parameters, string name)
        {
            JToken value = parameters[name];
            if (value == null)
            {
                return false;
            }

            if (value.Type != JTokenType.Boolean)
            {
                throw new ArgumentException("A boolean '" + name + "' value is required.");
            }

            return value.Value<bool>();
        }

        private static double RequiredDouble(JObject parameters, string name)
        {
            JToken value = parameters[name];
            if (value == null ||
                (value.Type != JTokenType.Float && value.Type != JTokenType.Integer))
            {
                throw new ArgumentException("A numeric '" + name + "' value is required.");
            }

            double number = value.Value<double>();
            EnsureFinite(number, name);
            return number;
        }

        private static void EnsureFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(name, "The value must be finite.");
            }
        }

        private static int ReadLimit(JObject parameters)
        {
            JToken value = parameters["limit"];
            int limit = value == null ? 100 : value.Value<int>();
            if (limit < 1 || limit > MaximumResults)
            {
                throw new ArgumentOutOfRangeException(
                    "limit",
                    "limit must be between 1 and " + MaximumResults + ".");
            }

            return limit;
        }

        private static string NormalizeValueType(string valueType)
        {
            return valueType.Trim().ToLowerInvariant();
        }
    }
}
