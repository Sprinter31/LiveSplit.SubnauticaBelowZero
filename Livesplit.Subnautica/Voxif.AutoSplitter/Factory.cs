using Livesplit.SubnauticaBelowZero;
using LiveSplit.Model;
using LiveSplit.UI.Components;
using System;
using System.Reflection;

namespace LiveSplit.SubnauticaBelowZero
{
    public class Factory : IComponentFactory {
        public string ComponentName => "Subnautica Below Zero Autosplitter";

        public string Description => "Autosplitter for Subnautica Below zero";

        public ComponentCategory Category => ComponentCategory.Control;

        public string UpdateName => ComponentName;

        public string XMLURL => UpdateURL + "Components/SubnauticaBelowZero.Updates.xml";

        public string UpdateURL => "https://raw.githubusercontent.com/Sprinter31/LiveSplit.SubnauticaBelowZero/main/";

        public Version Version => ExAssembly.GetName().Version;

        public IComponent Create(LiveSplitState state) => new SubnauticaComponent(state);

        public static Assembly ExAssembly = Assembly.GetExecutingAssembly();
    }
}