using Bonsai.Expressions;
using System;
using System.ComponentModel;
using System.Linq;

namespace Bonsai.Harp.Net
{
	/// <summary>
	/// Provides a type converter to list the names of available TCP connections.
	/// </summary>
	public class ConnectionNameConverter : StringConverter
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
			if (context != null)
			{
				var workflowBuilder = (WorkflowBuilder)context.GetService(typeof(WorkflowBuilder));
				if (workflowBuilder != null)
				{
					var connectionNames = (from builder in workflowBuilder.Workflow.Descendants()
											let createTcpServer = ExpressionBuilder.GetWorkflowElement(builder) as CreateTcpServer
											where createTcpServer != null && !string.IsNullOrEmpty(createTcpServer.Name)
											select createTcpServer.Name)
										   .Distinct()
										   .ToArray();

					if (connectionNames.Length > 0)
					{
						return new StandardValuesCollection(connectionNames);
					}
				}
			}

			return new StandardValuesCollection(new[] { string.Empty });
		}
	}
}
