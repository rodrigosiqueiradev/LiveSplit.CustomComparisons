using System;
using LiveSplit.CustomComparisons;
using LiveSplit.Model;
using LiveSplit.UI.Components;

// LiveSplit scans every DLL in its Components folder for this attribute.
[assembly: ComponentFactory(typeof(CustomComparisonsFactory))]

namespace LiveSplit.CustomComparisons;

public sealed class CustomComparisonsFactory : IComponentFactory
{
    public const string Name = "Custom Comparisons";

    // LiveSplit's updater reads update.LiveSplit.CustomComparisons.xml from here. Until the
    // first release publishes that file, the check simply finds nothing to update.
    private const string RepoRawUrl = "https://raw.githubusercontent.com/rodrigosiqueiradev/LiveSplit.CustomComparisons/main/";

    public string ComponentName => Name;

    public string Description => "Build your own comparisons from blocks: filters, aggregates, ranges and fallbacks over your run history.";

    public ComponentCategory Category => ComponentCategory.Other;

    public IComponent Create(LiveSplitState state) => new CustomComparisonsComponent(state);

    public string UpdateName => ComponentName;

    public string XMLURL => RepoRawUrl + "update.LiveSplit.CustomComparisons.xml";

    public string UpdateURL => RepoRawUrl;

    public Version Version => typeof(CustomComparisonsFactory).Assembly.GetName().Version;
}
