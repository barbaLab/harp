using System;
using System.ComponentModel;

namespace Bonsai.Harp.Net
{
    /// <summary>
    /// Provides a type converter that lists available TCP device names and disallows arbitrary input.
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
            return new StandardValuesCollection(Array.Empty<string>());
        }
    }
}
