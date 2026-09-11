using System;

namespace WrathAccess.Settings
{
    /// <summary>A settings-menu BUTTON: no value, nothing persisted — activating it runs
    /// <see cref="Run"/> (e.g. the Enhancements "claim earned achievements" sweep).</summary>
    public sealed class ActionSetting : Setting
    {
        public Action Run { get; set; }

        public ActionSetting(string key, string label, string localizationKey = "", Action run = null)
            : base(key, label, localizationKey)
        {
            Run = run;
        }

        public override bool IncludeInPath => false; // never a saved key
        public override object BoxedValue => null;   // not serialized
        public override void LoadValue(object value) { }
    }
}
