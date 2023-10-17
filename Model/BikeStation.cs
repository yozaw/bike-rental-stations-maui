using System.Text.Json.Serialization;

namespace BikeAvailability;

// オープンデータの API の JSON レスポンスから返されるシェアサイクル ステーションを表すクラス
public class BikeStation
{

    [JsonPropertyName("vehicle_type_capacity")]
    public StationCapacity StationCapacity { get; set; }

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

// オープンデータ の API の JSON レスポンスから返されるシェアサイクル ステーションの「vehicle_type_capacity」情報を表すクラス
public class StationCapacity
{

    [JsonPropertyName("num_bikes_rentalable")]
    public int BikesAvailable { get; set; }

    [JsonPropertyName("num_bikes_parkable")]
    public int EmptySlots { get; set; }

}
