using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Immutable.UI.Xaml.Controls;
using Immutable.UI.Xaml;
using System.Collections.Immutable;
using Prism.Commands;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace ImmutableTest
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
		private int _counter;

		public MainPage()
        {
            this.InitializeComponent();

			Content = Build().Create();
        }

		void SetState() => Build().Apply(Content);

		DelegateCommand _push => new DelegateCommand(() => {
			_counter++;
			SetState();
		});

		StackPanelModel Build() =>
			
			new StackPanelModel.Builder
			{
				Padding = new ThicknessModel(42),
				Children = ImmutableArray.Create<UIElementModel>(
					new TextBlockModel.Builder { Text = _counter.ToString(), FontSize = 42 }.ToImmutable(),
					new ButtonModel.Builder { Content = "Increment", FontSize = 42, Command = _push }.ToImmutable()
				)
			};
	}
}
