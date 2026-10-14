using System;
using System.Collections.Generic;
using DVSurvival.Core;
using DVSurvival.Core.Runtime;
using DVSurvival.Core.Simulation;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class RebuildArchitectureTests
    {
        [Fact]
        public void FixedPlayerStorageMatchesDictionaryAcrossRandomConnectDisconnects()
        {
            var slots = new PlayerSlots<int>(); var expected = new Dictionary<byte, int>(); var random = new Random(3741);
            for (int step = 0; step < 10000; step++)
            {
                byte id = (byte)random.Next(256);
                if (random.Next(3) == 0) Assert.Equal(expected.Remove(id), slots.Remove(id));
                else { int value = random.Next(); expected[id] = value; slots[id] = value; }
                Assert.Equal(expected.Count, slots.Count);
                int seen = 0;
                foreach (var pair in slots) { Assert.Equal(expected[pair.Key], pair.Value); seen++; }
                Assert.Equal(expected.Count, seen);
            }
            slots.Clear(); Assert.Equal(0, slots.Count);
            for (int i = 0; i < 256; i++) { Assert.False(slots.ContainsKey((byte)i)); slots[(byte)i] = i; }
            Assert.Equal(256, slots.Count);
            for (int i = 255; i >= 0; i--) Assert.True(slots.Remove((byte)i));
            Assert.False(slots.TryGetValue(255, out _));
        }

        [Fact]
        public void FixedPlayerStorageDoesNotAllocateAfterConstruction()
        {
            var slots = new PlayerSlots<int>();
            for (int i = 0; i < 256; i++) slots[(byte)i] = i;
            int sum = 0;
            foreach (var pair in slots) sum += pair.Value;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int step = 0; step < 1000; step++)
            {
                byte id = (byte)step; slots.Remove(id); slots[id] = id;
                foreach (var pair in slots) sum += pair.Value;
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); Assert.True(sum > 0);
        }

        [Fact]
        public void ScheduleAvoidsCatchupBurstsAndResetsEveryDeadline()
        {
            var schedule = new SessionSchedule();
            Assert.True(schedule.Due(ScheduledJob.Broadcast, 0, 2));
            Assert.False(schedule.Due(ScheduledJob.Broadcast, 1, 2));
            Assert.True(schedule.Due(ScheduledJob.Broadcast, 1000, 2));
            Assert.False(schedule.Due(ScheduledJob.Broadcast, 1000, 2));
            Assert.False(schedule.Due(ScheduledJob.Broadcast, double.NaN, 2));
            Assert.False(schedule.Due(ScheduledJob.Save, 0, double.NaN));
            Assert.False(schedule.Due(ScheduledJob.Save, 0, double.PositiveInfinity));
            Assert.False(schedule.Due(ScheduledJob.Save, 0, 0));
            Assert.False(schedule.Due(ScheduledJob.Save, 0, -1));
            schedule.Accumulate(float.NaN); schedule.Accumulate(float.PositiveInfinity); schedule.Accumulate(-1);
            Assert.False(schedule.TakeSimulation(out _)); Assert.False(schedule.TakeTreatment(out _));
            schedule.Accumulate(.3f); Assert.False(schedule.TakeSimulation(out _));
            schedule.Accumulate(.3f); Assert.True(schedule.TakeTreatment(out var healing)); Assert.Equal(.6f, healing);
            schedule.Accumulate(.4f); Assert.True(schedule.TakeSimulation(out var elapsed)); Assert.Equal(1, elapsed);
            Assert.False(schedule.TakeSimulation(out _));
            schedule.Reset(); Assert.Equal(0, schedule.TreatmentSeconds); Assert.True(schedule.Due(ScheduledJob.Broadcast, 0, 2));
        }

        [Fact]
        public void CalendarHookDrainsOnlyAlreadyAccumulatedSimulationTime()
        {
            var schedule = new SessionSchedule(); schedule.Accumulate(.9f);
            Assert.Equal(.9f, schedule.DrainSimulation()); Assert.False(schedule.TakeSimulation(out _));
            Assert.True(schedule.TakeTreatment(out var treatment)); Assert.Equal(.9f, treatment);
            schedule.Accumulate(60); Assert.Equal(5, schedule.DrainSimulation());
        }

        [Fact]
        public void ValueInputsAreDetachedAndDoNotMutateSharedSettingsOrWorld()
        {
            var settings = new SurvivalTuning { NeedsRateMultiplier = float.NaN };
            var world = new SurvivalEnvironment { GameHours = 1, AmbientTemperatureCelsius = -20 };
            var rules = new SimulationRules(settings); var frame = new EnvironmentFrame(world);
            world.AmbientTemperatureCelsius = 100; settings.NeedsRateMultiplier = 3;
            Assert.Equal(-20, frame.Air); Assert.Equal(1, rules.NeedsRate);
            var playerA = new SurvivalState(); var playerB = new SurvivalState();
            NeedsEngine.Advance(playerA, frame, rules, 1);
            Assert.Equal(100, playerB.Hunger); Assert.Equal(37, playerB.BodyTemperatureCelsius);
            Assert.Equal(100, world.AmbientTemperatureCelsius); Assert.Equal(3, settings.NeedsRateMultiplier);
        }

        [Fact]
        public void EngineHandlesInvalidEnvironmentWithoutAllocatingClampedCopies()
        {
            var source = new SurvivalEnvironment { GameHours = .00001f, RainIntensity = float.NaN, Exposure = -100 };
            var settings = new SurvivalTuning(); var state = new SurvivalState();
            for (int i = 0; i < 1000; i++) SurvivalSimulator.Advance(state, source, settings, 1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) SurvivalSimulator.Advance(state, source, settings, 1);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(float.IsNaN(source.RainIntensity)); Assert.True(state.IsValid());
        }

        [Fact]
        public void PhysicalConsumablesNeverTemporarilyAlterInventoryStock()
        {
            var state = new SurvivalState { Meals = 0, Hunger = 10 };
            Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.ConsumePhysical(state, ProvisionKind.Meal, new SurvivalTuning()));
            Assert.Equal(0, state.Meals); Assert.Equal(55, state.Hunger); Assert.Equal(1u, state.Revision);
        }
    }
}
