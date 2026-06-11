using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WrathAccess.Settings
{
    /// <summary>One option of a <see cref="ChoiceSetting"/>: a stable <see cref="Id"/> (persisted) and a
    /// display label (localized via the "settings" table with a raw fallback).</summary>
    public sealed class Choice
    {
        public string Id { get; }
        private readonly string _labelFallback;
        private readonly string _localizationKey;

        public Choice(string id, string label, string localizationKey = "")
        {
            Id = id;
            _labelFallback = label;
            _localizationKey = localizationKey;
        }

        public string Label => string.IsNullOrEmpty(_localizationKey)
            ? _labelFallback
            : WrathAccess.Localization.LocalizationManager.GetOrDefault("settings", _localizationKey, _labelFallback);
    }

    /// <summary>A pick-one-of-N setting (→ a dropdown in the menu). Persisted by the chosen option's Id.</summary>
    public sealed class ChoiceSetting : Setting
    {
        public IReadOnlyList<Choice> Choices { get; }
        public string Default { get; }
        public string ValueId { get; private set; }
        public event Action<string> Changed;

        public ChoiceSetting(string key, string label, IReadOnlyList<Choice> choices, string defaultId,
            string localizationKey = "") : base(key, label, localizationKey)
        {
            Choices = choices ?? new List<Choice>();
            Default = defaultId;
            ValueId = defaultId;
        }

        public Choice Current => Choices.FirstOrDefault(c => c.Id == ValueId);

        public override void ResetToDefault() => Set(Default);

        public void Set(string id)
        {
            if (ValueId == id || Choices.All(c => c.Id != id)) return;
            ValueId = id;
            ModSettings.MarkDirty();
            Changed?.Invoke(id);
        }

        public override object BoxedValue => ValueId;

        public override void LoadValue(object value)
        {
            if (value is JToken t && t.Type == JTokenType.String)
            {
                var id = t.Value<string>();
                if (id != ValueId && Choices.Any(c => c.Id == id)) { ValueId = id; Changed?.Invoke(id); }
            }
        }
    }
}
