using System.ComponentModel;

namespace Bonsai.Harp.Net
{
	/// <summary>
	/// Provides a type converter to list the names of devices connected to the selected TCP connection.
	/// </summary>
	public class DeviceNameConverter : StringConverter
	{
		/// <inheritdoc/>
		public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
		{
			return true;
		}

		/// <inheritdoc/>
		public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
		{
			return true;
		}

		/// <inheritdoc/>
		public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
		{

			var device = GetDevice(context == null ? null : context.Instance);
			if (device != null && !string.IsNullOrEmpty(device.ConnectionName))
			{
                TcpDeviceProbeRegistry.Clear(device.ConnectionName);
                TcpDeviceProbe.TryGetTcpDevices(context, device.ConnectionName);
				var deviceNames = TcpDeviceProbeRegistry.GetDeviceNames(device.ConnectionName);

                if (deviceNames.Length > 0)
				{
					return new StandardValuesCollection(deviceNames);
				}
			}

			return new StandardValuesCollection(new[] { string.Empty });
		}

		static Device GetDevice(object instance)
		{
			if (instance is Device device)
			{
				return device;
			}

			var array = instance as object[];
			if (array != null)
			{
				for (var i = 0; i < array.Length; i++)
				{
					if (array[i] is Device arrayDevice)
					{
						return arrayDevice;
					}
				}
			}

			return null;
		}
	}
}
