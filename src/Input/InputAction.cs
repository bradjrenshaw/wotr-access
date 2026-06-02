using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WrathAccess.Input
{
    /// <summary>
    /// A named mod command with one or more bindings. Exposes per-frame phase
    /// state (JustPressed/Held/Released); the dispatcher routes phases into the
    /// active navigator, and fires <see cref="Performed"/> for a JustPressed that
    /// the navigator didn't consume (the "global hotkey" fallback).
    /// </summary>
    public class InputAction
    {
        public string Key { get; }
        public string Label { get; }

        private readonly List<InputBinding> _bindings = new List<InputBinding>();
        public IReadOnlyList<InputBinding> Bindings => _bindings;

        /// <summary>Fired on JustPressed when not consumed by the navigator.</summary>
        public event Action Performed;

        /// <summary>Fired whenever the binding set changes (add/clear) — BindingSetting saves on this.</summary>
        public event Action BindingsChanged;

        public string BindingsDisplay =>
            _bindings.Count == 0 ? "(none)" : string.Join(", ", _bindings.Select(b => b.DisplayName));

        public InputAction(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public InputAction AddBinding(InputBinding binding)
        {
            _bindings.Add(binding);
            BindingsChanged?.Invoke();
            return this;
        }

        public InputAction AddBinding(KeyCode key, bool ctrl = false, bool shift = false, bool alt = false)
            => AddBinding(new KeyboardBinding(key, ctrl, shift, alt));

        /// <summary>Drop all bindings (a rebind replaces them, or a saved config reloads them).</summary>
        public void ClearBindings()
        {
            _bindings.Clear();
            BindingsChanged?.Invoke();
        }

        public bool JustPressed { get { for (int i = 0; i < _bindings.Count; i++) if (_bindings[i].JustPressed()) return true; return false; } }
        public bool Held { get { for (int i = 0; i < _bindings.Count; i++) if (_bindings[i].Held()) return true; return false; } }
        public bool Released { get { for (int i = 0; i < _bindings.Count; i++) if (_bindings[i].Released()) return true; return false; } }

        /// <summary>Whether this action auto-repeats while held (nav directions + Tab). Set via Repeating().</summary>
        public bool Repeats { get; private set; }
        internal float NextRepeatTime;

        public InputAction Repeating() { Repeats = true; return this; }

        internal void InvokePerformed() => Performed?.Invoke();
    }
}
