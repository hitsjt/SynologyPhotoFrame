using System.ComponentModel;

namespace SynologyPhotoFrame.Models;

public enum TransitionType
{
    [Description("Fade")]
    Fade,
    [Description("Slide Left")]
    SlideLeft,
    [Description("Slide Right")]
    SlideRight,
    [Description("Zoom In")]
    ZoomIn,
    [Description("Dissolve")]
    Dissolve,
    [Description("Random")]
    Random
}
