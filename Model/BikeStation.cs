using System.Text.Json.Serialization;

namespace BikeAvailability;

// オープンデータの API の JSON レスポンスから返されるシェアサイクル ステーションを表すクラス
public class BikeStation
{

    [JsonPropertyName("name")]
    public string StationName { get; set; }

    [JsonPropertyName("lon")]
    public double Longitude { get; set; }

    [JsonPropertyName("lat")]
    public double Latitude { get; set; }

    [JsonPropertyName("station_id")]
    public string StationID { get; set; }

    [JsonPropertyName("address")]
    public string Address { get; set; }


}

