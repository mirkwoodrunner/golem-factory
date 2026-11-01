using GolemFactory.Diagnostics;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The endgame profile's sampler, as the node layer sees it (docs/performance-report-template.md
    /// §3): null unless the <c>perf</c> scenario sets it, so a node's <c>using (NodePerf.Measure(...))</c>
    /// costs a null check in the game. Lets the report say which nodes a frame's process time
    /// goes to, beside Core's sections.
    /// </summary>
    public static class NodePerf
    {
        public static PerfSampler Sampler { get; set; }

        public static PerfSampler.Scope Measure(string section) => Sampler.Measure(section);
    }
}
