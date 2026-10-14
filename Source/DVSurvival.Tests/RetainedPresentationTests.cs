using System;
using DVSurvival.Core;
using DVSurvival.Mod;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class RetainedPresentationTests
    {
        [Fact]
        public void UnchangedHudDoesNotFormatOrAllocate()
        {
            var presenter = new NeedsPresenter(); var state = new SurvivalState();
            presenter.Capture(state, 22, true, true); ModLocalization.Calls = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) presenter.Capture(state, 22, true, true);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); Assert.Equal(0, ModLocalization.Calls);
        }
        [Fact]
        public void TemperatureChangeDoesNotReformatFourNeeds()
        {
            var presenter = new NeedsPresenter(); var state = new SurvivalState();
            presenter.Capture(state, 22, true, true);
            var health = presenter.Numbers[0]; var food = presenter.Numbers[1]; var rest = presenter.Numbers[3];
            ModLocalization.Calls = 0; presenter.Capture(state, 23, true, true);
            Assert.Equal(2, ModLocalization.Calls);
            Assert.Same(health, presenter.Numbers[0]); Assert.Same(food, presenter.Numbers[1]); Assert.Same(rest, presenter.Numbers[3]);
        }
        [Fact]
        public void WarningAndLanguageChangesRefreshImmediately()
        {
            var presenter = new NeedsPresenter(); var state = new SurvivalState { Health = 40.1f };
            ModLocalization.IsRussian = false; presenter.Capture(state, 22, true, true);
            Assert.Equal("Health", presenter.Names[0]); state.Health = 39.95f;
            presenter.Capture(state, 22, true, true); Assert.Contains("#FF5555", presenter.Alerts[0]);
            state.LowRestGameHours = 24; presenter.Capture(state, 22, true, true); Assert.Contains("!", presenter.Alerts[3]);
            ModLocalization.IsRussian = true; presenter.Capture(state, 22, true, false);
            Assert.Equal("Здоровье", presenter.Names[0]); Assert.DoesNotContain("#FF5555", presenter.Alerts[0]);
            state.Health = 80; presenter.Capture(state, 22, true, true); Assert.Equal("80%", presenter.Numbers[0]);
        }
        [Fact]
        public void EmptyCalendarDrainDoesNotAllocatePerFrame()
        {
            var calendar = new SleepCalendarReconciler(); calendar.DrainOperations(0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) calendar.DrainOperations(i / 60d);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        [Fact]
        public void PendingCalendarGraceDoesNotAllocatePerFrame()
        {
            var calendar = new SleepCalendarReconciler();
            long start = new DateTime(2026, 1, 1).Ticks;
            calendar.TryRecordSleep(new SleepCalendarSleep(1, 1, start, start + TimeSpan.TicksPerHour * 8, 0));
            calendar.DrainOperations(0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) calendar.DrainOperations(i / 1000d);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(1, calendar.PendingSleepCount);
        }
        [Fact]
        public void StateCopyDoesNotShareReferencesOrLoseFields()
        {
            var source = new SurvivalState
            {
                Health = 17,
                FirstAidSecondsRemaining = 12,
                Rest = 34,
                LowRestGameHours = 72,
                CoffeeUsesSinceSleep = 7,
                CollapseCount = 8,
                Revision = 13
            };
            var copy = new SurvivalState(); source.CopyTo(copy);
            foreach (var field in typeof(SurvivalState).GetFields()) if (!field.IsStatic) Assert.Equal(field.GetValue(source), field.GetValue(copy));
            source.Health = 99; Assert.Equal(17, copy.Health);
        }
    }
}
