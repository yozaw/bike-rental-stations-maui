using System.Text.Json.Serialization;

namespace BikeAvailability;

// オープンデータの API の JSON レスポンスから返されるシェアサイクル ステーションのステータスを表すクラス
public class BikeStatus
{

    [JsonPropertyName("station_id")]
    public string StationID { get; set; }

    [JsonPropertyName("num_bikes_available")]
    public int BikesAvailable { get; set; }

    [JsonPropertyName("num_docks_available")]
    public int EmptySlots { get; set; }

}

