using System;
using RAGENativeUI.Elements;

namespace AutomaticRoadblocks.Menu
{
    public interface IMenu : IDisposable
    {
        /// <summary>
        /// Check if the menu has been initialized.
        /// </summary>
        bool IsMenuInitialized { get; }
        
        /// <summary>
        /// Check if the menu is currently being shown.
        /// </summary>
        bool IsShown { get; }

        /// <summary>
        /// Get the total menu items that are available.
        /// </summary>
        int TotalItems { get; }

        /// <summary>
        /// Register the given component in the menu.
        /// </summary>
        /// <param name="component">Set the component to register.</param>
        void RegisterComponent(IMenuComponent<UIMenuItem> component);

        /// <summary>
        /// Activate the menu within the game.
        /// This can be used to activate the menu keys again after the <see cref="IDisposable"/> has been invoked.
        /// </summary>
        void Activate();
    }
}