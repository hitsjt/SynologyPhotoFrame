using System.Text.Json.Serialization;

namespace SynologyPhotoFrame.Models;

public class SynologyApiResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public SynologyApiError? Error { get; set; }
}

public class SynologyApiResponse<T> : SynologyApiResponse
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

public class SynologyApiError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }
}

public class SynologyListData<T>
{
    [JsonPropertyName("list")]
    public List<T> List { get; set; } = new();

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class SynologyLoginData
{
    [JsonPropertyName("sid")]
    public string Sid { get; set; } = string.Empty;
}
