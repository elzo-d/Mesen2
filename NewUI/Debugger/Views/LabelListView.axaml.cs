using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Debugger.ViewModels;
using Mesen.Debugger.Labels;
using Mesen.Debugger.Windows;
using System;
using static Mesen.Debugger.ViewModels.LabelListViewModel;
using Avalonia.Input;
using DataBoxControl;

namespace Mesen.Debugger.Views
{
	public class LabelListView : UserControl
	{
		public LabelListView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		protected override void OnDataContextChanged(EventArgs e)
		{
			if(DataContext is LabelListViewModel model) {
				model.InitContextMenu(this);
			}
			base.OnDataContextChanged(e);
		}

		private void OnGridDoubleClick(object sender, RoutedEventArgs e)
		{
			DataBox grid = (DataBox)sender;
			if(DataContext is LabelListViewModel listModel && grid.Selection.SelectedItem is LabelViewModel label) {
				LabelEditWindow.EditLabel(listModel.CpuType, this, label.Label);
			}
		}
	}
}
