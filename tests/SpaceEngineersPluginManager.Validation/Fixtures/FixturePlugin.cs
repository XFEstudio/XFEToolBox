using System;
using System.IO;
using VRage.Plugins;

namespace FixturePlugins
{
    public sealed class Plugin : IHandleInputPlugin
    {
#if FAIL_INIT
        private const string Name = "fails-init";
#elif FAIL_UPDATE
        private const string Name = "fails-update";
#elif FAIL_DISPOSE
        private const string Name = "fails-dispose";
#elif FAIL_INPUT
        private const string Name = "fails-input";
#elif DISABLED
        private const string Name = "disabled";
#elif HEALTHY_SECOND
        private const string Name = "healthy-second";
#else
        private const string Name = "healthy-first";
#endif
        private static void Journal(string operation)
        {
            File.AppendAllText(Environment.GetEnvironmentVariable("XFE_FIXTURE_JOURNAL"), Name + ":" + operation + Environment.NewLine);
        }
        public void Init(object gameInstance)
        {
            Journal("init:" + gameInstance.GetType().Name);
            Journal(FixtureDependency.Probe.Value);
#if FAIL_INIT
            throw new InvalidOperationException("fixture init failure");
#endif
        }
        public void Update()
        {
            Journal("update");
#if FAIL_UPDATE
            throw new InvalidOperationException("fixture update failure");
#endif
        }
        public void HandleInput()
        {
            Journal("input");
#if FAIL_INPUT
            throw new InvalidOperationException("fixture input failure");
#endif
        }
        public void Dispose()
        {
            Journal("dispose");
#if FAIL_DISPOSE
            throw new InvalidOperationException("fixture dispose failure");
#endif
        }
    }
}
