using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using AutomaticRoadblocks.AbstractionLayer;
using AutomaticRoadblocks.Barriers;
using AutomaticRoadblocks.Instances;
using AutomaticRoadblocks.LightSources;
using AutomaticRoadblocks.Lspdfr;
using AutomaticRoadblocks.Roadblock.Slot;
using AutomaticRoadblocks.Street.Info;
using AutomaticRoadblocks.Utils;
using AutomaticRoadblocks.Utils.Type;
using Rage;

namespace AutomaticRoadblocks.Roadblock
{
    /// <summary>
    /// The abstract basic implementation of <see cref="IRoadblock"/> which can be used for manual and pursuit roadblocks.
    /// This implementation does not verify any states, use <see cref="AbstractPursuitRoadblock"/> instead.
    /// <remarks>Make sure that the <see cref="Initialize"/> method is called within the constructor after all properties/fields are set for the roadblock.</remarks>
    /// </summary>
    public abstract class AbstractRoadblock : IRoadblock
    {
        protected const float LaneHeadingTolerance = 45f;
        protected const int BlipFlashDuration = 3500;
        protected const float AdditionalClippingSpace = 0.5f;

        protected static readonly ILogger Logger = IoC.Instance.GetInstance<ILogger>();
        protected static readonly IGame Game = IoC.Instance.GetInstance<IGame>();

        protected Blip Blip;

        /// <summary>
        /// Initialize a new roadblock instance.
        /// </summary>
        /// <param name="street">The road of that the roadblock will block.</param>
        /// <param name="mainBarrier">The main barrier used within the slots.</param>
        /// <param name="secondaryBarrier">The secondary barrier used within the slots.</param>
        /// <param name="targetMatchingHeading">The target heading in which the roadblock should be placed.</param>
        /// <param name="lightSources">The light sources to place for this roadblock.</param>
        /// <param name="flags">The roadblock configuration.</param>
        /// <param name="offset">The offset placement in regards to the road node.</param>
        internal AbstractRoadblock(Road street, BarrierModel mainBarrier, BarrierModel secondaryBarrier, float targetMatchingHeading,
            List<LightModel> lightSources,
            ERoadblockFlags flags, float offset = 0f)
        {
            Assert.NotNull(street, "road cannot be null");
            Assert.NotNull(mainBarrier, "mainBarrierType cannot be null");
            Assert.NotNull(secondaryBarrier, "secondaryBarrier cannot be null");
            Assert.NotNull(lightSources, "lightSources cannot be null");
            Road = street;
            MainBarrier = mainBarrier;
            SecondaryBarrier = secondaryBarrier;
            TargetHeading = targetMatchingHeading;
            LightSources = lightSources;
            Flags = flags;
            Offset = offset;
        }

        #region Properties

        /// <inheritdoc />
        public uint LastStateChange { get; private set; }

        /// <summary>
        /// Get the level of the roadblock.
        /// </summary>
        public abstract ERoadblockLevel Level { get; }

        /// <inheritdoc />
        public ERoadblockState State { get; private set; } = ERoadblockState.Preparing;

        /// <inheritdoc />
        public ERoadblockFlags Flags { get; }

        /// <inheritdoc />
        public int NumberOfSlots => Slots?.Count ?? 0;

        /// <inheritdoc />
        public Vector3 Position => Road.Position;

        /// <summary>
        /// The placement offset in regards to the node.
        /// </summary>
        public float Offset { get; }

        /// <summary>
        /// The offset position for the roadblock.
        /// </summary>
        public Vector3 OffsetPosition => Road.Position + MathHelper.ConvertHeadingToDirection(Road.Node.Heading) * Offset;

        /// <inheritdoc />
        public float Heading { get; private set; }

        /// <inheritdoc />
        public Road Road { get; }

        /// <summary>
        /// Get the main barrier type of the roadblock.
        /// </summary>
        protected BarrierModel MainBarrier { get; }

        /// <summary>
        /// Get the secondary barrier type of the roadblock.
        /// </summary>
        protected BarrierModel SecondaryBarrier { get; }

        /// <summary>
        /// Get the target heading of the roadblock.
        /// </summary>
        protected float TargetHeading { get; }

        /// <summary>
        /// The light sources of the roadblock.
        /// </summary>
        protected List<LightModel> LightSources { get; }

        /// <summary>
        /// Get the generated slots for this roadblock.
        /// </summary>
        protected IReadOnlyList<IRoadblockSlot> Slots { get; private set; }

        /// <summary>
        /// Get the scenery slots for this roadblock.
        /// </summary>
        protected List<IARInstance<Entity>> Instances { get; } = new();

        /// <summary>
        /// The speed zone limit to apply when flag <see cref="ERoadblockFlags.SlowTraffic"/> is set.
        /// </summary>
        protected virtual float SpeedZoneLimit => 5f;

        /// <summary>
        /// The speed zone for this roadblock.
        /// </summary>
        private ARSpeedZone SpeedZone { get; set; }

        #endregion

        #region Events

        /// <inheritdoc />
        public event RoadblockEvents.RoadblockStateChanged RoadblockStateChanged;

        /// <inheritdoc />
        public event RoadblockEvents.RoadblockCopKilled RoadblockCopKilled;

        /// <inheritdoc />
        public event RoadblockEvents.RoadblockCopsJoiningPursuit RoadblockCopsJoiningPursuit;

        #endregion

        #region IPreviewSupport

        /// <inheritdoc />
        public bool IsPreviewActive => Instances.Any(x => x.IsPreviewActive) ||
                                       Slots.Any(x => x.IsPreviewActive);

        /// <inheritdoc />
        public void CreatePreview()
        {
            if (IsPreviewActive)
                return;

            Logger.Trace($"Creating roadblock preview for {this}");
            CreateBlip();
            Logger.Debug($"Creating a total of {Slots.Count} slot previews for the roadblock preview");
            foreach (var roadblockSlot in Slots)
            {
                roadblockSlot.CreatePreview();
            }

            Road.CreatePreview();
            SpeedZone.CreatePreview();
            Instances.ForEach(x => x.CreatePreview());
            DoInternalDebugPreviewCreation();
        }

        /// <inheritdoc />
        public void DeletePreview()
        {
            if (!IsPreviewActive)
                return;

            DeleteBlip();
            Slots.ToList().ForEach(x => x.DeletePreview());
            Road.DeletePreview();
            SpeedZone.DeletePreview();
            Instances.ForEach(x => x.DeletePreview());
        }

        #endregion

        #region IDisposable

        /// <inheritdoc />
        public void Dispose()
        {
            UpdateState(ERoadblockState.Disposing);

            DeletePreview();
            DeleteBlip();
            SpeedZone.Dispose();
            Slots.ToList().ForEach(x => x.Dispose());
            Instances.ForEach(x => x.Dispose());

            UpdateState(ERoadblockState.Disposed);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Spawn the roadblock in the world.
        /// </summary>
        public virtual bool Spawn()
        {
            try
            {
                DeletePreview();

                // check if a speed zone needs to be created
                if (Flags.HasFlag(ERoadblockFlags.SlowTraffic))
                    SpeedZone.Spawn();

                Slots.ToList().ForEach(SpawnSlot);
                UpdateState(ERoadblockState.Active);

                CreateBlip();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to spawn roadblock, {ex.Message}", ex);
                UpdateState(ERoadblockState.Error);
            }

            return false;
        }

        /// <inheritdoc />
        public virtual void Release(bool releaseAll = false)
        {
            // verify if the roadblock is still active
            // otherwise, we cannot release the entities
            if (State != ERoadblockState.Active)
            {
                Logger.Trace($"Unable to release roadblock instance, instance is not active for {this}");
                return;
            }

            Logger.Trace($"Releasing roadblock instance {this}");
            InvokeCopsJoiningPursuit(releaseAll);
            ReleaseEntities(releaseAll);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return
                $"{nameof(Level)}: {Level}, {nameof(State)}: {State}, {nameof(Flags)}: {Flags}, {nameof(LightSources)}: [{string.Join(", ", LightSources)}], " +
                $"Number of {nameof(Slots)}: [{Slots.Count}]\n" +
                $"--- {nameof(Slots)} ---\n" +
                $"{string.Join("\n", Slots)}\n" +
                $"--- {nameof(Road)} ---\n" +
                $"{Road}";
        }

        #endregion

        #region Functions

        /// <summary>
        /// Initialize the scenery slots of this roadblock.
        /// </summary>
        protected abstract void InitializeScenery();

        /// <summary>
        /// Initialize the light slots of this roadblock.
        /// </summary>
        protected abstract void InitializeLights();

        /// <summary>
        /// Initialize additional vehicles for this roadblock.
        /// Additional vehicles can exists out of emergency service such as EMS, etc.
        /// </summary>
        protected virtual void InitializeAdditionalVehicles()
        {
            // no-op
        }

        /// <summary>
        /// Retrieve the lanes of the road which should be blocked.
        /// </summary>
        /// <returns>Returns the lanes to block.</returns>
        protected virtual IReadOnlyList<Road.Lane> LanesToBlock()
        {
            // filter any lanes which are to close to each other
            return FilterLanesWhichAreTooCloseToEachOther(Road.Lanes);
        }

        /// <summary>
        /// Delete the blip of the roadblock.
        /// </summary>
        protected void DeleteBlip()
        {
            if (Blip == null)
                return;

            Blip.Delete();
            Blip = null;
        }

        /// <summary>
        /// Update the state of the roadblock.
        /// </summary>
        /// <param name="state">The new state of the roadblock.</param>
        protected void UpdateState(ERoadblockState state)
        {
            if (State == state)
                return;

            LastStateChange = Game.GameTime;
            State = state;
            RoadblockStateChanged?.Invoke(this, state);
        }

        /// <summary>
        /// Create roadblock slots for the given lanes.
        /// </summary>
        /// <param name="lanesToBlock">The lanes to block.</param>
        /// <returns>Returns a list of created slots.</returns>
        protected abstract IReadOnlyList<IRoadblockSlot> CreateRoadblockSlots(IReadOnlyList<Road.Lane> lanesToBlock);

        /// <summary>
        /// Initialize the roadblock data.
        /// This method will calculate the slots and create all necessary entities for the roadblock.
        /// </summary>
        protected void Initialize()
        {
            InitializeRoadblockSlots();
            InitializeAdditionalVehicles();
            InitializeScenery();

            if (Flags.HasFlag(ERoadblockFlags.EnableLights))
                InitializeLights();
            
            SpeedZone = new ARSpeedZone(OffsetPosition, Road.Width, SpeedZoneLimit);
        }

        /// <summary>
        /// Indicate that a cop from the given roadblock slot was killed.
        /// </summary>
        protected void InvokeRoadblockCopKilled()
        {
            RoadblockCopKilled?.Invoke(this);
        }

        /// <summary>
        /// Indicate that the cops of this roadblock can join the pursuit.
        /// </summary>
        /// <param name="releaseAll"></param>
        protected void InvokeCopsJoiningPursuit(bool releaseAll)
        {
            switch (State)
            {
                case ERoadblockState.Bypassed when !Flags.HasFlag(ERoadblockFlags.JoinPursuitOnBypass):
                case ERoadblockState.Hit when !Flags.HasFlag(ERoadblockFlags.JoinPursuitOnHit):
                    return;
                default:
                    RoadblockCopsJoiningPursuit?.Invoke(this, RetrieveCopsJoiningThePursuit(releaseAll));
                    break;
            }
        }

        /// <summary>
        /// Retrieve a list of cops who will join the pursuit.
        /// </summary>
        /// <param name="releaseAll"></param>
        /// <returns>Returns the cops joining the pursuit.</returns>
        protected virtual IList<Ped> RetrieveCopsJoiningThePursuit(bool releaseAll)
        {
            var copsJoining = new List<Ped>();

            if (IsAllowedToJoinPursuit() || releaseAll)
            {
                foreach (var slot in Slots)
                {
                    try
                    {
                        var slotCops = releaseAll ? slot.Cops : slot.CopsJoiningThePursuit;
                        copsJoining.AddRange(slotCops.Select(x => x.GameInstance));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to retrieve slot cops joining the pursuit for {slot}, {ex.Message}", ex);
                        Game.DisplayNotificationDebug("~r~Cops are unable to join the pursuit");
                    }
                }

                Logger.Debug($"A total of {copsJoining.Count} cops are joining the pursuit for {this}");
            }
            else
            {
                Logger.Debug($"Cops are not allowed to join pursuit for {this}");
            }

            return copsJoining;
        }

        /// <summary>
        /// Verify if the cops are allowed to join the pursuit.
        /// </summary>
        /// <returns>Returns true if the cops are allowed to join.</returns>
        protected bool IsAllowedToJoinPursuit()
        {
            return Flags.HasFlag(ERoadblockFlags.JoinPursuit) ||
                   (Flags.HasFlag(ERoadblockFlags.JoinPursuitOnBypass) && State == ERoadblockState.Bypassed) ||
                   (Flags.HasFlag(ERoadblockFlags.JoinPursuitOnHit) && State == ERoadblockState.Hit) ||
                   IsPreviewActive;
        }

        private void InitializeRoadblockSlots()
        {
            Heading = Road.Lanes
                .Select(x => x.Heading)
                .Where(x => Math.Abs(x - TargetHeading) < LaneHeadingTolerance)
                .DefaultIfEmpty(Road.Lanes[0].Heading)
                .First();
            var lanesToBlock = LanesToBlock();

            if (lanesToBlock.Count == 0)
            {
                Logger.Warn("Lanes to block returned 0 lanes, resetting and using all road lanes instead");
                lanesToBlock = Road.Lanes;
            }

            Logger.Trace($"Roadblock will block {lanesToBlock.Count} lanes");
            Slots = CreateRoadblockSlots(lanesToBlock);
            PreventSlotVehiclesClipping();
        }

        private void CreateBlip()
        {
            if (Blip != null)
                return;

            Logger.Trace($"Creating roadblock blip at {OffsetPosition}");
            Blip = new Blip(OffsetPosition)
            {
                IsRouteEnabled = false,
                IsFriendly = true,
                Scale = 1f,
                Color = Color.LightBlue
            };
        }

        private IReadOnlyList<Road.Lane> FilterLanesWhichAreTooCloseToEachOther(IReadOnlyList<Road.Lane> lanesToBlock)
        {
            Road.Lane lastLane = null;

            Logger.Trace("Filtering lanes which are too close to each-other");
            var filteredLanesToBlock = lanesToBlock.Where(x =>
                {
                    var result = true;

                    if (lastLane != null)
                        result = x.Position.DistanceTo(lastLane.Position) >= 4f;

                    lastLane = x;
                    return result;
                })
                .ToList();
            Logger.Debug($"Filtered a total of {lanesToBlock.Count - filteredLanesToBlock.Count} lanes which are too close to each other");

            if (filteredLanesToBlock.Count != 0)
                return filteredLanesToBlock;

            Logger.Warn("Lanes too close filter has filtered out all lanes, resetting to original list");
            return lanesToBlock.ToList();
        }

        private void PreventSlotVehiclesClipping()
        {
            for (var i = 0; i < Slots.Count - 1; i++)
            {
                var currentSlot = Slots[i];
                var nextSlot = Slots[i + 1];

                // if the current slot doesn't contain any vehicle
                // skip the clipping calculation and move to the next one
                if (currentSlot.BackupType == EBackupUnit.None)
                    continue;

                var currentSlotDifference = CalculateSlotVehicleDifference(currentSlot);
                var nextSlotDifference = CalculateSlotVehicleDifference(nextSlot);

                // verify if the slot difference is smaller than 0
                // this means that the vehicle is exceeding the lane width and might clip into the other vehicle
                if (currentSlotDifference > 0)
                    continue;

                Logger.Trace($"Current slot vehicle is exceeding by {currentSlotDifference}");
                // check if there is enough space between this lane and the other one
                // if so, we're using the next lane space for the current exceeding slot vehicle
                if (nextSlotDifference > 0 && nextSlotDifference - Math.Abs(currentSlotDifference) >= 0)
                {
                    Logger.Trace($"Next slot had enough space ({nextSlotDifference}) for the current exceeding slot vehicle");
                    continue;
                }

                // move the current slot vehicle position by the difference
                var newPosition = currentSlot.OffsetPosition + MathHelper.ConvertHeadingToDirection(Heading - 90) *
                    (Math.Abs(currentSlotDifference) + AdditionalClippingSpace);
                Logger.Debug(
                    $"Slot vehicle is clipping into next slot by ({currentSlotDifference}), old position {currentSlot.OffsetPosition}, new position {newPosition}");
                currentSlot.ModifyVehiclePosition(newPosition);
            }
        }

        private float CalculateSlotVehicleDifference(IRoadblockSlot slot)
        {
            var laneWidth = slot.Lane.Width;
            var vehicleLength = slot.VehicleLength;

            return laneWidth - vehicleLength;
        }

        private void ReleaseEntities(bool releaseAll)
        {
            if (IsAllowedToJoinPursuit() || releaseAll)
            {
                Logger.Trace($"Releasing slot cops to LSPDFR for roadblock {this}");
                foreach (var slot in Slots)
                {
                    slot.Release(releaseAll);
                }
            }
            else
            {
                Logger.Debug($"Slot cops won't be released to LSPDFR for {this}");
            }

            Game.NewSafeFiber(() =>
            {
                GameFiber.Wait(BlipFlashDuration);
                DeleteBlip();
            }, "Roadblock.ReleaseEntitiesToLspdfr");
        }

        private void SpawnSlot(IRoadblockSlot slot)
        {
            slot.Spawn();

            if (Flags.HasFlag(ERoadblockFlags.ForceInVehicle))
            {
                slot.WarpInVehicle();
            }
        }

        [Conditional("DEBUG")]
        private void DoInternalDebugPreviewCreation()
        {
            Game.NewSafeFiber(() =>
            {
                Logger.Trace($"Creating roadblock debug preview for {GetType()}");
                var color = PursuitIndicatorColor();
                var copsJoiningThePursuit = RetrieveCopsJoiningThePursuit(false);

                while (IsPreviewActive)
                {
                    var remainingCops = Slots
                        .SelectMany(x => x.Cops)
                        .Where(x => !copsJoiningThePursuit.Contains(x.GameInstance))
                        .Select(x => x.GameInstance)
                        .ToList();

                    foreach (var ped in copsJoiningThePursuit)
                    {
                        GameUtils.CreateMarker(ped.Position, EMarkerType.MarkerTypeUpsideDownCone, color, 1f, 1f, false);
                    }

                    foreach (var ped in remainingCops)
                    {
                        GameUtils.CreateMarker(ped.Position, EMarkerType.MarkerTypeUpsideDownCone, Color.DarkRed, 1f, 1f, false);
                    }

                    Game.FiberYield();
                }
            }, "Roadblock.Preview");
        }

        private Color PursuitIndicatorColor()
        {
            var color = Color.DarkRed;

            if (Flags.HasFlag(ERoadblockFlags.JoinPursuit))
            {
                color = Color.Lime;
            }
            else if (Flags.HasFlag(ERoadblockFlags.JoinPursuitOnBypass))
            {
                color = Color.Gold;
            }
            else if (Flags.HasFlag(ERoadblockFlags.JoinPursuitOnHit))
            {
                color = Color.Coral;
            }

            return color;
        }

        #endregion
    }
}