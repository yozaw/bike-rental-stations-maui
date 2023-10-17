using BikeAvailability.ViewModel;
using Esri.ArcGISRuntime.RealTime;

namespace BikeAvailability;

public partial class FavoritesPage : ContentPage
{
    public FavoritesPage(CityBikesViewModel vm)
	{
		InitializeComponent();
        BindingContext = vm;
	}

    // マップページに移動して、選択したお気に入りのステーションを表示する
    private async void StationClicked(object sender, EventArgs e)
    {
        var stationButton = sender as Button;

        if (stationButton.BindingContext is DynamicEntity fav)
        {
            Dictionary<string, object> mapParams = new()
            {
                { "favorite", fav }
            };
            await Shell.Current.GoToAsync("//MainPage", mapParams);
        }
    }
}