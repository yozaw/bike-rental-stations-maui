using CommunityToolkit.Mvvm.ComponentModel;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.RealTime;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;

namespace BikeAvailability.ViewModel;

public partial class CityBikesViewModel : ObservableObject
{
    // シェアサイクル ステーションを表示するためのカスタム Dynamic EntityDataSource
    private CityBikesDataSource _cityBikesDataSource;
    // データソースからの動的エンティティの表示を処理する DynamicEntityLayer
    private DynamicEntityLayer _dynamicEntityLayer;
    private readonly object _thisLock = new();

    public CityBikesViewModel()
    {
        Init();
    }

    [ObservableProperty]
    private Esri.ArcGISRuntime.Mapping.Map _map;

    [ObservableProperty]
    private string _cityName = "HELLO CYCLING";

    private string _cityBikesUrl = "https://api-public.odpt.org/api/v4/gbfs/hellocycling/station_information.json";

    private MapPoint _initLocation = new MapPoint(139.76147890091, 35.654611251508, SpatialReferences.Wgs84);

    [ObservableProperty]
    private int _updateIntervalSeconds = 300; // 5分

    private readonly Dictionary<long, DynamicEntity> _favoriteBikeStations = new();

    [ObservableProperty]
    private List<DynamicEntity> _favoriteList = new();

    [ObservableProperty]
    private GraphicsOverlayCollection _graphicsOverlays = new();

    // 貸出可能台数に変更があるステーションを点滅させるためのグラフィックス オーバーレイ
    private GraphicsOverlay _flashOverlay;

    // 貸出可能台数の変化を追跡するための変数
    [ObservableProperty]
    private int _bikesAvailable;

    // クレジット表示する文字列
    [ObservableProperty]
    private string _bikesAttributionText = "OpenStreet株式会社 / 公共交通オープンデータ協議会";

    private void Init()
    {

        // 道路(夜)タイプのベースマップを使用して新しいマップを作成する
        var vectorTileUrl = "https://basemapstyles-api.arcgis.com/arcgis/rest/services/styles/v2/styles/arcgis/navigation-night?language=ja";
        ArcGISVectorTiledLayer vectorTiledLayer = new ArcGISVectorTiledLayer(new Uri(vectorTileUrl));
        Map = new Esri.ArcGISRuntime.Mapping.Map(new Basemap(vectorTiledLayer));

        // 更新されたフィーチャを点滅させるためのオーバーレイを作成する
        _flashOverlay = new GraphicsOverlay();

        GraphicsOverlays.Add(_flashOverlay);

    }

    public async Task<Viewpoint> ShowBikeStations()
    {
        // 既存の CityBikesDataSource をクリーンアップする
        if (_cityBikesDataSource != null)
        {
            await _cityBikesDataSource.DisconnectAsync();
            _cityBikesDataSource = null;
        }

        // 貸出可能台数の値をクリアする
        BikesAvailable = 0;

        // URL と取得間隔を使用してカスタム Dynamic EntityDataSource のインスタンスを作成する
        _cityBikesDataSource = new CityBikesDataSource(_cityBikesUrl, UpdateIntervalSeconds);

        // 接続が確立されたら、初期データセットをリクエストする
        _cityBikesDataSource.ConnectionStatusChanged += (s, e) =>
        {
            if (e == ConnectionStatus.Connected)
            {
                _ = _cityBikesDataSource.GetInitialBikeStations();
            }
        };

        // 作成される動的エンティティをリッスンし、最初の貸出可能な自転車台数を計算する
        _cityBikesDataSource.DynamicEntityReceived += (s, e) => CreateTotalBikeInventory(e.DynamicEntity);

        // 新しい観測データをリッスンする：更新がある場合は、ステーションを点滅し、貸出可能台数の変化の値を更新する
        _cityBikesDataSource.DynamicEntityObservationReceived += async (s, e) =>
        {
            var bikesAdded = (int)e.Observation.Attributes["InventoryChange"];
            if (bikesAdded == 0) { return; }

            UpdateBikeInventory(bikesAdded); // note: this might be negative if more bikes were taken than returned.
            await Task.Run(() => FlashDynamicEntityObservationAsync(e.Observation.Geometry as MapPoint, bikesAdded > 0));
        };

        // 既存の DynamicEntityLayer をマップから削除する
        Map.OperationalLayers.Remove(_dynamicEntityLayer);
        _dynamicEntityLayer = null;

        // 新しい CityBikesDataSource を使用して、新しい DynamicEntityLayer を作成しマップに追加する
        _dynamicEntityLayer = new DynamicEntityLayer(_cityBikesDataSource)
        {
            Renderer = CreateBikeStationsRenderer()
        };
        Map.OperationalLayers.Add(_dynamicEntityLayer);

        // 初期表示位置のビューポイントを返す
        return new Viewpoint(_initLocation, 130000);
    }

    private static Renderer CreateBikeStationsRenderer()
    {
        // 貸出可能な自転車の数に応じてシェアサイクル ステーションを表示するレンダリングを作成する
        // 貸出可能な自転車が多いほど、円が大きくなる
        var classBreaksRenderer = new ClassBreaksRenderer
        {
            FieldName = "BikesAvailable"
        };
        var noneSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.LightGray, 10);
        var fewSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.LightYellow, 12);
        var lotsSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.LightGreen, 14);
        var plentySymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Green, 16);
        var defaultSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Triangle, System.Drawing.Color.Beige, 14);

        var classBreakNo = new ClassBreak("no bikes", "None", 0, 0, noneSymbol);
        var classBreakFew = new ClassBreak("1-4 bikes", "A few", 0, 4, fewSymbol);
        var classBreakLots = new ClassBreak("5-8 bikes", "Lots", 4, 9, lotsSymbol);
        var classBreakPlenty = new ClassBreak("9-999 bikes", "Plenty", 9, 999, plentySymbol);

        classBreaksRenderer.ClassBreaks.Add(classBreakNo);
        classBreaksRenderer.ClassBreaks.Add(classBreakFew);
        classBreaksRenderer.ClassBreaks.Add(classBreakLots);
        classBreaksRenderer.ClassBreaks.Add(classBreakPlenty);
        classBreaksRenderer.DefaultSymbol = defaultSymbol;

        return classBreaksRenderer;
    }

    public CalloutDefinition GetCalloutDefinitionForStation(DynamicEntityObservation bikeStation,
        string favoriteIconUrl, string nonFavIconUrl)
    {
        var dynEntity = bikeStation.GetDynamicEntity();

        // シェアサイクル ステーション名と貸出/駐車可能な台数をコールアウトに表示する
        var stationName = bikeStation.Attributes["StationName"].ToString();
        var availableBikes = bikeStation.Attributes["BikesAvailable"].ToString();
        var emptySlots = bikeStation.Attributes["EmptySlots"].ToString();

        var calloutDef = new CalloutDefinition(stationName,
                             $"貸出可能: {availableBikes} 台 \n 駐車可能: {emptySlots} 台")
        {
            ButtonImage = _favoriteBikeStations.ContainsKey(dynEntity.EntityId) ?
                                       new RuntimeImage(new Uri(favoriteIconUrl)) :
                                       new RuntimeImage(new Uri(nonFavIconUrl)),
            Tag = dynEntity
        };

        return calloutDef;
    }

    public static CalloutDefinition GetCalloutDefinitionForStation(DynamicEntity favoriteStation,
        string removeFavoriteIconUrl)
    {
        // シェアサイクル ステーション名と貸出可能な台数をコールアウトに表示する
        var stationName = favoriteStation.Attributes["StationName"].ToString();
        var availableBikes = (int)favoriteStation.Attributes["BikesAvailable"];
        var calloutDef = new CalloutDefinition(stationName,
                             $"貸出可能: {availableBikes} 台")
        {
            ButtonImage = new RuntimeImage(new Uri(removeFavoriteIconUrl)),
            // 動的エンティティをコールアウト定義のタグとして設定する
            // (クリック イベント コードはタグを使用して動的エンティティを取得する)
            Tag = favoriteStation
        };

        return calloutDef;
    }

    public bool ToggleIsFavorite(DynamicEntity station)
    {
        var isFavorite = _favoriteBikeStations.ContainsKey(station.EntityId);
        if (isFavorite)
        {
            _favoriteBikeStations.Remove(station.EntityId);
            station.DynamicEntityChanged -= DynEntity_DynamicEntityChanged;
            isFavorite = false;
        }
        else
        {
            _favoriteBikeStations.Add(station.EntityId, station);
            station.DynamicEntityChanged += DynEntity_DynamicEntityChanged;
            isFavorite = true;
        }

        // アプリに表示されるお気に入りリストを更新する
        FavoriteList = _favoriteBikeStations.Values.ToList();

        return isFavorite;
    }

    private void DynEntity_DynamicEntityChanged(object sender, DynamicEntityChangedEventArgs e)
    {
        // TODO: お気に入りのステーションの貸出可能台数の変更を処理する
        //    （UI でカードをハイライトや点滅させるなど）
    }

    private void CreateTotalBikeInventory(DynamicEntity bikeStation)
    {
        var availableBikes = (int)bikeStation.Attributes["BikesAvailable"];

        BikesAvailable += availableBikes;

    }

    private void UpdateBikeInventory(int inventoryChange)
    {
        BikesAvailable += inventoryChange;

    }

    private async Task FlashDynamicEntityObservationAsync(MapPoint point, bool bikeAdded)
    {
        // 観測データが入ったら地図上で点滅させる（貸出可能な台数が増えた場合は青色で、減った場合は赤色で表示）
        Graphic halo = null;
        try
        {
            var attr = new Dictionary<string, object>
            {
                { "BikesAdded", bikeAdded }
            };
            var bikeAddedSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.FromArgb(128, System.Drawing.Color.Blue), 18);
            var bikeTakenSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.FromArgb(128, System.Drawing.Color.Red), 18);
            var bikeSym = bikeAdded ? bikeAddedSymbol : bikeTakenSymbol;
            halo = new Graphic(point, bikeSym) { IsVisible = false };
            lock (_thisLock)
            {
                _flashOverlay.Graphics.Add(halo);
            }
            for (var n = 0; n < 2; ++n)
            {
                halo.IsVisible = true;
                await Task.Delay(100);
                halo.IsVisible = false;
                await Task.Delay(100);
            }
        }
        catch
        {
            // 未処理
        }
        finally
        {
            try
            {
                lock (_thisLock)
                {
                    if (halo != null && _flashOverlay.Graphics.Contains(halo))
                    {
                        _flashOverlay.Graphics.Remove(halo);
                    }
                }
            }
            catch
            {
            }
        }
    }
}
