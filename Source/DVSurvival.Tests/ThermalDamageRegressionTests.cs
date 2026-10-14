using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class ThermalDamageRegressionTests
    {
        [Theory]
        [InlineData(34.4f, -30f)]
        [InlineData(38.6f, 55f)]
        [InlineData(37f, 56f)]
        public void ThermalDamageUsesActiveSecondsRegardlessOfDayLength(float body, float air)
        {
            var shortDay = new SurvivalState { BodyTemperatureCelsius = body };
            var longDay = shortDay.Clone();
            var tuning = new SurvivalTuning { DisableNeedsConsumption = true, ThermalTimeConstantSeconds = 3600 };
            for (int second = 0; second < 60; second++)
            {
                SurvivalSimulator.Advance(shortDay, new SurvivalEnvironment
                    { GameHours = 24f / (144 * 60), AmbientTemperatureCelsius = air }, tuning, 1);
                SurvivalSimulator.Advance(longDay, new SurvivalEnvironment
                    { GameHours = 24f / (1440 * 60), AmbientTemperatureCelsius = air }, tuning, 1);
            }
            Assert.Equal(shortDay.Health, longDay.Health, 4);
            Assert.True(longDay.Health <= 99.25f, "A minute of dangerous temperature must cause visible damage.");
        }

        [Theory]
        [InlineData(34.4f, -30f)] [InlineData(39f, 55f)]
        public void FrozenCalendarStillAllowsBodyResponseAndThermalDamage(float body, float air)
        {
            var state = new SurvivalState { BodyTemperatureCelsius = body };
            SurvivalSimulator.Advance(state, new SurvivalEnvironment
                { GameHours = 0, AmbientTemperatureCelsius = air }, new SurvivalTuning(), 1);
            Assert.True(state.Health < 100);
            Assert.NotEqual(body, state.BodyTemperatureCelsius);
            Assert.Equal(100, state.Hunger); Assert.Equal(100, state.Hydration); Assert.Equal(100, state.Rest);
            Assert.Equal(0, state.LowRestGameHours);
        }

        [Theory]
        [InlineData(34.9f)] [InlineData(38.6f)]
        public void RecoveryAcrossThresholdCannotSkipTheLastUnsafeTick(float body)
        {
            var state = new SurvivalState { Health = 10, BodyTemperatureCelsius = body };
            SurvivalSimulator.Advance(state, new SurvivalEnvironment
                { GameHours = .00001f, AmbientTemperatureCelsius = 22, ThermalRecoveryMultiplier = 5 },
                new SurvivalTuning { ThermalTimeConstantSeconds = 10 }, 1);
            Assert.InRange(state.BodyTemperatureCelsius, 35, 38.5f);
            Assert.True(state.Health < 10);
        }

        [Theory]
        [InlineData(35f)] [InlineData(38.5f)]
        public void SafeBoundaryDoesNotCauseThermalDamage(float body)
        {
            var state = new SurvivalState { BodyTemperatureCelsius = body };
            SurvivalSimulator.Advance(state, new SurvivalEnvironment
                { GameHours = 0, AmbientTemperatureCelsius = 22 }, new SurvivalTuning(), 1);
            Assert.Equal(100, state.Health);
        }

        [Fact]
        public void NoElapsedActiveTimeDoesNotApplyTemperatureDamage()
        {
            var state = new SurvivalState { BodyTemperatureCelsius = 34.4f };
            SurvivalSimulator.Advance(state, new SurvivalEnvironment
                { GameHours = 8, AmbientTemperatureCelsius = -30 },
                new SurvivalTuning { DisableNeedsConsumption = true }, 0);
            Assert.Equal(100, state.Health);
        }

        [Fact]
        public void DamageMultiplierStillAppliesAndZeroDisablesDamage()
        {
            var normal = new SurvivalState { BodyTemperatureCelsius = 34.4f };
            var doubled = normal.Clone(); var disabled = normal.Clone();
            var world = new SurvivalEnvironment { GameHours = .00001f, AmbientTemperatureCelsius = -30 };
            SurvivalSimulator.Advance(normal, world, new SurvivalTuning(), 1);
            SurvivalSimulator.Advance(doubled, world, new SurvivalTuning { DamageMultiplier = 2 }, 1);
            SurvivalSimulator.Advance(disabled, world, new SurvivalTuning { DamageMultiplier = 0 }, 1);
            Assert.Equal((100 - normal.Health) * 2, 100 - doubled.Health, 4);
            Assert.Equal(100, disabled.Health);
        }

        [Theory]
        [InlineData(-30f)] [InlineData(70f)]
        public void HazardousThermalTicksRemainAllocationFree(float air)
        {
            var state = new SurvivalState(); var tuning = new SurvivalTuning { DisableNeedsConsumption = true };
            var world = new SurvivalEnvironment { GameHours = 0, AmbientTemperatureCelsius = air };
            for (int i = 0; i < 1000; i++) SurvivalSimulator.Advance(state, world, tuning, 1);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) SurvivalSimulator.Advance(state, world, tuning, 1);
            Assert.Equal(0, System.GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(state.IsValid());
        }
    }
}
