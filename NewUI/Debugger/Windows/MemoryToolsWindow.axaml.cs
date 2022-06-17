using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using Mesen.Debugger.Controls;
using Mesen.Debugger.ViewModels;
using Mesen.Interop;
using System.ComponentModel;
using Avalonia.Interactivity;
using Mesen.Debugger.Utilities;
using System.IO;
using Mesen.Utilities;
using Mesen.Config;
using Mesen.Debugger.Labels;
using System.Linq;
using System.Collections.Generic;

namespace Mesen.Debugger.Windows
{
	public class MemoryToolsWindow : Window, INotificationHandler
	{
		private HexEditor _editor;
		private MemoryToolsViewModel _model;
		private MemorySearchWindow? _searchWnd;

		public MemoryToolsWindow()
		{
			InitializeComponent();
#if DEBUG
			this.AttachDevTools();
#endif

			_editor = this.FindControl<HexEditor>("Hex");
			_model = new MemoryToolsViewModel(_editor);
			DataContext = _model;

			if(Design.IsDesignMode) {
				return;
			}

			_model.Config.LoadWindowSettings(this);
			_editor.ByteUpdated += editor_ByteUpdated;
		}

		public static void ShowInMemoryTools(MemoryType memType, int address)
		{
			MemoryToolsWindow wnd = DebugWindowManager.GetOrOpenDebugWindow(() => new MemoryToolsWindow());
			wnd.SetCursorPosition(memType, address);
		}

		public void SetCursorPosition(MemoryType memType, int address)
		{
			if(_model.AvailableMemoryTypes.Contains(memType)) {
				_model.Config.MemoryType = memType;
				_editor.SetCursorPosition(address, scrollToTop: true);
				_editor.Focus();
			}
		}

		protected override void OnClosing(CancelEventArgs e)
		{
			base.OnClosing(e);
			_model.Config.SaveWindowSettings(this);
		}

		protected override void OnOpened(EventArgs e)
		{
			base.OnOpened(e);
			InitializeActions();
			_editor.Focus();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void OnSettingsClick(object sender, RoutedEventArgs e)
		{
			_model.Config.ShowOptionPanel = !_model.Config.ShowOptionPanel;
		}

		private void editor_ByteUpdated(object? sender, ByteUpdatedEventArgs e)
		{
			DebugApi.SetMemoryValue(_model.Config.MemoryType, (uint)e.ByteOffset, e.Value);
		}

		private void InitializeActions()
		{
			DebugConfig cfg = ConfigManager.Config.Debug;

			DebugShortcutManager.CreateContextMenu(_editor, new ContextMenuAction[] {
				GetMarkSelectionAction(),
				new ContextMenuSeparator(),
				GetAddWatchAction(),
				GetEditBreakpointAction(),
				GetEditLabelAction(),
				new ContextMenuSeparator(),
				GetViewInDebuggerAction(),
				new ContextMenuSeparator(),
				new ContextMenuAction() {
					ActionType = ActionType.Copy,
					IsEnabled = () => _editor.SelectionLength > 0,
					OnClick = () => _editor.CopySelection(),
					Shortcut = () => cfg.Shortcuts.Get(DebuggerShortcut.Copy)
				},
				new ContextMenuAction() {
					ActionType = ActionType.Paste,
					OnClick = () => _editor.PasteSelection(),
					Shortcut = () => cfg.Shortcuts.Get(DebuggerShortcut.Paste)
				},
				new ContextMenuSeparator(),
				new ContextMenuAction() {
					ActionType = ActionType.SelectAll,
					OnClick = () => _editor.SelectAll(),
					Shortcut = () => cfg.Shortcuts.Get(DebuggerShortcut.SelectAll)
				},
			});

			_model.FileMenuItems = _model.AddDisposables(new List<ContextMenuAction>() {
				GetImportAction(),
				GetExportAction(),
				new ContextMenuSeparator(),
				SaveRomActionHelper.GetSaveRomAction(this),
				SaveRomActionHelper.GetSaveEditsAsIpsAction(this),
				new ContextMenuSeparator(),
				new ContextMenuAction() {
					ActionType = ActionType.LoadTblFile,
					OnClick = async () => {
						string? filename = await FileDialogHelper.OpenFile(null, this, FileDialogHelper.TblExt);
						if(filename != null) {
							string[] tblData = File.ReadAllLines(filename);
							_model.TblConverter = TblLoader.Load(tblData);
							DebugWorkspaceManager.Workspace.TblMappings = tblData;
						}
					}
				},
				new ContextMenuAction() {
					ActionType = ActionType.ResetTblMappings,
					OnClick = () => {
						_model.TblConverter = null;
						DebugWorkspaceManager.Workspace.TblMappings = Array.Empty<string>();
					}
				},
				new ContextMenuSeparator(),
				new ContextMenuAction() {
					ActionType = ActionType.Exit,
					OnClick = () => Close()
				}
			});

			_model.SearchMenuItems = _model.AddDisposables(new List<ContextMenuAction>() {
				new ContextMenuAction() {
					ActionType = ActionType.GoToAddress,
					Shortcut = () => ConfigManager.Config.Debug.Shortcuts.Get(DebuggerShortcut.GoTo),
					OnClick = async () => {
						int? address = await new GoToWindow(DebugApi.GetMemorySize(_model.Config.MemoryType) - 1).ShowCenteredDialog<int?>(this);
						if(address != null) {
							_editor.SetCursorPosition(address.Value, scrollToTop: true);
							_editor.Focus();
						}
					}
				},
				new ContextMenuAction() {
					ActionType = ActionType.GoToAll,
					Shortcut = () => ConfigManager.Config.Debug.Shortcuts.Get(DebuggerShortcut.GoToAll),
					OnClick = async () => {
						GoToDestination? dest = await GoToAllWindow.Open(this, _model.Config.MemoryType.ToCpuType(), GoToAllOptions.ShowOutOfScope, DebugWorkspaceManager.SymbolProvider);
						if(dest?.RelativeAddress?.Type == _model.Config.MemoryType) {
							SetCursorPosition(dest.RelativeAddress.Value.Type, dest.RelativeAddress.Value.Address);
						} else if(dest?.AbsoluteAddress != null) {
							SetCursorPosition(dest.AbsoluteAddress.Value.Type, dest.AbsoluteAddress.Value.Address);
						}
					}
				},
				new ContextMenuSeparator(),
				new ContextMenuAction() {
					ActionType = ActionType.Find,
					Shortcut = () => ConfigManager.Config.Debug.Shortcuts.Get(DebuggerShortcut.Find),
					OnClick = () => OpenSearchWindow()
				},
				new ContextMenuAction() {
					ActionType = ActionType.FindPrev,
					Shortcut = () => ConfigManager.Config.Debug.Shortcuts.Get(DebuggerShortcut.FindPrev),
					OnClick = () => Find(SearchDirection.Backward)
				},
				new ContextMenuAction() {
					ActionType = ActionType.FindNext,
					Shortcut = () => ConfigManager.Config.Debug.Shortcuts.Get(DebuggerShortcut.FindNext),
					OnClick = () => Find(SearchDirection.Forward)
				},
			});

			_model.ToolbarItems = _model.AddDisposables(new List<ContextMenuAction>() { 
				GetImportAction(),
				GetExportAction(),
			});

			DebugShortcutManager.RegisterActions(this, _model.FileMenuItems);
			DebugShortcutManager.RegisterActions(this, _model.SearchMenuItems);
		}

		private void OpenSearchWindow()
		{
			if(_searchWnd == null) {
				_searchWnd = new MemorySearchWindow(_model.Search, _model);
				_searchWnd.Closed += (s, e) => _searchWnd = null;
				_searchWnd.ShowCenteredWithParent(this);
			} else {
				if(_searchWnd.WindowState == WindowState.Minimized) {
					_searchWnd.WindowState = WindowState.Normal;
				}
				_searchWnd.Activate();
			}
		}

		private void Find(SearchDirection direction)
		{
			if(!_model.Search.IsValid) {
				OpenSearchWindow();
			} else {
				_model.Find(direction);
			}
		}

		private ContextMenuAction GetViewInDebuggerAction()
		{
			AddressInfo? GetAddress()
			{
				MemoryType memType = _model.Config.MemoryType;
				if(_editor.SelectionLength <= 1 && !memType.IsPpuMemory()) {
					AddressInfo relAddr;
					if(!memType.IsRelativeMemory()) {
						relAddr = DebugApi.GetRelativeAddress(new AddressInfo() { Address = _model.SelectionStart, Type = memType }, memType.ToCpuType());
						return relAddr.Address >= 0 ? relAddr : null;
					} else {
						return new AddressInfo() { Address = _model.SelectionStart, Type = memType };
					}
				}
				return null;
			}

			return new ContextMenuAction() {
				ActionType = ActionType.ViewInDebugger,
				Shortcut = () => ConfigManager.Config.Debug.Shortcuts.Get(DebuggerShortcut.MemoryViewer_ViewInDebugger),
				IsEnabled = () => GetAddress() != null,
				HintText = () => GetAddressRange(),
				OnClick = () => {
					AddressInfo? relAddr = GetAddress();
					if(relAddr?.Address >= 0) {
						CpuType cpuType = relAddr.Value.Type.ToCpuType();
						DebuggerWindow.OpenWindowAtAddress(cpuType, relAddr.Value.Address);
					}
				}
			};
		}

		private ContextMenuAction GetImportAction()
		{
			return new ContextMenuAction() {
				ActionType = ActionType.Import,
				Shortcut = () => ConfigManager.Config.Debug.Shortcuts.Get(DebuggerShortcut.MemoryViewer_Import),
				IsEnabled = () => !_model.Config.MemoryType.IsRelativeMemory(),
				OnClick = () => Import()
			};
		}

		private ContextMenuAction GetExportAction()
		{
			return new ContextMenuAction() {
				ActionType = ActionType.Export,
				Shortcut = () => ConfigManager.Config.Debug.Shortcuts.Get(DebuggerShortcut.MemoryViewer_Export),
				OnClick = () => Export()
			};
		}

		public async void Import()
		{
			string? filename = await FileDialogHelper.OpenFile(ConfigManager.DebuggerFolder, this, FileDialogHelper.DmpExt);
			if(filename != null) {
				byte[] dmpData = File.ReadAllBytes(filename);
				DebugApi.SetMemoryState(_model.Config.MemoryType, dmpData, dmpData.Length);
			}
		}

		public async void Export()
		{
			string name = EmuApi.GetRomInfo().GetRomName() + " - " + _model.Config.MemoryType.ToString() + ".dmp";
			string? filename = await FileDialogHelper.SaveFile(ConfigManager.DebuggerFolder, name, this, FileDialogHelper.DmpExt);
			if(filename != null) {
				File.WriteAllBytes(filename, DebugApi.GetMemoryState(_model.Config.MemoryType));
			}
		}

		private ContextMenuAction GetEditLabelAction()
		{
			AddressInfo? GetAddress(MemoryType memType, int address)
			{
				if(memType.IsRelativeMemory()) {
					AddressInfo relAddress = new AddressInfo() {
						Address = address,
						Type = memType
					};

					AddressInfo absAddress = DebugApi.GetAbsoluteAddress(relAddress);
					return absAddress.Address >= 0 && absAddress.Type.SupportsLabels() ? absAddress : null;
				} else {
					return memType.SupportsLabels() ? new AddressInfo() { Address = address, Type = memType } : null;
				}
			}

			return new ContextMenuAction() {
				ActionType = ActionType.EditLabel,
				HintText = () => "$" + _editor.SelectionStart.ToString("X2"),
				Shortcut = () => ConfigManager.Config.Debug.Shortcuts.Get(DebuggerShortcut.MemoryViewer_EditLabel),

				IsEnabled = () => GetAddress(_model.Config.MemoryType, _editor.SelectionStart) != null,

				OnClick = () => {
					AddressInfo? addr = GetAddress(_model.Config.MemoryType, _editor.SelectionStart);
					if(addr == null) {
						return;
					}

					CodeLabel? label = LabelManager.GetLabel((uint)addr.Value.Address, addr.Value.Type);
					if(label == null) {
						label = new CodeLabel() {
							Address = (uint)addr.Value.Address,
							MemoryType = addr.Value.Type
						};
					}

					LabelEditWindow.EditLabel(label.MemoryType.ToCpuType(), this, label);
				}
			};
		}

		private ContextMenuAction GetAddWatchAction()
		{
			return new ContextMenuAction() {
				ActionType = ActionType.AddWatch,
				HintText = () => GetAddressRange(),
				Shortcut = () => ConfigManager.Config.Debug.Shortcuts.Get(DebuggerShortcut.MemoryViewer_AddToWatch),

				IsEnabled = () => _model.Config.MemoryType.SupportsWatch(),

				OnClick = () => {
					string[] toAdd = Enumerable.Range(_editor.SelectionStart, Math.Max(1, _editor.SelectionLength)).Select((num) => $"[${num.ToString("X2")}]").ToArray();
					WatchManager.GetWatchManager(_model.Config.MemoryType.ToCpuType()).AddWatch(toAdd);
				}
			};
		}

		private ContextMenuAction GetEditBreakpointAction()
		{
			return new ContextMenuAction() {
				ActionType = ActionType.EditBreakpoint,
				HintText = () => GetAddressRange(),
				Shortcut = () => ConfigManager.Config.Debug.Shortcuts.Get(DebuggerShortcut.MemoryViewer_EditBreakpoint),

				OnClick = () => {
					uint startAddress = (uint)_editor.SelectionStart;
					uint endAddress = (uint)(_editor.SelectionStart + Math.Max(1, _editor.SelectionLength) - 1);

					MemoryType memType = _model.Config.MemoryType;
					Breakpoint? bp = BreakpointManager.GetMatchingBreakpoint(startAddress, endAddress, memType);
					if(bp == null) {
						bp = new Breakpoint() { 
							MemoryType = memType, 
							CpuType = memType.ToCpuType(), 
							StartAddress = startAddress,
							EndAddress = endAddress,
							BreakOnWrite = true,
							BreakOnRead = true
						};
						if(bp.IsCpuBreakpoint) {
							bp.BreakOnExec = true;
						}
					}

					BreakpointEditWindow.EditBreakpoint(bp, this);
				}
			};
		}

		private ContextMenuAction GetMarkSelectionAction()
		{
			return MarkSelectionHelper.GetAction(
				() => _model.Config.MemoryType,
				() => _model.SelectionStart,
				() => _model.SelectionStart + _model.SelectionLength - 1,
				() => { }
			);
		}

		private string GetAddressRange()
		{
			string address = "$" + _editor.SelectionStart.ToString("X2");
			if(_editor.SelectionLength > 1) {
				address += "-$" + (_editor.SelectionStart + _editor.SelectionLength - 1).ToString("X2");
			}
			return address;
		}

		public void ProcessNotification(NotificationEventArgs e)
		{
			switch(e.NotificationType) {
				case ConsoleNotificationType.PpuFrameDone:
				case ConsoleNotificationType.CodeBreak:
					Dispatcher.UIThread.Post(() => {
						_editor.InvalidateVisual();
					});
					break;

				case ConsoleNotificationType.GameLoaded:
					_model.UpdateAvailableMemoryTypes();
					break;
			}
		}
	}
}
