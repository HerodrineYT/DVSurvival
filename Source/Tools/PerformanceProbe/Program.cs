using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        var bytes = File.ReadAllBytes(args[0]);
        var assembly = Assembly.Load(bytes);
        var stateType = assembly.GetType("DVSurvival.Core.SurvivalState", true);
        var envType = assembly.GetType("DVSurvival.Core.SurvivalEnvironment", true);
        var tuningType = assembly.GetType("DVSurvival.Core.SurvivalTuning", true);
        var state = Activator.CreateInstance(stateType);
        var env = Activator.CreateInstance(envType);
        var tuning = Activator.CreateInstance(tuningType);
        envType.GetField("GameHours").SetValue(env, .00001f);
        var method = assembly.GetType("DVSurvival.Core.SurvivalSimulator", true).GetMethod("Advance");
        var invoke = Expression.Lambda<Action>(Expression.Call(method, Expression.Constant(state, stateType),
            Expression.Constant(env, envType), Expression.Constant(tuning, tuningType), Expression.Constant(1f))).Compile();
        for (int i = 0; i < 1000; i++) invoke();
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) invoke();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        var reconcilerType = assembly.GetType("DVSurvival.Core.SleepCalendarReconciler", true);
        var reconciler = Activator.CreateInstance(reconcilerType, new object[] { 10d });
        var drain = Expression.Lambda<Action>(Expression.Block(
            Expression.Call(Expression.Constant(reconciler, reconcilerType),
                reconcilerType.GetMethod("DrainOperations"), Expression.Constant(100d)),
            Expression.Empty())).Compile();
        long idleCalendarBytes = Measure(drain);
        envType.GetField("RainIntensity").SetValue(env, float.NaN);
        long invalidEnvironmentBytes = Measure(invoke);
        Console.WriteLine(JsonSerializer.Serialize(new {
            dll = Path.GetFullPath(args[0]), sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            mvid = assembly.ManifestModule.ModuleVersionId, ticks = 10000,
            bytesAllocated = allocated, bytesPerTick = allocated / 10000d,
            idleCalendarBytes, idleCalendarBytesPerCall = idleCalendarBytes / 10000d,
            invalidEnvironmentBytes, invalidEnvironmentBytesPerTick = invalidEnvironmentBytes / 10000d,
            finalState = stateType.GetFields().Where(f => !f.IsStatic).ToDictionary(f => f.Name, f => f.GetValue(state))
        }));
        return 0;
    }

    private static long Measure(Action action)
    {
        for (int i = 0; i < 1000; i++) action();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
