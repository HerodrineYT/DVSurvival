using System;
using DVSurvival.Core;
using DVSurvival.Tests.Reference;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class DifferentialSimulationTests
    {
        [Theory]
        [InlineData(174, false)]
        [InlineData(811, true)]
        [InlineData(53, false)]
        public void CalendarSimulationPreservesGameplayAcrossRandomEventSequences(int seed, bool disabled)
        {
            var random = new Random(seed);
            var fields = typeof(SurvivalState).GetFields();
            for (int scenario = 0; scenario < 500; scenario++)
            {
                var oldState = new SurvivalState
                {
                    Health = random.Next(1, 101),
                    Hunger = random.Next(101),
                    Hydration = random.Next(101),
                    Rest = random.Next(101),
                    BodyTemperatureCelsius = 32 + (float)random.NextDouble() * 10,
                    LowRestGameHours = random.Next(146),
                    CoffeeUsesSinceSleep = random.Next(26),
                    Meals = 20,
                    Water = 20,
                    Coffee = 20,
                    FirstAid = 20,
                    HeatPacks = 20
                };
                var newState = oldState.Clone();
                var tuning = new SurvivalTuning
                {
                    DisableNeedsConsumption = disabled,
                    DamageMultiplier = .7f,
                    NeedsRateMultiplier = .9f,
                    ThermalTimeConstantSeconds = 120
                };
                for (int step = 0; step < 30; step++)
                {
                    var world = new SurvivalEnvironment
                    {
                        GameHours = (float)random.NextDouble() * 8,
                        AmbientTemperatureCelsius = random.Next(-30, 91),
                        IsWinter = true,
                        Activity = (float)random.NextDouble(),
                        RainIntensity = .2f,
                        WindSpeedMetersPerSecond = 2
                    };
                    int action = random.Next(4);
                    if (action == 0)
                    {
                        BaselineSimulator.Advance(oldState, world, tuning);
                        SurvivalSimulator.Advance(newState, world, tuning);
                    }
                    else if (action == 1)
                    {
                        float hours = random.Next(1, 13);
                        Assert.Equal(BaselineSimulator.Sleep(oldState, hours, world, tuning), SurvivalSimulator.Sleep(newState, hours, world, tuning));
                    }
                    else if (action == 2)
                    {
                        var item = (ProvisionKind)random.Next(1, 6);
                        Assert.Equal(BaselineSimulator.Consume(oldState, item, tuning), SurvivalSimulator.Consume(newState, item, tuning));
                    }
                    else
                    {
                        var trauma = (TraumaKind)random.Next(1, 4); float amount = random.Next(21);
                        Assert.Equal(BaselineSimulator.ApplyTrauma(oldState, trauma, amount, 1.8f, tuning),
                            SurvivalSimulator.ApplyTrauma(newState, trauma, amount, 1.8f, tuning));
                    }
                    foreach (var field in fields)
                    {
                        if (field.IsStatic) continue;
                        var expected = field.GetValue(oldState); var actual = field.GetValue(newState);
                        if (expected is float f) Assert.True(Math.Abs(f - (float)actual) < .0001f,
                            field.Name + " diverged in scenario " + scenario + ": " + expected + " vs " + actual);
                        else Assert.Equal(expected, actual);
                    }
                }
            }
        }
    }
}
