namespace SynologyPhotoFrame.Models;

public class AppSettings
{
    public string NasAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 5001;
    public bool UseHttps { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string EncryptedPassword { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
    public List<int> SelectedAlbumIds { get; set; } = new();
    public List<int> SelectedPersonIds { get; set; } = new();
    public List<int> SelectedTeamPersonIds { get; set; } = new();
    public int IntervalSeconds { get; set; } = 10;
    public TransitionType TransitionType { get; set; } = TransitionType.Fade;
    public double TransitionDurationSeconds { get; set; } = 1.0;
    public bool ShufflePhotos { get; set; } = true;
    public bool ShowClock { get; set; } = true;
    public bool ShowPhotoInfo { get; set; }
    public string PhotoSizePreference { get; set; } = "xl";
    public bool ScheduleEnabled { get; set; }
    public string ScheduleStartTime { get; set; } = "08:00";
    public string ScheduleEndTime { get; set; } = "22:00";
}
