﻿// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using Microsoft.VisualStudio.ComponentModelHost;
using VsChromium.Core.Ipc.TypedMessages;
using VsChromium.Core.Logging;
using VsChromium.Features.AutoUpdate;
using VsChromium.ServerProxy;
using VsChromium.Threads;
using VsChromium.Views;
using VsChromium.Wpf;

namespace VsChromium.Features.ToolWindows.SourceExplorer {
  /// <summary>
  /// Interaction logic for SourceExplorerControl.xaml
  /// </summary>
  public partial class SourceExplorerControl : UserControl {
    // For controlling scrolling inside tree view.
    private double _treeViewHorizScrollPos;
    private bool _treeViewResetHorizScroll;
    private ScrollViewer _treeViewScrollViewer;

    private readonly IProgressBarTracker _progressBarTracker;
    private IStatusBar _statusBar;
    private ITypedRequestProcessProxy _typedRequestProcessProxy;
    private IUIRequestProcessor _uiRequestProcessor;
    private bool _swallowsRequestBringIntoView = true;
    private SourceExplorerController _controller;

    public SourceExplorerControl() {
      InitializeComponent();

      base.DataContext = new SourceExplorerViewModel();

      _progressBarTracker = new ProgressBarTracker(ProgressBar);

      InitComboBox(FileNamesSearch, new ComboBoxInfo {
        SearchFunction = SearchFilesNames
      });
      InitComboBox(DirectoryNamesSearch, new ComboBoxInfo {
        SearchFunction = SearchDirectoryNames
      });
      InitComboBox(FileContentsSearch, new ComboBoxInfo {
        SearchFunction = SearchText
      });
    }

    public void OnToolWindowCreated(IServiceProvider serviceProvider) {
      var componentModel = (IComponentModel)serviceProvider.GetService(typeof(SComponentModel));

      _uiRequestProcessor = componentModel.DefaultExportProvider.GetExportedValue<IUIRequestProcessor>();
      _statusBar = componentModel.DefaultExportProvider.GetExportedValue<IStatusBar>();
      _typedRequestProcessProxy = componentModel.DefaultExportProvider.GetExportedValue<ITypedRequestProcessProxy>();
      
      _typedRequestProcessProxy.EventReceived += TypedRequestProcessProxy_EventReceived;

      var standarImageSourceFactory = componentModel.DefaultExportProvider.GetExportedValue<IStandarImageSourceFactory>();
      var clipboard = componentModel.DefaultExportProvider.GetExportedValue<IClipboard>();
      var windowsExplorer = componentModel.DefaultExportProvider.GetExportedValue<IWindowsExplorer>();
      var openDocumentHelper = componentModel.DefaultExportProvider.GetExportedValue<IOpenDocumentHelper>();
      var synchronizationContextProvider = componentModel.DefaultExportProvider.GetExportedValue<ISynchronizationContextProvider>();
      _controller = new SourceExplorerController(
        this,
        _uiRequestProcessor,
        _progressBarTracker,
        standarImageSourceFactory,
        windowsExplorer,
        clipboard,
        synchronizationContextProvider,
        openDocumentHelper);

      // TODO(rpaquay): leaky abstraction
      ViewModel.SetController(Controller);
      // TODO(rpaquay): leaky abstraction
      ViewModel.OnToolWindowCreated(serviceProvider);

      FetchFilesystemTree();
    }

    public SourceExplorerViewModel ViewModel {
      get {
        return (SourceExplorerViewModel) DataContext;
      }
    }

    public UpdateInfo UpdateInfo {
      get { return ViewModel.UpdateInfo; }
      set { ViewModel.UpdateInfo = value; }
    }

    public ISourceExplorerController Controller {
      get { return _controller; }
    }

    private void InitComboBox(EditableComboBox comboBox, ComboBoxInfo info) {
      comboBox.DataContext = new StringListViewModel();
      comboBox.TextChanged += (s, e) => info.SearchFunction();
      comboBox.KeyDown += (s, e) => {
        if (e.Key == Key.Return || e.Key == Key.Enter)
          info.SearchFunction();
      };
      comboBox.PrePreviewKeyDown += (s, e) => {
        if (e.KeyboardDevice.Modifiers == ModifierKeys.None &&
            e.Key == Key.Down &&
            !comboBox.IsDropDownOpen) {
          FileTreeView.Focus();
          e.Handled = true;
        }
      };
    }

    private void SearchFilesNames() {
      Controller.SearchFilesNames(FileNamesSearch.Text);
    }

    private void SearchDirectoryNames() {
      Controller.SearchDirectoryNames(DirectoryNamesSearch.Text);
    }

    private void SearchText() {
      Controller.SearchText(FileContentsSearch.Text);
    }

    private void TypedRequestProcessProxy_EventReceived(TypedEvent typedEvent) {
      DispatchFileSystemTreeComputing(typedEvent);
      DispatchFileSystemTreeComputed(typedEvent);
      DispatchSearchEngineFilesLoading(typedEvent);
      DispatchSearchEngineFilesLoaded(typedEvent);
      DispatchProgressReport(typedEvent);
    }

    private void DispatchProgressReport(TypedEvent typedEvent) {
      var @event = typedEvent as ProgressReportEvent;
      if (@event != null) {
        WpfUtilities.Post(this, () =>
          _statusBar.ReportProgress(@event.DisplayText, @event.Completed, @event.Total));
      }
    }

    private void DispatchFileSystemTreeComputing(TypedEvent typedEvent) {
      var @event = typedEvent as FileSystemTreeComputing;
      if (@event != null) {
        WpfUtilities.Post(this, () => {
          Logger.Log("FileSystemTree is being computed on server.");
          _progressBarTracker.Start(OperationsIds.FileSystemTreeComputing,
                                    "Loading files and directory names from file system.");
          ViewModel.FileSystemTreeComputing();
        });
      }
    }

    private void DispatchFileSystemTreeComputed(TypedEvent typedEvent) {
      var @event = typedEvent as FileSystemTreeComputed;
      if (@event != null) {
        WpfUtilities.Post(this, () => {
          _progressBarTracker.Stop(OperationsIds.FileSystemTreeComputing);
          if (@event.Error != null) {
            ViewModel.SetErrorResponse(@event.Error);
            return;
          }
          Logger.Log("New FileSystemTree bas been computed on server: version={0}.", @event.NewVersion);
          FetchFilesystemTree();
        });
      }
    }

    private void DispatchSearchEngineFilesLoading(TypedEvent typedEvent) {
      var @event = typedEvent as SearchEngineFilesLoading;
      if (@event != null) {
        Wpf.WpfUtilities.Post(this, () => {
          Logger.Log("Search engine is loading files on server.");
          _progressBarTracker.Start(OperationsIds.FilesLoading, "Loading files contents from file system.");
        });
      }
    }

    private void DispatchSearchEngineFilesLoaded(TypedEvent typedEvent) {
      var @event = typedEvent as SearchEngineFilesLoaded;
      if (@event != null) {
        WpfUtilities.Post(this, () => {
          _progressBarTracker.Stop(OperationsIds.FilesLoading);
          if (@event.Error != null) {
            ViewModel.SetErrorResponse(@event.Error);
            return;
          }
          Logger.Log("Search engine is done loading files on server.");
        });
      }
    }

    private void FetchFilesystemTree() {
      var request = new UIRequest() {
        Id = "GetFileSystemRequest",
        Request = new GetFileSystemRequest(),
        OnSuccess = (typedResponse) => {
          var response = (GetFileSystemResponse)typedResponse;
          Controller.SetFileSystemTree(response.Tree);
        },
        OnError = (errorResponse) => {
          ViewModel.SetErrorResponse(errorResponse);
        }
      };

      _uiRequestProcessor.Post(request);
    }

    private class ComboBoxInfo {
      public Action SearchFunction { get; set; }
    }

    private static class OperationsIds {
      public const string FileSystemTreeComputing = "file-system-collecting";
      public const string FilesLoading = "files-loading";
    }

    public void SwallowsRequestBringIntoView(bool value) {
      _swallowsRequestBringIntoView = value;
    }

    #region WPF Event handlers

    private void CancelSearchButton_Click(object sender, RoutedEventArgs e) {
      ViewModel.SwitchToFileSystemTree();
    }

    private void TreeViewItem_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e) {
      var tvi = sender as TreeViewItem;
      if (tvi == null)
        return;

      if (!tvi.IsSelected)
        return;

      if (Controller.ExecutedOpenCommandForItem(tvi.DataContext as TreeViewItemViewModel))
        e.Handled = true;
    }

    private void TreeViewItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e) {
      if (_swallowsRequestBringIntoView) {
        // This prevents the tree view for scrolling horizontally to make the
        // selected item as visibile as possible. This is useful for
        // "SearchText", as text extracts are usually wide enough to make tree
        // view navigation annoying when they are selected.
        e.Handled = true;
        return;
      }

      // Find the scroll viewer and hook up scroll changed event handler.
      if (this._treeViewScrollViewer == null) {
        this._treeViewScrollViewer = this.FileTreeView.Template.FindName("_tv_scrollviewer_", this.FileTreeView) as ScrollViewer;
        if (_treeViewScrollViewer != null) {
          this._treeViewScrollViewer.ScrollChanged += this.TreeViewScrollViewerScrollChanged;
        }
      }

      // If we got a scroll viewer, remember the horizontal offset so we can
      // restore it in the scroll changed event.
      if (_treeViewScrollViewer != null) {
        this._treeViewResetHorizScroll = true;
        this._treeViewHorizScrollPos = this._treeViewScrollViewer.HorizontalOffset;
      }
      e.Handled = false;
    }

    private void TreeViewScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e) {
      Debug.Assert(this._treeViewScrollViewer != null);

      if (this._treeViewResetHorizScroll) {
        this._treeViewScrollViewer.ScrollToHorizontalOffset(this._treeViewHorizScrollPos);
        this._treeViewResetHorizScroll = false;
      }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
      // Open the default web browser to the update URL.
      Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
      e.Handled = true;
    }

    private void FileTreeView_OnPreviewKeyDown(object sender, KeyEventArgs e) {
      if (e.Key == Key.Return) {
        e.Handled = Controller.ExecutedOpenCommandForItem(FileTreeView.SelectedItem as TreeViewItemViewModel);
      }
    }

    /// <summary>
    /// Ensures the item right-clicked on is selected before showing the context
    /// menu.
    /// </summary>
    private void FileTreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
      var source = e.OriginalSource as DependencyObject;
      if (source == null)
        return;

      var treeViewItem = WpfUtilities.VisualTreeGetParentOfType<TreeViewItem>(source);
      if (treeViewItem == null)
        return;

      treeViewItem.Focus();
      e.Handled = true;
    }

    #endregion
  }
}
