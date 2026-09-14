using System;
using System.IO;
using System.Linq;
using System.Reflection;
using VRage.Plugins;

public sealed class FixtureGame
{
    public string Identity { get { return "owned-fake-game"; } }
}

internal static class Program
{
    private static void Journal(string message)
    {
        File.AppendAllText(Environment.GetEnvironmentVariable("XFE_FIXTURE_JOURNAL"), message + Environment.NewLine);
    }

    private static int Main(string[] args)
    {
        try
        {
            Journal("game:start");
            foreach (string argument in args) Journal("game:arg:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(argument)));
            Journal("game:bin64:" + VRage.FileSystem.MyFileSystem.ExePath);
            Journal("game:root:" + VRage.FileSystem.MyFileSystem.RootPath);
            MyPlugins.Load();
            IPlugin[] entries = MyPlugins.Plugins.ToArray();
            if (entries.Length != 1) throw new InvalidOperationException("Expected exactly one self-developed bridge registered with game API.");
            var plugin = entries[0];
            try
            {
                plugin.Init(new FixtureGame());
                Journal("game:initialized");
                for (int i = 0; i < 3; i++)
                {
                    var input = plugin as IHandleInputPlugin;
                    if (input != null) input.HandleInput();
                    plugin.Update(); Journal("game:frame:" + i);
                }
            }
            finally { MyPlugins.Unload(); }
            Journal("game:finished");
            int index = Array.FindIndex(args, value => value == "--fixture-exit");
            return index >= 0 ? int.Parse(args[index + 1]) : 0;
        }
        catch (Exception exception)
        {
            Journal("game:failure:" + exception.ToString());
            Console.Error.WriteLine(exception);
            return 11;
        }
    }
}
