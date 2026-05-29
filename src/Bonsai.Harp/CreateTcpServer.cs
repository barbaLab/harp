using Bonsai;
using Bonsai.Harp.Net;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeviceNameRegister = Bonsai.Harp.DeviceName;

namespace Bonsai.Harp
{
	/// <summary>
	/// Represents an operator that creates a TCP server on the specified port.
	/// </summary>
	[WorkflowElementIcon(typeof(ElementCategory), "Bonsai:ElementIcon.Net")]
	[DefaultProperty(nameof(Name))]
	[Description("Creates a TCP server on the specified port.")]
	[WorkflowElementCategory(ElementCategory.Source)]
	public class CreateTcpServer : Source<TcpListener>, INamedElement
	{
        readonly TcpServerConfiguration configuration;
        const int ExpectedClientsGracePeriod = 250;
        const int MaxConcurrentProbes = 8;

        /// <summary>
        /// Initializes a new instance of the <see cref="CreateTcpServer"/> class.
        /// </summary>
        public CreateTcpServer()
            : this(new TcpServerConfiguration())
        {
        }

        private CreateTcpServer(TcpServerConfiguration configuration)
        {
            this.configuration = configuration;
        }

		/// <summary>
        /// Gets or sets the name of the communication channel to reserve
        /// for the Harp protocol.
        /// </summary>
        [Description("The name of the communication channel to reserve for the Harp protocol.")]
		[TypeConverter(typeof(TcpServerNameConverter))]
		public string Name
        {
            get { return configuration.Name; }
            set { configuration.Name = value; }
        }

		/// <summary>
		/// Gets or sets the file path used to log client connections and disconnections.
		/// </summary>
		[Description("The file path used to log client connections and disconnections.")]
		public string LogFilePath { get; set; }

		/// <summary>
        /// Gets or sets the port on which to listen for incoming connection attempts.
        /// </summary>
        [Description("The port on which to listen for incoming connection attempts.")]
        public int Port
        {
            get { return configuration.Port; }
            set { configuration.Port = value; }
        }

        /// <summary>
        /// Gets or sets a value that disables a delay when send or receive buffers
        /// are not full.
        /// </summary>
        [Description("If set to true, disables a delay when send or receive buffers are not full.")]
        public bool NoDelay
        {
            get { return configuration.NoDelay; }
            set { configuration.NoDelay = value; }
        }

        /// <summary>
        /// Gets or sets a value that enables or disables Network Address
        /// Translation (NAT) traversal on the TCP server.
        /// </summary>
        [Description("Enables or disables Network Address Translation (NAT) on the TCP server.")]
        public bool AllowNatTraversal
        {
            get { return configuration.AllowNatTraversal; }
            set { configuration.AllowNatTraversal = value; }
        }

		string INamedElement.Name => Name;

		/// <summary>
		/// Generates an observable sequence that contains the TCP listener object.
		/// </summary>
		/// <returns>A sequence containing the created <see cref="TcpListener"/> object.</returns>
		public override IObservable<TcpListener> Generate()
		{
			return Observable.Create<TcpListener>(observer =>
			{
                if (string.IsNullOrWhiteSpace(Name))
                {
                    throw new InvalidOperationException("CreateTcpServer requires a non-empty and unique Name attribute.");
                }

                if (Port <= 0 || Port > 65535)
                {
                    throw new InvalidOperationException("CreateTcpServer requires a valid Port attribute in the range 1-65535.");
                }

				var listener = new TcpListener(IPAddress.Any, Port);
                listener.AllowNatTraversal(configuration.AllowNatTraversal);
				listener.Start();
                WriteLog("TCP server started on port " + Port);

				var cancellation = new CancellationTokenSource();
                var probeGate = new SemaphoreSlim(MaxConcurrentProbes, MaxConcurrentProbes);
                TcpClientsManager.EnsureConnection(configuration.Name);
                _ = Task.Run(() => AcceptClientsAsync(listener, cancellation.Token, probeGate));
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // If there are no expected clients registered (no Device blocks),
                        // wait a short grace period; if still none, proceed. If a registration
                        // appears during the timeout, wait for full readiness.
                        if (!TcpClientsManager.HasExpectedClients(configuration.Name))
                        {
                            if (ExpectedClientsGracePeriod > 0)
                            {
                                try { await Task.Delay(ExpectedClientsGracePeriod, cancellation.Token).ConfigureAwait(false); }
                                catch (OperationCanceledException) { return; }
                            }

                            if (!TcpClientsManager.HasExpectedClients(configuration.Name))
                            {
                                if (!cancellation.IsCancellationRequested)
                                {
                                    WriteLog("No expected clients registered after grace; proceeding.");
                                    WriteLog("Registered clients: " + TcpClientsManager.GetTcpClients(configuration.Name).Count);
                                    observer.OnNext(listener);
                                }
                                return;
                            }
                        }

                        await TcpClientsManager.WaitForConnectionReadyAsync(configuration.Name, cancellation.Token).ConfigureAwait(false);
                        if (!cancellation.IsCancellationRequested)
                        {
                            WriteLog("Registered clients: " + TcpClientsManager.GetTcpClients(configuration.Name).Count);
                            observer.OnNext(listener);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                });

				return Disposable.Create(() =>
				{
					cancellation.Cancel();
					listener.Stop();
					cancellation.Dispose();
                    probeGate.Dispose();

					Dictionary<string, TcpClient> clients = TcpClientsManager.UnregisterTcpClients(configuration.Name);

                    WriteLog("TCP server stopping on port " + Port + " (" + (clients != null ? clients.Count : 0) + " client(s) connected)");
                    foreach (var client in clients)
                    {
                        WriteLog("client disconnected: " + client.Key);
						try { client.Value.Close(); } catch { }
						try { client.Value.Dispose(); } catch { }
                    }
                    WriteLog("TCP server stopped on port " + Port);
				});
			});
		}

        async Task AcceptClientsAsync(TcpListener listener, CancellationToken cancellationToken, SemaphoreSlim probeGate)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = null;

                try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (SocketException) { if (cancellationToken.IsCancellationRequested) break; throw; }
                catch (ObjectDisposedException) { break; }

                if (client == null) { break; }
                client.NoDelay = configuration.NoDelay;
                // client.SendTimeout = 2000;
                // client.ReceiveTimeout = 2000;

                _ = ProbeAndRegisterClientAsync(client, cancellationToken, probeGate);
            }

        }

        async Task ProbeAndRegisterClientAsync(TcpClient client, CancellationToken cancellationToken, SemaphoreSlim probeGate)
        {
            var gateAcquired = false;
            var registered = false;

            try
            {
                await probeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                gateAcquired = true;

                var deviceName = await TcpDeviceProbe.GetDeviceName(client).ConfigureAwait(false);
                if (string.IsNullOrEmpty(deviceName))
                {
                    WriteLog("client rejected: " + deviceName + " (invalid Harp identity)");
                    return;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // FIXME: deviceUid should be retrieved from Harp protocol instead of being generated here
                var deviceIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                var deviceUid = TcpDeviceProbe.CreateClientKey(configuration.Name, deviceIp, deviceName);
                await TcpClientsManager.WaitForExpectedTcpClientAsync(configuration.Name, deviceUid, cancellationToken).ConfigureAwait(false);
                if (!TcpClientsManager.TryAttachTcpClient(configuration.Name, deviceUid, client))
                {
                    WriteLog("client rejected: " + deviceName + " (" + deviceUid + ") (duplicate Harp device)");
                    return;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    TcpClientsManager.UnregisterTcpClient(configuration.Name, deviceUid);
                    return;
                }

                registered = true;
                WriteLog("client connected: " + deviceName + " (" + deviceUid + ") (Harp device)");

                _ = Task.Run(() => MonitorClient(client, deviceName, cancellationToken));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                WriteLog("client probe failed: " + ex.Message);
            }
            finally
            {
                if (gateAcquired)
                {
                    probeGate.Release();
                }

                if (!registered)
                {
                    try { client.Close(); } catch { }
                    try { client.Dispose(); } catch { }
                }
            }
        }

        async Task MonitorClient(TcpClient client, string deviceName, CancellationToken cancellationToken)
        {
            // TODO: use Harp heartbeat to monitor client connection
            try
            {
                while (!cancellationToken.IsCancellationRequested && !(client.Client.Poll(0, SelectMode.SelectRead) && client.Client.Available == 0))
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    // FIXME: deviceUid should be retrieved from Harp protocol instead of being generated here
                    var deviceIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    var deviceUid = TcpDeviceProbe.CreateClientKey(configuration.Name, deviceIp, deviceName);
                    TcpClientsManager.UnregisterTcpClient(configuration.Name, deviceUid);
                    WriteLog("client disconnected: " + deviceName + " (" + deviceUid + ")");
                    client.Dispose();
                }
            }
        }

		void WriteLog(string message)
		{
			var logFilePath = LogFilePath;
			if (string.IsNullOrEmpty(logFilePath)) return;

			var directory = Path.GetDirectoryName(logFilePath);
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}

			lock (this)
			{
				using (var stream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
				using (var writer = new StreamWriter(stream, Encoding.UTF8))
				{
					writer.WriteLine(DateTime.UtcNow.ToString("o") + " " + message);
				}
			}
		}
    }
}
