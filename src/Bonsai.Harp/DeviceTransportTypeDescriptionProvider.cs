using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Bonsai.Harp
{
    /// <summary>
    /// Provides a type descriptor that filters device properties based on the selected transport mode.
    /// </summary>
    public sealed class DeviceTransportTypeDescriptionProvider : TypeDescriptionProvider
    {
        static readonly TypeDescriptionProvider parentProvider = TypeDescriptor.GetProvider(typeof(Device));

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceTransportTypeDescriptionProvider"/> class.
        /// </summary>
        public DeviceTransportTypeDescriptionProvider()
            : base(parentProvider)
        {
        }

        /// <summary>
        /// Returns a type descriptor that adapts the visible properties for the current device transport.
        /// </summary>
        /// <param name="objectType">The type of the object to describe.</param>
        /// <param name="instance">The object instance being described.</param>
        /// <returns>A type descriptor for the specified object.</returns>
        public override ICustomTypeDescriptor GetTypeDescriptor(Type objectType, object instance)
        {
            var parentDescriptor = base.GetTypeDescriptor(objectType, instance);
            return instance != null ? new DeviceTransportTypeDescriptor(instance, parentDescriptor) : parentDescriptor;
        }

        /// <summary>
        /// Describes the device properties that should be visible for the current transport.
        /// </summary>
        sealed class DeviceTransportTypeDescriptor : CustomTypeDescriptor
        {
            readonly Device device;

            /// <summary>
            /// Initializes a new instance of the <see cref="DeviceTransportTypeDescriptor"/> class.
            /// </summary>
            /// <param name="instance">The device instance being described.</param>
            /// <param name="parentDescriptor">The parent type descriptor.</param>
            public DeviceTransportTypeDescriptor(object instance, ICustomTypeDescriptor parentDescriptor)
                : base(parentDescriptor)
            {
                device = (Device)instance;
            }

            /// <summary>
            /// Gets the visible properties for the current transport mode.
            /// </summary>
            /// <param name="attributes">The attributes used to filter properties.</param>
            /// <returns>The filtered property collection.</returns>
            public override PropertyDescriptorCollection GetProperties(Attribute[] attributes)
            {
                var baseProperties = base.GetProperties(attributes);
                var filtered = new List<PropertyDescriptor>(baseProperties.Count);
                var useTcp = device.TransportMode == TransportMode.Tcp;

                foreach (PropertyDescriptor property in baseProperties)
                {
                    if (useTcp)
                    {
                        if (property.Name == nameof(Device.PortName))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (property.Name == nameof(Device.ConnectionName) ||
                            property.Name == nameof(Device.DeviceName))
                        {
                            continue;
                        }
                    }

                    filtered.Add(property);
                }

                return new PropertyDescriptorCollection(filtered.ToArray(), true);
            }

            /// <summary>
            /// Gets the visible properties for the current transport mode.
            /// </summary>
            /// <returns>The filtered property collection.</returns>
            public override PropertyDescriptorCollection GetProperties()
            {
                return GetProperties(Array.Empty<Attribute>());
            }
        }
    }
}
