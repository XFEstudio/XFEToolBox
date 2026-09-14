using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace VRage.Plugins
{
    // Contract-only stand-in compiled into the same assembly identity as the installed game API.
    // No Keen game implementation or third-party loader code is included in the fixture.
    public interface IPlugin : IDisposable
    {
        void Init(object gameInstance);
        void Update();
    }

    public interface IHandleInputPlugin : IPlugin
    {
        void HandleInput();
    }

    public static class MyPlugins
    {
        private static readonly List<Assembly> Registered = new List<Assembly>();
        private static readonly List<IPlugin> Loaded = new List<IPlugin>();
        public static IEnumerable<IPlugin> Plugins { get { return Loaded; } }
        public static void RegisterUserAssemblyFiles(List<string> paths)
        {
            foreach (string path in paths) Registered.Add(Assembly.LoadFrom(path));
        }
        public static void Load()
        {
            foreach (Assembly assembly in Registered)
                foreach (Type type in assembly.GetTypes().Where(type => typeof(IPlugin).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface))
                    Loaded.Add((IPlugin)Activator.CreateInstance(type));
        }
        public static void Unload()
        {
            foreach (IPlugin plugin in Loaded) plugin.Dispose();
            Loaded.Clear();
        }
    }
}
