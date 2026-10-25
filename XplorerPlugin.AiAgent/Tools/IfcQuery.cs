using System;
using System.Collections.Generic;
using System.Linq;
using Xbim.Common;
using Xbim.Ifc4.Interfaces;

namespace XplorerPlugin.AiAgent.Tools
{
    /// <summary>Schema-agnostic helpers for reading IFC data through IIfc* interfaces.</summary>
    internal static class IfcQuery
    {
        public static string Ref(IPersistEntity e)
        {
            if (e == null)
                return null;
            var root = e as IIfcRoot;
            var gid = root != null ? root.GlobalId.ToString() : null;
            return gid != null ? "#" + e.EntityLabel + " (" + gid + ")" : "#" + e.EntityLabel;
        }

        public static string NameOf(IPersistEntity e)
        {
            var named = e as IIfcRoot;
            return named?.Name?.ToString() ?? "";
        }

        public static string TypeName(IPersistEntity e)
        {
            return e == null ? "" : e.GetType().Name.Replace("Ifc", "");
        }

        /// <summary>
        /// Matches an entity against an IFC class name (e.g. 'Wall', 'IfcWall', 'IIfcWall').
        /// Subtypes are included via the IIfc* interfaces, so 'Wall' also matches WallStandardCase.
        /// </summary>
        public static bool TypeNameMatches(IPersistEntity e, string want)
        {
            if (e == null || string.IsNullOrWhiteSpace(want))
                return false;
            want = want.Trim();
            if (want.StartsWith("IIfc", StringComparison.Ordinal))
                want = want.Substring(4);
            else if (want.StartsWith("Ifc", StringComparison.Ordinal))
                want = want.Substring(3);
            if (string.Equals(e.GetType().Name, "Ifc" + want, StringComparison.OrdinalIgnoreCase))
                return true;
            var iface = IfcInterfaces.Get("IIfc" + want);
            return iface != null && iface.IsInstanceOfType(e);
        }

        private static class IfcInterfaces
        {
            private static readonly Dictionary<string, Type> Cache = BuildCache();

            private static Dictionary<string, Type> BuildCache()
            {
                var map = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                Type[] types;
                try
                {
                    types = typeof(IIfcRoot).Assembly.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }
                foreach (var t in types)
                {
                    if (t.IsInterface && t.Name.StartsWith("IIfc", StringComparison.Ordinal))
                        map[t.Name] = t;
                }
                return map;
            }

            public static Type Get(string name)
            {
                Type t;
                return Cache.TryGetValue(name, out t) ? t : null;
            }
        }

        public static string StoreyOf(IIfcProduct product)
        {
            return product?.IsContainedIn?.Name?.ToString() ?? "";
        }

        public static IEnumerable<KeyValuePair<string, string>> Properties(IIfcObject obj, int maxValues = 200)
        {
            var count = 0;
            foreach (var rel in obj.IsDefinedBy)
            {
                if (count >= maxValues)
                    yield break;
                var pset = rel.RelatingPropertyDefinition as IIfcPropertySet;
                if (pset == null)
                    continue;
                foreach (var p in pset.HasProperties)
                {
                    if (count >= maxValues)
                        yield break;
                    var single = p as IIfcPropertySingleValue;
                    if (single == null)
                        continue;
                    var val = single.NominalValue != null ? single.NominalValue.ToString() : "";
                    yield return new KeyValuePair<string, string>(
                        pset.Name?.ToString() + "." + p.Name.ToString(), val);
                    count++;
                }
            }
        }

        public static IPersistEntity ResolveEntity(IModel model, string reference)
        {
            if (model == null || string.IsNullOrWhiteSpace(reference))
                return null;
            reference = reference.Trim();
            if (reference.StartsWith("#", StringComparison.Ordinal))
                reference = reference.Substring(1);
            int id;
            if (int.TryParse(reference, out id))
            {
                try
                {
                    return model.Instances[id];
                }
                catch (Exception)
                {
                    // not an entity label, try global id
                }
            }
            var gid = reference.Trim('"');
            foreach (var root in model.Instances.OfType<IIfcRoot>())
            {
                if (string.Equals(root.GlobalId.ToString(), gid, StringComparison.OrdinalIgnoreCase))
                    return (IPersistEntity)root;
            }
            return null;
        }

        public static string EscapeCsv(string s)
        {
            if (s == null)
                return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        public static string HtmlEncode(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }
    }
}
