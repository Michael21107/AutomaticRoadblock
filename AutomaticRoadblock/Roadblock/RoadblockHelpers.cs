using System.Collections.Generic;
using System.Linq;
using AutomaticRoadblocks.AbstractionLayer;
using AutomaticRoadblocks.Instances;
using AutomaticRoadblocks.Roadblock.Slot;
using AutomaticRoadblocks.Vehicles;
using Rage;

namespace AutomaticRoadblocks.Roadblock
{
    internal static class RoadblockHelpers
    {
        private static readonly ILogger Logger = IoC.Instance.GetInstance<ILogger>();

        public static void ReleaseInstancesToLspdfr(IRoadblockSlot slot)
        {
            Assert.NotNull(slot, "slot cannot be null");
            ReleaseInstancesToLspdfr(slot.Instances, slot.Vehicle);
        }

        internal static void ReleaseInstancesToLspdfr(List<InstanceSlot> instances, Vehicle vehicle)
        {
            Assert.NotNull(instances, "instances cannot be null");
            Assert.NotNull(vehicle, "vehicle cannot be null");
            var copPeds = instances
                .Where(x => x.Type == EEntityType.CopPed)
                .Select(x => x.Instance)
                .Select(x => (ARPed)x)
                .ToList();
            
            // release the cops & cop vehicle instances
            instances
                .Where(x => x.Type is EEntityType.CopPed or EEntityType.CopVehicle)
                .Select(x => x.Instance)
                .ToList()
                .ForEach(x => x.Release());

            // make sure the cops are in a vehicle when releasing them
            Logger.Trace($"Releasing a total of {copPeds.Count} cops to LSPDFR");
            copPeds
                .Select(x => x.GameInstance)
                .ToList()
                .ForEach(x =>
                {
                    // make sure the ped is the vehicle or at least entering it
                    if (!x.IsInVehicle(vehicle, true))
                        x.Tasks.EnterVehicle(vehicle, 3000, (int)EVehicleSeat.Any);
                });

            // remove all cop instances so that we don't remove them by accident when disposing
            // these instances are now in control of LSPDFR
            instances.RemoveAll(x => x.Type is EEntityType.CopPed or EEntityType.CopVehicle);
        }
    }
}