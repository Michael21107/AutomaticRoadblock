using System;
using AutomaticRoadblocks.Utils.Road;
using Rage;

namespace AutomaticRoadblocks.SpikeStrip.Dispatcher
{
    public interface ISpikeStripDispatcher : IDisposable
    {
        /// <summary>
        /// Spawn a spike strip on the given road.
        /// This will spawn an undeployed spike strip.
        /// </summary>
        /// <param name="road"></param>
        /// <param name="stripLocation"></param>
        /// <returns></returns>
        ISpikeStrip Spawn(Road road, ESpikeStripLocation stripLocation);
        
        /// <summary>
        /// Deploy a spike strip on the nearby road for the given location.
        /// This will spawn an undeployed spike strip which will directly be deployed upon creation.
        /// </summary>
        /// <param name="position">The position to deploy a spike strip at.</param>
        /// <param name="stripLocation">The location of the spike strip on the road.</param>
        /// <returns>Returns the deployed spike strip.</returns>
        ISpikeStrip Deploy(Vector3 position, ESpikeStripLocation stripLocation);

        /// <summary>
        /// Deploy a spike strip on the given road.
        /// This will spawn an undeployed spike strip which will directly be deployed upon creation.
        /// </summary>
        /// <param name="road">The road to deploy a spike strip at.</param>
        /// <param name="stripLocation">The location of the spike strip on the road.</param>
        /// <returns>Returns the deployed spike strip.</returns>
        ISpikeStrip Deploy(Road road, ESpikeStripLocation stripLocation);

        /// <summary>
        /// Remove all spike strips.
        /// </summary>
        void RemoveAll();
    }
}