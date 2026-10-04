using System.Collections.Generic;
using System.Linq;

namespace WrathAccess.Settings
{
    /// <summary>A node that nests child settings (a tree branch). Not persisted itself.</summary>
    public class CategorySetting : Setting
    {
        private readonly List<Setting> _children = new List<Setting>();
        public override bool IncludeInPath { get; }

        public IReadOnlyList<Setting> Children => _children;

        /// <summary>When true the settings menu renders this category's children flat inside the parent
        /// group instead of as a nested group of their own (the saved path keeps the key). For
        /// subtrees whose heading would only repeat a sibling dropdown's selected value.</summary>
        public bool Inline { get; set; }

        public CategorySetting(string key, string label, bool includeInPath = true, string localizationKey = "")
            : base(key, label, localizationKey)
        {
            IncludeInPath = includeInPath;
        }

        public CategorySetting Add(Setting child)
        {
            child.Parent = this;
            _children.Add(child);
            return this;
        }

        /// <summary>Remove a child (e.g. deleting a user-created overlay's subtree). Reindex + save after.</summary>
        /// <summary>Reset every child (recursively) to its default.</summary>
        public override void ResetToDefault()
        {
            for (int i = 0; i < _children.Count; i++) _children[i].ResetToDefault();
        }

        public void Remove(Setting child)
        {
            if (child != null && _children.Remove(child)) child.Parent = null;
        }

        public T Get<T>(string key) where T : Setting
            => _children.OfType<T>().FirstOrDefault(c => c.Key == key);

        public Setting GetByKey(string key) => _children.FirstOrDefault(c => c.Key == key);

        public override object BoxedValue => null;
        public override void LoadValue(object value) { }
    }

    /// <summary>The tree root: no parent, contributes nothing to child paths.</summary>
    public sealed class RootCategorySetting : CategorySetting
    {
        public RootCategorySetting() : base("", "", includeInPath: false) { }
        public override bool IsRoot => true;
    }
}
