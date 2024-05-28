using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.RealTime;
using System.Diagnostics;
using System.Text.Json;

namespace BikeAvailability;

internal class CityBikesDataSource : DynamicEntityDataSource
{
    // 指定された間隔で更新をリクエストするするタイマー
    private readonly IDispatcherTimer _getBikeUpdatesTimer = Application.Current.Dispatcher.CreateTimer();

    // HELLO CYCLING のシェアサイクル ステーションのオープンデータのエンドポイント
    // 各ステーションの現時点での貸出し・駐車可能台数が含まれる
    private readonly string _cityBikesStatusUrl;
    // 各ステーションの名前や緯度経度が含まれる
    private readonly string _cityBikesStationUrl = "https://api-public.odpt.org/api/v4/gbfs/hellocycling/station_information.json";

    // シェアサイクル ステーションの以前の観測データ（台数の変化を確認するため）
    private readonly Dictionary<string, Dictionary<string, object>> _previousObservations = new();
    // 一定の間隔で観測データを表示するために使用されるタイマーと関連する変数
    private readonly IDispatcherTimer _addBikeUpdatesTimer = Application.Current.Dispatcher.CreateTimer();
    private readonly List<Tuple<MapPoint, Dictionary<string, object>>> _currentObservations = new();
    private readonly bool _showSmoothUpdates;

    private List<BikeStation> bikeStations;


    public CityBikesDataSource(string cityBikesStatusUrl,
        int updateIntervalSeconds, bool smoothUpdateDisplay = true)
    {
        // タイマー間隔（URL に更新をリクエストする頻度）を保存する
        _getBikeUpdatesTimer.Interval = TimeSpan.FromSeconds(updateIntervalSeconds);
        // シェアサイクル ステーションの空き状況の URL
        _cityBikesStatusUrl = cityBikesStatusUrl;

        // タイマー間隔ごとに実行する関数を設定
        _getBikeUpdatesTimer.Tick += (s, e) => _ = PullBikeUpdates();
        // 各ステーションの更新を時間の経過とともに一貫して表示するか、最初のデータ取得時に表示するかを保存する
        _showSmoothUpdates = smoothUpdateDisplay;
        if (smoothUpdateDisplay)
        {
            // _addBikeUpdatesTimer.Interval = TimeSpan.FromSeconds(3);
            _addBikeUpdatesTimer.Tick += (s, e) => AddBikeObservations();
        }
    }

    protected override Task OnConnectAsync(CancellationToken cancellationToken)
    {
        // タイマーを開始して、定期的に更新を取得する
        _getBikeUpdatesTimer.Start();

        return Task.CompletedTask;
    }

    protected override Task OnDisconnectAsync()
    {
        // タイマーを停止する (更新リクエストを一時停止する)
        _getBikeUpdatesTimer.Stop();
        _addBikeUpdatesTimer.Stop();

        // 以前の観測データのディクショナリをクリアする
        _previousObservations.Clear();

        return Task.CompletedTask;
    }

    protected override Task<DynamicEntityDataSourceInfo> OnLoadAsync()
    {
        // データソースがロードされたら、以下を定義するメタデータを作成する
        // - 観測データ（シェアサイクル ステーション）のスキーマ（フィールド）
        // - エンティティを一意に識別するフィールド（StationID）
        // - ステーションの位置の空間参照（WGS84）
        var fields = new List<Field>
        {
            new Field(FieldType.Text, "StationID", "", 50),　//一意の ID
            new Field(FieldType.Text, "StationName", "", 125), //ステーション名
            new Field(FieldType.Text, "Address", "", 125), //ステーションの住所
            new Field(FieldType.Float32, "Longitude", "", 0), //ステーションの経度
            new Field(FieldType.Float32, "Latitude", "", 0), //ステーションの緯度
            new Field(FieldType.Int32, "BikesAvailable", "", 0), //貸出可能な台数
            new Field(FieldType.Int32, "EmptySlots", "", 0), //駐車可能な台数
            new Field(FieldType.Int32, "InventoryChange", "", 0), //貸出可能台数の変化数
            new Field(FieldType.Text, "ImageUrl", "", 255)
        };
        var info = new DynamicEntityDataSourceInfo("StationID", fields)
        {
            SpatialReference = SpatialReferences.Wgs84
        };

        return Task.FromResult(info);
    }

    private async Task PullBikeUpdates()
    {
        // データ ソースが接続されていない場合は終了する
        if (this.ConnectionStatus != ConnectionStatus.Connected) { return; }

        try
        {
            // 更新の取得中に観測データを追加するタイマーを停止する
            _addBikeUpdatesTimer.Stop();

            // 各ステーションの更新を一貫して表示する場合は、最後の更新から残っている更新を処理する
            if (_showSmoothUpdates)
            {
                for (int i = _currentObservations.Count - 1; i > 0; i--)
                {
                    var obs = _currentObservations[i];
                    AddObservation(obs.Item1, obs.Item2);
                    _currentObservations.Remove(obs);
                }
            }

            // 関数を呼び出して、一連のシェアサイクル ステーション（場所と属性）を取得する
            var bikeUpdates = await GetDeserializedCityBikeResponse();
            var updatedStationCount = 0;
            var totalInventoryChange = 0;

            // 各ステーションの情報を反復する
            foreach (var update in bikeUpdates)
            {
                // このステーションの位置、属性、ID を取得する
                var location = update.Item1;
                var attributes = update.Item2;
                var id = attributes["StationID"].ToString();

                // このステーションの最後の更新の値のセットを取得する（存在する場合）
                _previousObservations.TryGetValue(id, out Dictionary<string, object> lastObservation);
                if (lastObservation is not null)
                {
                    // 最新の更新の BikesAvailable の値が異なるかどうかを確認する
                    if ((int)attributes["BikesAvailable"] != (int)lastObservation["BikesAvailable"])
                    {
                        // 貸出可能台数の変化を計算する
                        var stationInventoryChange = (int)attributes["BikesAvailable"] - (int)lastObservation["BikesAvailable"];
                        attributes["InventoryChange"] = stationInventoryChange;
                        totalInventoryChange += stationInventoryChange;
                        updatedStationCount++;

                        // 更新をすぐに表示する場合は、更新をデータソースに追加する
                        if (!_showSmoothUpdates)
                        {
                            AddObservation(location, attributes);
                        }
                        else
                        {

                            var demoFlag = false;

                            if (demoFlag == true)
                            {
                                var point = new MapPoint(140.0418738, 35.6484731, SpatialReference.Create(4326));
                                var buffer = GeometryEngine.BufferGeodetic(point, 10, LinearUnits.Kilometers);
                                var contains = GeometryEngine.Contains(buffer, location);
                                if (contains == true)
                                {
                                    var observation = new Tuple<MapPoint, Dictionary<string, object>>(location, attributes);
                                    _currentObservations.Add(observation);
                                }
                            }
                            else {

                                // 各ステーションの更新を一貫して表示する場合は、処理のために現在の観測データのリストに追加する
                                var observation = new Tuple<MapPoint, Dictionary<string, object>>(location, attributes);
                                _currentObservations.Add(observation);

                            }
                        }
                    }

                    // このステーションの最新の更新をアップデートする
                    _previousObservations[id] = attributes;
                }
            }

            // 更新を一貫して表示する場合は、データソースに観測データを追加するためのタイマーを設定する
            if (_showSmoothUpdates)
            {
                if (_currentObservations.Count > 0)
                {
                    var updatesPerSecond = (int)Math.Ceiling(_currentObservations.Count / _getBikeUpdatesTimer.Interval.TotalSeconds);

                    if (updatesPerSecond > 0)
                    {
                        long ticksPerUpdate = 10000000 / updatesPerSecond;
                        _addBikeUpdatesTimer.Interval = TimeSpan.FromTicks(ticksPerUpdate);
                        _addBikeUpdatesTimer.Start(); // Tick イベントにより 1 つの更新が追加される
                    }

                    Debug.WriteLine($"**** Stations from this update = {updatedStationCount}, total to process = {_currentObservations.Count}");
                }
            }

            Debug.WriteLine($"**** Total inventory change: {totalInventoryChange} for {updatedStationCount} stations");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(@"\tERROR {0}", ex.Message);
        }
    }

    private void AddBikeObservations()
    {
        // タイマー間隔で1つの観測データを追加する
        // この間隔は、次の更新を取得するスパンに、これらの追加を分散するように計算されている
        if (_currentObservations.Count > 0)
        {
            var obs = _currentObservations[^1];
            AddObservation(obs.Item1, obs.Item2);
            _currentObservations.Remove(obs);
        }
    }

    public async Task GetInitialBikeStations()
    {

        // 各シェアサイクル ステーションの ID、名前、緯度経度、住所情報を含む List を作成する
        // HTTP リクエストから JSON のレスポンスを取得する
        var client = new HttpClient();
        HttpResponseMessage response = await client.GetAsync(new Uri(_cityBikesStationUrl));
        if (response.IsSuccessStatusCode)
        {
            var cityBikeStationJson = await response.Content.ReadAsStringAsync();

            // JSON の "stations" 部分を取得し、ステーションのリストを逆シリアル化する
            var stationsStartPos = cityBikeStationJson.IndexOf(@"""stations"":[") + 11;
            var stationsEndPos = cityBikeStationJson.LastIndexOf(@"]") + 1;
            var stationsJson = cityBikeStationJson[stationsStartPos..stationsEndPos];
            bikeStations = JsonSerializer.Deserialize<List<BikeStation>>(stationsJson);
        }

        // データソースが接続されていない場合は終了する
        if (this.ConnectionStatus != ConnectionStatus.Connected) { return; }

        try
        {
            // 関数を呼び出して、一連のシェアサイクル ステーション（場所と属性）を取得する
            var bikeUpdates = await GetDeserializedCityBikeResponse();

            // 各ステーションの情報を反復する
            foreach (var update in bikeUpdates)
            {
                var location = update.Item1;
                var attributes = update.Item2;

                // このステーションの最新の更新をアップデートする
                _previousObservations[attributes["StationID"].ToString()] = attributes;

                // 更新をデータソースに追加する
                AddObservation(location, attributes);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(@"\tERROR {0}", ex.Message);
        }
    }

    private async Task<List<Tuple<MapPoint, Dictionary<string, object>>>> GetDeserializedCityBikeResponse()
    {
        // JSON 応答を、シェアサイクル ステーションの場所と属性のリストとしてデシリアライズする
        List<Tuple<MapPoint, Dictionary<string, object>>> bikeInfo = new();

        try
        {
            // HTTP リクエストから JSON のレスポンスを取得する
            var client = new HttpClient();
            HttpResponseMessage response = await client.GetAsync(new Uri(_cityBikesStatusUrl));
            if (response.IsSuccessStatusCode)
            {
                // このシェアサイクルの情報（すべてのステーションを含む）の JSON レスポンスを読み取る
                var cityBikeJson = await response.Content.ReadAsStringAsync();

                // JSON の "stations" 部分を取得し、ステーションのリストを逆シリアル化する
                var stationsStartPos = cityBikeJson.IndexOf(@"""stations"":[") + 11;
                var stationsEndPos = cityBikeJson.LastIndexOf(@"]") + 1;
                var stationsJson = cityBikeJson[stationsStartPos..stationsEndPos];
                var bikeUpdates = JsonSerializer.Deserialize<List<BikeStatus>>(stationsJson);

                // 各ステーションの情報を反復する
                foreach (var update in bikeUpdates)
                {
                    // 最初に取得した各ステーションの List から名前や緯度経度を取得する
                    string stationName = bikeStations.Find(x => x.StationID == update.StationID).StationName;
                    string address = bikeStations.Find(x => x.StationID == update.StationID).Address;
                    double longitude = bikeStations.Find(x => x.StationID == update.StationID).Longitude;
                    double latitude = bikeStations.Find(x => x.StationID == update.StationID).Latitude;

                    // レスポンスから属性のディクショナリを作成する
                    var attributes = new Dictionary<string, object>
                    {
                        { "StationID", update.StationID },
                        { "StationName", stationName },
                        { "Address", address },
                        { "Longitude", longitude },
                        { "Latitude", latitude },
                        { "BikesAvailable", update.BikesAvailable },
                        { "EmptySlots", update.EmptySlots },
                        { "InventoryChange", 0 },
                        { "ImageUrl", "https://static.arcgis.com/images/Symbols/Transportation/esriDefaultMarker_189.png" }
                    };

                    // 経度（x）と緯度（y）の値からマップ ポイントを作成する
                    var location = new MapPoint(longitude, latitude, SpatialReferences.Wgs84);

                    // このシェアサイクル ステーションの情報をリストに追加する
                    bikeInfo.Add(new Tuple<MapPoint, Dictionary<string, object>>(location, attributes));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(@"\tERROR {0}", ex.Message);
        }

        return bikeInfo;
    }
}
