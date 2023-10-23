using BikeAvailability.ViewModel;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.RealTime;
using Esri.ArcGISRuntime.UI;

namespace BikeAvailability;

public partial class MainPage : ContentPage, IQueryAttributable
{
    // お気に入りに追加/削除するボタンに使用する画像
    private readonly string _makeFavoriteImage = "https://raw.githubusercontent.com/ThadT/bike-rental-stations-maui/main/MakeFavorite.png";
    private readonly string _unFavoriteImage = "https://raw.githubusercontent.com/ThadT/bike-rental-stations-maui/main/UnFavorite.png";
    private readonly CityBikesViewModel _vm;

    public MainPage(CityBikesViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        _vm = vm;

        // シェアサイクル ステーションを表示する
        var viewpoint = _vm.ShowBikeStations();

        // 指定した初期表示位置にズームする
        mapView.SetViewpoint(viewpoint.Result);

        // 全体の貸出可能な自転車台数を表示する
        BikeInventoryPanel.IsVisible = true;
    }

    private async void MapViewTapped(object sender, GeoViewInputEventArgs e)
    {
        // 現在開いているコールアウトを閉じる
        mapView.DismissCallout();
        
        var dynamicEntityLayer = mapView.Map.OperationalLayers.OfType<DynamicEntityLayer>().FirstOrDefault();

        if (dynamicEntityLayer != null)
        {
            // タップした場所からシェアサイク ステーションを特定する
            var results = await mapView.IdentifyLayerAsync(dynamicEntityLayer, e.Position, 4, false, 1);
            if (results.GeoElements.Count == 0 || results.GeoElements[0] is not DynamicEntityObservation bikeStation) { return; }

            // ビューモデルからコールアウト定義を取得する
            var calloutDef = _vm.GetCalloutDefinitionForStation(bikeStation, _unFavoriteImage, _makeFavoriteImage);
            // このステーションをお気に入りとして追加/削除するためのボタン クリックを設定する
            calloutDef.OnButtonClick = (tag) =>
            {
                // このステーションをお気に入りリストに追加/削除する
                var isFavorite = _vm.ToggleIsFavorite(tag as DynamicEntity);

                // 正しい画像をコールアウトに適用し、再度表示する
                calloutDef.ButtonImage = isFavorite ?
                                           new RuntimeImage(new Uri(_unFavoriteImage)) :
                                           new RuntimeImage(new Uri(_makeFavoriteImage));
                mapView.ShowCalloutAt(bikeStation.Geometry as MapPoint, calloutDef);
            };
            // コールアウトを表示する
            mapView.ShowCalloutAt(bikeStation.Geometry as MapPoint, calloutDef);
        } 
    }

    private void OnDrawStatusChanged(object sender, DrawStatusChangedEventArgs e)
    {
        if (e.Status == DrawStatus.Completed)
        {
            // マップ上に表示するクレジット表記を設定する
            var attributionText = mapView.AttributionText + ", " + _vm.BikesAttributionText;
            AttributionText.Text = attributionText;
        }
    }

    // お気に入りページのボタン クリックからステーションに移動する処理
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query["favorite"] is DynamicEntity favorite)
        {
            var location = favorite.Geometry as MapPoint;
            mapView.SetViewpoint(new Viewpoint(location, 10000));

            // 現在開いているコールアウトを閉じる
            mapView.DismissCallout();

            var calloutDef = CityBikesViewModel.GetCalloutDefinitionForStation(favorite, _unFavoriteImage);
            calloutDef.OnButtonClick = (tag) =>
            {
                // このステーションをお気に入りリストに追加/削除する
                var isFavorite = _vm.ToggleIsFavorite(tag as DynamicEntity);

                // 正しい画像をコールアウトに適用し、再度表示する
                calloutDef.ButtonImage = isFavorite ?
                                           new RuntimeImage(new Uri(_unFavoriteImage)) :
                                           new RuntimeImage(new Uri(_makeFavoriteImage));
                mapView.ShowCalloutAt(location, calloutDef);
            };
            mapView.ShowCalloutAt(location, calloutDef);
        }
    }
}
