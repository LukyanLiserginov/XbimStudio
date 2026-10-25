using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Xbim.Common;
using Xbim.Ifc4.Interfaces;
using XplorerPlugin.AiAgent.Agent;
using XplorerPlugin.AiAgent.Settings;

namespace XplorerPlugin.AiAgent.Tools
{
    /// <summary>
    /// Tools that let the LLM inspect the currently open IFC model.
    /// All model access is executed by the caller on the UI thread (xBIM is not thread safe).
    /// </summary>
    internal class ModelQueryTools
    {
        private readonly Func<IModel> _modelAccessor;
        private readonly AgentSettings _settings;
        private readonly Action<IPersistEntity> _selectAction;

        public ModelQueryTools(Func<IModel> modelAccessor, AgentSettings settings, Action<IPersistEntity> selectAction)
        {
            _modelAccessor = modelAccessor;
            _settings = settings;
            _selectAction = selectAction;
        }

        private IModel Model => _modelAccessor();

        public IEnumerable<AgentTool> GetAll()
        {
            yield return new AgentTool
            {
                Name = "get_model_summary",
                Description = "Get a high-level summary of the open IFC model: schema, projects, storeys and counts of the most common element types.",
                ParametersSchema = "{\"type\":\"object\",\"properties\":{},\"required\":[]}",
                Execute = _ => GetModelSummary()
            };
            yield return new AgentTool
            {
                Name = "get_spatial_structure",
                Description = "Get the spatial structure of the model: IfcProject > IfcSite > IfcBuilding > IfcBuildingStorey hierarchy with the number of directly contained elements per storey.",
                ParametersSchema = "{\"type\":\"object\",\"properties\":{},\"required\":[]}",
                Execute = _ => GetSpatialStructure()
            };
            yield return new AgentTool
            {
                Name = "query_entities",
                Description = "Query building elements with filters. All filters are optional and combined with AND. Returns rows with entity label, global id, type, name and storey. " +
                              "'reference' style ids used across tools are the entity label (shown as #N in XbimXplorer). " +
                              "entityType is the IFC class name without the 'Ifc' prefix, e.g. 'Wall', 'Door', 'Slab' (case-insensitive). " +
                              "Use limit to restrict the number of rows (max " + MaxRows + ").",
                ParametersSchema =
                    "{\"type\":\"object\",\"properties\":{" +
                    "\"entityType\":{\"type\":\"string\",\"description\":\"IFC class name like Wall, Door, Slab, Window, Column, Beam\"}," +
                    "\"nameContains\":{\"type\":\"string\",\"description\":\"case-insensitive substring of the element name\"}," +
                    "\"storey\":{\"type\":\"string\",\"description\":\"name of the building storey the element belongs to\"}," +
                    "\"limit\":{\"type\":\"integer\",\"description\":\"max number of rows to return\"}" +
                    "},\"required\":[]}",
                Execute = QueryEntities
            };
            yield return new AgentTool
            {
                Name = "get_entity_details",
                Description = "Get full details of a single element by its entity label (e.g. '1234' or '#1234') or GlobalId: type, name, description, storey and all property set values.",
                ParametersSchema =
                    "{\"type\":\"object\",\"properties\":{" +
                    "\"reference\":{\"type\":\"string\",\"description\":\"entity label (#1234) or GlobalId of the element\"}" +
                    "},\"required\":[\"reference\"]}",
                Execute = GetEntityDetails
            };
            yield return new AgentTool
            {
                Name = "find_elements_by_text",
                Description = "Free-text search over element names, descriptions, object types and property values. Returns matching rows (entity label, global id, type, name, storey).",
                ParametersSchema =
                    "{\"type\":\"object\",\"properties\":{" +
                    "\"text\":{\"type\":\"string\",\"description\":\"text to search for (case-insensitive)\"}," +
                    "\"entityType\":{\"type\":\"string\",\"description\":\"optional IFC class filter like Wall, Door\"}," +
                    "\"limit\":{\"type\":\"integer\"}" +
                    "},\"required\":[\"text\"]}",
                Execute = FindByText
            };
            yield return new AgentTool
            {
                Name = "list_property_sets",
                Description = "List all property set names used in the model with the number of elements that have them.",
                ParametersSchema = "{\"type\":\"object\",\"properties\":{},\"required\":[]}",
                Execute = _ => ListPropertySets()
            };
            yield return new AgentTool
            {
                Name = "select_element_in_view",
                Description = "Select and highlight an element in the 3D view and model tree of XbimXplorer. Use after get_entity_details to show the element to the user. Reference is entity label or GlobalId.",
                ParametersSchema =
                    "{\"type\":\"object\",\"properties\":{" +
                    "\"reference\":{\"type\":\"string\",\"description\":\"entity label (#1234) or GlobalId\"}" +
                    "},\"required\":[\"reference\"]}",
                Execute = SelectInView
            };
        }

        private int MaxRows => Math.Max(1, _settings.MaxRows);

        private string RequireModel()
        {
            return Model == null ? "{\"error\":\"No IFC model is open. Ask the user to open a model first.\"}" : null;
        }

        /// <summary>Storey label -> number of elements directly contained in it.</summary>
        private Dictionary<int, int> ContainedCounts()
        {
            var counts = new Dictionary<int, int>();
            foreach (var rel in Model.Instances.OfType<IIfcRelContainedInSpatialStructure>())
            {
                var structure = rel.RelatingStructure;
                if (structure == null)
                    continue;
                counts.TryGetValue(structure.EntityLabel, out var c);
                counts[structure.EntityLabel] = c + rel.RelatedElements.Count;
            }
            return counts;
        }

        // ---------------------------------------------------------------- summary

        private string GetModelSummary()
        {
            var err = RequireModel();
            if (err != null)
                return err;
            var model = Model;

            var sb = new JsonObject
            {
                ["schema"] = model.SchemaVersion.ToString(),
                ["projects"] = new JsonArray(),
                ["storeys"] = new JsonArray(),
                ["totalProducts"] = model.Instances.OfType<IIfcProduct>().Count(),
                ["topElementTypes"] = new JsonArray()
            };

            foreach (var p in model.Instances.OfType<IIfcProject>())
            {
                ((JsonArray)sb["projects"]).Add(new JsonObject
                {
                    ["name"] = p.Name?.ToString(),
                    ["globalId"] = p.GlobalId.ToString()
                });
            }

            var contained = ContainedCounts();
            var storeys = model.Instances.OfType<IIfcBuildingStorey>()
                .OrderBy(s => s.Elevation.HasValue ? (double)s.Elevation.Value : double.MaxValue)
                .ToList();
            foreach (var s in storeys)
            {
                var storeyNode = new JsonObject
                {
                    ["name"] = s.Name?.ToString(),
                    ["containedElements"] = contained.TryGetValue(s.EntityLabel, out var n) ? n : 0
                };
                if (s.Elevation.HasValue)
                {
                    double elev = s.Elevation.Value;
                    storeyNode["elevation"] = elev;
                }
                ((JsonArray)sb["storeys"]).Add(storeyNode);
            }

            var typeCounts = new Dictionary<string, int>();
            foreach (var prod in model.Instances.OfType<IIfcProduct>())
            {
                var t = IfcQuery.TypeName((IPersistEntity)prod);
                typeCounts.TryGetValue(t, out var c);
                typeCounts[t] = c + 1;
            }
            foreach (var kv in typeCounts.OrderByDescending(kv => kv.Value).Take(25))
            {
                ((JsonArray)sb["topElementTypes"]).Add(new JsonObject
                {
                    ["type"] = kv.Key,
                    ["count"] = kv.Value
                });
            }
            return sb.ToJsonString();
        }

        // ---------------------------------------------------------------- spatial

        private string GetSpatialStructure()
        {
            var err = RequireModel();
            if (err != null)
                return err;
            var model = Model;
            var contained = ContainedCounts();
            var projects = model.Instances.OfType<IIfcProject>().ToList();
            var arr = new JsonArray();
            foreach (var project in projects)
            {
                arr.Add(BuildSpatialNode(project, contained, 0));
            }
            return new JsonObject { ["projects"] = arr }.ToJsonString();
        }

        private JsonNode BuildSpatialNode(IIfcObjectDefinition node, Dictionary<int, int> contained, int depth)
        {
            if (depth > 8)
                return null;
            var obj = new JsonObject
            {
                ["type"] = IfcQuery.TypeName((IPersistEntity)node),
                ["name"] = node.Name?.ToString()
            };
            var spatial = node as IIfcSpatialStructureElement;
            if (spatial != null)
            {
                if (contained.TryGetValue(spatial.EntityLabel, out var n))
                    obj["containedElements"] = n;
                var storey = spatial as IIfcBuildingStorey;
                if (storey != null && storey.Elevation.HasValue)
                {
                    double elev = storey.Elevation.Value;
                    obj["elevation"] = elev;
                }
            }
            var children = new JsonArray();
            foreach (var child in node.IsDecomposedBy.SelectMany(r => r.RelatedObjects))
            {
                var spatialChild = child as IIfcSpatialStructureElement;
                if (spatialChild == null && !(child is IIfcProject))
                    continue; // keep the spatial hierarchy only
                var c = BuildSpatialNode(child, contained, depth + 1);
                if (c != null)
                    children.Add(c);
            }
            if (children.Count > 0)
                obj["children"] = children;
            return obj;
        }

        // ---------------------------------------------------------------- query

        private string QueryEntities(JsonNode args)
        {
            var err = RequireModel();
            if (err != null)
                return err;

            var typeFilter = AgentTool.ArgString(args, "entityType");
            var nameFilter = AgentTool.ArgString(args, "nameContains");
            var storeyFilter = AgentTool.ArgString(args, "storey");
            var limit = Math.Min(AgentTool.ArgInt(args, "limit", MaxRows), MaxRows);

            IEnumerable<IIfcProduct> q = Model.Instances.OfType<IIfcProduct>();

            if (!string.IsNullOrWhiteSpace(typeFilter))
            {
                var want = typeFilter.Trim();
                q = q.Where(p => IfcQuery.TypeNameMatches((IPersistEntity)p, want));
            }
            if (!string.IsNullOrWhiteSpace(nameFilter))
            {
                var needle = nameFilter.Trim();
                q = q.Where(p =>
                {
                    var name = p.Name?.ToString();
                    return name != null && name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
                });
            }
            if (!string.IsNullOrWhiteSpace(storeyFilter))
            {
                var sneedle = storeyFilter.Trim();
                q = q.Where(p =>
                {
                    var name = p.IsContainedIn?.Name?.ToString();
                    return name != null && name.IndexOf(sneedle, StringComparison.OrdinalIgnoreCase) >= 0;
                });
            }

            var rows = new JsonArray();
            var total = 0;
            foreach (var p in q)
            {
                total++;
                if (rows.Count >= limit)
                    continue;
                rows.Add(Row(p));
            }
            return new JsonObject
            {
                ["totalMatches"] = total,
                ["returned"] = rows.Count,
                ["truncated"] = total > rows.Count,
                ["rows"] = rows
            }.ToJsonString();
        }

        private static JsonObject Row(IIfcProduct p)
        {
            return new JsonObject
            {
                ["entityLabel"] = ((IPersistEntity)p).EntityLabel,
                ["globalId"] = p.GlobalId.ToString(),
                ["type"] = IfcQuery.TypeName((IPersistEntity)p),
                ["name"] = p.Name?.ToString(),
                ["storey"] = IfcQuery.StoreyOf(p)
            };
        }

        // ---------------------------------------------------------------- details

        private string GetEntityDetails(JsonNode args)
        {
            var err = RequireModel();
            if (err != null)
                return err;
            var reference = AgentTool.ArgString(args, "reference");
            var entity = IfcQuery.ResolveEntity(Model, reference);
            if (entity == null)
                return "{\"error\":\"Entity not found for reference '" + reference + "'\"}";

            var obj = new JsonObject
            {
                ["entityLabel"] = entity.EntityLabel,
                ["type"] = IfcQuery.TypeName(entity)
            };
            var root = entity as IIfcRoot;
            if (root != null)
            {
                obj["globalId"] = root.GlobalId.ToString();
                obj["name"] = root.Name?.ToString();
                obj["description"] = root.Description?.ToString();
            }
            var product = entity as IIfcProduct;
            if (product != null)
            {
                obj["storey"] = IfcQuery.StoreyOf(product);
                obj["objectType"] = product.ObjectType?.ToString();
                var container = product.IsContainedIn;
                if (container != null)
                    obj["container"] = container.Name?.ToString();
            }
            var ifcObject = entity as IIfcObject;
            if (ifcObject != null)
            {
                var props = new JsonArray();
                foreach (var kv in IfcQuery.Properties(ifcObject))
                {
                    props.Add(new JsonObject { ["key"] = kv.Key, ["value"] = kv.Value });
                }
                obj["properties"] = props;
            }
            return obj.ToJsonString();
        }

        // ---------------------------------------------------------------- search

        private string FindByText(JsonNode args)
        {
            var err = RequireModel();
            if (err != null)
                return err;
            var text = AgentTool.ArgString(args, "text");
            if (string.IsNullOrWhiteSpace(text))
                return "{\"error\":\"text parameter is required\"}";
            text = text.Trim();
            var typeFilter = AgentTool.ArgString(args, "entityType");
            var limit = Math.Min(AgentTool.ArgInt(args, "limit", MaxRows), MaxRows);

            IEnumerable<IIfcProduct> q = Model.Instances.OfType<IIfcProduct>();
            if (!string.IsNullOrWhiteSpace(typeFilter))
            {
                var want = typeFilter.Trim();
                q = q.Where(p => IfcQuery.TypeNameMatches((IPersistEntity)p, want));
            }

            var rows = new JsonArray();
            var total = 0;
            foreach (var p in q)
            {
                if (total >= limit * 4) // do not scan the whole model for free text on huge models
                    break;
                if (!MatchesText(p, text))
                    continue;
                total++;
                if (rows.Count < limit)
                    rows.Add(Row(p));
            }
            return new JsonObject
            {
                ["totalMatches"] = total,
                ["returned"] = rows.Count,
                ["truncated"] = total > rows.Count,
                ["rows"] = rows
            }.ToJsonString();
        }

        private static bool MatchesText(IIfcProduct p, string text)
        {
            var name = p.Name?.ToString();
            if (name != null && name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            var description = p.Description?.ToString();
            if (description != null && description.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            var objectType = p.ObjectType?.ToString();
            if (objectType != null && objectType.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            var ifcObject = p as IIfcObject;
            if (ifcObject != null)
            {
                foreach (var kv in IfcQuery.Properties(ifcObject, 80))
                {
                    if (kv.Key.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (kv.Value != null && kv.Value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0))
                        return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- psets

        private string ListPropertySets()
        {
            var err = RequireModel();
            if (err != null)
                return err;
            var counts = new Dictionary<string, int>();
            foreach (var obj in Model.Instances.OfType<IIfcObject>())
            {
                foreach (var rel in obj.IsDefinedBy)
                {
                    var pset = rel.RelatingPropertyDefinition as IIfcPropertySet;
                    var psetName = pset?.Name?.ToString();
                    if (string.IsNullOrEmpty(psetName))
                        continue;
                    counts.TryGetValue(psetName, out var c);
                    counts[psetName] = c + 1;
                }
            }
            var arr = new JsonArray();
            foreach (var kv in counts.OrderByDescending(kv => kv.Value).Take(100))
            {
                arr.Add(new JsonObject { ["name"] = kv.Key, ["elements"] = kv.Value });
            }
            return new JsonObject { ["propertySets"] = arr }.ToJsonString();
        }

        // ---------------------------------------------------------------- select

        private string SelectInView(JsonNode args)
        {
            var err = RequireModel();
            if (err != null)
                return err;
            var reference = AgentTool.ArgString(args, "reference");
            var entity = IfcQuery.ResolveEntity(Model, reference);
            if (entity == null)
                return "{\"error\":\"Entity not found for reference '" + reference + "'\"}";
            _selectAction?.Invoke(entity);
            return new JsonObject
            {
                ["selected"] = true,
                ["entityLabel"] = entity.EntityLabel,
                ["name"] = IfcQuery.NameOf(entity),
                ["type"] = IfcQuery.TypeName(entity)
            }.ToJsonString();
        }
    }
}
