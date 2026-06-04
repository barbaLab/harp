using Bonsai.Harp.Net;
using System;
using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Bonsai.Harp
{
    /// <summary>
    /// Represents the transport used to communicate with the Harp device.
    /// </summary>
    public enum TransportMode
    {
        /// <summary>
        /// Uses a serial port connection.
        /// </summary>
        Serial = 0,
        /// <summary>
        /// Uses a TCP connection.
        /// </summary>
        Tcp = 1
    }

    /// <summary>
    /// Represents an observable source of messages from the Harp device connected at the specified serial port.
    /// </summary>
    [XmlType(Namespace = Constants.XmlNamespace)]
    [TypeDescriptionProvider(typeof(DeviceTransportTypeDescriptionProvider))]
    [Editor("Bonsai.Harp.Design.DeviceConfigurationEditor, Bonsai.Harp.Design", typeof(ComponentEditor))]
    [Description("Produces a sequence of messages from the Harp device connected at the specified serial port or TCP connection.")]
    public partial class Device : Source<HarpMessage>, INamedElement
    {
        string name;
        string uid;
        string portName;
        string connectionName;
        string deviceName;
        readonly int deviceId;
        readonly FirmwareMetadata deviceFirmware;

        /// <summary>
        /// Initializes a new instance of the <see cref="Device"/> class.
        /// </summary>
        public Device() : this(0)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Device"/> class
        /// accepting connections only from Harp devices with the specified identifier.
        /// </summary>
        /// <param name="whoAmI">The device identifier to match against serial connections.</param>
        public Device(int whoAmI)
            : this(whoAmI, firmware: null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Device"/> class
        /// accepting connections only from Harp devices with the specified
        /// identifier and firmware version.
        /// </summary>
        /// <param name="whoAmI">The device identifier to match against serial connections.</param>
        /// <param name="firmware">Provides information about the expected device firmware version.</param>
        public Device(int whoAmI, FirmwareMetadata firmware)
        {
            deviceId = whoAmI;
            deviceFirmware = firmware;
            if (deviceFirmware != null && deviceId == 0)
            {
                throw new ArgumentException(
                    "A valid device identifier must be specified when firmware metadata is provided.",
                    nameof(whoAmI));
            }

            TransportMode = TransportMode.Serial;
            portName = "COMx";
            OperationMode = OperationMode.Active;
            OperationLed = LedState.On;
            VisualIndicators = LedState.On;
            DumpRegisters = true;
            Heartbeat = EnableFlag.Disabled;
            MuteReplies = false;
        }

        /// <summary>
        /// Gets or sets a value specifying the transport mode of the device at initialization.
        /// </summary>
        [Description("Specifies the transport used to communicate with the Harp device.")]
        [RefreshProperties(RefreshProperties.All)]
        public TransportMode TransportMode { get; set; }

        /// <summary>
        /// Gets or sets a value specifying the operation mode of the device at initialization.
        /// </summary>
        [Description("Specifies the operation mode of the device at initialization.")]
        public OperationMode OperationMode { get; set; }

        /// <summary>
        /// Gets or sets a value specifying the state of the LED reporting device operation.
        /// </summary>
        [Description("Specifies the state of the LED reporting device operation.")]
        public LedState OperationLed { get; set; }

#pragma warning disable CS0612 // Type or member is obsolete
        /// <summary>
        /// Gets or sets the state of the device at run time.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Description("Specifies the state of the device at run time.")]
        public DeviceState DeviceState
        {
            get { return OperationMode == OperationMode.Active ? DeviceState.Active : DeviceState.Standby; }
            set { OperationMode = value == DeviceState.Active ? OperationMode.Active : OperationMode.Standby; }
        }

        /// <summary>
        /// Gets or sets the state of the device LED.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Description("Specifies the state of the device LED.")]
        public LedState LedState
        {
            get { return OperationLed; }
            set { OperationLed = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the <see cref="DeviceState"/> property should be serialized.
        /// </summary>
        [Obsolete]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool ShouldSerializeDeviceState() => false;

        /// <summary>
        /// Gets or sets a value indicating whether the <see cref="LedState"/> property should be serialized.
        /// </summary>
        [Obsolete]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool ShouldSerializeLedState() => false;


        [Obsolete]
        [EditorBrowsable(EditorBrowsableState.Never)]
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        public void set_Heartbeat(EnableType value)
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
        {
            Heartbeat = value == EnableType.Enable ? EnableFlag.Enabled : EnableFlag.Disabled;
        }
#pragma warning restore CS0612 // Type or member is obsolete

        /// <summary>
        /// Gets or sets a value indicating whether the device should send the content of all registers during initialization.
        /// </summary>
        [Description("Specifies whether the device should send the content of all registers during initialization.")]
        public bool DumpRegisters { get; set; }

        /// <summary>
        /// Gets or sets a value specifying the state of all the visual indicators in the device.
        /// </summary>
        [Description("Specifies the state of all the visual indicators in the device.")]
        public LedState VisualIndicators { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the Device sends the Timestamp event each second.
        /// </summary>
        [Description("Specifies if the Device sends the Timestamp event each second.")]
        public EnableFlag Heartbeat { get; set; }

        [Description("Specifies if the Device replies to commands.")]
        bool MuteReplies { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether error messages parsed during acquisition should be ignored or raise an exception.
        /// </summary>
        [Description("Specifies whether error messages parsed during acquisition should be ignored or raise an error.")]
        public bool IgnoreErrors { get; set; }

        /// <summary>
        /// Gets or sets the name of the serial port used to communicate with the Harp device.
        /// </summary>
        [Category("Connectivity")]
        [TypeConverter(typeof(PortNameConverter))]
        [Description("The name of the serial port used to communicate with the Harp device.")]
        public string PortName
        {
            get { return portName; }
            set
            {
                portName = value;
                if (TransportMode == TransportMode.Serial && deviceId == 0)
                {
                    var deviceName = nameof(Device);
                    try
                    {
                        var transport = new SerialTransport(portName, new Subject<HarpMessage>());
                        transport.IgnoreErrors = true;
                        deviceName = DeviceProbe.GetDeviceName(transport, true).GetAwaiter().GetResult();
                        var deviceUid = DeviceProbe.GetUid(transport).GetAwaiter().GetResult();
                        uid = DeviceProbe.GetReadableUid(deviceUid);
                    }
                    catch { /*ignore*/ }
                    finally
                    {
                        this.deviceName = deviceName;
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the name of the TCP connection used to communicate with the Harp device.
        /// </summary>
        [Category("Connectivity")]
        [TypeConverter(typeof(ConnectionNameConverter))]
        [RefreshProperties(RefreshProperties.All)]
        [Description("The name of the TCP connection used to communicate with the Harp device.")]
        public string ConnectionName
        {
            get { return connectionName; }
            set { connectionName = value; }
        }

        /// <summary>
        /// Gets or sets the name of the Harp device to select when using TCP.
        /// </summary>
        [XmlIgnore]
        [Category("Connectivity")]
        [Editor("Bonsai.Harp.Design.DeviceNameEditor, Bonsai.Harp.Design", DesignTypes.UITypeEditor)]
        [Description("The name of the Harp device to select when using TCP.")]
        public string DeviceName
        {
            get { return deviceName; }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    deviceName = null;
                    name = nameof(Device);
                    uid = null;
                    return;
                }

                var openParen = value.LastIndexOf('(');
                var closeParen = value.LastIndexOf(')');
                deviceName = openParen > 0 ? value.Substring(0, openParen).TrimEnd() : value;
                name = !string.IsNullOrEmpty(deviceName) ? deviceName : nameof(Device);
                uid = openParen >= 0 && closeParen > openParen ? value.Substring(openParen + 1, closeParen - openParen - 1).Trim() : null;
            }
        }

        /// <summary>
        /// Gets or sets the name of the Harp device to select when using TCP. It is always without the client UID appended.
        /// </summary>
        [XmlElement("DeviceName")]
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public string DeviceNameSerialized
        {
            get { return deviceName; }
            set { deviceName = value; }
        }

        /// <summary>
        /// Gets or sets the unique identifier (uid) of the Harp device.
        /// </summary>
        [XmlElement("Uid")]
        [Category("Connectivity")]
        [ReadOnly(true)]
        [Description("The unique identifier (uid) of the Harp device.")]
        public string Uid
        {
            get { return uid; }
            set { uid = value; }
        }

        string INamedElement.Name => !string.IsNullOrEmpty(name) ? name : !string.IsNullOrEmpty(deviceName) ? deviceName : default;

        OperationControlPayload CreateOperationControlPayload() => new(
            OperationMode,
            DumpRegisters,
            MuteReplies,
            VisualIndicators,
            OperationLed,
            Heartbeat
        );

        /// <summary>
        /// Connects to the specified serial port and returns an observable sequence of Harp messages
        /// coming from the device.
        /// </summary>
        /// <returns>The observable sequence of Harp messages produced by the device.</returns>
        public override IObservable<HarpMessage> Generate()
        {
            var portName = PortName;
            var connectionName = ConnectionName;
            var ignoreErrors = IgnoreErrors;
            var controlPayload = CreateOperationControlPayload();
            return Observable.Create<HarpMessage>(async (observer, cancellationToken) =>
            {
                var transport = await CreateTransportAsync(portName, connectionName, ignoreErrors, controlPayload, observer, cancellationToken);
                return Disposable.Create(() => CloseTransport(transport, controlPayload));
            });
        }

        /// <summary>
        /// Connects to the specified serial port and sends the observable sequence of Harp messages.
        /// The return value is an observable sequence of Harp messages coming from the device.
        /// </summary>
        /// <param name="source">An observable sequence of Harp messages to send to the device.</param>
        /// <returns>The observable sequence of Harp messages produced by the device.</returns>
        public IObservable<HarpMessage> Generate(IObservable<HarpMessage> source)
        {
            var portName = PortName;
            var connectionName = ConnectionName;
            var ignoreErrors = IgnoreErrors;
            var controlPayload = CreateOperationControlPayload();
            return Observable.Create<HarpMessage>(async (observer, cancellationToken) =>
            {
                var transport = await CreateTransportAsync(portName, connectionName, ignoreErrors, controlPayload, observer, cancellationToken);
                var sourceDisposable = new SingleAssignmentDisposable();
                sourceDisposable.Disposable = source.Subscribe(
                    transport.Write,
                    observer.OnError,
                    observer.OnCompleted);

                return Disposable.Create(() =>
                {
                    sourceDisposable.Dispose();
                    CloseTransport(transport, controlPayload);
                });
            });
        }

        async Task<ITransport> CreateTransportAsync(
            string portName,
            string connectionName,
            bool ignoreErrors,
            OperationControlPayload controlPayload,
            IObserver<HarpMessage> observer,
            CancellationToken cancellationToken)
        {
            return TransportMode == TransportMode.Tcp
                ? await CreateTcpTransportAsync(connectionName, ignoreErrors, controlPayload, observer, cancellationToken)
                : await CreateSerialTransportAsync(portName, ignoreErrors, controlPayload, observer, cancellationToken);
        }

        async Task<ITransport> CreateSerialTransportAsync(
            string portName,
            bool ignoreErrors,
            OperationControlPayload controlPayload,
            IObserver<HarpMessage> observer,
            CancellationToken cancellationToken)
        {
            ITransport transport;
            using (var device = new AsyncDevice(portName, leaveOpen: true))
            {
                try
                {
                    var whoAmI = await device.ReadWhoAmIAsync(cancellationToken);
                    if (deviceId > 0 && whoAmI != deviceId)
                    {
                        throw new HarpException(string.Format(
                            "The device ID {1} on {0} was unexpected. Check whether the correct device is connected to the specified serial port.",
                            portName, whoAmI));
                    }

                    if (deviceFirmware != null)
                    {
                        var firmwareVersion = (await device.ReadVersionAsync(cancellationToken)).FirmwareVersion;
                        if (firmwareVersion != deviceFirmware.FirmwareVersion)
                        {
                            throw new HarpException(string.Format(
                                "The device firmware version was unexpected. Expected version {0} and device reported {1}.",
                                deviceFirmware.FirmwareVersion, firmwareVersion));
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    device.Transport.Close();
                    throw;
                }

                transport = device.Transport;
                transport.IgnoreErrors = ignoreErrors;
                transport.SetObserver(Observer.Create<HarpMessage>(
                    message =>
                    {
                        if (message.Address != OperationControl.Address)
                        {
                            Console.Error.WriteLine("Unexpected Harp data frame before operation control: {0}.", message);
                            return;
                        }

                        transport.SetObserver(observer);
                    },
                    observer.OnError,
                    observer.OnCompleted));
            }

            var writeOpCtrl = OperationControl.FromPayload(MessageType.Write, controlPayload);
            transport.Write(writeOpCtrl);
            return transport;
        }

        async Task<ITransport> CreateTcpTransportAsync(
            string connectionName,
            bool ignoreErrors,
            OperationControlPayload controlPayload,
            IObserver<HarpMessage> observer,
            CancellationToken cancellationToken)
        {
            TcpClientsManager.RegisterExpectedTcpClient(connectionName, uid);

            var client = await TcpClientsManager.WaitForTcpClientAsync(connectionName, uid, cancellationToken).ConfigureAwait(false);

            ITransport transport;

            using (var device = new AsyncDevice(client, leaveOpen: true))
            {
                // FIXME: does not work!
                // try
                // {
                //     var whoAmI = await device.ReadWhoAmIAsync(cancellationToken);
                //     if (deviceId > 0 && whoAmI != deviceId)
                //     {
                //         throw new HarpException(string.Format(
                //             "The device ID {1} on {0} was unexpected. Check whether the correct device is connected to the specified TCP server.",
                //             connectionName, whoAmI));
                //     }

                //     if (deviceFirmware != null)
                //     {
                //         var firmwareVersion = await device.ReadFirmwareVersionAsync(cancellationToken);
                //         if (firmwareVersion != deviceFirmware.FirmwareVersion)
                //         {
                //             throw new HarpException(string.Format(
                //                 "The device firmware version was unexpected. Expected version {0} and device reported {1}.",
                //                 deviceFirmware.FirmwareVersion, firmwareVersion));
                //         }
                //     }
                // }
                // catch (OperationCanceledException)
                // {
                //     device.Transport.Close();
                //     throw;
                // }

                transport = device.Transport;
                transport.IgnoreErrors = ignoreErrors;
                transport.SetObserver(Observer.Create<HarpMessage>(
                    message =>
                    {
                        if (message.Address != OperationControl.Address)
                        {
                            Console.Error.WriteLine("Unexpected Harp data frame before operation control: {0}.", message);
                            return;
                        }

                        transport.SetObserver(observer);
                    },
                    observer.OnError,
                    observer.OnCompleted));
            }

            var writeOpCtrl = OperationControl.FromPayload(MessageType.Write, controlPayload);
            transport.Write(writeOpCtrl);
            return transport;
        }

        private void CloseTransport(ITransport transport, OperationControlPayload controlPayload)
        {
            try
            {
                controlPayload.OperationMode = OperationMode.Standby;
                controlPayload.DumpRegisters = false;
                var writeOpCtrl = OperationControl.FromPayload(MessageType.Write, controlPayload);
                transport.Write(writeOpCtrl);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is ObjectDisposedException || ex is SocketException)
            {
                // ignore port IO errors
            }
            finally { transport.Close(); }
        }
    }
}
