using Bonsai.Expressions;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace Bonsai.Harp
{
    /// <summary>
    /// Provides validation to ensure TCP server names are unique in the workflow.
    /// </summary>
    public class TcpServerNameConverter : StringConverter
    {
        /// <inheritdoc/>
        public override bool IsValid(ITypeDescriptorContext context, object value)
        {
            if (!base.IsValid(context, value)) return false;
            if (value is not string name || string.IsNullOrWhiteSpace(name)) return true;
            return !HasDuplicate(context, name);
        }

        /// <inheritdoc/>
        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            var converted = base.ConvertFrom(context, culture, value);
            if (converted is string name && !string.IsNullOrWhiteSpace(name) && HasDuplicate(context, name))
            {
                throw new ArgumentException(
                    string.Format("A TCP server named '{0}' already exists in the workflow.", name),
                    nameof(value));
            }

            return converted;
        }

        static bool HasDuplicate(ITypeDescriptorContext context, string name)
        {
            if (context == null || string.IsNullOrWhiteSpace(name)) return false;

            var workflowBuilder = (WorkflowBuilder)context.GetService(typeof(WorkflowBuilder));
            if (workflowBuilder?.Workflow == null) return false;

            var current = GetCurrentServer(context.Instance);

            return workflowBuilder.Workflow.Descendants()
                .Select(builder => ExpressionBuilder.GetWorkflowElement(builder) as CreateTcpServer)
                .Any(server =>
                    server != null &&
                    !ReferenceEquals(server, current) &&
                    string.Equals(server.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        static CreateTcpServer GetCurrentServer(object instance)
        {
            if (instance is CreateTcpServer server) return server;

            if (instance is object[] array)
            {
                foreach (var item in array)
                {
                    if (item is CreateTcpServer arrayServer) return arrayServer;
                }
            }

            return null;
        }
    }
}
