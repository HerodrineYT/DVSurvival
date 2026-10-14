using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public class ClimateAndConsumableTests
    {
        [Theory]
        [InlineData(36.9f, 37f)]
        [InlineData(35f, 35.2f)]
        [InlineData(38f, 38f)]
        public void CoffeeWarmsWithoutCrossing37OrCoolingHotPlayer(float before, float after)
        {
            var state = SurvivalState.CreateDefault(new SurvivalTuning());
            state.BodyTemperatureCelsius = before;
            Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.Consume(state, ProvisionKind.Coffee, new SurvivalTuning()));
            Assert.Equal(after, state.BodyTemperatureCelsius, 3);
        }

        [Theory]
        [InlineData(36.1f, 36f)]
        [InlineData(39f, 38.8f)]
        [InlineData(35f, 35f)]
        public void WaterCoolsWithoutCrossing36OrWarmingColdPlayer(float before, float after)
        {
            var state = SurvivalState.CreateDefault(new SurvivalTuning());
            state.Hydration = 10f;
            state.BodyTemperatureCelsius = before;
            Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.Consume(state, ProvisionKind.Water, new SurvivalTuning()));
            Assert.Equal(after, state.BodyTemperatureCelsius, 3);
            Assert.Equal(60f, state.Hydration);
        }

        [Theory]
        [InlineData(33f)]
        [InlineData(36f)]
        [InlineData(39f)]
        public void HeatPackSetsFixedBodyTemperature(float before)
        {
            var state = SurvivalState.CreateDefault(new SurvivalTuning());
            state.BodyTemperatureCelsius = before;
            SurvivalSimulator.Consume(state, ProvisionKind.HeatPack, new SurvivalTuning());
            Assert.Equal(37f, state.BodyTemperatureCelsius);
        }

        [Fact]
        public void SmallOverheatCausesDamageAndMultiplierZeroDisablesIt()
        {
            var state = SurvivalState.CreateDefault(new SurvivalTuning());
            state.BodyTemperatureCelsius = 38.6f;
            var protectedState = state.Clone();
            var environment = new SurvivalEnvironment { GameHours = 0.001f, AmbientTemperatureCelsius = 37f };
            SurvivalSimulator.Advance(state, environment, new SurvivalTuning());
            SurvivalSimulator.Advance(protectedState, environment, new SurvivalTuning { DamageMultiplier = 0f });
            Assert.InRange(state.Health, 99.99f, 99.99999f);
            Assert.Equal(100f, protectedState.Health);
        }

        [Theory]
        [InlineData(39.99f, false)]
        [InlineData(40f, true)]
        [InlineData(100f, true)]
        public void SprintThresholdIsStrict(float health, bool expected)
        {
            Assert.Equal(expected, SurvivalSimulator.CanRun(new SurvivalState { Health = health }));
        }

        [Fact]
        public void CoolingToExactly38Point5DoesNotDamage()
        {
            var state = SurvivalState.CreateDefault(new SurvivalTuning());
            state.BodyTemperatureCelsius = 38.6f;
            state.Health = 10f;
            state.Hunger = state.Hydration = state.Rest = 100f;
            SurvivalSimulator.Advance(state, new SurvivalEnvironment
            {
                GameHours = 1f, AmbientTemperatureCelsius = 38.5f, Exposure = 1f
            }, new SurvivalTuning { PassiveHealthRecoveryPerHour = 0f }, 60f);
            Assert.True(state.BodyTemperatureCelsius <= 38.6f);
            Assert.Equal(8.8f, state.Health, 3);
            Assert.Equal(0u, state.CollapseCount);
        }

        [Fact]
        public void CoolingTickCannotSkipDamageAtVisible38Point5()
        {
            var state = SurvivalState.CreateDefault(new SurvivalTuning());
            state.BodyTemperatureCelsius = 38.6f;
            state.Health = 10f;
            SurvivalSimulator.Advance(state, new SurvivalEnvironment
            {
                GameHours = 0.01f, AmbientTemperatureCelsius = 22f, Exposure = 0f
            }, new SurvivalTuning { PassiveHealthRecoveryPerHour = 0f }, 1f);
            Assert.True(state.BodyTemperatureCelsius >= 38.5f);
            Assert.True(state.Health < 10f);
            Assert.Equal(0u, state.CollapseCount);
        }

        [Theory]
        [InlineData(34.99f, 8.94f)]
        [InlineData(35f, 10f)]
        public void ColdDamageStartsStrictlyBelow35(float temperature, float expectedHealth)
        {
            var state = SurvivalState.CreateDefault(new SurvivalTuning());
            state.BodyTemperatureCelsius = temperature;
            state.Health = 10f;
            SurvivalSimulator.Advance(state, new SurvivalEnvironment
            {
                GameHours = 1f, AmbientTemperatureCelsius = temperature, Exposure = 1f
            }, new SurvivalTuning { PassiveHealthRecoveryPerHour = 0f }, 60f);
            Assert.Equal(expectedHealth, state.Health, 3);
            Assert.Equal(0u, state.CollapseCount);
        }

        [Theory]
        [InlineData(34.5f)]
        [InlineData(38.5f)]
        public void DamageMultiplierZeroDisablesThresholdTemperatureDamage(float temperature)
        {
            var state = SurvivalState.CreateDefault(new SurvivalTuning());
            state.BodyTemperatureCelsius = temperature;
            state.Health = 10f;
            SurvivalSimulator.Advance(state, new SurvivalEnvironment
            {
                GameHours = 1f, AmbientTemperatureCelsius = temperature, Exposure = 1f
            }, new SurvivalTuning { DamageMultiplier = 0f, PassiveHealthRecoveryPerHour = 0f }, 0f);
            Assert.Equal(10f, state.Health);
        }

        [Fact]
        public void DifferentCabinsDoNotShareHeatAndOpenSteamCabCools()
        {
            var hot = new CabinClimate();
            var cold = new CabinClimate();
            for (int i = 0; i < 700; i++) hot.Advance(0.5f, -5f, false, 0f, false, false, 90f);
            Assert.InRange(hot.AirTemperature, 89.9f, 90.1f);
            Assert.Equal(-5f, cold.Advance(0f, -5f, false, 0f, false, false));
            for (int i = 0; i < 300; i++) hot.Advance(0.5f, -5f, false, 0f, true, false, 90f);
            Assert.InRange(hot.AirTemperature, -5f, -4.99f);
        }

        [Fact]
        public void HotAirIsNotClampedTo60()
        {
            var environment = new SurvivalEnvironment { AmbientTemperatureCelsius = 120f };
            Assert.True(environment.IsValid());
            environment.Clamp();
            Assert.Equal(120f, environment.AmbientTemperatureCelsius);
            environment.AmbientTemperatureCelsius = float.NaN;
            Assert.False(environment.IsValid());
            environment.Clamp();
            Assert.True(environment.IsValid());
        }

        [Fact]
        public void CabinWarmsGraduallyRespondsToRpmAndVentilatesToOutside()
        {
            var cabin = new CabinClimate();
            Assert.Equal(-10f, cabin.Advance(0f, -10f, true, 0f, false, false));
            var first = cabin.Advance(0.5f, -10f, true, 0f, false, false);
            Assert.InRange(first, -10f, -9f);
            for (int i = 0; i < 1500; i++) cabin.Advance(0.5f, -10f, true, 0f, false, false);
            Assert.InRange(cabin.AirTemperature, 21.9f, 22.1f);
            var change = cabin.Advance(0.5f, -10f, true, 1f, false, false);
            Assert.InRange(change, 21.9f, 22.1f);
            for (int i = 0; i < 600; i++) cabin.Advance(0.5f, -10f, true, 1f, false, false);
            Assert.InRange(cabin.AirTemperature, 29.9f, 30.1f);
            for (int i = 0; i < 300; i++) cabin.Advance(0.5f, -10f, true, 1f, true, false);
            Assert.InRange(cabin.AirTemperature, -10.01f, -9.99f);
        }

        [Fact]
        public void FanCoolsAndShutdownDoesNotInstantlyDiscardEngineHeat()
        {
            var fan = new CabinClimate();
            var normal = new CabinClimate();
            for (int i = 0; i < 1500; i++)
            {
                fan.Advance(0.5f, 0f, true, 1f, false, true);
                normal.Advance(0.5f, 0f, true, 1f, false, false);
            }
            Assert.InRange(normal.AirTemperature - fan.AirTemperature, 2.1f, 2.3f);
            Assert.True(normal.Advance(0.5f, 0f, false, 0f, false, false) > 29f);
            for (int i = 0; i < 2000; i++) normal.Advance(0.5f, 0f, false, 0f, false, false);
            Assert.InRange(normal.AirTemperature, 0f, 0.01f);
        }

        [Fact]
        public void Dm1uNaturalConvectionIsQuarterSpeedAndFanRestoresNormalHeatingRate()
        {
            var natural = new CabinClimate();
            var blown = new CabinClimate();
            natural.Advance(0f, -10f, true, 0f, false, false, null, .25f);
            blown.Advance(0f, -10f, true, 0f, false, true, null, 1f);
            for (var i = 0; i < 120; i++)
            {
                natural.Advance(.5f, -10f, true, 0f, false, false, null, .25f);
                blown.Advance(.5f, -10f, true, 0f, false, true, null, 1f);
            }
            Assert.True(blown.AirTemperature > natural.AirTemperature + 5f);
        }

        [Fact]
        public void DisabledDm1uHeaterCannotWarmCabAndOpeningsCoolAtFullSpeed()
        {
            var cabin = new CabinClimate();
            for (var i = 0; i < 300; i++)
                cabin.Advance(.5f, -10f, true, 1f, false, false, null, .25f, false);
            Assert.InRange(cabin.AirTemperature, -10.01f, -9.99f);
            for (var i = 0; i < 600; i++)
                cabin.Advance(.5f, -10f, true, 1f, false, false, null, .25f, true);
            var warm = cabin.AirTemperature;
            for (var i = 0; i < 120; i++)
                cabin.Advance(.5f, -10f, true, 1f, true, false, null, .25f, true);
            Assert.True(warm > 0f);
            Assert.InRange(cabin.AirTemperature, -10.01f, -9.5f);

            var unpowered = new CabinClimate();
            for (var i = 0; i < 300; i++)
                unpowered.Advance(.5f, -10f, false, 1f, false, false, null, .25f, true);
            Assert.InRange(unpowered.AirTemperature, -10.01f, -9.99f);
        }

        [Fact]
        public void RunningEngineCannotSustainPreviouslyWarmedCabWhenDm1uHeaterIsOff()
        {
            var cabin = new CabinClimate();
            for (var i = 0; i < 900; i++)
                cabin.Advance(.5f, -10f, true, 1f, false, true, null, 1f, true);
            var previous = cabin.AirTemperature;
            Assert.True(previous > 20f);

            for (var i = 0; i < 300; i++)
            {
                var next = cabin.Advance(.5f, -10f, true, 1f, false, false, null, .25f, false);
                Assert.True(next <= previous);
                previous = next;
            }
            Assert.InRange(cabin.AirTemperature, -10.1f, -9.5f);
        }

        [Fact]
        public void DisabledDm1uHeaterAllowsOnlyNaturalExchangeWithWarmerOutdoors()
        {
            var cabin = new CabinClimate();
            cabin.Advance(0f, -20f, true, 1f, false, false, null, .25f, false);
            var previous = cabin.AirTemperature;
            for (var i = 0; i < 300; i++)
            {
                var next = cabin.Advance(.5f, 5f, true, 1f, false, false, null, .25f, false);
                Assert.InRange(next, previous, 5f);
                previous = next;
            }
            Assert.InRange(cabin.AirTemperature, 4.8f, 5f);
        }
    }
}
