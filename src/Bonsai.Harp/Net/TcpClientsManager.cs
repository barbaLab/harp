using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Bonsai.Harp.Net
{
    // TODO: simplify TcpClientsManager and enhance members naming for better readability and clarity
    internal static class TcpClientsManager
    {
        sealed class ClientRegistration
        {
            readonly TaskCompletionSource<bool> expectedReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            readonly TaskCompletionSource<TcpClient> clientReady = new TaskCompletionSource<TcpClient>(TaskCreationOptions.RunContinuationsAsynchronously);

            public TcpClient Client { get; private set; }

            public bool IsExpected => expectedReady.Task.IsCompleted;

            public bool IsAttached => Client != null;

            public void MarkExpected()
            {
                expectedReady.TrySetResult(true);
            }

            public void Attach(TcpClient client)
            {
                if (client == null) throw new ArgumentNullException(nameof(client));
                if (!IsExpected) throw new InvalidOperationException("The client must be registered before it can be attached.");
                if (Client != null) throw new InvalidOperationException("The client has already been attached.");

                Client = client;
                clientReady.TrySetResult(client);
            }

            public void Cancel()
            {
                expectedReady.TrySetCanceled();
                clientReady.TrySetCanceled();
            }
        }

        sealed class ConnectionRegistration
        {
            public readonly Dictionary<string, ClientRegistration> Clients = new Dictionary<string, ClientRegistration>(StringComparer.OrdinalIgnoreCase);
            public TaskCompletionSource<bool> Changed { get; private set; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool IsReady
            {
                get
                {
                    if (Clients.Count == 0) return false;
                    foreach (var client in Clients.Values)
                    {
                        if (!client.IsAttached) return false;
                    }

                    return true;
                }
            }

            public void SignalChanged()
            {
                var changed = Changed;
                Changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                changed.TrySetResult(true);
            }
        }

        static readonly Dictionary<string, ConnectionRegistration> connections = new Dictionary<string, ConnectionRegistration>(StringComparer.OrdinalIgnoreCase);
        static readonly object clientsLock = new object();

        static ConnectionRegistration GetOrCreateConnection(string connectionName)
        {
            if (!connections.TryGetValue(connectionName, out var connectionClients))
            {
                connectionClients = new ConnectionRegistration();
                connections.Add(connectionName, connectionClients);
            }

            return connectionClients;
        }

        static ConnectionRegistration GetConnection(string connectionName)
        {
            connections.TryGetValue(connectionName, out var connectionClients);
            return connectionClients;
        }

        static async Task WaitForSignalAsync(Task signal, CancellationToken cancellationToken)
        {
            if (signal.IsCompleted)
            {
                await signal.ConfigureAwait(false);
                return;
            }

            var completed = await Task.WhenAny(signal, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
            if (completed != signal)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            await signal.ConfigureAwait(false);
        }

        public static void EnsureConnection(string connectionName)
        {
            if (string.IsNullOrEmpty(connectionName))
            {
                throw new ArgumentException("A connection name must be specified.", nameof(connectionName));
            }

            lock (clientsLock)
            {
                GetOrCreateConnection(connectionName);
            }
        }

        public static void RegisterExpectedTcpClient(string connectionName, string clientUid)
        {
            if (string.IsNullOrEmpty(connectionName))
            {
                throw new ArgumentException("A connection name must be specified.", nameof(connectionName));
            }

            if (string.IsNullOrEmpty(clientUid))
            {
                throw new ArgumentException("A client UID must be specified.", nameof(clientUid));
            }

            lock (clientsLock)
            {
                var connectionClients = GetOrCreateConnection(connectionName);
                if (!connectionClients.Clients.TryGetValue(clientUid, out var registration))
                {
                    registration = new ClientRegistration();
                    connectionClients.Clients.Add(clientUid, registration);
                }
                else if (registration.IsAttached)
                {
                    throw new InvalidOperationException("The client UID is already associated with a TCP client.");
                }

                registration.MarkExpected();
                connectionClients.SignalChanged();
            }
        }

        public static async Task WaitForExpectedTcpClientAsync(string connectionName, string clientUid, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(connectionName))
            {
                throw new ArgumentException("A connection name must be specified.", nameof(connectionName));
            }

            if (string.IsNullOrEmpty(clientUid))
            {
                throw new ArgumentException("A client UID must be specified.", nameof(clientUid));
            }

            while (true)
            {
                Task waitTask;
                lock (clientsLock)
                {
                    var connectionClients = GetConnection(connectionName);
                    if (connectionClients != null && connectionClients.Clients.TryGetValue(clientUid, out var registration) && registration.IsExpected)
                    {
                        return;
                    }

                    if (connectionClients == null)
                    {
                        connectionClients = GetOrCreateConnection(connectionName);
                    }

                    waitTask = connectionClients.Changed.Task;
                }

                await WaitForSignalAsync(waitTask, cancellationToken).ConfigureAwait(false);
            }
        }

        public static async Task<TcpClient> WaitForTcpClientAsync(string connectionName, string clientUid, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(connectionName))
            {
                throw new ArgumentException("A connection name must be specified.", nameof(connectionName));
            }

            if (string.IsNullOrEmpty(clientUid))
            {
                throw new ArgumentException("A client UID must be specified.", nameof(clientUid));
            }

            while (true)
            {
                Task waitTask;
                lock (clientsLock)
                {
                    var connectionClients = GetConnection(connectionName);
                    if (connectionClients != null && connectionClients.Clients.TryGetValue(clientUid, out var registration) && registration.IsAttached)
                    {
                        return registration.Client;
                    }

                    if (connectionClients == null)
                    {
                        connectionClients = GetOrCreateConnection(connectionName);
                    }

                    waitTask = connectionClients.Changed.Task;
                }

                await WaitForSignalAsync(waitTask, cancellationToken).ConfigureAwait(false);
            }
        }

        public static async Task WaitForConnectionReadyAsync(string connectionName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(connectionName))
            {
                throw new ArgumentException("A connection name must be specified.", nameof(connectionName));
            }

            while (true)
            {
                Task waitTask;
                lock (clientsLock)
                {
                    var connectionClients = GetConnection(connectionName);
                    if (connectionClients != null && connectionClients.IsReady)
                    {
                        return;
                    }

                    if (connectionClients == null)
                    {
                        connectionClients = GetOrCreateConnection(connectionName);
                    }

                    waitTask = connectionClients.Changed.Task;
                }

                await WaitForSignalAsync(waitTask, cancellationToken).ConfigureAwait(false);
            }
        }

        public static TcpClient GetTcpClient(string connectionName, string clientUid)
        {
            if (string.IsNullOrEmpty(connectionName))
            {
                throw new ArgumentException("A connection name must be specified.", nameof(connectionName));
            }

            if (string.IsNullOrEmpty(clientUid))
            {
                throw new ArgumentException("A client UID must be specified.", nameof(clientUid));
            }

            lock (clientsLock)
            {
                var connectionClients = GetConnection(connectionName);
                if (connectionClients == null) return null;
                if (!connectionClients.Clients.TryGetValue(clientUid, out var registration)) return null;
                return registration.Client;
            }
        }

        public static Dictionary<string, TcpClient> GetTcpClients(string connectionName)
        {
            if (string.IsNullOrEmpty(connectionName))
            {
                throw new ArgumentException("A connection name must be specified.", nameof(connectionName));
            }

            lock (clientsLock)
            {
                var clients = new Dictionary<string, TcpClient>(StringComparer.OrdinalIgnoreCase);
                var connectionClients = GetConnection(connectionName);
                if (connectionClients == null)
                {
                    return clients;
                }

                foreach (var entry in connectionClients.Clients)
                {
                    if (entry.Value.Client != null)
                    {
                        clients.Add(entry.Key, entry.Value.Client);
                    }
                }

                return clients;
            }
        }

        public static bool HasExpectedClients(string connectionName)
        {
            if (string.IsNullOrEmpty(connectionName))
            {
                throw new ArgumentException("A connection name must be specified.", nameof(connectionName));
            }

            lock (clientsLock)
            {
                var connectionClients = GetConnection(connectionName);
                if (connectionClients == null) return false;
                foreach (var registration in connectionClients.Clients.Values)
                {
                    if (registration.IsExpected) return true;
                }

                return false;
            }
        }

        public static bool TryAttachTcpClient(string connectionName, string clientUid, TcpClient client)
        {
            if (string.IsNullOrEmpty(connectionName))
            {
                throw new ArgumentException("A connection name must be specified.", nameof(connectionName));
            }

            if (string.IsNullOrEmpty(clientUid))
            {
                throw new ArgumentException("A client UID must be specified.", nameof(clientUid));
            }

            lock (clientsLock)
            {
                var connectionClients = GetConnection(connectionName);
                if (connectionClients == null) return false;
                if (!connectionClients.Clients.TryGetValue(clientUid, out var registration)) return false;
                if (!registration.IsExpected || registration.IsAttached) return false;

                registration.Attach(client);
                connectionClients.SignalChanged();
                return true;
            }
        }

        public static bool UnregisterTcpClient(string connectionName, string clientUid)
        {
            if (string.IsNullOrEmpty(connectionName))
            {
                throw new ArgumentException("A connection name must be specified.", nameof(connectionName));
            }

            if (string.IsNullOrEmpty(clientUid))
            {
                throw new ArgumentException("A client UID must be specified.", nameof(clientUid));
            }

            lock (clientsLock)
            {
                var connectionClients = GetConnection(connectionName);
                if (connectionClients == null) return false;
                if (!connectionClients.Clients.TryGetValue(clientUid, out var registration)) return false;

                connectionClients.Clients.Remove(clientUid);
                registration.Cancel();
                connectionClients.SignalChanged();

                if (connectionClients.Clients.Count == 0)
                {
                    connections.Remove(connectionName);
                }

                return true;
            }
        }

        public static Dictionary<string, TcpClient> UnregisterTcpClients(string connectionName)
        {
            if (string.IsNullOrEmpty(connectionName))
            {
                throw new ArgumentException("A connection name must be specified.", nameof(connectionName));
            }

            lock (clientsLock)
            {
                var clients = new Dictionary<string, TcpClient>(StringComparer.OrdinalIgnoreCase);
                var connectionClients = GetConnection(connectionName);
                if (connectionClients != null)
                {
                    foreach (var entry in connectionClients.Clients)
                    {
                        entry.Value.Cancel();
                        if (entry.Value.Client != null)
                        {
                            clients.Add(entry.Key, entry.Value.Client);
                        }
                    }

                    connections.Remove(connectionName);
                    connectionClients.SignalChanged();
                }

                return clients;
            }
        }
    }
}
