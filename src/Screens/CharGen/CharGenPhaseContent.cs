using System;
using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.CharGen.Phases;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Class;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Portrait;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Pregen;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Race;
using WrathAccess.UI;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Builds (and live-refreshes) the navigable content for one chargen phase. One subclass per
    /// phase VM type, created fresh on phase entry by <see cref="CharGenPhaseContentFactory"/> so it
    /// can hold per-entry state (panels it refreshes). Keeps each phase isolated; CharGenScreen just
    /// dispatches. Mirrors the "let the game drive, we wrap+activate its controls" model.
    /// </summary>
    public abstract class CharGenPhaseContent
    {
        public abstract void Build(Container content);

        /// <summary>Called each frame while the phase is unchanged — for in-place updates (e.g. a
        /// list that depends on a sub-selection). Must not disturb the focus path.</summary>
        public virtual void Tick() { }
    }

    /// <summary>Typed base: reads the concrete phase VM without casting.</summary>
    public abstract class CharGenPhaseContent<TVM> : CharGenPhaseContent where TVM : CharGenPhaseBaseVM
    {
        protected readonly TVM Phase;
        protected CharGenPhaseContent(TVM phase) { Phase = phase; }
    }

    /// <summary>Maps a phase VM type to its content builder. Add a phase = write a class + Register.</summary>
    public static class CharGenPhaseContentFactory
    {
        private static readonly Dictionary<Type, Func<CharGenPhaseBaseVM, CharGenPhaseContent>> _map =
            new Dictionary<Type, Func<CharGenPhaseBaseVM, CharGenPhaseContent>>();

        static CharGenPhaseContentFactory() { RegisterDefaults(); }

        public static void Register<TVM>(Func<TVM, CharGenPhaseContent> create) where TVM : CharGenPhaseBaseVM
            => _map[typeof(TVM)] = vm => create((TVM)vm);

        /// <summary>The content builder for a phase, or null if we don't handle it yet (→ placeholder).</summary>
        public static CharGenPhaseContent Create(CharGenPhaseBaseVM phase)
        {
            if (phase != null && _map.TryGetValue(phase.GetType(), out var create)) return create(phase);
            return null;
        }

        private static void RegisterDefaults()
        {
            Register<CharGenPregenPhaseVM>(vm => new PregenPhaseContent(vm));
            Register<CharGenPortraitPhaseVM>(vm => new PortraitPhaseContent(vm));
            Register<CharGenClassPhaseVM>(vm => new ClassPhaseContent(vm));
            Register<CharGenRacePhaseVM>(vm => new RacePhaseContent(vm));
        }
    }
}
