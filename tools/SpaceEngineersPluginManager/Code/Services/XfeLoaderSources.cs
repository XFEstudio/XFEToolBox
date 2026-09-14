namespace XFEToolBox.Tools.SpaceEngineers;

// These C# 5 programs are compiled locally against the user's own installed game.
// No game assemblies or third-party loader are distributed with this tool.
internal static class XfeLoaderSources
{
    public const string Bootstrap = """
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class XfeBootstrap
{
    private static string gameBin;
    private static string logFile;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string directory);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string bridge = null, profile = null;
            var gameArguments = new List<string>();
            bool passthrough = false;
            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                if (passthrough) { gameArguments.Add(argument); continue; }
                if (argument == "--") { passthrough = true; continue; }
                if (index + 1 >= args.Length) throw new ArgumentException("Missing value for " + argument);
                string value = args[++index];
                switch (argument)
                {
                    case "--game-bin64": gameBin = Path.GetFullPath(value); break;
                    case "--bridge": bridge = Path.GetFullPath(value); break;
                    case "--profile": profile = Path.GetFullPath(value); break;
                    case "--log": logFile = Path.GetFullPath(value); break;
                    default: throw new ArgumentException("Unknown launcher argument: " + argument);
                }
            }
            if (String.IsNullOrEmpty(gameBin) || String.IsNullOrEmpty(bridge) || String.IsNullOrEmpty(profile) || String.IsNullOrEmpty(logFile))
                throw new ArgumentException("Game directory, bridge, profile and log are required.");
            foreach (string argument in gameArguments)
                if (String.Equals(argument, "-plugin", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("XFE owns plugin registration; remove the -plugin game argument.");
            string gameExecutable = Path.Combine(gameBin, "SpaceEngineers.exe");
            if (!File.Exists(gameExecutable) || !File.Exists(bridge) || !File.Exists(profile))
                throw new FileNotFoundException("The game, XFE bridge or profile file is missing.");
            if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("Space Engineers requires a 64-bit process.");

            Directory.CreateDirectory(Path.GetDirectoryName(logFile));
            Environment.SetEnvironmentVariable("XFE_SE_PROFILE", profile, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("XFE_SE_LOG", logFile, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("SteamAppId", "244850", EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("SteamGameId", "244850", EnvironmentVariableTarget.Process);
            Environment.CurrentDirectory = gameBin;
            if (!SetDllDirectory(gameBin)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            AppDomain.CurrentDomain.AssemblyResolve += ResolveGameAssembly;

            // The engine normally derives these from its entry EXE, which is now our bootstrap.
            Assembly library = Assembly.LoadFrom(Path.Combine(gameBin, "VRage.Library.dll"));
            Type fileSystem = library.GetType("VRage.FileSystem.MyFileSystem", true);
            SetField(fileSystem, "ExePath", gameBin);
            SetField(fileSystem, "RootPath", Directory.GetParent(gameBin).FullName);

            Assembly vrage = Assembly.LoadFrom(Path.Combine(gameBin, "VRage.dll"));
            Type plugins = vrage.GetType("VRage.Plugins.MyPlugins", true);
            MethodInfo register = plugins.GetMethod("RegisterUserAssemblyFiles", BindingFlags.Public | BindingFlags.Static,
                null, new Type[] { typeof(List<string>) }, null);
            if (register == null) throw new MissingMethodException("This game version does not expose MyPlugins.RegisterUserAssemblyFiles.");
            register.Invoke(null, new object[] { new List<string> { bridge } });
            Log("XFE bridge registered through the engine's public plugin API.");

            Assembly game = Assembly.LoadFrom(gameExecutable);
            MethodInfo entry = game.EntryPoint;
            if (entry == null || entry.GetParameters().Length != 1 || entry.GetParameters()[0].ParameterType != typeof(string[]))
                throw new MissingMethodException("Unsupported Space Engineers entry point.");
            Log("Starting Space Engineers " + game.GetName().Version);
            object result = entry.Invoke(null, new object[] { gameArguments.ToArray() });
            Log("Space Engineers exited normally.");
            return result is int ? (int)result : 0;
        }
        catch (Exception error)
        {
            var invocation = error as TargetInvocationException;
            if (invocation != null && invocation.InnerException != null) error = invocation.InnerException;
            Log("BOOTSTRAP ERROR: " + error);
            return 1;
        }
    }

    private static void SetField(Type type, string name, string value)
    {
        FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
        if (field == null || field.FieldType != typeof(string)) throw new MissingFieldException(type.FullName, name);
        field.SetValue(null, value);
    }

    private static Assembly ResolveGameAssembly(object sender, ResolveEventArgs args)
    {
        var requested = new AssemblyName(args.Name);
        string name = requested.Name;
        if (String.IsNullOrEmpty(name) || name != Path.GetFileName(name)) return null;
        string candidate = Path.Combine(gameBin, name + ".dll");
        if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
        candidate = Path.Combine(gameBin, name + ".exe");
        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }

    private static void Log(string message)
    {
        try
        {
            if (!String.IsNullOrEmpty(logFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFile));
                File.AppendAllText(logFile, DateTime.UtcNow.ToString("o") + " [bootstrap] " + message + Environment.NewLine);
            }
        }
        catch { }
    }
}
""";

    public const string Bridge = """
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using VRage.Plugins;

[DataContract]
internal sealed class XfeProfile
{
    [DataMember(Name = "formatVersion")] public int FormatVersion { get; set; }
    [DataMember(Name = "plugins")] public XfePluginEntry[] Plugins { get; set; }
}

[DataContract]
internal sealed class XfePluginEntry
{
    [DataMember(Name = "id")] public string Id { get; set; }
    [DataMember(Name = "name")] public string Name { get; set; }
    [DataMember(Name = "assemblyPath")] public string AssemblyPath { get; set; }
    [DataMember(Name = "enabled")] public bool Enabled { get; set; }
}

public sealed class XfePluginBridge : IHandleInputPlugin
{
    private sealed class State
    {
        public string Label;
        public IPlugin Plugin;
        public bool Failed;
        public bool Disposed;
    }
    private readonly List<State> loaded = new List<State>();
    private readonly Dictionary<Assembly, string> owners = new Dictionary<Assembly, string>();
    private readonly object resolverGate = new object();
    private readonly object logGate = new object();
    private string logFile;
    private bool initialized;
    private bool disposed;

    public void Init(object gameInstance)
    {
        if (initialized || disposed) return;
        initialized = true;
        logFile = Environment.GetEnvironmentVariable("XFE_SE_LOG");
        AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginAssembly;
        try
        {
            string profileFile = Environment.GetEnvironmentVariable("XFE_SE_PROFILE");
            if (String.IsNullOrEmpty(profileFile)) throw new InvalidDataException("XFE profile was not specified.");
            XfeProfile profile;
            using (var stream = new FileStream(profileFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException("XFE profile is too large.");
                profile = (XfeProfile)new DataContractJsonSerializer(typeof(XfeProfile)).ReadObject(stream);
            }
            if (profile == null || profile.FormatVersion != 1 || profile.Plugins == null)
                throw new InvalidDataException("Unsupported XFE profile format.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XfePluginEntry plugin in profile.Plugins)
            {
                if (plugin == null || !plugin.Enabled) continue;
                string label = plugin.Name ?? plugin.Id ?? plugin.AssemblyPath ?? "Unknown plugin";
                try
                {
                    if (String.IsNullOrWhiteSpace(plugin.AssemblyPath) || !Path.IsPathRooted(plugin.AssemblyPath))
                        throw new InvalidDataException("The plugin entry DLL must use an absolute path.");
                    string file = Path.GetFullPath(plugin.AssemblyPath);
                    if (!File.Exists(file)) throw new FileNotFoundException("Plugin entry DLL was not found.", file);
                    if (!paths.Add(file)) { Log("SKIP duplicate DLL: " + label); continue; }
                    if (String.Equals(file, typeof(XfePluginBridge).Assembly.Location, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The bridge cannot load itself as a plugin.");
                    Load(file, label, gameInstance);
                }
                catch (Exception error) { Log("LOAD ERROR " + label + ": " + Describe(error)); }
            }
            Log("Ready: " + loaded.Count(state => !state.Failed) + " plugin instance(s).");
        }
        catch (Exception error) { Log("PROFILE ERROR: " + Describe(error)); }
    }

    private void Load(string file, string label, object gameInstance)
    {
        Assembly assembly = Assembly.LoadFrom(file);
        lock (resolverGate) owners[assembly] = Path.GetDirectoryName(file);
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException error)
        {
            types = error.Types;
            foreach (Exception detail in error.LoaderExceptions) if (detail != null) Log("TYPE ERROR " + label + ": " + Describe(detail));
        }
        int found = 0;
        foreach (Type type in types)
        {
            if (type == null || !type.IsClass || type.IsAbstract || type.ContainsGenericParameters ||
                !typeof(IPlugin).IsAssignableFrom(type) || type == typeof(XfePluginBridge)) continue;
            found++;
            State state = null;
            try
            {
                var instance = (IPlugin)Activator.CreateInstance(type);
                state = new State { Label = label + " / " + type.FullName, Plugin = instance };
                loaded.Add(state);
                instance.Init(gameInstance);
                Log("INIT " + state.Label);
            }
            catch (Exception error)
            {
                if (state == null) Log("CONSTRUCTOR ERROR " + label + " / " + type.FullName + ": " + Describe(error));
                else Fail(state, "INIT", error);
            }
        }
        if (found == 0) Log("No IPlugin implementations: " + label);
    }

    public void Update()
    {
        if (disposed) return;
        foreach (State state in loaded)
        {
            if (state.Failed) continue;
            try { state.Plugin.Update(); }
            catch (Exception error) { Fail(state, "UPDATE", error); }
        }
    }

    public void HandleInput()
    {
        if (disposed) return;
        foreach (State state in loaded)
        {
            if (state.Failed) continue;
            var input = state.Plugin as IHandleInputPlugin;
            if (input == null) continue;
            try { input.HandleInput(); }
            catch (Exception error) { Fail(state, "INPUT", error); }
        }
    }

    private void Fail(State state, string stage, Exception error)
    {
        state.Failed = true;
        Log(stage + " ERROR " + state.Label + ": " + Describe(error));
        DisposePlugin(state);
    }

    private void DisposePlugin(State state)
    {
        if (state.Disposed) return;
        state.Disposed = true;
        try { state.Plugin.Dispose(); Log("DISPOSE " + state.Label); }
        catch (Exception error) { Log("DISPOSE ERROR " + state.Label + ": " + Describe(error)); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        for (int index = loaded.Count - 1; index >= 0; index--) DisposePlugin(loaded[index]);
        loaded.Clear();
        AppDomain.CurrentDomain.AssemblyResolve -= ResolvePluginAssembly;
        lock (resolverGate) owners.Clear();
        Log("Bridge disposed.");
    }

    private Assembly ResolvePluginAssembly(object sender, ResolveEventArgs args)
    {
        if (args.RequestingAssembly == null) return null;
        lock (resolverGate)
        {
            string root;
            if (!owners.TryGetValue(args.RequestingAssembly, out root)) return null;
            string name = new AssemblyName(args.Name).Name;
            if (String.IsNullOrEmpty(name) || name != Path.GetFileName(name)) return null;
            string candidate = Path.Combine(root, name + ".dll");
            if (!File.Exists(candidate)) return null;
            Assembly dependency = Assembly.LoadFrom(candidate);
            owners[dependency] = root;
            return dependency;
        }
    }

    private static string Describe(Exception error)
    {
        var invocation = error as TargetInvocationException;
        return (invocation != null && invocation.InnerException != null ? invocation.InnerException : error).ToString();
    }

    private void Log(string message)
    {
        try
        {
            if (!String.IsNullOrEmpty(logFile))
                lock (logGate) File.AppendAllText(logFile, DateTime.UtcNow.ToString("o") + " [bridge] " + message + Environment.NewLine);
        }
        catch { }
    }
}
""";
}
