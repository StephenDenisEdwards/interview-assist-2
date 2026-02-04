using InterviewAssist.Library.Audio;

namespace Interview_assist_audio_android;

/// <summary>
/// Android implementation of IAudioDeviceEnumerator.
/// Returns a single default microphone device as Android handles device selection internally.
/// </summary>
public sealed class AndroidAudioDeviceEnumerator : IAudioDeviceEnumerator
{
    private static readonly IReadOnlyList<AudioDeviceInfo> DefaultMicrophone = new List<AudioDeviceInfo>
    {
        new AudioDeviceInfo
        {
            Id = "android-default-mic",
            Name = "Default Microphone",
            IsDefault = true
        }
    };

    private static readonly IReadOnlyList<AudioDeviceInfo> EmptyList = Array.Empty<AudioDeviceInfo>();

    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        return DefaultMicrophone;
    }

    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        // Loopback/render device enumeration is not supported on Android
        return EmptyList;
    }
}
