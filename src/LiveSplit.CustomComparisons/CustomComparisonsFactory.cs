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

    // LiveSplit's updater reads update.LiveSplit.CustomComparisons.xml from the latest GitHub
    // release, then downloads LiveSplit.CustomComparisons.dll from the same release into
    // Components. CI attaches both to every release (see CHANGELOG.md). The repository must be
    // public: until it is, or before the first release, the check just finds nothing.
    private const string LatestReleaseUrl = "https://github.com/rodrigosiqueiradev/LiveSplit.CustomComparisons/releases/latest/download/";

    public string ComponentName => Name;

    public string Description => "Build your own comparisons from blocks: filters, aggregates, ranges and fallbacks over your run history.";

    public ComponentCategory Category => ComponentCategory.Other;

    public IComponent Create(LiveSplitState state) => new CustomComparisonsComponent(state);

    public string UpdateName => ComponentName;

    public string XMLURL => LatestReleaseUrl + "update.LiveSplit.CustomComparisons.xml";

    public string UpdateURL => LatestReleaseUrl;

    public Version Version => typeof(CustomComparisonsFactory).Assembly.GetName().Version;
}
