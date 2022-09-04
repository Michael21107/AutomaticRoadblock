using Rage;

namespace AutomaticRoadblocks.Animation
{
    public class AnimationExecutor
    {
        private float _speed = 1f;
        
        internal AnimationExecutor(Entity entity, AnimationDictionary dictionary, string animation)
        {
            Entity = entity;
            Dictionary = dictionary;
            Animation = animation;
        }

        #region Properties
        
        /// <summary>
        /// The entity for which this task is playing.
        /// </summary>
        public Entity Entity { get; }
        
        /// <summary>
        /// The dictionary this animation is using.
        /// </summary>
        public AnimationDictionary Dictionary { get; }
        
        /// <summary>
        /// The animation that is being played.
        /// </summary>
        public string Animation { get; }

        /// <summary>
        /// Verify if the animation is still playing.
        /// </summary>
        public bool IsPlaying => AnimationHelper.IsAnimationPlaying(Entity, Dictionary, Animation);
        
        /// <summary>
        /// The animation speed of this task.
        /// </summary>
        public float Speed
        {
            get => _speed;
            set => UpdateAnimationSpeed(value);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Wait the current fiber till the animation has completed.
        /// </summary>
        public AnimationExecutor WaitForCompletion()
        {
            while (IsPlaying)
            {
                GameFiber.Yield();
            }

            return this;
        }

        /// <summary>
        /// Stop the animation.
        /// </summary>
        public AnimationExecutor Stop()
        {
            AnimationHelper.StopAnimation(Entity, Dictionary, Animation);

            return this;
        }

        private void UpdateAnimationSpeed(float value)
        {
            _speed = value;
            AnimationHelper.SetAnimationSpeed(Entity, Dictionary, Animation, value);
        }

        #endregion
    }
}