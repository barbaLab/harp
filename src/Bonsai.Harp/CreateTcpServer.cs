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
				Task.Run(() => AcceptClients(listener, cancellation.Token));

                observer.OnNext(listener);

				return Disposable.Create(() =>
				{
					cancellation.Cancel();
					listener.Stop();
					cancellation.Dispose();

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

        void AcceptClients(TcpListener listener, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = null;

                try { client = listener.AcceptTcpClient(); }
                catch (SocketException) { if (cancellationToken.IsCancellationRequested) break; throw; }
                catch (ObjectDisposedException) { break; }

                if (client == null) { break; }
                client.NoDelay = configuration.NoDelay;
                // client.SendTimeout = 2000;
                // client.ReceiveTimeout = 2000;

                var deviceName = TcpDeviceProbe.GetDeviceName(client).GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(deviceName))
                {
                    WriteLog("client rejected: " + deviceName + " (invalid Harp identity)");
                    client.Close();
                    continue;
                }
                WriteLog("client connected: " + deviceName + " (Harp device)");

                TcpClientsManager.RegisterTcpClient(configuration.Name, deviceName, client);
                WriteLog("Registered clients: " + TcpClientsManager.GetTcpClients(configuration.Name).Count);

                Task.Run(() => MonitorClient(client, deviceName, cancellationToken));
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
                    WriteLog("client disconnected: " + deviceName);
                    TcpClientsManager.UnregisterTcpClient(configuration.Name, deviceName);
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
