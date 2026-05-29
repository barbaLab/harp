using Bonsai.Expressions;
using System;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive;
using System.Reactive.Subjects;
using DeviceNameRegister = Bonsai.Harp.DeviceName;

namespace Bonsai.Harp.Net
{
    static class TcpDeviceProbe
    {
        const int ServerTimeoutMilliseconds = 1000;
        const int MaxConcurrentProbes = 8;

        public static void TryGetTcpDevices(ITypeDescriptorContext context, string connectionName)
        {
            if (context == null || string.IsNullOrEmpty(connectionName)) return;

            CreateTcpServer server;
            if (!TryGetServer(context, connectionName, out server)) return;
            if (server == null || server.Port <= 0) return;

            TcpListener listener = null;
            TcpClient client = null;
            var probeTasks = new List<Task>();
            var probeGate = new SemaphoreSlim(MaxConcurrentProbes, MaxConcurrentProbes);

            try
            {
                listener = new TcpListener(IPAddress.Any, server.Port);
                listener.Start();

                var deadline = DateTime.UtcNow.AddMilliseconds(ServerTimeoutMilliseconds);
                while (DateTime.UtcNow < deadline)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;

                    var acceptTask = listener.AcceptTcpClientAsync();
                    if (!acceptTask.Wait(remaining)) break;

                    client = acceptTask.Result;
                    client.NoDelay = true;
                    client.SendTimeout = ServerTimeoutMilliseconds;
                    client.ReceiveTimeout = ServerTimeoutMilliseconds;

                    probeTasks.Add(ProbeClientAsync(client, connectionName, probeGate));
                    client = null;
                }

                if (probeTasks.Count > 0)
                {
                    Task.WaitAll(probeTasks.ToArray());
                }
            }
            catch
            {
                // Ignore any exceptions that may occur during the design-time Harp devices probe.
            }
            finally
            {
                try { client?.Close(); } catch { }
                try { probeGate.Dispose(); } catch { }
                try { listener?.Stop(); } catch { }
            }
        }

        static bool TryGetServer(ITypeDescriptorContext context, string connectionName, out CreateTcpServer server)
        {
            server = null;
            if (context == null || string.IsNullOrEmpty(connectionName)) return false;

            var workflowBuilder = (WorkflowBuilder)context.GetService(typeof(WorkflowBuilder));
            if (workflowBuilder == null || workflowBuilder.Workflow == null) return false;

            server = workflowBuilder.Workflow.Descendants()
                .Select(builder => ExpressionBuilder.GetWorkflowElement(builder) as CreateTcpServer)
                .FirstOrDefault(createTcpServer => createTcpServer != null && string.Equals(createTcpServer.Name, connectionName, StringComparison.OrdinalIgnoreCase));

            return server != null;
        }

        static async Task ProbeClientAsync(TcpClient client, string connectionName, SemaphoreSlim probeGate)
        {
            await probeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var deviceIp = GetRemoteIp(client);
                var deviceName = await GetDeviceName(client).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(deviceName) && !string.IsNullOrEmpty(deviceIp))
                {
                    TcpDeviceProbeRegistry.Add(connectionName, deviceName, deviceIp);
                }
            }
            finally
            {
                try { client?.Close(); } catch { }
                probeGate.Release();
            }
        }

        public static async Task<string> GetDeviceName(TcpClient client)
        {
            if (client == null) return string.Empty;

            var tcs = new TaskCompletionSource<string>();
            var transport = default(TcpTransport);

            var cmdReadWhoAmI = HarpCommand.ReadUInt16(WhoAmI.Address);
            var cmdReadDeviceName = HarpCommand.ReadByte(DeviceNameRegister.Address);

            var whoAmI = 0;
            var messageObserver = Observer.Create<HarpMessage>(
                message =>
            {
                    switch (message.Address)
                    {
                        case WhoAmI.Address:
                            whoAmI = WhoAmI.GetPayload(message);
                            if (whoAmI == 0) tcs.TrySetResult(string.Empty);
                            else transport.Write(cmdReadDeviceName);
                            break;
                        case DeviceNameRegister.Address:
                            var deviceName = nameof(Device);
                            if (!message.Error) deviceName = DeviceNameRegister.GetPayload(message);
                            tcs.TrySetResult(deviceName);
                            break;
                        default:
                            break;
                    }
                },
                ex => tcs.TrySetException(ex),
                () => { if (!tcs.Task.IsCompleted) tcs.TrySetCanceled(); });

            transport = new TcpTransport(client, messageObserver);
            transport.IgnoreErrors = true;

            try
            {
                transport.Write(cmdReadWhoAmI);

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(ServerTimeoutMilliseconds)).ConfigureAwait(false);
                if (completed == tcs.Task) return await tcs.Task.ConfigureAwait(false);
            }
            catch { /*ignore*/ }
            finally { try { transport.Close(); } catch { } }

            return string.Empty;
        }

        static string GetRemoteIp(TcpClient client)
        {
            try
            {
                if (client != null && client.Client.RemoteEndPoint is IPEndPoint endpoint)
                {
                    return endpoint.Address.ToString();
                }
            }
            catch
            {
            }

            return string.Empty;
        }
    }
}
