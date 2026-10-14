using System;
using System.IO;
using DVSurvival.Core;
using DVSurvival.Mod;
using DVSurvival.Multiplayer;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class PerformanceRegressionTests
    {
        [Theory]
        [InlineData(1)] [InlineData(8)] [InlineData(256)]
        public void PlayerSnapshotsReuseStorageWithoutAllocationsAfterWarmup(int count)
        {
            var cache = new ReusablePlayerSnapshots();
            Action refresh = () => {
                cache.Begin();
                for (int i = 0; i < count; i++)
                {
                    var p = cache.Get((byte)i); p.PlayerId = (byte)i; p.PositionX = i;
                    cache.Add((byte)i); cache.Add((byte)i);
                }
            };
            refresh(); var first = cache.View[0]; var view = cache.View;
            for (int i = 0; i < 100; i++) refresh();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) refresh();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, bytes); Assert.Equal(count, cache.View.Count);
            Assert.Same(view, cache.View); Assert.Same(first, cache.View[0]);
            cache.Reset(); refresh(); Assert.NotSame(first, cache.View[0]);
        }

        [Theory]
        [InlineData("Cab_Fan-Control", "cabfan", false, true)]
        [InlineData("c_heating-rotary ", "cheatingrotary", true, true)]
        [InlineData("_C HeatingRotary(Clone)", "cheatingrotary", true, false)]
        [InlineData("shop decoration", "shop", false, true)]
        [InlineData("xshop", "shop", true, false)]
        [InlineData("shopx", "shop", true, false)]
        [InlineData("cabcaB_Fan", "cabfan", false, true)]
        [InlineData("---", "shop", false, false)]
        [InlineData(null, "shop", false, false)]
        public void AllocationFreeNameMatchingPreservesNormalization(string value, string token, bool exact, bool expected)
        { Assert.Equal(expected, NormalizedName.Contains(value, token, exact)); }

        [Fact]
        public void CopyEnvironmentCopiesEveryFieldWithoutSharingTheObject()
        {
            var source = new SurvivalEnvironment { GameHours = 3, AmbientTemperatureCelsius = -20,
                RainIntensity = .3f, WindSpeedMetersPerSecond = 7, Exposure = .2f, Activity = .9f,
                ShelterWarmthCelsius = 8, ThermalRecoveryMultiplier = 3, IsWinter = true };
            var copy = new SurvivalEnvironment(); source.CopyTo(copy);
            foreach (var field in typeof(SurvivalEnvironment).GetFields())
                if (!field.IsStatic) Assert.Equal(field.GetValue(source), field.GetValue(copy));
            copy.GameHours = 1; Assert.Equal(3, source.GameHours);
        }

        [Theory]
        [InlineData(-200f)] [InlineData(22f)] [InlineData(75f)]
        public void SimulationDoesNotMutateBorrowedEnvironment(float temperature)
        {
            var env = new SurvivalEnvironment { GameHours = .02f, AmbientTemperatureCelsius = temperature };
            var original = env.Clone(); var state = new SurvivalState(); var tuning = new SurvivalTuning();
            SurvivalSimulator.Advance(state, env, tuning, 1);
            SurvivalSimulator.Sleep(state, 1, env, tuning);
            foreach (var field in typeof(SurvivalEnvironment).GetFields())
                if (!field.IsStatic) Assert.Equal(field.GetValue(original), field.GetValue(env));
        }

        [Fact]
        public void ValidSimulationTicksAllocateNoThermalResultsOrEnvironmentCopies()
        {
            var env = new SurvivalEnvironment { GameHours = .0001f };
            var tuning = new SurvivalTuning(); var state = new SurvivalState();
            for (int i = 0; i < 100; i++) SurvivalSimulator.Advance(state, env, tuning, 1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) SurvivalSimulator.Advance(state, env, tuning, 1);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        private static SurvivalStateMessage Message(string session = "a", int price = 100) =>
            new SurvivalStateMessage { SessionId = session, MealPrice = price, WaterPrice = 40,
                CoffeePrice = 70, FirstAidPrice = 300, HeatPackPrice = 120, DisableNeedsConsumption = true };

        [Fact]
        public void CompactStateWaitsForMatchingConfigurationEvenWithReorderedDelivery()
        {
            var sender = new StateConfigurationCache(); var receiver = new StateConfigurationCache();
            var full = Message(); Assert.True(sender.Prepare(full, false));
            var compact = Message(); Assert.False(sender.Prepare(compact, false));
            Assert.False(compact.HasConfiguration); Assert.False(receiver.Apply(compact));
            Assert.True(receiver.Apply(full));
            compact.MealPrice = 0; compact.DisableNeedsConsumption = false;
            Assert.True(receiver.Apply(compact)); Assert.Equal(100, compact.MealPrice);
            Assert.True(compact.DisableNeedsConsumption);
            var changed = Message(price: 120); Assert.True(sender.Prepare(changed, false));
            Assert.True(receiver.Apply(changed)); Assert.True(receiver.Apply(full));
            Assert.False(receiver.Apply(compact)); // Old telemetry cannot borrow new config.
            var currentCompact = Message(price: 120); sender.Prepare(currentCompact, false);
            currentCompact.MealPrice = 0; Assert.True(receiver.Apply(currentCompact));
            Assert.Equal(120, currentCompact.MealPrice);
            Assert.Equal(2u, changed.ConfigurationRevision);
            var reliable = Message(price: 120); Assert.True(sender.Prepare(reliable, true));
            Assert.True(reliable.HasConfiguration); Assert.Equal(2u, reliable.ConfigurationRevision);
        }

        [Fact]
        public void DelayedOldSessionCannotEraseNewSessionConfiguration()
        {
            var receiver = new SessionConfigurationReceiver();
            Assert.True(receiver.Apply(Message("old", 10)));
            Assert.True(receiver.Apply(Message("new", 20)));
            Assert.True(receiver.Apply(Message("old", 10)));
            var compact = Message("new", 0); compact.HasConfiguration = false;
            Assert.True(receiver.Apply(compact)); Assert.Equal(20, compact.MealPrice);
            receiver.Reset(); Assert.False(receiver.Apply(compact));
        }

        [Fact]
        public void ConfigurationReceiverRejectsInvalidAndUnknownConfiguration()
        {
            var receiver = new SessionConfigurationReceiver();
            Assert.False(receiver.Apply(Message("", 10)));
            Assert.False(receiver.Apply(Message("a", -1)));
            var invalidRevision = Message(); invalidRevision.ConfigurationRevision = 0;
            Assert.False(receiver.Apply(invalidRevision));
            for (int i = 0; i < 17; i++) Assert.True(receiver.Apply(Message("session" + i)));
            var expired = Message("session0"); expired.HasConfiguration = false;
            Assert.False(receiver.Apply(expired));
        }

        [Fact]
        public void EnvironmentUsesUnreliableTelemetryButReliableTravelAndSafetyRefresh()
        {
            var policy = new EnvironmentDeliveryPolicy();
            var report = new SurvivalEnvironmentReport { SessionId = "a" };
            Assert.True(policy.Prepare(report, 0)); Assert.Equal(1u, report.TravelRevision);
            Assert.False(policy.Prepare(report, 1)); Assert.False(policy.Prepare(report, 2));
            Assert.True(policy.Prepare(report, 3)); Assert.Equal(1u, report.TravelRevision);
            report.IsTravelling = true; Assert.True(policy.Prepare(report, 3.1f)); Assert.Equal(2u, report.TravelRevision);
            report.IsTravelling = false; Assert.True(policy.Prepare(report, 3.2f)); Assert.Equal(3u, report.TravelRevision);
            report.SessionId = "b"; Assert.True(policy.Prepare(report, 3.3f)); Assert.Equal(1u, report.TravelRevision);
            policy.Reset(); Assert.True(policy.Prepare(report, 0));
        }

        [Fact]
        public void ReorderedTravelPacketsCannotRestartCompletedTravel()
        {
            var receiver = new TravelTransitionOrder();
            Assert.False(receiver.Accept(0, true));
            Assert.True(receiver.Accept(2, true)); Assert.True(receiver.Accept(3, false));
            Assert.False(receiver.Accept(2, true)); Assert.False(receiver.Accept(3, true));
            Assert.True(receiver.Accept(3, false)); Assert.True(receiver.Accept(4, true));
        }

        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void StatePacketOmitsOnlyStaticConfiguration(bool full)
        {
            var message = Message(); message.HasConfiguration = full; message.ConfigurationRevision = 27;
            message.State.Health = 27; message.State.Rest = 13; message.ItemIdentity = "receipt";
            var packet = new SurvivalStatePacket { Message = message };
            using (var stream = new MemoryStream())
            {
                packet.Serialize(new BinaryWriter(stream)); long length = stream.Length;
                stream.Position = 0; var read = new SurvivalStatePacket(); read.Deserialize(new BinaryReader(stream));
                Assert.Equal(stream.Length, stream.Position); Assert.Equal(full, read.Message.HasConfiguration);
                Assert.Equal(27u, read.Message.ConfigurationRevision); Assert.Equal(27, read.Message.State.Health);
                Assert.Equal(13, read.Message.State.Rest); Assert.Equal("receipt", read.Message.ItemIdentity);
                if (full) { Assert.Equal(100, read.Message.MealPrice); Assert.True(read.Message.DisableNeedsConsumption); }
                stream.SetLength(0); stream.Position = 0; message.HasConfiguration = !full; packet.Serialize(new BinaryWriter(stream));
                Assert.Equal(full ? -29 : 29, stream.Length - length);
            }
        }

        [Fact]
        public void EnvironmentPacketCarriesTravelRevision()
        {
            using (var stream = new MemoryStream())
            {
                var packet = new SurvivalEnvironmentPacket(); packet.Report.TravelRevision = 43;
                packet.Report.IsTravelling = true; packet.Serialize(new BinaryWriter(stream));
                stream.Position = 0; var read = new SurvivalEnvironmentPacket(); read.Deserialize(new BinaryReader(stream));
                Assert.Equal(43u, read.Report.TravelRevision); Assert.True(read.Report.IsTravelling);
                Assert.Equal(stream.Length, stream.Position);
            }
        }

        [Fact]
        public void TrainProbeKeepsFullRateThroughoutRiskWindow()
        {
            var cadence = new TrainProbeCadence(); Assert.Equal(.5f, cadence.Interval(0));
            cadence.ObserveRisk(1); Assert.Equal(.125f, cadence.Interval(1));
            Assert.Equal(.125f, cadence.Interval(3.99f)); cadence.ObserveRisk(3.9f);
            Assert.True(cadence.IsHighRisk(6.8f)); Assert.False(cadence.IsHighRisk(6.91f));
            cadence.Reset(); Assert.False(cadence.IsHighRisk(0));
        }

        [Fact]
        public void HudCachesTextButInvalidatesThresholdsWithinSameRoundedNumber()
        {
            var cache = new HudPresentationRevision(); var state = new SurvivalState { Health = 40.1f };
            Func<bool> changed = () => cache.Observe(state, 22, true, true, true);
            Assert.True(changed()); Assert.False(changed()); state.Health = 40.05f; Assert.False(changed());
            state.Health = 39.95f; Assert.True(changed()); Assert.False(changed());
            state.BodyTemperatureCelsius = 38.49f; Assert.True(changed());
            state.BodyTemperatureCelsius = 38.51f; Assert.True(changed());
            state.LowRestGameHours = 48; Assert.True(changed());
            Assert.True(cache.Observe(state, 22, true, true, false));
            Assert.True(cache.Observe(state, 22, false, true, false));
            Assert.True(cache.Observe(state, 22, false, false, false));
        }

        [Fact]
        public void SaveNormalizationPreservesLatestRecordsOrderAndListIdentity()
        {
            var document = new SurvivalSaveDocument(); var list = document.Players;
            var first = new SurvivalPlayerRecord { IdentityId = "a", LastSeenUtcTicks = 1 };
            var latest = new SurvivalPlayerRecord { IdentityId = "A", LastSeenUtcTicks = 3 };
            var tied = new SurvivalPlayerRecord { IdentityId = "a", LastSeenUtcTicks = 3 };
            var other = new SurvivalPlayerRecord { IdentityId = "b", LastSeenUtcTicks = 5 };
            list.Add(null); list.Add(first); list.Add(other); list.Add(latest); list.Add(tied);
            document.Normalize(new SurvivalTuning());
            Assert.Same(list, document.Players); Assert.Equal(2, list.Count);
            Assert.Same(latest, list[0]); Assert.Same(other, list[1]);
            document.Normalize(new SurvivalTuning()); Assert.Same(latest, list[0]);
        }
    }
}
