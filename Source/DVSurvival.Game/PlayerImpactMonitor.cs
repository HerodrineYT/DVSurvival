using DVSurvival.Core;
using UnityEngine;

namespace DVSurvival.Mod
{
    internal sealed class PlayerImpactMonitor : MonoBehaviour
    {
        private const float TrainDamageThresholdKmh = 7f;
        private const float TrainHitCooldownSeconds = 1.5f;
        private readonly Collider[] trainContactProbe = new Collider[24];
        private SurvivalRuntime runtime;
        private CustomFirstPersonController firstPerson;
        private CharacterController capsule;
        private bool wasGrounded;
        private bool trackingFall;
        private float fallPeakY;
        private float airborneSince;
        private float nextTrainHit;
        private float nextTrainContactProbe;
        private float nextRiskProbe;
        private readonly TrainProbeCadence probeCadence = new TrainProbeCadence();
        private Vector3 previousPosition;
        private int trainColliderMask;
        private TrainCar previousCar;
        private float lastCarSpeed, dismountSpeed, dismountedAt;
        private float suppressUntil;

        public void Initialize(SurvivalRuntime survivalRuntime,
            CustomFirstPersonController playerController = null)
        {
            runtime = survivalRuntime;
            firstPerson = playerController == null
                ? GetComponent<CustomFirstPersonController>()
                : playerController;
            capsule = GetComponent<CharacterController>();
            if (capsule == null && firstPerson != null)
                capsule = firstPerson.GetComponent<CharacterController>();
            var layer = LayerMask.NameToLayer("Train_Big_Collider");
            trainColliderMask = layer >= 0 ? 1 << layer : 1 << 10;
            ResetTracking();
        }

        public void ResetTracking()
        {
            var position = transform.position;
            previousPosition = position;
            fallPeakY = position.y;
            trackingFall = false;
            wasGrounded = capsule != null && capsule.isGrounded;
            previousCar = PlayerManager.Car;
            lastCarSpeed = previousCar == null ? 0f : previousCar.GetAbsSpeed() * 3.6f;
            dismountSpeed = 0f;
            suppressUntil = Time.realtimeSinceStartup + 1f;
            nextRiskProbe = nextTrainContactProbe = 0f;
            probeCadence.ObserveRisk(Time.realtimeSinceStartup);
        }

        private void Update()
        {
            if (runtime == null || capsule == null || !runtime.IsSessionReady) return;
            if (runtime.IsRespawning || runtime.IsHomeTravelPending || FastTravelController.IsFastTravelling ||
                LoadingScreenManager.IsLoading) { ResetTracking(); return; }
            var position = transform.position;
            if ((position - previousPosition).sqrMagnitude > 144f ||
                (firstPerson != null && (firstPerson.IsClimbingLadders || firstPerson.underwater)))
            {
                ResetTracking();
                return;
            }
            var grounded = capsule.isGrounded;
            var car = PlayerManager.Car;
            if (car != null)
            {
                previousCar = car;
                lastCarSpeed = car.GetAbsSpeed() * 3.6f;
                dismountSpeed = 0f;
            }
            else if (previousCar != null)
            {
                dismountSpeed = Time.realtimeSinceStartup >= suppressUntil ? lastCarSpeed : 0f;
                dismountedAt = Time.realtimeSinceStartup;
                probeCadence.ObserveRisk(dismountedAt);
                previousCar = null;
            }
            bool dismountHit = false;
            if (dismountSpeed > 5f && grounded && Time.realtimeSinceStartup - dismountedAt > 0.12f)
            {
                RaycastHit support;
                var onTrain = Physics.Raycast(transform.TransformPoint(capsule.center), Vector3.down,
                    out support, capsule.height * 0.5f + .3f, trainColliderMask, QueryTriggerInteraction.Ignore);
                if (!onTrain)
                    runtime.OnLocalTrauma(TraumaKind.TrainDismount, dismountSpeed, GetPlayerHeight());
                dismountSpeed = 0f;
                dismountHit = true;
                nextTrainHit = Time.realtimeSinceStartup + TrainHitCooldownSeconds;
            }
            if (Time.realtimeSinceStartup - dismountedAt > 10f) dismountSpeed = 0f;
            if (!grounded)
            {
                if (wasGrounded || !trackingFall)
                {
                    trackingFall = true;
                    airborneSince = Time.realtimeSinceStartup;
                    fallPeakY = position.y;
                }
                else if (position.y > fallPeakY)
                {
                    fallPeakY = position.y;
                }
            }
            else if (!wasGrounded && trackingFall)
            {
                var fallDistance = fallPeakY - position.y;
                var playerHeight = GetPlayerHeight();
                if (!dismountHit && Time.realtimeSinceStartup - airborneSince > 0.12f &&
                    fallDistance > playerHeight * 1.5f)
                    runtime.OnLocalTrauma(TraumaKind.Fall, fallDistance, playerHeight);
                trackingFall = false;
                fallPeakY = position.y;
            }
            else if (grounded)
            {
                fallPeakY = position.y;
            }
            wasGrounded = grounded;
            previousPosition = position;
            if (PlayerManager.Car == null && Time.realtimeSinceStartup >= nextTrainContactProbe)
            {
                var now = Time.realtimeSinceStartup;
                // A broad train-layer-only look-ahead wakes the exact probe BEFORE contact.
                // Callbacks alone cannot do this for all CharacterController/train combinations.
                if (now >= nextRiskProbe)
                {
                    nextRiskProbe = now + .5f;
                    using (new SurvivalProfiler("DVSurvival.Impact.RiskProbe"))
                        if (Physics.CheckSphere(transform.position, 40f, trainColliderMask,
                            QueryTriggerInteraction.Collide)) probeCadence.ObserveRisk(now);
                }
                nextTrainContactProbe = now + probeCadence.Interval(now);
                if (probeCadence.IsHighRisk(now)) ProbeTrainContact();
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit != null) TryReportTrainCollision(hit.collider);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision != null) TryReportTrainCollision(collision.collider);
        }

        private void OnTriggerEnter(Collider other)
        {
            TryReportTrainCollision(other);
        }

        private void ProbeTrainContact()
        {
            using (new SurvivalProfiler("DVSurvival.Impact.TrainProbe"))
            {
            if (capsule == null || Time.realtimeSinceStartup < nextTrainHit) return;
            var center = transform.TransformPoint(capsule.center);
            // CharacterController keeps a small separation from solid train colliders. The
            // slightly expanded capsule reaches that contact gap without becoming a proximity hit.
            var radius = Mathf.Max(0.22f, capsule.radius * 1.15f + 0.08f);
            var halfLine = Mathf.Max(0f, capsule.height * 0.5f - radius);
            var offset = transform.up * halfLine;
            var count = Physics.OverlapCapsuleNonAlloc(center + offset, center - offset, radius,
                trainContactProbe, trainColliderMask, QueryTriggerInteraction.Collide);
            for (var index = 0; index < count; index++)
            {
                var collider = trainContactProbe[index];
                trainContactProbe[index] = null;
                if (TryReportTrainCollision(collider)) break;
            }
            }
        }

        private bool TryReportTrainCollision(Collider collider)
        {
            if (runtime == null || !runtime.IsSessionReady || runtime.IsRespawning || collider == null ||
                Time.realtimeSinceStartup < nextTrainHit || dismountSpeed > 5f || previousCar != null ||
                Time.realtimeSinceStartup < suppressUntil)
                return false;
            var car = collider.GetComponentInParent<TrainCar>();
            if (car == null || car == PlayerManager.Car) return false;
            probeCadence.ObserveRisk(Time.realtimeSinceStartup);
            var speedKmh = car.GetAbsSpeed() * 3.6f;
            if (speedKmh <= TrainDamageThresholdKmh) return false;
            nextTrainHit = Time.realtimeSinceStartup + TrainHitCooldownSeconds;
            runtime.OnLocalTrauma(TraumaKind.TrainCollision, speedKmh, GetPlayerHeight());
            return true;
        }

        private float GetPlayerHeight()
        {
            var height = firstPerson != null ? firstPerson.CapsuleHeightNoVRCrouch :
                (capsule == null ? 1.8f : capsule.height);
            return Mathf.Clamp(height, 1.4f, 2.4f);
        }
    }
}
