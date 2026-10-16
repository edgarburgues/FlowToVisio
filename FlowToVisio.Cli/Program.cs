using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LinkeD365.FlowToVisio
{
    /// <summary>
    /// flowtovisio: draws Power Automate cloud flows (and Logic Apps) into a Visio file, one page per flow,
    /// with the FlowToVisio engine. Input files can be:
    ///  - a flow definition as Dataverse keeps it (workflow.clientdata: {"properties": {"definition", "connectionReferences"}}),
    ///  - an object with that definition in "clientdata" (string or object), optionally with "_meta.name",
    ///  - a flow or Logic App as the Power Automate / Azure APIs return it ({"name", "properties": {"displayName", "definition", ...}}).
    /// </summary>
    internal static class Program
    {
        private const string Usage = @"Usage: flowtovisio <file.json | folder>... -o <output.vsdx> [options]

  -o, --output <file>     Visio file to write (.vsdx). Required.
  -l, --list <file>       Text file with one input path per line (keeps that order).
  -m, --manifest <file>   Writes a JSON with one entry per page: page, name, source, shapes, unknown actions, error.
  -a, --actions <file>    actions.json to use instead of the embedded one.
  -d, --details           Show trigger conditions, concurrency, secure inputs/outputs, tracked properties and tracking id.
  -h, --help              This help.

Folders are read recursively (*.json), sorted by path. Nothing is sent anywhere: no telemetry, no download.";

        private static int Main(string[] args)
        {
            var inputs = new List<string>();
            string output = null, manifest = null, actions = null;
            var details = false;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o": case "--output": output = args[++i]; break;
                    case "-l": case "--list": inputs.AddRange(File.ReadAllLines(args[++i]).Select(l => l.Trim()).Where(l => l.Length > 0)); break;
                    case "-m": case "--manifest": manifest = args[++i]; break;
                    case "-a": case "--actions": actions = args[++i]; break;
                    case "-d": case "--details": details = true; break;
                    case "-h": case "--help": Console.WriteLine(Usage); return 0;
                    default: inputs.Add(args[i]); break;
                }
            }
            if (output == null || inputs.Count == 0)
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }

            var files = new List<string>();
            foreach (var input in inputs)
            {
                if (Directory.Exists(input))
                    files.AddRange(Directory.GetFiles(input, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                else if (File.Exists(input))
                    files.Add(input);
                else
                {
                    Console.Error.WriteLine("Not found: " + input);
                    return 2;
                }
            }

            Utils.ActionTemplateJson = actions != null ? File.ReadAllText(actions) : ReadResource("actions.json");
            Utils.Display = new Display
            {
                ShowTriggers = details,
                ShowConCurrency = details,
                ShowSecure = details,
                ShowTrackedProps = details,
                ShowTrackingID = details,
            };

            var pages = new List<object>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var drawn = 0;
            using (var visio = new VisioDocument(output))
            {
                foreach (var file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file), error = null;
                    var shapes = 0;
                    AppInsights.Events.Clear();
                    try
                    {
                        var root = JObject.Parse(File.ReadAllText(file));
                        name = (string)root["_meta"]?["name"] ?? (string)root["properties"]?["displayName"] ?? name;
                        var flow = FlowObject(root);
                        if (flow == null)
                            error = "no flow definition in the file";
                        else
                        {
                            Utils.Comments = ReadComments(root);
                            shapes = visio.AddFlow(flow, PageName(name, usedNames));
                            drawn++;
                        }
                    }
                    catch (Exception exc)
                    {
                        error = exc.GetType().Name + ": " + exc.Message;
                    }

                    var unknown = AppInsights.Events.Distinct().ToList();
                    Console.WriteLine(error == null
                        ? $"{drawn,4}  {name}  ({shapes} shapes{(unknown.Count > 0 ? ", " + unknown.Count + " unknown" : "")})"
                        : $"   -  {name}  NOT DRAWN: {error}");
                    pages.Add(new { page = error == null ? drawn : (int?)null, name, source = Path.GetFullPath(file), shapes, unknown, error });
                }
            }

            if (manifest != null)
                File.WriteAllText(manifest, JsonConvert.SerializeObject(pages, Formatting.Indented));
            Console.WriteLine($"{drawn} of {files.Count} flows drawn into {output}");
            return drawn == files.Count ? 0 : 1;
        }

        /// <summary>The definition in the shape the engine expects: {"properties": {"definition", "connectionReferences"}}.</summary>
        private static JObject FlowObject(JObject root)
        {
            if (root["properties"]?["definition"] != null)
                return root;
            var clientData = root["clientdata"];
            if (clientData == null || clientData.Type == JTokenType.Null)
                return null;
            var flow = clientData.Type == JTokenType.String ? JObject.Parse((string)clientData) : (JObject)clientData;
            return flow["properties"]?["definition"] != null ? flow : null;
        }

        private static List<Comment> ReadComments(JObject root)
        {
            if (!(root["comments"] is JArray comments))
                return new List<Comment>();
            return comments.Select(c => new Comment
            {
                AnchorId = (string)c["anchor"],
                CommentString = (string)c["text"] ?? (string)c["body"],
                Commenter = (string)c["createdby"],
                Created = (DateTime?)c["createdon"] ?? DateTime.MinValue,
            }).ToList();
        }

        /// <summary>Visio page names must be unique; long names are cut.</summary>
        private static string PageName(string name, HashSet<string> used)
        {
            var baseName = name.Length > 60 ? name.Substring(0, 60).TrimEnd() : name;
            var pageName = baseName;
            for (var n = 2; !used.Add(pageName); n++)
                pageName = $"{baseName} ({n})";
            return pageName;
        }

        private static string ReadResource(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }
    }
}
