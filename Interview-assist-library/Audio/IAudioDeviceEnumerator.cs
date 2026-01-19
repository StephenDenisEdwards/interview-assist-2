using System.Collections.Generic;

namespace InterviewAssist.Library.Audio;

public interface IAudioDeviceEnumerator
{
    IReadOnlyList<AudioDeviceInfo> GetCaptureDevices();
    IReadOnlyList<AudioDeviceInfo> GetRenderDevices();
}
