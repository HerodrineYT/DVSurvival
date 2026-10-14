using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DVSurvival.Core
{
    /// <summary>
    /// A calendar interval observed by the multiplayer authority. The sequence identifies a
    /// single observation so retransmitted jumps can be ignored.
    /// </summary>
    public struct SleepCalendarJump
    {
        public SleepCalendarJump(uint sequence, long beforeTicks, long afterTicks,
            double observedAtSeconds, bool allowsDurationAlignment = false)
        {
            if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            SleepCalendarReconciler.ValidateInterval(beforeTicks, afterTicks);
            SleepCalendarReconciler.ValidateTimestamp(observedAtSeconds, nameof(observedAtSeconds));
            Sequence = sequence;
            BeforeTicks = beforeTicks;
            AfterTicks = afterTicks;
            ObservedAtSeconds = observedAtSeconds;
            AllowsDurationAlignment = allowsDurationAlignment;
        }

        public uint Sequence { get; private set; }
        public long BeforeTicks { get; private set; }
        public long AfterTicks { get; private set; }
        public double ObservedAtSeconds { get; private set; }
        public bool AllowsDurationAlignment { get; private set; }
        public double Hours { get { return SleepCalendarReconciler.Hours(BeforeTicks, AfterTicks); } }
    }

    /// <summary>
    /// One completed native sleep reported to the authority. Calendar ticks are captured around
    /// the native time advance. Their duration is validated and, when client and host calendar
    /// coordinates differ, aligned only to a host-captured advance of the same duration.
    /// </summary>
    public struct SleepCalendarSleep
    {
        public SleepCalendarSleep(byte playerId, uint requestId, long beforeTicks, long afterTicks,
            double receivedAtSeconds)
        {
            if (requestId == 0) throw new ArgumentOutOfRangeException(nameof(requestId));
            SleepCalendarReconciler.ValidateInterval(beforeTicks, afterTicks);
            SleepCalendarReconciler.ValidateTimestamp(receivedAtSeconds, nameof(receivedAtSeconds));
            PlayerId = playerId;
            RequestId = requestId;
            BeforeTicks = beforeTicks;
            AfterTicks = afterTicks;
            ReceivedAtSeconds = receivedAtSeconds;
        }

        public byte PlayerId { get; private set; }
        public uint RequestId { get; private set; }
        public long BeforeTicks { get; private set; }
        public long AfterTicks { get; private set; }
        public double ReceivedAtSeconds { get; private set; }
        public double Hours { get { return SleepCalendarReconciler.Hours(BeforeTicks, AfterTicks); } }
    }

    /// <summary>
    /// A calendar jump that is safe to apply. Call <see cref="GetAwakeHours"/> separately for
    /// every connected player; sleep is excluded only from the player who reported it.
    /// </summary>
    public sealed class SleepCalendarAdvance
    {
        private readonly IReadOnlyDictionary<byte, double> matchedSleepHours;

        internal SleepCalendarAdvance(SleepCalendarJump jump,
            Dictionary<byte, double> matchedSleepHours)
            : this(jump, matchedSleepHours, jump.Sequence, jump.Hours, true)
        {
        }

        internal SleepCalendarAdvance(SleepCalendarJump jump,
            Dictionary<byte, double> matchedSleepHours, uint sourceJumpSequence,
            double sourceJumpHours, bool isFinalSourceSegment)
        {
            Jump = jump;
            this.matchedSleepHours = new ReadOnlyDictionary<byte, double>(matchedSleepHours);
            SourceJumpSequence = sourceJumpSequence;
            SourceJumpHours = sourceJumpHours;
            IsFinalSourceSegment = isFinalSourceSegment;
        }

        public SleepCalendarJump Jump { get; private set; }
        public double TotalHours { get { return Jump.Hours; } }
        public IReadOnlyDictionary<byte, double> MatchedSleepHours { get { return matchedSleepHours; } }
        public uint SourceJumpSequence { get; private set; }
        public double SourceJumpHours { get; private set; }
        public bool IsFinalSourceSegment { get; private set; }
        public double SourceFraction
        {
            get { return SourceJumpHours > 0d ? TotalHours / SourceJumpHours : 0d; }
        }

        public double GetMatchedSleepHours(byte playerId)
        {
            double hours;
            return matchedSleepHours.TryGetValue(playerId, out hours) ? hours : 0d;
        }

        public double GetAwakeHours(byte playerId)
        {
            return Math.Max(0d, TotalHours - GetMatchedSleepHours(playerId));
        }
    }

    public enum SleepCalendarOperationKind : byte
    {
        Advance = 0,
        CommitSleep = 1,
        RejectSleep = 2
    }

    /// <summary>
    /// One authority operation in calendar order. Advance operations must be applied before moving
    /// to the next item; CommitSleep is the only operation that authorizes simulation of sleep.
    /// </summary>
    public sealed class SleepCalendarOperation
    {
        private SleepCalendarOperation(SleepCalendarOperationKind kind,
            SleepCalendarAdvance advance, SleepCalendarSleep sleep)
        {
            Kind = kind;
            Advance = advance;
            Sleep = sleep;
        }

        public SleepCalendarOperationKind Kind { get; private set; }
        public SleepCalendarAdvance Advance { get; private set; }
        public SleepCalendarSleep Sleep { get; private set; }

        internal static SleepCalendarOperation ForAdvance(SleepCalendarAdvance advance)
        {
            return new SleepCalendarOperation(SleepCalendarOperationKind.Advance, advance,
                default(SleepCalendarSleep));
        }

        internal static SleepCalendarOperation ForSleep(SleepCalendarSleep sleep, bool accepted)
        {
            return new SleepCalendarOperation(accepted
                    ? SleepCalendarOperationKind.CommitSleep
                    : SleepCalendarOperationKind.RejectSleep,
                null, sleep);
        }
    }

    /// <summary>
    /// Reconciles independently delivered native-sleep actions and authoritative calendar jumps.
    /// The helper is intentionally state-only and uses caller-supplied monotonic timestamps.
    /// It is not thread-safe; the runtime should call it from its authority update thread.
    /// </summary>
    public sealed class SleepCalendarReconciler
    {
        public const double DefaultGracePeriodSeconds = 10d;
        public const long DurationAlignmentToleranceTicks = TimeSpan.TicksPerSecond;
        private const int AmbiguousDurationAlignmentIndex = -2;
        private const int MaximumRememberedJumpSequences = 4096;
        private static readonly IReadOnlyList<SleepCalendarAdvance> NoAdvances =
            new SleepCalendarAdvance[0];
        private static readonly IReadOnlyList<SleepCalendarOperation> NoOperations =
            new SleepCalendarOperation[0];

        private readonly List<PendingJump> pendingJumps = new List<PendingJump>();
        private readonly List<SleepCalendarSleep> pendingSleeps = new List<SleepCalendarSleep>();
        private readonly List<SleepCalendarSleep> committedScratch = new List<SleepCalendarSleep>(32);
        private readonly HashSet<uint> seenJumpSequences = new HashSet<uint>();
        private readonly Queue<uint> seenJumpSequenceOrder = new Queue<uint>();
        private readonly Dictionary<byte, HashSet<uint>> seenSleepRequests =
            new Dictionary<byte, HashSet<uint>>();

        public SleepCalendarReconciler(double gracePeriodSeconds = DefaultGracePeriodSeconds)
        {
            ValidateTimestamp(gracePeriodSeconds, nameof(gracePeriodSeconds));
            if (gracePeriodSeconds <= 0d)
                throw new ArgumentOutOfRangeException(nameof(gracePeriodSeconds));
            GracePeriodSeconds = gracePeriodSeconds;
        }

        public double GracePeriodSeconds { get; private set; }
        public int PendingJumpCount { get { return pendingJumps.Count; } }
        public int PendingSleepCount { get { return pendingSleeps.Count; } }

        /// <summary>Records a jump once. Returns false for a retransmitted sequence.</summary>
        public bool TryRecordJump(SleepCalendarJump jump)
        {
            ValidateJump(jump);
            if (!seenJumpSequences.Add(jump.Sequence)) return false;
            seenJumpSequenceOrder.Enqueue(jump.Sequence);
            while (seenJumpSequenceOrder.Count > MaximumRememberedJumpSequences)
                seenJumpSequences.Remove(seenJumpSequenceOrder.Dequeue());
            var pending = new PendingJump(jump, true, true);
            for (var i = 0; i < pendingSleeps.Count; i++)
                pending.TryMatch(pendingSleeps[i], GracePeriodSeconds);
            pendingJumps.Add(pending);
            return true;
        }

        /// <summary>
        /// Queues an ordinary calendar interval behind an unresolved discontinuity. It preserves
        /// ordering but can neither authorize sleep nor add another grace-period delay.
        /// </summary>
        public bool TryRecordOrderedAdvance(SleepCalendarJump jump)
        {
            ValidateJump(jump);
            if (!RememberJumpSequence(jump.Sequence)) return false;
            pendingJumps.Add(new PendingJump(jump, false, false));
            return true;
        }

        /// <summary>Returns true when an interval could contribute to a pending exact sleep.</summary>
        public bool IntersectsPendingSleep(long beforeTicks, long afterTicks)
        {
            ValidateInterval(beforeTicks, afterTicks);
            for (var i = 0; i < pendingSleeps.Count; i++)
                if (Intersects(beforeTicks, afterTicks, pendingSleeps[i].BeforeTicks,
                    pendingSleeps[i].AfterTicks))
                    return true;
            return false;
        }

        /// <summary>Records a completed sleep once per player/request id.</summary>
        public bool TryRecordSleep(SleepCalendarSleep sleep)
        {
            ValidateSleep(sleep);
            HashSet<uint> requests;
            if (!seenSleepRequests.TryGetValue(sleep.PlayerId, out requests))
            {
                requests = new HashSet<uint>();
                seenSleepRequests.Add(sleep.PlayerId, requests);
            }
            if (!requests.Add(sleep.RequestId)) return false;

            for (var i = 0; i < pendingJumps.Count; i++)
                pendingJumps[i].TryMatch(sleep, GracePeriodSeconds);
            pendingSleeps.Add(sleep);
            return true;
        }

        /// <summary>
        /// Returns jumps whose grace period has elapsed. A jump stays pending at the exact deadline
        /// so events carrying that same monotonic timestamp can still be recorded first.
        /// </summary>
        public IReadOnlyList<SleepCalendarAdvance> DrainReady(double nowSeconds)
        {
            ValidateTimestamp(nowSeconds, nameof(nowSeconds));
            List<SleepCalendarAdvance> ready = null;
            for (var i = 0; i < pendingJumps.Count;)
            {
                var pending = pendingJumps[i];
                if (nowSeconds <= pending.Jump.ObservedAtSeconds + GracePeriodSeconds)
                {
                    i++;
                    continue;
                }
                if (ready == null) ready = new List<SleepCalendarAdvance>();
                ready.Add(pending.CreateAdvance());
                pendingJumps.RemoveAt(i);
            }

            for (var i = pendingSleeps.Count - 1; i >= 0; i--)
                if (nowSeconds > pendingSleeps[i].ReceivedAtSeconds + GracePeriodSeconds)
                    pendingSleeps.RemoveAt(i);
            return ready == null ? NoAdvances : ready.ToArray();
        }

        /// <summary>
        /// Drains a transaction in calendar order. Unlike <see cref="DrainReady"/>, this method
        /// emits sleep commits only when pending jumps cover the sleep's complete tick interval.
        /// A caller must use one drain API consistently for a reconciler instance.
        /// </summary>
        public IReadOnlyList<SleepCalendarOperation> DrainOperations(double nowSeconds)
        {
            ValidateTimestamp(nowSeconds, nameof(nowSeconds));
            if (pendingJumps.Count == 0 && pendingSleeps.Count == 0) return NoOperations;

            var drainCount = ReadyPrefixLength(nowSeconds);
            if (drainCount > 0)
            {
                // If an exact sleep spans a ready jump and a later pending jump, retain the entire
                // chronological suffix until it can be committed as one ordered transaction.
                for (var i = 0; i < pendingSleeps.Count; i++)
                {
                    var sleep = pendingSleeps[i];
                    if (IsCovered(sleep, pendingJumps, drainCount)) continue;
                    var firstOverlap = FirstCorrelatedOverlap(sleep, drainCount);
                    var completeCoverageIsPending =
                        IsCovered(sleep, pendingJumps, pendingJumps.Count);
                    var canStillReceiveCoverage = nowSeconds <=
                        sleep.ReceivedAtSeconds + GracePeriodSeconds;
                    if (firstOverlap >= 0 && firstOverlap < drainCount &&
                        (completeCoverageIsPending || canStillReceiveCoverage))
                        drainCount = firstOverlap;
                }
            }

            var committedSleeps = committedScratch;
            committedSleeps.Clear();
            if (drainCount > 0)
            {
                for (var i = 0; i < pendingSleeps.Count; i++)
                    if (IsCovered(pendingSleeps[i], pendingJumps, drainCount))
                        committedSleeps.Add(pendingSleeps[i]);
            }

            List<SleepCalendarOperation> operations = null;
            if (drainCount > 0)
            {
                operations = BuildOperations(drainCount, committedSleeps);
                pendingJumps.RemoveRange(0, drainCount);
            }

            for (var i = pendingSleeps.Count - 1; i >= 0; i--)
            {
                var sleep = pendingSleeps[i];
                var committed = ContainsSleep(committedSleeps, sleep.PlayerId, sleep.RequestId);
                var invalidated = !committed && drainCount > 0 &&
                    OverlapsDrainedCalendar(sleep, operations);
                var stillExactlyCovered = !committed &&
                    IsCovered(sleep, pendingJumps, pendingJumps.Count);
                var expired = !stillExactlyCovered &&
                    nowSeconds > sleep.ReceivedAtSeconds + GracePeriodSeconds;
                if (!committed && !invalidated && !expired) continue;
                pendingSleeps.RemoveAt(i);
                if (committed) continue;
                if (operations == null) operations = new List<SleepCalendarOperation>();
                operations.Add(SleepCalendarOperation.ForSleep(sleep, false));
            }

            return operations == null || operations.Count == 0 ? NoOperations : operations.ToArray();
        }

        /// <summary>Removes all pending and replay-protection state owned by a disconnected player.</summary>
        public void DisconnectPlayer(byte playerId)
        {
            for (var i = pendingSleeps.Count - 1; i >= 0; i--)
                if (pendingSleeps[i].PlayerId == playerId) pendingSleeps.RemoveAt(i);
            for (var i = 0; i < pendingJumps.Count; i++) pendingJumps[i].RemovePlayer(playerId);
            seenSleepRequests.Remove(playerId);
        }

        /// <summary>Clears the entire session, including replay-protection state.</summary>
        public void Reset()
        {
            pendingJumps.Clear();
            pendingSleeps.Clear();
            committedScratch.Clear();
            seenJumpSequences.Clear();
            seenJumpSequenceOrder.Clear();
            seenSleepRequests.Clear();
        }

        internal static void ValidateInterval(long beforeTicks, long afterTicks)
        {
            if (beforeTicks < DateTime.MinValue.Ticks || beforeTicks > DateTime.MaxValue.Ticks)
                throw new ArgumentOutOfRangeException(nameof(beforeTicks));
            if (afterTicks < DateTime.MinValue.Ticks || afterTicks > DateTime.MaxValue.Ticks)
                throw new ArgumentOutOfRangeException(nameof(afterTicks));
            if (afterTicks <= beforeTicks)
                throw new ArgumentException("The calendar interval must move forwards.");
        }

        internal static void ValidateTimestamp(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        internal static double Hours(long beforeTicks, long afterTicks)
        {
            return (afterTicks - beforeTicks) / (double)TimeSpan.TicksPerHour;
        }

        private static void ValidateJump(SleepCalendarJump jump)
        {
            if (jump.Sequence == 0) throw new ArgumentOutOfRangeException(nameof(jump));
            ValidateInterval(jump.BeforeTicks, jump.AfterTicks);
            ValidateTimestamp(jump.ObservedAtSeconds, nameof(jump));
        }

        private static void ValidateSleep(SleepCalendarSleep sleep)
        {
            if (sleep.RequestId == 0) throw new ArgumentOutOfRangeException(nameof(sleep));
            ValidateInterval(sleep.BeforeTicks, sleep.AfterTicks);
            ValidateTimestamp(sleep.ReceivedAtSeconds, nameof(sleep));
        }

        private int ReadyPrefixLength(double nowSeconds)
        {
            var count = 0;
            while (count < pendingJumps.Count)
            {
                var pending = pendingJumps[count];
                if (pending.RequiresGrace &&
                    nowSeconds <= pending.Jump.ObservedAtSeconds + GracePeriodSeconds)
                    break;
                count++;
            }
            return count;
        }

        private int FirstCorrelatedOverlap(SleepCalendarSleep sleep, int count)
        {
            var durationAlignmentIndex = BestDurationAlignmentJumpIndex(sleep, pendingJumps);
            for (var i = 0; i < count; i++)
            {
                long matchedBefore;
                long matchedAfter;
                if (TryGetMatchedHostInterval(pendingJumps[i], i, durationAlignmentIndex,
                    sleep, out matchedBefore, out matchedAfter))
                    return i;
            }
            return -1;
        }

        private bool IsCovered(SleepCalendarSleep sleep, List<PendingJump> jumps, int count)
        {
            var durationTicks = sleep.AfterTicks - sleep.BeforeTicks;
            var durationAlignmentIndex = BestDurationAlignmentJumpIndex(sleep, jumps);
            long cursor = 0L;
            while (cursor < durationTicks)
            {
                var furthest = cursor;
                for (var i = 0; i < count; i++)
                {
                    long matchedBefore;
                    long matchedAfter;
                    if (!TryGetMatchedHostInterval(jumps[i], i, durationAlignmentIndex,
                        sleep, out matchedBefore, out matchedAfter))
                        continue;
                    long relativeBefore;
                    long relativeAfter;
                    if (i == durationAlignmentIndex)
                    {
                        relativeBefore = 0L;
                        relativeAfter = durationTicks;
                    }
                    else
                    {
                        relativeBefore = matchedBefore - sleep.BeforeTicks;
                        relativeAfter = matchedAfter - sleep.BeforeTicks;
                    }
                    if (relativeBefore > cursor || relativeAfter <= cursor) continue;
                    if (relativeAfter > furthest) furthest = Math.Min(relativeAfter, durationTicks);
                }
                if (furthest <= cursor) return false;
                cursor = furthest;
            }
            return true;
        }

        private List<SleepCalendarOperation> BuildOperations(int drainCount,
            List<SleepCalendarSleep> committedSleeps)
        {
            var operations = new List<SleepCalendarOperation>();
            var emittedSleepKeys = new HashSet<ulong>();
            var jumps = new List<PendingJump>(drainCount);
            for (var i = 0; i < drainCount; i++) jumps.Add(pendingJumps[i]);
            jumps.Sort(PendingJump.CompareByCalendar);

            for (var jumpIndex = 0; jumpIndex < jumps.Count; jumpIndex++)
            {
                var source = jumps[jumpIndex].Jump;
                var boundaries = new List<long> { source.BeforeTicks, source.AfterTicks };
                for (var sleepIndex = 0; sleepIndex < committedSleeps.Count; sleepIndex++)
                {
                    var sleep = committedSleeps[sleepIndex];
                    var alignmentIndex = BestDurationAlignmentJumpIndex(sleep, jumps);
                    long matchedBefore;
                    long matchedAfter;
                    if (!TryGetMatchedHostInterval(jumps[jumpIndex], jumpIndex,
                        alignmentIndex, sleep, out matchedBefore, out matchedAfter))
                        continue;
                    if (matchedBefore > source.BeforeTicks && matchedBefore < source.AfterTicks)
                        boundaries.Add(matchedBefore);
                    if (matchedAfter > source.BeforeTicks && matchedAfter < source.AfterTicks)
                        boundaries.Add(matchedAfter);
                }
                boundaries.Sort();
                RemoveDuplicateBoundaries(boundaries);

                for (var segmentIndex = 0; segmentIndex < boundaries.Count - 1; segmentIndex++)
                {
                    var before = boundaries[segmentIndex];
                    var after = boundaries[segmentIndex + 1];
                    var matched = new Dictionary<byte, double>();
                    var segmentHours = Hours(before, after);
                    for (var sleepIndex = 0; sleepIndex < committedSleeps.Count; sleepIndex++)
                    {
                        var sleep = committedSleeps[sleepIndex];
                        var alignmentIndex = BestDurationAlignmentJumpIndex(sleep, jumps);
                        long matchedBefore;
                        long matchedAfter;
                        if (TryGetMatchedHostInterval(jumps[jumpIndex], jumpIndex,
                            alignmentIndex, sleep, out matchedBefore, out matchedAfter) &&
                            matchedBefore <= before && matchedAfter >= after)
                            matched[sleep.PlayerId] = segmentHours;
                    }
                    var segmentJump = new SleepCalendarJump(source.Sequence, before, after,
                        source.ObservedAtSeconds);
                    operations.Add(SleepCalendarOperation.ForAdvance(new SleepCalendarAdvance(
                        segmentJump, matched, source.Sequence, source.Hours,
                        segmentIndex == boundaries.Count - 2)));

                    for (var sleepIndex = 0; sleepIndex < committedSleeps.Count; sleepIndex++)
                    {
                        var sleep = committedSleeps[sleepIndex];
                        var sleepKey = ((ulong)sleep.PlayerId << 32) | sleep.RequestId;
                        var alignmentIndex = BestDurationAlignmentJumpIndex(sleep, jumps);
                        long matchedBefore;
                        long matchedAfter;
                        if (TryGetMatchedHostInterval(jumps[jumpIndex], jumpIndex,
                            alignmentIndex, sleep, out matchedBefore, out matchedAfter) &&
                            matchedAfter == after &&
                            (jumpIndex == alignmentIndex || matchedAfter == sleep.AfterTicks) &&
                            emittedSleepKeys.Add(sleepKey))
                            operations.Add(SleepCalendarOperation.ForSleep(sleep, true));
                    }
                }
            }
            return operations;
        }

        private static bool OverlapsDrainedCalendar(SleepCalendarSleep sleep,
            List<SleepCalendarOperation> operations)
        {
            if (operations == null) return false;
            for (var i = 0; i < operations.Count; i++)
            {
                var advance = operations[i].Advance;
                if (operations[i].Kind == SleepCalendarOperationKind.Advance &&
                    Intersects(advance.Jump.BeforeTicks, advance.Jump.AfterTicks,
                        sleep.BeforeTicks, sleep.AfterTicks))
                    return true;
            }
            return false;
        }

        private static bool ContainsSleep(List<SleepCalendarSleep> sleeps, byte playerId,
            uint requestId)
        {
            for (var i = 0; i < sleeps.Count; i++)
                if (sleeps[i].PlayerId == playerId && sleeps[i].RequestId == requestId)
                    return true;
            return false;
        }

        private static bool CanCorrelate(SleepCalendarJump jump, SleepCalendarSleep sleep,
            double gracePeriodSeconds)
        {
            return Math.Abs(jump.ObservedAtSeconds - sleep.ReceivedAtSeconds) <= gracePeriodSeconds;
        }

        private int BestDurationAlignmentJumpIndex(SleepCalendarSleep sleep,
            List<PendingJump> jumps)
        {
            var candidateIndex = -1;
            var candidateCount = 0;
            var exactIndex = -1;
            for (var i = 0; i < jumps.Count; i++)
            {
                var pending = jumps[i];
                var jump = pending.Jump;
                if (!pending.CanMatchSleep || !jump.AllowsDurationAlignment ||
                    !CanCorrelate(jump, sleep, GracePeriodSeconds) ||
                    !HasMatchingDuration(jump, sleep))
                    continue;
                if (jump.BeforeTicks == sleep.BeforeTicks && jump.AfterTicks == sleep.AfterTicks)
                {
                    if (exactIndex >= 0) return AmbiguousDurationAlignmentIndex;
                    exactIndex = i;
                    continue;
                }
                candidateIndex = i;
                candidateCount++;
            }
            // A byte packet does not identify whether a relayed TimeAdvance came from sleep or
            // fast travel. Exact host coordinates are decisive; otherwise accept duration-only
            // alignment only when there is one possible captured interval in the correlation
            // window. Ambiguity fails closed instead of crediting the wrong player with sleep.
            if (exactIndex >= 0) return exactIndex;
            if (candidateCount == 1) return candidateIndex;
            return candidateCount == 0 ? -1 : AmbiguousDurationAlignmentIndex;
        }

        private bool TryGetMatchedHostInterval(PendingJump pending, int jumpIndex,
            int durationAlignmentIndex, SleepCalendarSleep sleep,
            out long matchedBefore, out long matchedAfter)
        {
            matchedBefore = 0L;
            matchedAfter = 0L;
            if (durationAlignmentIndex == AmbiguousDurationAlignmentIndex) return false;
            if (!pending.CanMatchSleep ||
                !CanCorrelate(pending.Jump, sleep, GracePeriodSeconds))
                return false;
            if (durationAlignmentIndex >= 0)
            {
                // Once a host-captured interval has won duration alignment, the client's absolute
                // tick coordinates are from a different clock domain. Do not also let an accidental
                // overlap claim time from any other host interval.
                if (jumpIndex != durationAlignmentIndex) return false;
                matchedBefore = pending.Jump.BeforeTicks;
                matchedAfter = pending.Jump.AfterTicks;
                return true;
            }
            matchedBefore = Math.Max(pending.Jump.BeforeTicks, sleep.BeforeTicks);
            matchedAfter = Math.Min(pending.Jump.AfterTicks, sleep.AfterTicks);
            return matchedAfter > matchedBefore;
        }

        private static bool HasMatchingDuration(SleepCalendarJump jump,
            SleepCalendarSleep sleep)
        {
            var jumpTicks = jump.AfterTicks - jump.BeforeTicks;
            var sleepTicks = sleep.AfterTicks - sleep.BeforeTicks;
            return Math.Abs(jumpTicks - sleepTicks) <= DurationAlignmentToleranceTicks;
        }

        private static bool Intersects(long firstBefore, long firstAfter,
            long secondBefore, long secondAfter)
        {
            return firstBefore < secondAfter && secondBefore < firstAfter;
        }

        private static void RemoveDuplicateBoundaries(List<long> boundaries)
        {
            for (var i = boundaries.Count - 1; i > 0; i--)
                if (boundaries[i] == boundaries[i - 1]) boundaries.RemoveAt(i);
        }

        private bool RememberJumpSequence(uint sequence)
        {
            if (!seenJumpSequences.Add(sequence)) return false;
            seenJumpSequenceOrder.Enqueue(sequence);
            while (seenJumpSequenceOrder.Count > MaximumRememberedJumpSequences)
                seenJumpSequences.Remove(seenJumpSequenceOrder.Dequeue());
            return true;
        }

        private sealed class PendingJump
        {
            private readonly Dictionary<byte, List<TickInterval>> matchedIntervals =
                new Dictionary<byte, List<TickInterval>>();

            public PendingJump(SleepCalendarJump jump, bool canMatchSleep, bool requiresGrace)
            {
                Jump = jump;
                CanMatchSleep = canMatchSleep;
                RequiresGrace = requiresGrace;
            }
            public SleepCalendarJump Jump { get; private set; }
            public bool CanMatchSleep { get; private set; }
            public bool RequiresGrace { get; private set; }

            public static int CompareByCalendar(PendingJump left, PendingJump right)
            {
                var before = left.Jump.BeforeTicks.CompareTo(right.Jump.BeforeTicks);
                return before != 0 ? before : left.Jump.Sequence.CompareTo(right.Jump.Sequence);
            }

            public void TryMatch(SleepCalendarSleep sleep, double gracePeriodSeconds)
            {
                if (!CanMatchSleep) return;
                if (Math.Abs(Jump.ObservedAtSeconds - sleep.ReceivedAtSeconds) > gracePeriodSeconds)
                    return;
                var start = Math.Max(Jump.BeforeTicks, sleep.BeforeTicks);
                var end = Math.Min(Jump.AfterTicks, sleep.AfterTicks);
                if (end <= start) return;

                List<TickInterval> intervals;
                if (!matchedIntervals.TryGetValue(sleep.PlayerId, out intervals))
                {
                    intervals = new List<TickInterval>();
                    matchedIntervals.Add(sleep.PlayerId, intervals);
                }
                intervals.Add(new TickInterval(start, end));
            }

            public void RemovePlayer(byte playerId) { matchedIntervals.Remove(playerId); }

            public SleepCalendarAdvance CreateAdvance()
            {
                var matched = new Dictionary<byte, double>();
                foreach (var pair in matchedIntervals)
                {
                    var intervals = pair.Value;
                    intervals.Sort(TickInterval.Compare);
                    var start = intervals[0].Start;
                    var end = intervals[0].End;
                    long unionTicks = 0;
                    for (var i = 1; i < intervals.Count; i++)
                    {
                        var current = intervals[i];
                        if (current.Start <= end)
                        {
                            if (current.End > end) end = current.End;
                            continue;
                        }
                        unionTicks += end - start;
                        start = current.Start;
                        end = current.End;
                    }
                    unionTicks += end - start;
                    matched.Add(pair.Key, Math.Min(Jump.Hours,
                        unionTicks / (double)TimeSpan.TicksPerHour));
                }
                return new SleepCalendarAdvance(Jump, matched);
            }
        }

        private struct TickInterval
        {
            public TickInterval(long start, long end) { Start = start; End = end; }
            public long Start { get; private set; }
            public long End { get; private set; }

            public static int Compare(TickInterval left, TickInterval right)
            {
                var start = left.Start.CompareTo(right.Start);
                return start != 0 ? start : left.End.CompareTo(right.End);
            }
        }
    }
}
