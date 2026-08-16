#if DEBUG
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace WrathAccess.Dev
{
    /// <summary>
    /// Dev-only in-process driver, gated behind the WRATHACCESS_DEV env var. Exposes a loopback HTTP
    /// server so an external driver (Claude, curl) can introspect and drive the live mod/game:
    ///   POST /eval           body = C# source, run against the live game (REPL state persists across
    ///                        calls); returns captured output + result/errors.
    ///   GET  /speech?since=N lines the mod has spoken since cursor N (we can't hear the TTS, so this is
    ///                        how we observe it). Tapped at the SpeechManager chokepoint.
    ///   GET  /health         liveness.
    ///
    /// Eval runs on the Unity main thread: HTTP requests enqueue a job and block until <see cref="Pump"/>
    /// (called once per frame from Main.OnFrame) executes it. /speech reads a thread-safe buffer directly
    /// off the HTTP thread.
    ///
    /// This whole subsystem is compiled only in DEBUG (#if DEBUG) — a Release build has none of it, so it
    /// cannot be toggled on by anything. Even in Debug it stays inert unless WRATHACCESS_DEV=1.
    /// </summary>
    public sealed class DevServer
    {
        public static readonly DevServer Instance = new DevServer();

        // Routes the MODULE registers on Load (and unregisters on Dispose) — /gui, /input,
        // /loadsave live module-side because they call module types. handler(method, body, query).
        private readonly Dictionary<string, Func<string, string, string, string>> _moduleRoutes
            = new Dictionary<string, Func<string, string, string, string>>(StringComparer.OrdinalIgnoreCase);
        private readonly object _routeLock = new object();

        public void RegisterRoute(string route, Func<string, string, string, string> handler)
        { lock (_routeLock) _moduleRoutes[route] = handler; }

        public void UnregisterRoute(string route)
        { lock (_routeLock) _moduleRoutes.Remove(route); }

        /// <summary>Run work on the Unity main thread (next Pump) and block for its result — for
        /// module-registered route handlers that touch game state.</summary>
        public string OnMain(Func<string> work, int timeoutSeconds = 30) => OnMainThread(work, timeoutSeconds);

        /// <summary>Feed one spoken line into the /speech ring buffer (the module wires its
        /// SpeechManager observer here — we can't hear the TTS; this is how the driver observes it).</summary>
        public void TapSpeech(string line) => _speech.Add(line);

        public const string EnableEnv = "WRATHACCESS_DEV";
        public const string PortEnv = "WRATHACCESS_DEV_PORT";
        public const string MarkerFile = "devserver.enable"; // under persistentDataPath/WrathAccess/
        private const int DefaultPort = 8771; // Tangledeep's dev server uses 8770; keep ours distinct.

        // Enabled by the env var OR a marker file the dev launcher drops. The marker is immune to HOW the
        // game is launched: a Steam relaunch spawns a fresh process that doesn't inherit our $env: var
        // (observed — the server returned early at the env gate while the mod itself loaded fine), whereas
        // the file is read from persistentDataPath regardless. Still DEBUG-only, so neither exists in Release.
        private static bool DevEnabled(out string how)
        {
            how = null;
            if (Environment.GetEnvironmentVariable(EnableEnv) == "1") { how = "env"; return true; }
            try
            {
                string marker = System.IO.Path.Combine(
                    UnityEngine.Application.persistentDataPath, "WrathAccess", MarkerFile);
                if (System.IO.File.Exists(marker)) { how = "marker"; return true; }
            }
            catch { }
            return false;
        }

        private sealed class Job
        {
            public Func<string> Work;
            public string Result = "";
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        private readonly SpeechLog _speech = new SpeechLog();
        private readonly CSharpEvaluator _evaluator = new CSharpEvaluator();
        private readonly ConcurrentQueue<Job> _jobs = new ConcurrentQueue<Job>();
        private DevHttpServer _http;
        private bool _enabled;

        /// <summary>Stand up the server if WRATHACCESS_DEV=1; otherwise stay inert.</summary>
        public void Start()
        {
            string how;
            if (!DevEnabled(out how)) return;

            // Keep the Unity player loop (and thus our main-thread Pump, and thus /eval) running while the
            // game is unfocused — otherwise the loop freezes the moment our terminal takes focus and eval
            // jobs never execute. WotR's AutoPauseController pauses game LOGIC on focus loss but not the
            // loop, and nothing in the game writes runInBackground, so setting it true here (during focused
            // boot, before any focus loss) and re-asserting each Pump holds. DEBUG/dev-only behavior.
            UnityEngine.Application.runInBackground = true;

            int port = DefaultPort;
            string p = Environment.GetEnvironmentVariable(PortEnv);
            if (!string.IsNullOrEmpty(p)) int.TryParse(p, out port);

            // (The module wires its SpeechManager observer to TapSpeech on Load.)
            try
            {
                _http = new DevHttpServer(port, HandleRequest);
                _http.Start();
                _enabled = true;
                Main.Log?.Log("Dev server on http://127.0.0.1:" + port + " (gate: " + how + "; POST /eval, GET /speech)");
            }
            catch (Exception e)
            {
                Main.Log?.Error("Dev server failed to start: " + e);
            }
        }

        /// <summary>Run queued main-thread jobs. Call once per frame from the tick.</summary>
        public void Pump()
        {
            if (!_enabled) return;
            UnityEngine.Application.runInBackground = true; // re-assert each frame (cheap insurance vs any reset)
            Job job;
            while (_jobs.TryDequeue(out job))
            {
                try { job.Result = job.Work() ?? ""; }
                catch (Exception e) { job.Result = "[host error] " + e + "\n"; }
                job.Done.Set();
            }
        }

        /// <summary>Run <paramref name="work"/> on the main thread (next Pump) and block for its result.</summary>
        private string OnMainThread(Func<string> work, int timeoutSeconds = 30)
        {
            var job = new Job { Work = work };
            _jobs.Enqueue(job);
            if (!job.Done.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
                return "[timeout] main thread did not run the job within " + timeoutSeconds + "s (frozen / not pumping?)\n";
            return job.Result;
        }

        // Runs on the HTTP thread.
        private string HandleRequest(string method, string path, string body)
        {
            string route = path;
            string query = "";
            int q = path.IndexOf('?');
            if (q >= 0) { route = path.Substring(0, q); query = path.Substring(q + 1); }

            if (route == "/eval" && method == "POST")
            {
                if (string.IsNullOrWhiteSpace(body)) return "[empty] POST C# source as the request body\n";
                return OnMainThread(() => _evaluator.Eval(body));
            }

            if (route == "/screenshot" && method == "GET")
                return Screenshot();

            if (route == "/reload" && method == "POST")
                return OnMainThread(() =>
                {
                    string result = WrathAccess.Modularity.ModuleLoader.Reload();
                    _evaluator.Reset(); // next eval binds against the fresh generation
                    return result;
                }, 60);

            if (route == "/module" && method == "GET")
                return WrathAccess.Modularity.ModuleLoader.Describe();

            // Module-registered routes (/gui, /input, /loadsave, ...) — absent until the module loads.
            Func<string, string, string, string> moduleHandler;
            lock (_routeLock) _moduleRoutes.TryGetValue(route, out moduleHandler);
            if (moduleHandler != null)
                return moduleHandler(method, body, query);

            if (route == "/speech" && method == "GET")
            {
                long since = 0;
                foreach (string kv in query.Split('&'))
                    if (kv.StartsWith("since=", StringComparison.Ordinal))
                        long.TryParse(kv.Substring("since=".Length), out since);
                long next;
                string lines = _speech.Render(since, out next);
                return "cursor: " + next + "\n" + lines;
            }

            if (route == "/health" || route == "/") return "ok\n";

            return "[404] " + method + " " + route + "\n";
        }

        // Capture the game framebuffer to a PNG (works unfocused) and return its path for the driver to Read.
        // ScreenCapture writes asynchronously over the next frame(s): trigger on the main thread, then wait
        // here (HTTP thread) for the file to appear and its size to settle.
        private string Screenshot()
        {
            string path = Path.Combine(Path.GetTempPath(), "wa_shot.png");
            OnMainThread(() =>
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
                UnityEngine.ScreenCapture.CaptureScreenshot(path);
                return "requested";
            });

            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < 8)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        long size = new FileInfo(path).Length;
                        if (size > 0)
                        {
                            Thread.Sleep(60); // let the write settle, then confirm the size is stable
                            if (new FileInfo(path).Length == size) return path + "\n";
                        }
                    }
                }
                catch { }
                Thread.Sleep(50);
            }
            return "[timeout] screenshot not written within 8s\n";
        }
    }
}
#endif
