using System;

namespace Bonsai.Harp
{
    /// <summary>
    /// Defines an interface for transport mechanisms that can be used to communicate with Harp devices,
    /// allowing for sending and receiving messages, managing observers, and handling connection state.
    /// </summary>
    public interface ITransport : IDisposable
    {
        /// <summary>
        /// Gets or sets a value indicating whether to ignore errors encountered during transport operations.
        /// </summary>
        bool IgnoreErrors { get; set; }

        /// <summary>
        /// Sets the observer that will receive messages from the transport. This method allows for dynamic
        /// assignment of observers, enabling flexibility in how messages are processed and handled.
        /// </summary>
        /// <param name="observer"></param>
        void SetObserver(IObserver<HarpMessage> observer);

        /// <summary>
        /// Writes a message to the transport, sending it to the connected Harp device.
        /// </summary>
        /// <param name="input"></param>
        void Write(HarpMessage input);

        /// <summary>
        /// Closes the transport connection, releasing any resources associated with it. After calling this method, the transport should no longer be used for communication.
        /// </summary>
        void Close();
    }
}
