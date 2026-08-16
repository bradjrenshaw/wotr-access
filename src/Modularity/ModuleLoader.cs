using System;
using System.Reflection;

namespace WrathAccess.Modularity
{
    /// <summary>
    /// Loads and hot-swaps the feature module (<c>Module/WrathAccess.Module.dll</c> under the mod
    /// folder — NOT under Assemblies/, where the Owlcat loader would Assembly.LoadFrom it and lock
    /// the file). The dll is loaded FROM BYTES so the file is never locked: rebuilding and copying
    /// it while the game runs is safe, and <see cref="Reload"/> swaps generations without a restart.
    ///
    /// Mono gotcha (learned in tbw-access — do not regress): Mono binds non-strong-named assemblies
    /// by SIMPLE NAME, so byte-loading an unchanged name returns the OLD image and the reload
    /// silently no-ops. The module csproj therefore stamps a fresh <c>AssemblyName</c>
    /// (WrathAccess.Module_yyyyMMddHHmmss) into every build; <see cref="Describe"/> exposes the
    /// loaded identity so a swap can be verified. Old generations leak (net48 has no collectible
    /// load contexts) — acceptable for a dev loop.
    /// </summary>
    public static class ModuleLoader
    {
        public static Assembly CurrentAssembly { get; private set; }
        public static int Generation { get; private set; }

        private static IModModule _module;
        private static string _path;

        /// <summary>Load the initial generation. Returns false (logged) when the dll is missing —
        /// the host then idles, which keeps the failure diagnosable rather than fatal.</summary>
        public static bool LoadInitial(string modulePath)
        {
            _path = modulePath;
            return LoadGeneration();
        }

        /// <summary>Dispose the current generation and load a fresh one from the same path.</summary>
        public static string Reload()
        {
            DisposeCurrent();
            bool ok = LoadGeneration();
            return ok ? Describe() : "[reload failed] see Player.log\n";
        }

        public static void Tick()
        {
            try { _module?.Tick(); }
            catch (Exception e) { Main.Log?.Error("[module tick] " + e); }
        }

        public static string Describe()
        {
            if (_module == null) return "module: none loaded\n";
            var asm = CurrentAssembly;
            return "module: gen " + Generation
                + "\nassembly: " + (asm != null ? asm.GetName().Name : "?")
                + "\nfile: " + _path + " (written " + SafeWriteTime(_path) + ")\n";
        }

        private static string SafeWriteTime(string p)
        {
            try { return System.IO.File.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm:ss"); }
            catch { return "?"; }
        }

        private static void DisposeCurrent()
        {
            if (_module == null) return;
            try { _module.Dispose(); }
            catch (Exception e) { Main.Log?.Error("[module dispose] " + e); }
            _module = null;
        }

        private static bool LoadGeneration()
        {
            try
            {
                if (!System.IO.File.Exists(_path))
                {
                    Main.Log?.Error("[module] not found: " + _path);
                    return false;
                }
                var bytes = System.IO.File.ReadAllBytes(_path);
                var asm = Assembly.Load(bytes);
                Type impl = null;
                foreach (var t in asm.GetTypes())
                    if (typeof(IModModule).IsAssignableFrom(t) && !t.IsAbstract) { impl = t; break; }
                if (impl == null)
                {
                    Main.Log?.Error("[module] no IModModule implementor in " + asm.GetName().Name);
                    return false;
                }
                var module = (IModModule)Activator.CreateInstance(impl);
                Generation++;
                CurrentAssembly = asm;
                _module = module;
                module.Load();
                Main.Log?.Log("[module] gen " + Generation + " loaded: " + asm.GetName().Name);
                return true;
            }
            catch (Exception e)
            {
                Main.Log?.Error("[module load] " + e);
                return false;
            }
        }
    }
}
