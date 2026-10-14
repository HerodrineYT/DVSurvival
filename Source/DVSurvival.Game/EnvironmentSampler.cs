using System;
using System.Collections.Generic;
using DV;
using DV.CabControls;
using DV.InventorySystem;
using DV.Openables;
using DV.Simulation.Cars;
using DV.Simulation.Controllers;
using DV.ThingTypes;
using DV.Utils;
using DV.WeatherSystem;
using DVSurvival.Core;
using UnityEngine;

namespace DVSurvival.Mod
{
    internal sealed class EnvironmentSampler
    {
        private const float HeatedBuildingTemperature = 22f;
        private const float FireboxHeatRadiusMeters = 7f;
        private const string CabFanOutputPortId = "cabFan.OUTPUT";
        private readonly SurvivalModSettings settings;
        private readonly CabHeaterSwitchSystem cabHeaters;
        private readonly DvSeasonsTemperatureProvider seasons = new DvSeasonsTemperatureProvider();
        private readonly StationOfficeVolumes stationOffices = new StationOfficeVolumes();
        private readonly Dictionary<byte, Vector3> previousRemotePositions = new Dictionary<byte, Vector3>();
        private readonly Dictionary<byte, TrainCar> riddenCars = new Dictionary<byte, TrainCar>();
        private readonly SurvivalEnvironment localSample = new SurvivalEnvironment();
        private readonly SurvivalEnvironment remoteSample = new SurvivalEnvironment();
        private readonly HashSet<DoorsAndWindowsController> controllerScratch = new HashSet<DoorsAndWindowsController>();
        private readonly HashSet<OpenableControl> openingScratch = new HashSet<OpenableControl>();
        private readonly List<DoorsAndWindowsController> controllerList = new List<DoorsAndWindowsController>();
        private readonly List<OpenableControl> openingList = new List<OpenableControl>();
        private readonly List<ControlImplBase> controlList = new List<ControlImplBase>();
        private readonly GameObject[] cabRoots = new GameObject[4];
        private readonly Dictionary<Transform, bool> shopClassification = new Dictionary<Transform, bool>();
        private float shopClassificationExpires, roofExpires;
        private Vector3 roofPosition, roofWorldMove;
        private bool roofValue;
        public void InvalidateSpatialCache() { roofExpires = 0f; shopClassification.Clear(); }
        private readonly Collider[] buildingProbe = new Collider[64];
        private readonly Collider[] trainProbe = new Collider[48];
        private WeatherDriver weather;
        private float nextWeatherProbe;
        private Transform cachedPlayerTransform;
        private CustomFirstPersonController cachedPlayerController;
        private TrainCar cachedCar;
        private GameObject cachedInterior;
        private GameObject cachedExternal;
        private readonly DiscoveryRetry cabScanRetry = new DiscoveryRetry();
        private readonly List<DoorsAndWindowsController> cachedDoorsAndWindows = new List<DoorsAndWindowsController>();
        private FireboxSimController cachedFirebox;
        private EngineOnReader cachedEngineOn;
        private ControlImplBase cachedCabFanControl;
        private ControlImplBase cachedDm1uHeaterControl;
        private LocoSim.Implementations.SimulationFlow cachedClimateFlow;
        private LocoSim.Implementations.Port cachedRpmNormalized, cachedIdleRpmNormalized, cachedEngineOnPort;
        private LocoSim.Implementations.Port cachedCabFanOutputPort;
        private bool cachedCabScanComplete;
        private readonly List<OpenableControl> cachedOpenables = new List<OpenableControl>();
        private readonly List<ControlImplBase> cachedOpeningControls = new List<ControlImplBase>();
        private readonly Dictionary<TrainCar, CabHistory> cabHistory = new Dictionary<TrainCar, CabHistory>();
        private readonly CabinDiagnosticStatus cabinStatus = new CabinDiagnosticStatus();
        public string CabinStatus { get { return cabinStatus.Text; } }

        private sealed class CabHistory
        {
            public readonly CabinClimate Climate = new CabinClimate();
            public float LastSampleTime;
        }

        public EnvironmentSampler(SurvivalModSettings settings, CabHeaterSwitchSystem cabHeaters)
        {
            this.settings = settings;
            this.cabHeaters = cabHeaters ?? throw new ArgumentNullException(nameof(cabHeaters));
        }

        public SurvivalEnvironment SampleLocal(float gameHours)
        {
            cabinStatus.Clear();
            var transform = PlayerManager.PlayerTransform;
            if (transform == null) return Default(localSample, gameHours);
            var inCar = PlayerManager.Car != null;
            var inLocomotive = inCar && PlayerManager.Car.IsLoco;
            if (transform != cachedPlayerTransform)
            {
                cachedPlayerTransform = transform;
                cachedPlayerController = transform.GetComponent<CustomFirstPersonController>();
                if (cachedPlayerController == null)
                    cachedPlayerController = transform.GetComponentInChildren<CustomFirstPersonController>(true);
                if (cachedPlayerController == null)
                    cachedPlayerController = transform.GetComponentInParent<CustomFirstPersonController>();
            }
            var controller = cachedPlayerController;
            var position = controller == null ? transform.position : controller.transform.position;
            var activity = 0f;
            if (controller != null && controller.m_Input.sqrMagnitude > 0.01f)
                activity = controller.m_IsWalking ? 0.45f : 1f;
            var underwater = controller != null && controller.underwater;
            return Sample(localSample, position, gameHours, inCar, inLocomotive, activity, underwater, true);
        }

        public SurvivalEnvironment SampleRemote(SurvivalPlayerInfo player, float gameHours, float realSeconds)
        {
            if (player == null) return Default(remoteSample, gameHours);
            var position = new Vector3(player.PositionX, player.PositionY, player.PositionZ);
            var activity = 0f;
            Vector3 previous;
            if (previousRemotePositions.TryGetValue(player.PlayerId, out previous) && realSeconds > 0.01f && !player.IsOnCar)
            {
                var speed = Vector3.Distance(position, previous) / realSeconds;
                if (speed < 30f) activity = Mathf.Clamp01(speed / 7f);
            }
            previousRemotePositions[player.PlayerId] = position;
            return Sample(remoteSample, position, gameHours, player.IsOnCar, player.OccupiedCarIsLocomotive,
                activity, false, false);
        }

        public bool IsLocalNearShop()
        {
            return PlayerManager.PlayerTransform != null &&
                IsNear(PlayerManager.PlayerTransform.position, settings.ShopUseDistance, true);
        }

        public bool IsPlayerNearShop(SurvivalPlayerInfo player)
        {
            if (player == null) return false;
            return IsNear(new Vector3(player.PositionX, player.PositionY, player.PositionZ),
                settings.ShopUseDistance, true);
        }

        public bool IsPlayerNearBed(SurvivalPlayerInfo player)
        {
            if (player == null) return false;
            return IsNear(new Vector3(player.PositionX, player.PositionY, player.PositionZ),
                settings.BedValidationDistance, false);
        }

        public bool IsLocalNearBed()
        {
            return PlayerManager.PlayerTransform != null &&
                IsNear(PlayerManager.PlayerTransform.position, settings.BedValidationDistance, false);
        }

        public bool IsPlayerNearNativeBed(SurvivalPlayerInfo player, SurvivalActionRequest request)
        {
            if (player == null || !player.IsLoaded) return false;
            if (IsPlayerNearBed(player)) return true;
            // Buildings/cab interiors near a remote client need not be streamed on the host.
            // Check the native bed anchor against MP's authenticated player position instead
            // of accepting all requests when the host's local bed cache is empty.
            var absolute = new Vector3(player.PositionX, player.PositionY, player.PositionZ) - WorldMover.currentMove;
            return NativeBedValidation.IsNearReportedBed(request, absolute.x, absolute.y,
                absolute.z, settings.BedValidationDistance);
        }

        public bool IsPlayerNearMovingTrain(SurvivalPlayerInfo player, float minimumSpeedKmh)
        { return NearbyTrainSpeed(player) > minimumSpeedKmh; }

        public float NearbyTrainSpeed(SurvivalPlayerInfo player)
        {
            if (player == null) return 0f;
            var position = new Vector3(player.PositionX, player.PositionY, player.PositionZ);
            var count = Physics.OverlapSphereNonAlloc(position, 3.25f, trainProbe,
                TrainColliderLayerMask(), QueryTriggerInteraction.Collide);
            var speed = 0f;
            for (var index = 0; index < count; index++)
            {
                var collider = trainProbe[index];
                trainProbe[index] = null;
                if (collider == null) continue;
                var car = collider.GetComponentInParent<TrainCar>();
                if (car != null && (!player.IsOnCar || car.CarGUID == player.OccupiedCarId))
                    speed = Mathf.Max(speed, car.GetAbsSpeed() * 3.6f);
            }
            return speed;
        }

        public float OccupiedTrainSpeed(SurvivalPlayerInfo player)
        {
            if (player == null || !player.IsOnCar || string.IsNullOrEmpty(player.OccupiedCarId)) return 0f;
            TrainCar car;
            if (!riddenCars.TryGetValue(player.PlayerId, out car) || car == null || car.CarGUID != player.OccupiedCarId)
            {
                car = null;
                var spawner = CarSpawner.Instance;
                if (spawner != null && spawner.AllCars != null)
                    foreach (var candidate in spawner.AllCars)
                        if (candidate != null && candidate.CarGUID == player.OccupiedCarId) { car = candidate; break; }
                riddenCars[player.PlayerId] = car;
            }
            return car == null ? 0f : car.GetAbsSpeed() * 3.6f;
        }

        public double PlayerMoney
        {
            get
            {
                var inventory = SingletonBehaviour<Inventory>.Instance;
                return inventory == null ? 0d : inventory.PlayerMoney;
            }
        }

        public bool TrySpendMoney(int amount)
        {
            if (amount < 0) return false;
            var inventory = SingletonBehaviour<Inventory>.Instance;
            return inventory != null && inventory.RemoveMoney(amount);
        }

        public double DeductMoneyUpTo(int amount)
        {
            if (amount <= 0) return 0d;
            var inventory = SingletonBehaviour<Inventory>.Instance;
            if (inventory == null) return 0d;
            var deducted = Math.Min(amount, Math.Max(0d, inventory.PlayerMoney));
            if (deducted <= 0d) return 0d;
            if (deducted >= amount && inventory.RemoveMoney(amount)) return amount;
            return inventory.SetMoney(Math.Max(0d, inventory.PlayerMoney - deducted)) ? deducted : 0d;
        }

        public void RefundMoney(int amount)
        {
            if (amount <= 0) return;
            var inventory = SingletonBehaviour<Inventory>.Instance;
            if (inventory != null) inventory.AddMoney(amount);
        }

        public void ForgetPlayer(byte playerId)
        {
            riddenCars.Remove(playerId);
            previousRemotePositions.Remove(playerId);
        }

        public void Reset()
        {
            riddenCars.Clear();
            SurvivalWorldRegistry.Reset();
            InvalidateSpatialCache();
            FireboxRegistry.Reset();
            StationOfficeVolumes.Reset();
            weather = null;
            nextWeatherProbe = 0f;
            cachedPlayerTransform = null;
            cachedPlayerController = null;
            InvalidateCabControls();
            cabHistory.Clear();
            previousRemotePositions.Clear();
            seasons.Reset();
        }

        /// <summary>
        /// Invalidates only locomotive hierarchy/control references. Cabin thermal history is
        /// deliberately retained so reconnecting does not make the air temperature jump.
        /// </summary>
        public void InvalidateCabControls()
        {
            cabinStatus.Clear();
            cachedCar = null;
            cachedInterior = null;
            cachedExternal = null;
            cabScanRetry.Reset();
            cachedDoorsAndWindows.Clear();
            cachedFirebox = null;
            cachedEngineOn = null;
            cachedCabFanControl = null;
            cachedDm1uHeaterControl = null;
            cachedClimateFlow = null;
            cachedRpmNormalized = cachedIdleRpmNormalized = cachedEngineOnPort = null;
            cachedCabFanOutputPort = null;
            cachedCabScanComplete = false;
            cachedOpenables.Clear();
            cachedOpeningControls.Clear();
            controllerScratch.Clear(); openingScratch.Clear();
            controllerList.Clear(); openingList.Clear(); controlList.Clear();
            Array.Clear(cabRoots, 0, cabRoots.Length);
        }

        public float GetDayLengthMinutes()
        {
            ProbeWeather();
            try
            {
                if (weather != null) return Mathf.Clamp((float)weather.DayLengthInMinutes, 1f, 1440f);
            }
            catch
            {
                // Fall through to the game parameters.
            }
            try { return Mathf.Clamp(Globals.G.GameParams.DayLengthInMinutes, 1f, 1440f); }
            catch { return 144f; }
        }

        public bool IsGameTimeOverridden
        {
            get
            {
                ProbeWeather();
                try { return weather != null && weather.TimeOfDayHours.IsOverridden; }
                catch { return false; }
            }
        }

        public bool TryGetGameDateTime(out DateTime value)
        {
            value = default(DateTime);
            ProbeWeather();
            try
            {
                if (weather == null || weather.manager == null || weather.manager.todSky == null ||
                    weather.manager.todSky.Cycle == null)
                    return false;
                value = weather.manager.RealDateTime;
                return value != DateTime.MinValue && value != DateTime.MaxValue;
            }
            catch { return false; }
        }

        private SurvivalEnvironment Sample(SurvivalEnvironment target, Vector3 position, float gameHours, bool inCar,
            bool inLocomotive, float activity, bool underwater, bool isLocalPlayer)
        {
            using (new SurvivalProfiler("DVSurvival.Environment.Sample"))
            {
            ProbeWeather();
            var rain = weather == null ? 0f : Mathf.Clamp01(weather.RainValue.CurrentValue);
            var wind = weather == null ? 0f : Mathf.Clamp((float)weather.WindSpeed, 0f, 80f);
            if (inCar) roofExpires = 0f;
            var roof = inCar || (isLocalPlayer ? CachedRoof(position) : HasRoof(position));
            var exposure = inCar ? 0.10f : (roof ? 0.28f : 1f);
            var shelterWarmth = inCar ? 2.5f : 0f;
            bool isWinter;
            var ambient = GetAmbientTemperature(position, rain, out isWinter);
            var thermalRecovery = 1f;
            // Exact cab controls exist only in the local player's loaded interior. Remote clients
            // report their measured environment to the authoritative host separately.
            if (isLocalPlayer && inLocomotive && PlayerManager.Car != null)
            {
                ApplyLocomotiveClimate(PlayerManager.Car, position, ambient, ref ambient, ref rain,
                    ref wind, ref exposure, ref shelterWarmth, ref thermalRecovery);
            }
            else if (!inCar && stationOffices.Contains(BuildingProbePosition(position, isLocalPlayer)))
            {
                // A thermostat heats a cold office to 22 C; it does not provide
                // air conditioning when the outside air is already warmer.
                ambient = Mathf.Max(ambient, HeatedBuildingTemperature);
                rain = 0f;
                wind = 0f;
                exposure = 0.03f;
                shelterWarmth = 0f;
                thermalRecovery = 2.5f;
            }
            else if (!inCar && IsInsideHeatedShop(BuildingProbePosition(position, isLocalPlayer), roof))
            {
                ambient = HeatedBuildingTemperature;
                rain = 0f;
                wind = 0f;
                exposure = 0.03f;
                shelterWarmth = 0f;
                thermalRecovery = 2.5f;
            }
            else if (!inCar)
            {
                ApplyNearbyFireboxHeat(position, ref ambient);
            }
            if (underwater)
            {
                exposure = 1f;
                ambient = Math.Min(ambient, 8f);
                rain = 1f;
                thermalRecovery = 1f;
            }
            target.GameHours = gameHours;
            target.AmbientTemperatureCelsius = ambient;
            target.RainIntensity = rain;
            target.WindSpeedMetersPerSecond = wind;
            target.Exposure = exposure;
            target.Activity = Mathf.Clamp01(activity);
            target.ShelterWarmthCelsius = shelterWarmth;
            target.ThermalRecoveryMultiplier = thermalRecovery;
            target.IsWinter = isWinter;
            return target;
            }
        }

        private void ApplyLocomotiveClimate(TrainCar car, Vector3 position, float outside,
            ref float ambient, ref float rain, ref float wind, ref float exposure,
            ref float shelterWarmth, ref float thermalRecovery)
        {
            RefreshCabComponents(car);
            var open = false;
            for (int i = 0; !open && i < cachedDoorsAndWindows.Count; i++)
                try { open = cachedDoorsAndWindows[i] != null && cachedDoorsAndWindows[i].AnythingOpen(); }
                catch { /* Other initialized controllers/direct controls can still report openings. */ }
            // Direct cached controls also cover interiors whose controller has not initialized its entries.
            for (int i = 0; !open && i < cachedOpenables.Count; i++)
            {
                var control = cachedOpeningControls[i];
                var opening = cachedOpenables[i];
                if (control == null || opening == null) continue;
                open = opening.closedAtZero ? control.Value >= 0.1f : control.Value <= 0.9f;
            }
            var isDm1u = car != null && car.carType == TrainCarType.LocoDM1U;
            var isDm3 = car != null && car.carType == TrainCarType.LocoDM3;
            var isVanillaDieselHeater = IsVanillaDieselHeater(car);
            var fanValue = cachedCabFanControl == null ? 0f : cachedCabFanControl.Value;
            var fanOutputValue = cachedCabFanOutputPort == null ? 0f : cachedCabFanOutputPort.Value;
            var fanOn = !float.IsNaN(fanValue) && !float.IsInfinity(fanValue) &&
                (isDm1u ? fanValue >= 0.5f : fanValue > 0.1f);
            if (isVanillaDieselHeater && !isDm3)
                fanOn = !float.IsNaN(fanOutputValue) && !float.IsInfinity(fanOutputValue) &&
                    fanOutputValue > 0.1f;
            var heaterValue = cachedDm1uHeaterControl == null ? 0f : cachedDm1uHeaterControl.Value;
            var dm1uHeaterOn = isDm1u && !float.IsNaN(heaterValue) &&
                !float.IsInfinity(heaterValue) && heaterValue >= 0.5f;
            var heaterPower = isVanillaDieselHeater ? cabHeaters.GetLevel(car) : 1f;
            var vanillaDieselHeaterOn = isVanillaDieselHeater && heaterPower > 0f;
            float? steamTarget = null;
            // DM1U is diesel-only. Refuse any stray/custom FireboxSimController so it cannot
            // bypass the stock Heating Rotary fail-closed gate.
            if (!isDm1u && cachedFirebox != null)
            {
                var temperature = 0f;
                var hasTemperature = TryGetFireboxTemperature(car, out temperature);
                var heat = hasTemperature
                    ? Mathf.InverseLerp(80f, 1050f, temperature)
                    : Mathf.Max(cachedFirebox.CombustionRateNormalized,
                        cachedFirebox.NormalizedFireboxContents * 0.45f);
                if (!cachedFirebox.IsFireOn) heat *= 0.15f;
                var distanceFactor = Mathf.Clamp01(1f -
                    Vector3.Distance(position, cachedFirebox.transform.position) / FireboxHeatRadiusMeters);
                distanceFactor = Mathf.Max(0.38f, distanceFactor);
                var doorFactor = Mathf.Lerp(0.62f, 1.35f,
                    Mathf.Clamp01(cachedFirebox.FireboxDoorOpening));
                steamTarget = outside + heat * 38f * distanceFactor * doorFactor;
            }
            var running = false;
            var load = 0f;
            try
            {
                running = cachedEngineOnPort != null ? cachedEngineOnPort.Value > 0.5f :
                    cachedEngineOn != null && cachedEngineOn.IsOn;
                if (cachedRpmNormalized != null)
                {
                    var idle = cachedIdleRpmNormalized == null ? 0.25f : cachedIdleRpmNormalized.Value;
                    load = Mathf.InverseLerp(Mathf.Clamp(idle, 0f, 0.95f), 1f, cachedRpmNormalized.Value);
                    if (float.IsNaN(load) || float.IsInfinity(load)) load = 0f;
                }
            }
            catch { running = false; }
            CabHistory history;
            if (!cabHistory.TryGetValue(car, out history))
            {
                // Bounded cache, no scene searches and no per-frame dictionary allocations.
                if (cabHistory.Count >= 32) cabHistory.Clear();
                history = new CabHistory { LastSampleTime = Time.time };
                cabHistory.Add(car, history);
            }
            var elapsed = Mathf.Clamp(Time.time - history.LastSampleTime, 0f, 5f);
            history.LastSampleTime = Time.time;
            var heatingRateMultiplier = isDm1u && !fanOn ? 0.25f : isDm3 ? heaterPower : 1f;
            var heaterAllowsEngineHeat = isDm1u ? dm1uHeaterOn :
                !isVanillaDieselHeater || vanillaDieselHeaterOn;
            // A second adapter-level gate ensures DM1U engine heat begins only after the
            // physical heater is enabled; CabinClimate independently enforces the same rule.
            ambient = history.Climate.Advance(elapsed, outside, running && heaterAllowsEngineHeat,
                load, open, fanOn, steamTarget, heatingRateMultiplier, heaterAllowsEngineHeat, heaterPower);
            float sharedCabAir = settings.UseDvSeasonsTemperature && SeasonsCabHeating.Active
                ? SeasonsCabHeating.GetCabinTemperature(car) : float.NaN;
            if (!float.IsNaN(sharedCabAir) && !float.IsInfinity(sharedCabAir)) ambient = sharedCabAir;
            cabinStatus.Capture(open, cachedOpenables.Count, isDm1u, isVanillaDieselHeater,
                isDm1u ? dm1uHeaterOn : vanillaDieselHeaterOn, fanOn, running, outside);
            ambient = Mathf.Clamp(ambient, SurvivalEnvironment.MinimumAirTemperature, SurvivalEnvironment.MaximumAirTemperature);
            rain *= open ? 0.22f : 0f;
            wind *= open ? 0.65f : 0f;
            exposure = open ? 0.68f : 0.05f;
            shelterWarmth = 0f;
            thermalRecovery = open ? 1.15f : 2.15f;
        }

        private void RefreshCabComponents(TrainCar car)
        {
            using (new SurvivalProfiler("DVSurvival.Environment.CabDiscovery"))
            {
            var interior = car == null ? null : car.loadedInterior;
            var external = car == null ? null : car.loadedExternalInteractables;
            var flow = car == null || car.SimController == null ? null : car.SimController.SimulationFlow;
            var isVanillaDieselHeater = IsVanillaDieselHeater(car);
            var heaterControlReady = !isVanillaDieselHeater || interior == null;
            var sameContext = car == cachedCar && interior == cachedInterior && external == cachedExternal &&
                flow == cachedClimateFlow;
            if (sameContext && (cachedCabScanComplete || Time.realtimeSinceStartup < cabScanRetry.NextAttempt)) return;
            if (!sameContext) cabScanRetry.Reset();
            cabScanRetry.RecordAttempt(Time.realtimeSinceStartup);
            cachedCar = car;
            cachedInterior = interior;
            cachedExternal = external;
            if (isVanillaDieselHeater && interior != null)
            {
                heaterControlReady = cabHeaters.Ensure(car);
                if (!heaterControlReady) heaterControlReady = cabHeaters.IsSetupSettled(car);
            }
            // Persistent cabin and external door objects are not necessarily children of loadedInterior.
            var controllers = controllerScratch;
            controllers.Clear();
            var openings = openingScratch;
            openings.Clear();
            cabRoots[0] = car == null ? null : car.gameObject; cabRoots[1] = interior; cabRoots[2] = external;
            cabRoots[3] = car == null || car.interior == null ? null : car.interior.gameObject;
            var roots = cabRoots;
            foreach (var root in roots)
            {
                if (root == null) continue;
                root.GetComponentsInChildren(true, controllerList);
                foreach (var value in controllerList)
                {
                    controllers.Add(value);
                    if (value.entries != null)
                        foreach (var opening in value.entries) if (opening != null) openings.Add(opening);
                }
                root.GetComponentsInChildren(true, openingList);
                foreach (var opening in openingList) openings.Add(opening);
            }
            cachedDoorsAndWindows.Clear();
            foreach (var value in controllers) cachedDoorsAndWindows.Add(value);
            cachedFirebox = car == null ? null : car.GetComponentInChildren<FireboxSimController>(true);
            cachedEngineOn = car == null ? null : car.GetComponentInChildren<EngineOnReader>(true);
            cachedClimateFlow = flow;
            cachedRpmNormalized = cachedIdleRpmNormalized = cachedEngineOnPort = null;
            cachedCabFanOutputPort = null;
            cachedCabScanComplete = false;
            if (flow != null)
            {
                // Build 99 exposes these ports on DE2, DH4 and DE6. TryGetPort's final
                // argument only suppresses an empty-id diagnostic; it does not make a
                // missing non-empty port silent, so never probe unrelated locomotives.
                if (isVanillaDieselHeater && car.carType != TrainCarType.LocoDM3)
                {
                    flow.TryGetPort(CabFanOutputPortId, out cachedCabFanOutputPort, false);
                }
                foreach (var component in flow.OrderedSimComps)
                {
                    var direct = component as LocoSim.Implementations.DieselEngineDirect;
                    var power = component as LocoSim.Implementations.DieselEnginePowerSource;
                    var drive = component as LocoSim.Implementations.DieselEngineDirectDrive;
                    if (direct != null)
                    {
                        cachedRpmNormalized = direct.engineRpmNormalizedReadOut;
                        cachedIdleRpmNormalized = direct.engineIdleRpmNormalizedReadOut;
                        cachedEngineOnPort = direct.engineOnReadOut;
                    }
                    else if (power != null)
                    {
                        cachedRpmNormalized = power.engineRpmNormalizedReadOut;
                        cachedIdleRpmNormalized = power.engineIdleRpmNormalizedReadOut;
                        cachedEngineOnPort = power.engineOnReadOut;
                    }
                    else if (drive != null)
                    {
                        cachedRpmNormalized = drive.engineRpmNormalizedReadOut;
                        cachedIdleRpmNormalized = drive.engineIdleRpmNormalizedReadOut;
                        cachedEngineOnPort = drive.engineOnReadOut;
                    }
                    if (cachedEngineOnPort != null) break;
                }
            }
            cachedOpenables.Clear(); cachedOpeningControls.Clear();
            foreach (var opening in openings)
            {
                cachedOpenables.Add(opening);
                cachedOpeningControls.Add(opening.GetComponent<ControlImplBase>());
            }
            cachedCabFanControl = null;
            cachedDm1uHeaterControl = null;
            if (interior == null) return;
            interior.GetComponentsInChildren(true, controlList);
            foreach (var control in controlList)
            {
                if (control == null) continue;
                if (cachedCabFanControl == null && IsCabFanName(control.transform))
                    cachedCabFanControl = control;
                if (car != null && car.carType == TrainCarType.LocoDM1U &&
                    cachedDm1uHeaterControl == null && IsDm1uHeaterName(control.transform))
                    cachedDm1uHeaterControl = control;
                if (cachedCabFanControl != null && cachedDm1uHeaterControl != null) break;
            }
            var isDm1u = car != null && car.carType == TrainCarType.LocoDM1U;
            cachedCabScanComplete = isDm1u
                ? cachedCabFanControl != null && cachedDm1uHeaterControl != null
                // Successful setup and a bounded final heater failure stop hierarchy scans;
                // transient failures retain quick bootstrap retries then a slow recovery path.
                : heaterControlReady;
            }
        }

        private static bool TryGetFireboxTemperature(TrainCar car, out float temperature)
        {
            temperature = 0f;
            try
            {
                if (car == null || car.SimController == null || car.SimController.SimulationFlow == null)
                    return false;
                LocoSim.Implementations.Port port;
                if (!car.SimController.SimulationFlow.TryGetPort("firebox.TEMPERATURE", out port, false) ||
                    port == null)
                    return false;
                temperature = port.Value;
                return !float.IsNaN(temperature) && !float.IsInfinity(temperature) && temperature >= 0f;
            }
            catch { return false; }
        }

        private void ApplyNearbyFireboxHeat(Vector3 position, ref float ambient)
        {
            using (new SurvivalProfiler("DVSurvival.Environment.Firebox"))
            {
            var best = ambient;
            foreach (var firebox in FireboxRegistry.GetLoaded())
            {
                if (firebox == null || !firebox.gameObject.activeInHierarchy) continue;
                var squaredDistance = (position - firebox.transform.position).sqrMagnitude;
                if (squaredDistance >= FireboxHeatRadiusMeters * FireboxHeatRadiusMeters) continue;
                var distanceFactor = 1f - Mathf.Sqrt(squaredDistance) / FireboxHeatRadiusMeters;
                if (distanceFactor <= 0f) continue;
                try
                {
                    // Init may have just failed on a third-party car; do not let its
                    // unbound ports prevent sampling other nearby heat sources.
                    if (!firebox.IsFireOn) continue;
                    var heat = Mathf.Max(firebox.CombustionRateNormalized,
                        firebox.NormalizedFireboxContents * 0.45f);
                    var doorFactor = Mathf.Lerp(0.35f, 1.25f,
                        Mathf.Clamp01(firebox.FireboxDoorOpening));
                    best = Mathf.Max(best, ambient + heat * 24f * distanceFactor * doorFactor);
                }
                catch (NullReferenceException) { }
            }
            ambient = Mathf.Clamp(best, SurvivalEnvironment.MinimumAirTemperature, SurvivalEnvironment.MaximumAirTemperature);
            }
        }

        private bool IsInsideHeatedShop(Vector3 position, bool hasRoof)
        {
            using (new SurvivalProfiler("DVSurvival.Environment.ShopProbe"))
            {
            // Shop interiors use their existing collision/proximity detection.
            // Station offices are handled by their exact native room volumes.
            var count = Physics.OverlapSphereNonAlloc(position, 0.2f, buildingProbe,
                ~0, QueryTriggerInteraction.Collide);
            for (var index = 0; index < count; index++)
            {
                var collider = buildingProbe[index];
                buildingProbe[index] = null;
                if (collider != null && IsCachedHeatedShop(collider.transform) &&
                    (collider.ClosestPoint(position) - position).sqrMagnitude < 0.0001f)
                    return true;
            }
            // Shops do not all have a dedicated interior trigger. Keep the proximity fallback,
            // but require a roof so the heated zone cannot leak into the street.
            return hasRoof && IsNear(position, 22f, true);
            }
        }

        private static Vector3 BuildingProbePosition(Vector3 fallback, bool isLocalPlayer)
        {
            if (isLocalPlayer && PlayerManager.PlayerCamera != null)
                return PlayerManager.PlayerCamera.transform.position;
            // Remote player reports normally supersede host-side sampling. This offset keeps the
            // fallback at head/chest height, matching the local camera containment check.
            return fallback + Vector3.up * 1.55f;
        }

        private static int TrainColliderLayerMask()
        {
            var layer = LayerMask.NameToLayer("Train_Big_Collider");
            return layer >= 0 ? 1 << layer : 1 << 10;
        }

        private static bool IsHeatedShopName(Transform transform)
        {
            for (var depth = 0; transform != null && depth < 7; depth++, transform = transform.parent)
            {
                var name = transform.name;
                if (NormalizedName.Contains(name, "shopinterior"))
                    return true;
            }
            return false;
        }

        private static bool IsCabFanName(Transform transform)
        {
            for (var depth = 0; transform != null && depth < 4; depth++, transform = transform.parent)
            {
                var name = transform.name;
                if (NormalizedName.Contains(name, "cabfan") || NormalizedName.Contains(name, "fanswitch")) return true;
            }
            return false;
        }

        private static bool IsDm1uHeaterName(Transform transform)
        {
            // The stock DM1U control hierarchy is RightCluster/Heating Rotary/C_Heating Rotary.
            // Match that unique control rather than other locomotives' decorative CabHeater labels.
            for (var depth = 0; transform != null && depth < 4; depth++, transform = transform.parent)
            {
                var name = transform.name;
                if (NormalizedName.Contains(name, "cheatingrotary", true) || NormalizedName.Contains(name, "heatingrotary", true)) return true;
            }
            return false;
        }

        private static bool IsVanillaDieselHeater(TrainCar car)
        {
            if (car == null) return false;
            return car.carType == TrainCarType.LocoShunter ||
                car.carType == TrainCarType.LocoDH4 ||
                car.carType == TrainCarType.LocoDM3 ||
                car.carType == TrainCarType.LocoDiesel;
        }

        private bool IsCachedHeatedShop(Transform transform)
        {
            if (Time.realtimeSinceStartup >= shopClassificationExpires || shopClassification.Count >= 2048)
            { shopClassification.Clear(); shopClassificationExpires = Time.realtimeSinceStartup + 30f; }
            bool result;
            if (!shopClassification.TryGetValue(transform, out result))
            { result = IsHeatedShopName(transform); shopClassification[transform] = result; }
            return result;
        }

        private bool CachedRoof(Vector3 position)
        {
            var now = Time.realtimeSinceStartup;
            if (now >= roofExpires || (position - roofPosition).sqrMagnitude > .04f || roofWorldMove != WorldMover.currentMove)
            {
                roofValue = HasRoof(position); roofPosition = position;
                roofWorldMove = WorldMover.currentMove; roofExpires = now + .9f;
            }
            return roofValue;
        }

        private float GetAmbientTemperature(Vector3 position, float rain, out bool isWinter)
        {
            var seasonal = 18f;
            var fromSeasons = settings.UseDvSeasonsTemperature && seasons.TryGetTemperature(out seasonal);
            var date = DateTime.Now;
            try
            {
                if (weather != null && weather.manager != null) date = weather.manager.DateTime;
            }
            catch
            {
                // Use the system clock only as a last-resort fallback.
            }
            if (!fromSeasons || !seasons.TryGetWinter(out isWinter))
                isWinter = date.Month == 12 || date.Month <= 2;
            // Seasons 0.3.13+ already includes live weather and the daily cycle.
            if (fromSeasons && seasons.IncludesWeather) return seasonal;
            if (!fromSeasons)
            {
                var yearPhase = 2d * Math.PI * (date.DayOfYear - 172d) / 365.25d;
                seasonal = 11f + 10f * (float)Math.Cos(yearPhase);
            }
            var hour = date.TimeOfDay.TotalHours;
            var diurnal = 3.8f * (float)Math.Sin(2d * Math.PI * (hour - 9d) / 24d);
            var altitudeCooling = Mathf.Clamp(-(position.y - 200f) * 0.0035f, -5f, 2f);
            return Mathf.Clamp(seasonal + diurnal + altitudeCooling - rain * 2.5f,
                SurvivalEnvironment.MinimumAirTemperature, SurvivalEnvironment.MaximumAirTemperature);
        }

        private bool IsNear(Vector3 position, float distance, bool shop)
        {
            var maxSqr = distance * distance;
            if (shop)
            {
                foreach (var value in SurvivalWorldRegistry.Shops)
                    if (value != null && value.gameObject.activeInHierarchy && (value.transform.position - position).sqrMagnitude <= maxSqr) return true;
            }
            else
            {
                foreach (var value in SurvivalWorldRegistry.Beds)
                    if (value != null && value.gameObject.activeInHierarchy && ((value.transform.position - position).sqrMagnitude <= maxSqr ||
                        (value.pillowTarget != null && (value.pillowTarget.position - position).sqrMagnitude <= maxSqr))) return true;
            }
            return false;
        }

        private void ProbeWeather()
        {
            if (weather != null || Time.realtimeSinceStartup < nextWeatherProbe) return;
            nextWeatherProbe = Time.realtimeSinceStartup + 5f;
            weather = UnityEngine.Object.FindObjectOfType<WeatherDriver>();
        }

        private static bool HasRoof(Vector3 position)
        {
            RaycastHit hit;
            return Physics.Raycast(position + Vector3.up * 0.25f, Vector3.up, out hit, 15f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private static SurvivalEnvironment Default(SurvivalEnvironment target, float gameHours)
        {
            target.GameHours = gameHours; target.AmbientTemperatureCelsius = 18f;
            target.RainIntensity = target.WindSpeedMetersPerSecond = target.Activity = target.ShelterWarmthCelsius = 0f;
            target.Exposure = .5f; target.ThermalRecoveryMultiplier = 1f; target.IsWinter = false;
            return target;
        }
    }
}
