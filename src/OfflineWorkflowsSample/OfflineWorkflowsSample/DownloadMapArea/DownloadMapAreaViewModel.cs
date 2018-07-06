﻿using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Portal;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.Tasks.Offline;
using Esri.ArcGISRuntime.UI;
using OfflineWorkflowsSample.Infrastructure;
using OfflineWorkflowsSample.Models;
using Prism.Commands;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Windows.UI;

namespace OfflineWorkflowsSample.DownloadMapArea
{
    public class DownloadMapAreaViewModel : BaseViewModel
    {
        private readonly ArcGISPortal _portal;
        private GraphicsOverlay _areasOverlay;
        private DelegateCommand _downloadMapAreaCommand;
        private DelegateCommand<string> _syncMapAreaCommand;

        public DownloadMapAreaViewModel(ArcGISPortal portal)
        {
            _portal = portal ?? throw new ArgumentNullException(nameof(portal));
            _areasOverlay = new GraphicsOverlay()
            {
                Renderer = new SimpleRenderer()
                {
                    Symbol = new SimpleFillSymbol(
                      SimpleFillSymbolStyle.Solid,
                      Infrastructure.ColorHelper.GetSolidColorBrush("#4C080808").Color,
                      new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, Colors.Yellow, 1))
                }
            };

            var graphicsOverlays = new GraphicsOverlayCollection()
                        {
                           _areasOverlay
                        };
            GraphicsOverlays = graphicsOverlays;
            _downloadMapAreaCommand = new DelegateCommand(DownloadMapArea, CanDownloadMapArea);
            _syncMapAreaCommand = new DelegateCommand<string>(SyncMapArea, CanSyncMapArea);
            Initialize();
        }

        private async void DownloadMapArea()
        {
            try
            {
                IsBusy = true;
                IsBusyText = "Downloading selected area...";

                var offlineDataFolder = Path.Combine(OfflineDataStorageHelper.GetDataFolder(),
                     "PreplannedMapAreas", Map.Item.ItemId);

                // If temporary data folder exists remove it
                if (Directory.Exists(offlineDataFolder))
                    Directory.Delete(offlineDataFolder, true);
                // If temporary data folder doesn't exists, create it
                if (!Directory.Exists(offlineDataFolder))
                    Directory.CreateDirectory(offlineDataFolder);

                // Step 1 Create task that is used to access map information and download areas
                var task = await OfflineMapTask.CreateAsync(Map);

                // Step 2 Create job that handles the download and provides status information 
                // about the progress
                var job = task.DownloadPreplannedOfflineMap(SelectedMapArea.GetArea, offlineDataFolder);
                job.ProgressChanged += async (s, e) =>
                {
                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => {
                        var generateOfflineMapJob = s as DownloadPreplannedOfflineMapJob;
                        ProgressPercentage = generateOfflineMapJob.Progress.ToString() + "%";
                    });
                };
                
                // Step 3 Run the job and wait the results
                var results = await job.GetResultAsync();

                // Step 4 Check errors 
                if (results.HasErrors)
                {
                    // If one or more layers fails, layer errors are populated with corresponding errors.
                    foreach (var layerError in results.LayerErrors)
                    {
                        Debug.WriteLine($"Error occurred on {layerError.Key.Name} : {layerError.Value.Message}");
                    }
                    foreach (var tableError in results.TableErrors)
                    {
                        Debug.WriteLine($"Error occurred on {tableError.Key.TableName} : {tableError.Value.Message}");
                    }
                }
                // Step 5 Set offline map to use
                Map = results.OfflineMap;

                InOnlineMode = false;
                _areasOverlay.Graphics.Clear();
            }
            catch (Exception ex)
            {
                throw;
            }
            finally
            {
                RefreshCommands();
                IsBusy = false;
                IsBusyText = string.Empty;
            }
        }

        private bool CanDownloadMapArea()
        {
            if (SelectedMapArea != null)
                return true;
            return false;
        }

        public ICommand DownloadMapAreaCommand => _downloadMapAreaCommand;

        // Sync map area
        private async void SyncMapArea(string parameter)
        {
            try
            {
                IsBusy = true;
                var synchronizationMode = SyncDirection.Bidirectional;
                switch (parameter)
                {
                    case "Download":
                        synchronizationMode = SyncDirection.Download;
                        IsBusyText = "Getting latest updates...";
                        break;
                    case "Upload":
                        synchronizationMode = SyncDirection.Upload;
                        IsBusyText = "Pushing local changes...";
                        break;
                    default:
                        synchronizationMode = SyncDirection.Bidirectional;
                        IsBusyText = "Synchronazing features...";
                        break;
                }

                // Create task that is used to synchronize the offline map
                var task = await OfflineMapSyncTask.CreateAsync(Map);
                // Create parameters 
                var parameters = new OfflineMapSyncParameters()
                {
                    SyncDirection = synchronizationMode
                };

                // Create job that does the work asyncronously
                var job = task.SyncOfflineMap(parameters);
                job.ProgressChanged += async (s, e) =>
                {
                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => {
                        var offlineMapSyncJob = s as OfflineMapSyncJob;
                        ProgressPercentage = offlineMapSyncJob.Progress.ToString() + "%";
                    });
                };

                // Run the job and wait the results
                var results = await job.GetResultAsync();
                if (results.HasErrors)
                {
                    // handle nicely
                }

                foreach (var message in job.Messages.Select(x =>x.Message))
                {
                    Debug.WriteLine(message);
                }
            }
            catch (Exception ex)
            {
                // handle nicely
                throw;
            }
            finally
            {
                RefreshCommands();
                IsBusy = false;
                IsBusyText = string.Empty;
            }
        }

        private bool CanSyncMapArea(string parameter)
        {
            if (InOnlineMode == false)
                return true;
            return false;
        }

        public ICommand SyncMapAreaCommand => _syncMapAreaCommand;

        private GraphicsOverlayCollection _graphicsOverlays;
        public GraphicsOverlayCollection GraphicsOverlays
        {
            get { return _graphicsOverlays; }
            set { SetProperty(ref _graphicsOverlays, value); }
        }

        private ObservableCollection<MapAreaModel> _mapAreas = new ObservableCollection<MapAreaModel>();
        public ObservableCollection<MapAreaModel> MapAreas
        {
            get { return _mapAreas; }
            set { SetProperty(ref _mapAreas, value); }
        }

        private MapAreaModel _selectedMapArea;
        public MapAreaModel SelectedMapArea
        {
            get { return _selectedMapArea; }
            set { SetProperty(ref _selectedMapArea, value); UpdateMap(); RefreshCommands(); }
        }

        private async void UpdateMap()
        {
            _areasOverlay.SelectionColor = Colors.Red;
            _areasOverlay.ClearSelection();
            var selectedGraphic = _areasOverlay.Graphics.FirstOrDefault(x => x.Attributes["Name"].ToString() == SelectedMapArea.Title);
            if (selectedGraphic != null)
            {
                selectedGraphic.IsSelected = true;
            }
            await MapViewService.SetViewpointGeometryAsync(selectedGraphic.Geometry, 20d);
        }

        public void RefreshCommands()
        {
            _downloadMapAreaCommand.RaiseCanExecuteChanged();
            _syncMapAreaCommand.RaiseCanExecuteChanged();
        }

        private bool _inOnlineMode = true;
        public bool InOnlineMode
        {
            get { return _inOnlineMode; }
            set { SetProperty(ref _inOnlineMode, value); }
        }


        private async void Initialize()
        {
            try     
            {
                IsBusy = true;
                IsBusyText = "Loading map...";

                // Load map from portal
                var webmapItem = await PortalItem.CreateAsync(
                    _portal, "acc027394bc84c2fb04d1ed317aac674");
                Map = new Map(webmapItem);
                await Map.LoadAsync();

                // Create new task to 
                var offlineMapTask = await OfflineMapTask.CreateAsync(Map);
                
                // Get list of areas
                var preplannedMapAreas = await offlineMapTask.GetPreplannedMapAreasAsync();

                // Create UI from the areas
                foreach (var preplannedMapArea in preplannedMapAreas.OrderBy(x => x.PortalItem.Title))
                {
                    // Load area to get the metadata 
                    await preplannedMapArea.LoadAsync();
                    // Using a custom model for easier visualization
                    var model = new MapAreaModel(preplannedMapArea);
                    MapAreas.Add(model);
                    // Graphic that shows the area in the map
                    var graphic = new Graphic(preplannedMapArea.AreaOfInterest);
                    graphic.Attributes.Add("Name", preplannedMapArea.PortalItem.Title);
                    _areasOverlay.Graphics.Add(graphic);
                }

                IsBusy = false;
                IsBusyText = string.Empty;
            }
            catch (Exception ex)
            {
                // Handle
            }
        }
    }
}