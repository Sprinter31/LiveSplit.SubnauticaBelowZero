using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Livesplit.SubnauticaBelowZero
{
    public abstract class SubnauticaSplitSetting : UserControl
    {
        public abstract ComboBox ComboBox { get; }
        public abstract CheckBox CbSplitOnce { get; }
        public abstract Button BtnEdit { get; }
        public abstract Button BtnRemove { get; }
        public abstract SplitName SplitName { get; }
        public abstract SubnauticaSplit Split { get; }

        public static SplitName GetSplitName(string text)
        {
            foreach (SplitName split in Enum.GetValues(typeof(SplitName)))
            {
                string name = split.ToString();
                MemberInfo info = typeof(SplitName).GetMember(name)[0];
                DescriptionAttribute description = (DescriptionAttribute)info.GetCustomAttributes(typeof(DescriptionAttribute), false)[0];

                if (name.Equals(text, StringComparison.OrdinalIgnoreCase) || description.Description.Equals(text, StringComparison.OrdinalIgnoreCase))
                {
                    return split;
                }
            }
            return SplitName.None;
        }

        public static TechType GetTechType(string text)
        {
            foreach (TechType techType in Enum.GetValues(typeof(TechType)))
            {
                string name = techType.ToString();
                string rawName = Localization.GetRawName(name);

                if (name.Equals(text, StringComparison.OrdinalIgnoreCase) || rawName.Equals(text, StringComparison.OrdinalIgnoreCase))
                {
                    return techType;
                }
            }
            return TechType.None;
        }

        public static EncyEntry GetEncyEntry(string text)
        {
            foreach (EncyEntry encyEntry in Enum.GetValues(typeof(EncyEntry)))
            {
                string name = encyEntry.ToString();
                string rawName = Localization.GetRawName(name);

                if (name.Equals(text, StringComparison.OrdinalIgnoreCase) || rawName.Equals(text, StringComparison.OrdinalIgnoreCase))
                {
                    return encyEntry;
                }
            }
            return EncyEntry.None;
        }
    }    
    
    public class SubnauticaSplit
    {
        public SplitName SplitName { get; set; }
        public bool OnlySplitOnce { get; set; }
        public virtual string GetDescription() => string.Empty;
    }

    public enum SplitName
    {
        [Description("None"), ToolTip("None")]
        None,
        [Description("Inventory Split"), ToolTip("Splits when you have a certain item in the inventory")]
        Inventory,
        [Description("Blueprint Split"), ToolTip("Splits when you have a certain blueprint unlocked")]
        Blueprint,
        [Description("Encyclopedia Split"), ToolTip("Splits when you have a certain entry in the encyclopedia unlocked")]
        Encyclopedia,        
    }
    public class ToolTipAttribute : Attribute
    {
        public string ToolTip { get; set; }
        public ToolTipAttribute(string text)
        {
            ToolTip = text;
        }
    }
}