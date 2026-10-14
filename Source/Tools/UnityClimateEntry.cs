using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;

// Copied into the generated, isolated editor project by verify_climate.ps1.
public static class UnityClimateEntry
{
    public static void Run()
    {
        var root = Environment.GetEnvironmentVariable("DVSURVIVAL_VERIFY_ROOT");
        var mod = Path.Combine(root, "artifacts/build/DVSurvival");
        var managed = Path.Combine(Environment.GetEnvironmentVariable("DVSURVIVAL_VERIFY_GAME"), "DerailValley_Data/Managed");
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            foreach (var directory in new[] { mod, managed, Path.Combine(managed, "UnityModManager"),
                Path.Combine(root, "artifacts/dependencies/LanguageHelper-1.2.1/DVLangHelper"),
                Path.Combine(root, "artifacts/dependencies/CustomItemMod-0.2.0") })
            {
                var path = Path.Combine(directory, new AssemblyName(args.Name).Name + ".dll");
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        int code = 0;
        try
        {
            Assembly.LoadFrom(Path.Combine(root, "artifacts/verification/VerifyStationOffice.dll"))
                .GetType("VerifyStationOffice").GetMethod("Run").Invoke(null, new object[] { mod });
        }
        catch (Exception exception) { Debug.LogException(exception); code = 1; }
        EditorApplication.Exit(code);
    }
}
