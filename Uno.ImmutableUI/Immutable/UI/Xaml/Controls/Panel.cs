using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Immutable.UI.Xaml.Controls
{
    partial class PanelModel
	{
		// The generated equality comparer does not compare using derived equality
		private static IEqualityComparer Children_CustomComparer => EqualityComparer<object>.Default;
	}
}
