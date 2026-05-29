using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bonsai.Harp.Net
{
    /// <summary>
    /// Provides settings for creating and configuring a Harp communication server
    /// over TCP.
    /// </summary>
    public class TcpServerConfiguration
    {
        /// <summary>
        /// Gets or sets the name of the communication channel to reserve
        /// for the Harp protocol.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the port on which to listen for incoming connection attempts.
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Gets or sets a value that disables a delay when send or receive buffers
        /// are not full.
        /// </summary>
        public bool NoDelay { get; set; }

        /// <summary>
        /// Gets or sets a value that enables or disables Network Address
        /// Translation (NAT) traversal on the TCP server.
        /// </summary>
        public bool AllowNatTraversal { get; set; }
    }
}
