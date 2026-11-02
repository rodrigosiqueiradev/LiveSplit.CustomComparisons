using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml;
using CustomComparisons.Engine.Serialization;
using LiveSplit.CustomComparisons;
using LiveSplit.CustomComparisons.Editor;
using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.Model.RunFactories;
using LiveSplit.Options;
using LiveSplit.UI;

namespace LiveSplit.CustomComparisons.Tests
{
    internal sealed class Session : IDisposable
    {
        public Session(LiveSplitState state, CustomComparisonsComponent component)
        {
            State = state;
            Component = component;
        }

        public LiveSplitState State { get; }

        public CustomComparisonsComponent Component { get; }

        public IRun Run => State.Run;

        public void Dispose() => Component.Dispose();
    }

    /// <summary>Loads a checked-in .lss through LiveSplit and hosts the component against it.</summary>
    internal static class LiveSplitHarness
    {
        static LiveSplitHarness()
        {
            // .NET Framework does not run module initializers, so register here as well.
            LiveSplitAssemblyResolve.Register();
            // RunFactory parses splits through native livesplit_core.dll, which ships in x64\ or x86\.
            string native = Path.Combine(AppContext.BaseDirectory, "native", Environment.Is64BitProcess ? "x64" : "x86");
            SetDllDirectory(native);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string path);

        public static Session Open(string lssFile, params string[] programs)
        {
            IRun run = Load(lssFile);
            var settings = new Settings();
            var state = new LiveSplitState(run, null, new Layout(), null, settings);
            var component = new CustomComparisonsComponent(state);
            if (programs != null && programs.Length > 0)
            {
                component.SetSettings(ComparisonsSettings(programs));
            }

            return new Session(state, component);
        }

        public static IRun Load(string lssFile)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", lssFile);
            using (FileStream stream = File.OpenRead(path))
            {
                var factory = new StandardFormatsRunFactory(stream, path);
                return factory.Create(new StandardComparisonGeneratorsFactory());
            }
        }

        public static string ProgramJson(string name, string rootObjectJson, int version = 1) =>
            "{\"format\":\"livesplit-custom-comparison\",\"version\":" + version
            + ",\"name\":\"" + name + "\",\"root\":" + rootObjectJson + "}";

        public static XmlNode ComparisonsSettings(IEnumerable<string> programs)
        {
            var document = new XmlDocument();
            XmlElement root = document.CreateElement("Settings");
            XmlElement version = document.CreateElement("Version");
            version.InnerText = "2";
            root.AppendChild(version);

            XmlElement comparisons = document.CreateElement("Comparisons");
            foreach (string program in programs)
            {
                XmlElement comparison = document.CreateElement("Comparison");
                comparison.InnerText = program;
                comparisons.AppendChild(comparison);
            }

            root.AppendChild(comparisons);
            document.AppendChild(root);
            return root;
        }

        public static ProgramComparisonGenerator Generator(IRun run, string name) =>
            run.ComparisonGenerators.OfType<ProgramComparisonGenerator>().Single(g => g.Name == name);

        public static double?[] Splits(IRun run, string comparison, TimingMethod method)
        {
            var seconds = new double?[run.Count];
            for (int i = 0; i < run.Count; i++)
            {
                TimeSpan? time = run[i].Comparisons.TryGetValue(comparison, out Time value) ? value[method] : null;
                seconds[i] = time?.TotalSeconds;
            }

            return seconds;
        }

        /// <summary>Segment milliseconds from an editor preview reply, null where the row has none.</summary>
        public static double?[] PreviewSegmentMilliseconds(string replyJson)
        {
            var reply = (JsonObject)JsonReader.Parse(replyJson);
            var rows = (JsonArray)reply["rows"];
            return rows.Items.Select(item =>
            {
                JsonValue ms = ((JsonObject)item)["segmentMs"];
                return ms is JsonNumber number ? (double?)number.Value : null;
            }).ToArray();
        }

        public static string Preview(CustomComparisonsComponent component, LiveSplitState state, string programJson, TimingMethod method)
        {
            var bridge = new EditorBridge(component, state, null);
            string timing = method == TimingMethod.GameTime ? "GameTime" : "RealTime";
            return bridge.Handle("{\"type\":\"preview\",\"requestId\":1,\"timingMethod\":\"" + timing + "\",\"program\":" + programJson + "}");
        }
    }
}
