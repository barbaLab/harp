using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Net;
using System.Net.Sockets;

namespace Bonsai.Harp.Net
{
    internal static class TcpClientsManager
    {
        static readonly Dictionary<string, Dictionary<string, TcpClient>> connections = new Dictionary<string, Dictionary<string, TcpClient>>();
        static readonly object clientsLock = new object();

        public static TcpClient RegisterTcpClient(string connectionName, string clientUid, TcpClient client)
        {
            if (string.IsNullOrEmpty(connectionName))
            {
                throw new ArgumentException("A connection name must be specified.", "connectionName");
            }

            if (string.IsNullOrEmpty(clientUid))
            {
                throw new ArgumentException("A client UID must be specified.", "clientUid");
            }

            lock (clientsLock)
            {
                if (!connections.TryGetValue(connectionName, out var connectionClients))
                {
                    connectionClients = new Dictionary<string, TcpClient>(StringComparer.OrdinalIgnoreCase);
                    connections.Add(connectionName, connectionClients);
                }
                if (connectionClients.TryGetValue(clientUid, out var existingClient))
                {
                    return existingClient;
                }

                connectionClients.Add(clientUid, client);
                return client;
            }
        }

        public static TcpClient GetTcpClient(string connectionName, string clientUid)
        {
            if (string.IsNullOrEmpty(connectionName))
            {
                throw new ArgumentException("A connection name must be specified.", "connectionName");
            }

            if (string.IsNullOrEmpty(clientUid))
            {
                throw new ArgumentException("A client UID must be specified.", "clientUid");
            }

            lock (clientsLock)
            {
                if (!connections.TryGetValue(connectionName, out var connectionClients)) return null;
                if (!connectionClients.TryGetValue(clientUid, out var client)) return null;
                return client;
            }
        }

        public static Dictionary<string, TcpClient> GetTcpClients(string connectionName)
        {
            if (string.IsNullOrEmpty(connectionName))
            {
                throw new ArgumentException("A connection name must be specified.", "connectionName");
            }

            lock (clientsLock)
            {
                if (!connections.TryGetValue(connectionName, out var connectionClients))
                {
                    return new Dictionary<string, TcpClient>();
                }

                return new Dictionary<string, TcpClient>(connectionClients, StringComparer.OrdinalIgnoreCase);
            }
        }

        public static bool UnregisterTcpClient(string connectionName, string clientUid)
        {
            var client = GetTcpClient(connectionName, clientUid);
            if (client == null) return false;
            lock (clientsLock)
            {
                if (!connections.TryGetValue(connectionName, out var connectionClients)) return false;
                return connectionClients.Remove(clientUid);
            }
        }

        public static Dictionary<string, TcpClient> UnregisterTcpClients(string connectionName)
        {
            var clients = GetTcpClients(connectionName);
            lock (clientsLock)
            {
                connections.Remove(connectionName);
            }
            return clients;
        }
    }
}
