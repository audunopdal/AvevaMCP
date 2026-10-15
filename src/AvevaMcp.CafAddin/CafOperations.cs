using System;
using System.Collections.Generic;
using System.Globalization;
using Aveva.Core.Database;
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
