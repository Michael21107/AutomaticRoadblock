using System;
using System.Collections.Generic;
using System.Linq;
using AutomaticRoadblocks.Animation;
using AutomaticRoadblocks.Barriers;
using AutomaticRoadblocks.Instances;
using AutomaticRoadblocks.Lspdfr;
using AutomaticRoadblocks.Roadblock.Slot;
using AutomaticRoadblocks.SpikeStrip.Dispatcher;
using AutomaticRoadblocks.Street.Info;
using JetBrains.Annotations;
using Rage;

namespace AutomaticRoadblocks.SpikeStrip.Slot
{
    /// <summary>
    /// A <see cref="IRoadblockSlot"/> which deploys a spike strip.
    /// This slot won't create any barriers and will always use the local vehicle type.
    /// </summary>
    public class SpikeStripSlot : AbstractRoadblockSlot
    {
        private const float DeploySpikeStripRange = 50f;
        private const int DelayBetweenStateChangeAndUndeploy = 2 * 1000;
        private const float PlacementInFrontOfVehicle = 0.1f;

        private bool _hasBeenDeployed;

        public SpikeStripSlot(ISpikeStripDispatcher spikeStripDispatcher, Road street, Road.Lane lane, Vehicle targetVehicle, float heading,
            bool shouldAddLights, float offset = 0)
            : base(lane, BarrierModel.None, BarrierModel.None, EBackupUnit.LocalPatrol, heading, shouldAddLights, false, offset)
        {
            Assert.NotNull(spikeStripDispatcher, "spikeStripDispatcher cannot be null");
            Assert.NotNull(targetVehicle, "targetVehicle cannot be null");
            SpikeStripDispatcher = spikeStripDispatcher;
            TargetVehicle = targetVehicle;
            Road = street;
            Location = DetermineLocation();
            NumberOfCops = 1;

            Initialize();
        }

        #region Properties

        /// <inheritdoc />
        public override IList<ARPed> CopsJoiningThePursuit => new List<ARPed>();

        /// <summary>
        /// The road this spike strip slot is placed on.
        /// </summary>
        private Road Road { get; }

        /// <summary>
        /// The target vehicle of the spike strip.
        /// </summary>
        private Vehicle TargetVehicle { get; }

        /// <summary>
        /// The spike strip dispatcher to use for creating an instance.
        /// </summary>
        private ISpikeStripDispatcher SpikeStripDispatcher { get; }

        /// <summary>
        /// Retrieve the spike strip instance of this slot.
        /// </summary>
        [CanBeNull]
        private ISpikeStrip SpikeStrip => Instances
            .Where(x => x.Type == EEntityType.SpikeStrip)
            .Select(x => (ARSpikeStrip)x)
            .Where(x => x is { SpikeStrip: { } })
            .Select(x => x.SpikeStrip)
            .FirstOrDefault();

        /// <summary>
        /// Determine the spike strip location.
        /// </summary>
        private ESpikeStripLocation Location { get; }

        #endregion

        #region Methods

        public override void Spawn()
        {
            base.Spawn();
            StartMonitor();
        }

        /// <inheritdoc />
        public override void Release(bool releaseAll = false)
        {
            SpikeStrip?.Undeploy();
        }

        #endregion

        #region Functions

        /// <inheritdoc />
        protected override void InitializeScenery()
        {
            // create the spike strip
            Instances.Add(new ARSpikeStrip(CreateSpikeStripInstance()));
        }

        /// <inheritdoc />
        protected override void InitializeLights()
        {
            // no-op
        }

        /// <inheritdoc />
        protected override Vector3 CalculatePositionBehindVehicle()
        {
            return CalculateVehiclePositionOnSide() + CalculateDirectionInFrontOfVehicle(PlacementInFrontOfVehicle);
        }

        /// <inheritdoc />
        protected override float CalculateCopHeading()
        {
            return Location switch
            {
                ESpikeStripLocation.Right => Heading + 90,
                _ => Heading - 90
            };
        }

        /// <inheritdoc />
        protected override float CalculateVehicleHeading()
        {
            return Heading + Random.Next(-VehicleHeadingMaxOffset, VehicleHeadingMaxOffset);
        }

        /// <inheritdoc />
        protected override Vector3 CalculateVehiclePosition()
        {
            return CalculateVehiclePositionOnSide();
        }

        private ISpikeStrip CreateSpikeStripInstance()
        {
            var offset = VehicleLength + PlacementInFrontOfVehicle;
            var spikeStrip = SpikeStripDispatcher.Spawn(Road, Lane, Location, TargetVehicle, offset);
            spikeStrip.StateChanged += SpikeStripStateChanged;
            return spikeStrip;
        }

        private void SpikeStripStateChanged(ISpikeStrip spikeStrip, ESpikeStripState newState)
        {
            switch (newState)
            {
                case ESpikeStripState.Hit:
                    // delay the undeploy to pop additional tires
                    Game.NewSafeFiber(() =>
                    {
                        GameFiber.Wait(1500);
                        DoUndeploy();
                    }, "SpikeStripStateChanged.Hit");
                    break;
                case ESpikeStripState.Bypassed:
                    DoUndeploy();
                    break;
            }
        }

        private void StartMonitor()
        {
            if (_hasBeenDeployed)
                return;

            Logger.Trace("Starting spike strip slot monitor");
            Game.NewSafeFiber(() =>
            {
                while (!_hasBeenDeployed)
                {
                    if (TargetVehicle != null && TargetVehicle.DistanceTo(Position) <= DeploySpikeStripRange)
                    {
                        DoSpikeStripDeploy();
                    }

                    Game.FiberYield();
                }
            }, "SpikeStripSlot.Monitor");
        }

        private void DoUndeploy()
        {
            Game.NewSafeFiber(() =>
            {
                GameFiber.Wait(DelayBetweenStateChangeAndUndeploy);
                ExecuteWithCop(cop =>
                    AnimationHelper.PlayAnimation(cop.GameInstance, Animations.Dictionaries.ObjectDictionary, Animations.ObjectPickup, AnimationFlags.None));
                SpikeStrip?.Undeploy();
            }, "SpikeStripSlot.Undeploy");
        }

        private void DoSpikeStripDeploy()
        {
            if (_hasBeenDeployed)
            {
                Game.DisplayNotificationDebug($"~r~Unable to deploy {GetType()}, has already been deployed");
                return;
            }

            var distanceToTarget = TargetVehicle.DistanceTo(Position);
            Logger.Trace($"Target vehicle is in range of spike strip ({distanceToTarget}), deploying spike strip");
            Game.DisplayNotificationDebug($"Deploying spike strip (distance from target: {distanceToTarget})");
            
            var spikeStrip = SpikeStrip;
            ExecuteWithCop(cop =>
                AnimationHelper.PlayAnimation(cop.GameInstance, Animations.Dictionaries.GrenadeDictionary, Animations.ThrowShortLow, AnimationFlags.None));
            spikeStrip?.Deploy();
            _hasBeenDeployed = true;
        }

        private Vector3 CalculateVehiclePositionOnSide()
        {
            var position = OffsetPosition;
            var headingRotation = Location switch
            {
                ESpikeStripLocation.Right => Heading - 90f,
                _ => Heading + 90f
            };

            if (Location == ESpikeStripLocation.Middle)
            {
                position += MathHelper.ConvertHeadingToDirection(Heading) * 2f;
            }

            return position + MathHelper.ConvertHeadingToDirection(headingRotation) * (Lane.Width / 2);
        }

        private Vector3 CalculateDirectionInFrontOfVehicle(float additionalVerticalOffset)
        {
            return MathHelper.ConvertHeadingToDirection(Heading) * (VehicleLength + additionalVerticalOffset);
        }

        private ESpikeStripLocation DetermineLocation()
        {
            var distanceLeft = Road.LeftSide.DistanceTo2D(Lane.Position);
            var distanceMiddle = Road.Position.DistanceTo2D(Lane.Position);
            var distanceRight = Road.RightSide.DistanceTo2D(Lane.Position);
            var totalLanes = Road.NumberOfLanesSameDirection + Road.NumberOfLanesOppositeDirection;

            Logger.Trace($"Spike strip location data, {nameof(Lane.IsOppositeHeadingOfRoadNodeHeading)}: {Lane.IsOppositeHeadingOfRoadNodeHeading}, " +
                         $"{nameof(distanceMiddle)}: {distanceMiddle},  {nameof(distanceLeft)}: {distanceLeft},  {nameof(distanceRight)}: {distanceRight}");
            if (totalLanes > 2 && distanceMiddle < distanceRight && distanceMiddle < distanceLeft)
            {
                return ESpikeStripLocation.Middle;
            }

            return distanceRight <= distanceLeft ? ESpikeStripLocation.Right : ESpikeStripLocation.Left;
        }

        private void ExecuteWithCop(Action<ARPed> action)
        {
            var cop = Cops.FirstOrDefault();
            if (cop != null)
            {
                try
                {
                    action.Invoke(cop);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to invoke spike strip action, {ex.Message}", ex);
                }
            }
            else
            {
                Logger.Warn($"Spike strip slot has no valid cop ped, {this}");
            }
        }

        #endregion
    }
}