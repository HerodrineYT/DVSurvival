using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DV.Teleporters;
using DV.Utils;
using DVSurvival.Core;
using DVSurvival.Core.Runtime;
using DVSurvival.Core.Simulation;
using UnityEngine;
using UnityModManagerNet;

namespace DVSurvival.Mod
{
    // Native integration boundary. Pure simulation and cadence are owned by Core/Simulation and Core/Runtime.
    internal sealed partial class SurvivalRuntime
    {
        internal bool TryGetGameDateTime(out DateTime value)
        {
            return environment.TryGetGameDateTime(out value);
        }

        public void OnWorldTimeAdvanceCompleted(long calendarBeforeTicks,
            long calendarAfterTicks, bool isNativeSleepOrigin = false)
        {
            if (!sessionReady || !network.IsAuthority || calendarBeforeTicks <= 0L ||
                calendarAfterTicks <= calendarBeforeTicks)
                return;
            try
            {
                var before = new DateTime(calendarBeforeTicks);
                var after = new DateTime(calendarAfterTicks);
                // First account for normal calendar progress since the last sample, then queue
                // the exact host-side TimeAdvance interval. Capturing it in the Harmony postfix
                // avoids client/host clock drift and prevents the next periodic sample from
                // counting the same skip a second time.
                var realSeconds = simulationAccumulator;
                simulationAccumulator = 0f;
                var beforeHours = ObserveCalendar(before, realSeconds, false);
                if (beforeHours > 0f || sleepCalendar.PendingJumpCount == 0) AdvanceAuthority(realSeconds, beforeHours,
                    travelPausedPlayers: observedTravelPause);
                // A generic relayed TimeAdvance may be fast travel. It can authorize duration-only
                // clock alignment only when the host already has a native sleep intent; the host's
                // own call is independently identified at the native SleepCoro call site.
                var allowsDurationAlignment = isNativeSleepOrigin ||
                    sleepCalendar.PendingSleepCount > 0;
                var immediateHours = ObserveCalendar(after, 0f, true,
                    allowsDurationAlignment);
                if (immediateHours > 0f) AdvanceAuthority(0f, immediateHours,
                    travelPausedPlayers: observedTravelPause);
            }
            catch (ArgumentOutOfRangeException)
            {
                RebaseGameCalendar();
            }
        }

        public bool OnLocalSleepStarting(float amountOfSecondsToSleep,
            long calendarBeforeTicks)
        {
            // In MP the intent is sent before Multiplayer's own TimeAdvance request, allowing
            // the authority to mark a relayed interval for cross-clock duration alignment.
            // MPAPI delivery is reliable but unordered; send order is not a transport guarantee.
            // Host/offline sleep keeps the
            // completion path below, where the exact local calendar result is already available.
            if (!sessionReady || !network.IsSessionActive || network.IsAuthority ||
                IsPersonalSleepMode ||
                !network.IsSupported || !HasConfirmedLocalState || lastHostMessage == null ||
                string.IsNullOrEmpty(lastHostMessage.SessionId) ||
                amountOfSecondsToSleep < 360f || amountOfSecondsToSleep > 86400f ||
                float.IsNaN(amountOfSecondsToSleep) || float.IsInfinity(amountOfSecondsToSleep) ||
                calendarBeforeTicks <= 0L ||
                Time.realtimeSinceStartup - lastCompletedSleepRequestTime < 1f)
                return false;
            try
            {
                var calendarAfterTicks = checked(calendarBeforeTicks +
                    TimeSpan.FromSeconds(amountOfSecondsToSleep).Ticks);
                if (calendarAfterTicks > DateTime.MaxValue.Ticks) return false;
                var sent = RequestAction(new SurvivalActionRequest
                {
                    RequestId = ++requestSequence,
                    Action = SurvivalActionKind.Sleep,
                    Amount = amountOfSecondsToSleep / 3600f,
                    CalendarBeforeTicks = calendarBeforeTicks,
                    CalendarAfterTicks = calendarAfterTicks
                });
                if (!sent) return false;
                lastCompletedSleepRequestTime = Time.realtimeSinceStartup;
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (OverflowException) { return false; }
        }

        public void OnLocalSleepCompleted(float amountOfSecondsToSleep,
            long calendarBeforeTicks = 0L, long calendarAfterTicks = 0L)
        {
            if (!sessionReady || amountOfSecondsToSleep <= 0f ||
                Time.realtimeSinceStartup - lastCompletedSleepRequestTime < 1f)
                return;
            lastCompletedSleepRequestTime = Time.realtimeSinceStartup;
            var sleepHours = amountOfSecondsToSleep / 3600f;
            if (calendarBeforeTicks > 0L && calendarAfterTicks > calendarBeforeTicks)
            {
                var measuredHours = TimeSpan.FromTicks(calendarAfterTicks - calendarBeforeTicks).TotalHours;
                if (measuredHours >= 0.1d && measuredHours <= 24d)
                    sleepHours = (float)measuredHours;
            }
            RequestAction(new SurvivalActionRequest
            {
                RequestId = ++requestSequence,
                Action = SurvivalActionKind.Sleep,
                Amount = sleepHours,
                CalendarBeforeTicks = calendarBeforeTicks,
                CalendarAfterTicks = calendarAfterTicks
            });
            // There are no other players whose independently delivered action can claim this
            // jump in local play, so finish the transaction without an artificial ten-second wait.
            if (!network.IsSessionActive && network.IsAuthority)
                DrainCalendarOperations(double.MaxValue);
        }

        private float ObserveAndQueueCalendar(float fallbackRealSeconds)
        {
            DateTime current;
            if (environment.TryGetGameDateTime(out current))
                return ObserveCalendar(current, fallbackRealSeconds, false, false);
            gameCalendarClock.Reset();
            var dayMinutes = environment.GetDayLengthMinutes();
            observedTravelPause = travelPause.Capture(Time.realtimeSinceStartup, false);
            return dayMinutes > 0f
                ? Mathf.Clamp(fallbackRealSeconds * 24f / (dayMinutes * 60f), 0f, 24f)
                : 0f;
        }

        private float ObserveCalendar(DateTime current, float fallbackRealSeconds,
            bool capturedTimeAdvance, bool allowsDurationAlignment = false)
        {
            var beforeTicks = gameCalendarClock.LastObservedTicks;
            var gameHours = gameCalendarClock.Observe(current);
            if (gameHours <= 0d || beforeTicks <= 0L)
            {
                observedTravelPause = travelPause.Capture(Time.realtimeSinceStartup, false);
                return 0f;
            }
            var afterTicks = current.Ticks;
            var maximumTicks = (long)(GameCalendarClock.MaximumObservedJumpHours *
                TimeSpan.TicksPerHour);
            if (afterTicks - beforeTicks > maximumTicks)
                beforeTicks = afterTicks - maximumTicks;
            var sequence = ++calendarJumpSequence;
            if (sequence == 0u) sequence = ++calendarJumpSequence;
            var jump = new SleepCalendarJump(sequence, beforeTicks, afterTicks,
                Time.realtimeSinceStartup, allowsDurationAlignment);
            var needsCorrelation = capturedTimeAdvance ||
                GameCalendarClock.IsDiscontinuousAdvance(gameHours,
                    fallbackRealSeconds, environment.GetDayLengthMinutes()) ||
                sleepCalendar.IntersectsPendingSleep(beforeTicks, afterTicks);
            observedTravelPause = travelPause.Capture(Time.realtimeSinceStartup, needsCorrelation);
            var queued = needsCorrelation
                ? sleepCalendar.TryRecordJump(jump)
                : sleepCalendar.PendingJumpCount > 0 &&
                    sleepCalendar.TryRecordOrderedAdvance(jump);
            if (queued)
            {
                calendarJumpRealSeconds[sequence] = Math.Max(0f, fallbackRealSeconds);
                if (observedTravelPause != null) calendarTravelPauses[sequence] = observedTravelPause;
                return 0f;
            }
            return (float)gameHours;
        }

        private void RebaseGameCalendar()
        {
            ResetCalendarReconciliation();
            gameCalendarClock.Reset();
            DateTime current;
            if (environment.TryGetGameDateTime(out current)) gameCalendarClock.Observe(current);
        }

        private void DrainCalendarOperations(double nowSeconds)
        {
            var operations = sleepCalendar.DrainOperations(nowSeconds);
            for (var i = 0; i < operations.Count; i++)
            {
                var operation = operations[i];
                if (operation.Kind == SleepCalendarOperationKind.Advance)
                {
                    var advance = operation.Advance;
                    float sourceRealSeconds;
                    if (!calendarJumpRealSeconds.TryGetValue(advance.SourceJumpSequence,
                        out sourceRealSeconds))
                        sourceRealSeconds = 0f;
                    var segmentRealSeconds = sourceRealSeconds * (float)advance.SourceFraction;
                    HashSet<byte> paused;
                    calendarTravelPauses.TryGetValue(advance.SourceJumpSequence, out paused);
                    AdvanceAuthority(segmentRealSeconds, (float)advance.TotalHours, advance, paused);
                    if (advance.IsFinalSourceSegment)
                    {
                        calendarJumpRealSeconds.Remove(advance.SourceJumpSequence);
                        calendarTravelPauses.Remove(advance.SourceJumpSequence);
                    }
                }
                else if (operation.Kind == SleepCalendarOperationKind.CommitSleep)
                    CommitPendingNativeSleep(operation.Sleep);
                else if (operation.Kind == SleepCalendarOperationKind.RejectSleep)
                    RejectPendingNativeSleep(operation.Sleep);
            }
        }

        private bool TryQueueNativeSleep(byte playerId, SurvivalActionRequest request,
            SurvivalEnvironment sleepEnvironment)
        {
            if (request == null || sleepEnvironment == null || request.RequestId == 0u ||
                float.IsNaN(request.Amount) || float.IsInfinity(request.Amount) ||
                request.Amount < 0.1f || request.Amount > 24f ||
                request.CalendarBeforeTicks <= 0L ||
                request.CalendarAfterTicks <= request.CalendarBeforeTicks ||
                pendingNativeSleeps.Count >= 32)
                return false;
            try
            {
                var measuredHours = TimeSpan.FromTicks(
                    request.CalendarAfterTicks - request.CalendarBeforeTicks).TotalHours;
                var tolerance = Math.Max(0.01d, request.Amount * 0.01d);
                if (measuredHours < 0.1d || measuredHours > 24d ||
                    Math.Abs(measuredHours - request.Amount) > tolerance)
                    return false;
                var key = NativeSleepKey(playerId, request.RequestId);
                if (pendingNativeSleeps.ContainsKey(key)) return false;
                var sleep = new SleepCalendarSleep(playerId, request.RequestId,
                    request.CalendarBeforeTicks, request.CalendarAfterTicks,
                    Time.realtimeSinceStartup);
                if (!sleepCalendar.TryRecordSleep(sleep)) return false;
                pendingNativeSleeps.Add(key, new PendingNativeSleepAction(playerId,
                    request.RequestId, (float)measuredHours, sleepEnvironment.Clone()));
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private void CommitPendingNativeSleep(SleepCalendarSleep sleep)
        {
            var key = NativeSleepKey(sleep.PlayerId, sleep.RequestId);
            PendingNativeSleepAction pending;
            if (!pendingNativeSleeps.TryGetValue(key, out pending)) return;
            pendingNativeSleeps.Remove(key);
            SurvivalPlayerRecord record;
            if (!activeRecords.TryGetValue(sleep.PlayerId, out record)) return;
            var previousCollapse = record.State.CollapseCount;
            var healthBeforeSleep = record.State.Health;
            var result = SurvivalSimulator.Sleep(record.State, pending.Hours,
                pending.Environment, settings.CachedTuning);
            entry.Logger.Log("Native sleep committed: player=" + sleep.PlayerId +
                ", hours=" + pending.Hours + ", result=" + result +
                ", rest=" + record.State.Rest + ", deprivationHours=" + record.State.LowRestGameHours +
                ", health=" + healthBeforeSleep + "->" + record.State.Health +
                ", bodyC=" + record.State.BodyTemperatureCelsius +
                ", airC=" + pending.Environment.AmbientTemperatureCelsius);
            var status = result == SurvivalResultCode.Success ? "sleep_ok" : string.Empty;
            if (ApplyDeathPenalty(record.State, previousCollapse)) status = "death";
            record.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
            if (sleep.PlayerId == lastLocalAuthorityId) SetLocalState(record.State);
            SendResult(sleep.PlayerId, record.State, sleep.RequestId, result, status);
            stateDirty = true;
        }

        private void RejectPendingNativeSleep(SleepCalendarSleep sleep)
        {
            var key = NativeSleepKey(sleep.PlayerId, sleep.RequestId);
            if (!pendingNativeSleeps.Remove(key)) return;
            entry.Logger.Warning("Native sleep rejected: no matching calendar advance for player " + sleep.PlayerId + ".");
            SurvivalPlayerRecord record;
            if (activeRecords.TryGetValue(sleep.PlayerId, out record))
                SendResult(sleep.PlayerId, record.State, sleep.RequestId,
                    SurvivalResultCode.InvalidRequest, string.Empty);
        }

        private bool HasPendingNativeSleep(byte playerId)
        {
            foreach (var pending in pendingNativeSleeps.Values)
                if (pending.PlayerId == playerId) return true;
            return false;
        }

        private void RemovePendingNativeSleeps(byte playerId)
        {
            if (pendingNativeSleeps.Count == 0) return;
            var keys = new List<ulong>();
            foreach (var pair in pendingNativeSleeps)
                if (pair.Value.PlayerId == playerId) keys.Add(pair.Key);
            for (var i = 0; i < keys.Count; i++) pendingNativeSleeps.Remove(keys[i]);
        }

        private static ulong NativeSleepKey(byte playerId, uint requestId)
        {
            return ((ulong)playerId << 32) | requestId;
        }

        private void ResetCalendarReconciliation()
        {
            calendarTravelPauses.Clear();
            observedTravelPause = null;
            sleepCalendar.Reset();
            calendarJumpRealSeconds.Clear();
            pendingNativeSleeps.Clear();
            calendarJumpSequence = 0u;
        }

        private static float ScaleRealSeconds(float realSeconds, float playerHours, float totalHours)
        {
            return totalHours > 0f ? Math.Max(0f, realSeconds) * playerHours / totalHours : 0f;
        }
    }
}
