using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Immutable.UI.Xaml
{
    partial class ThicknessModel
    {
		public ThicknessModel(double value)
		{
			Top = Bottom = Left = Right = value;
		}
    }
}
