using System.Collections.Generic;
using System.Reflection;

namespace LinkeD365.FlowToVisio
{
    /// <summary>
    /// Replaces the XrmToolBox telemetry client: nothing is sent anywhere. The engine reports the actions it does
    /// not know through it, so those events are kept to tell the user which shapes are drawn as plain actions.
    /// </summary>
    public class AppInsights
    {
        public static readonly List<string> Events = new List<string>();

        public AppInsights(string endpoint, string key, Assembly assembly)
        {
        }

        public void WriteEvent(string eventName, double? count = null)
        {
            if (eventName.StartsWith("No ") || eventName.StartsWith("Error in Action")) Events.Add(eventName);
        }
    }

    /// <summary>
    /// Flow comment as the engine draws it. The plugin reads them from Dataverse; the command line tool takes
    /// them from the "comments" array of the input file, if there is one.
    /// </summary>
    public class Comment
    {
        public string AnchorId { get; set; }

        public int Kind { get; set; }

        public string Commenter { get; set; }

        public string CommentString { get; set; }

        public System.DateTime Created { get; set; }
    }
}
