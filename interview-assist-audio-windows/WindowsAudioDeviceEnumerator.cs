using System.Collections.Generic;
using InterviewAssist.Library.Audio;
using NAudio.CoreAudioApi;

namespace InterviewAssist.Audio.Windows;

public sealed class WindowsAudioDeviceEnumerator : IAudioDeviceEnumerator
{
    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        var result = new List<AudioDeviceInfo>();
        foreach (var d in devices)
        {
            result.Add(new AudioDeviceInfo
            {
                Id = d.ID,
                Name = d.FriendlyName,
                IsDefault = d.ID == enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia).ID
            });
        }
        return result;
    }

    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        var result = new List<AudioDeviceInfo>();
        foreach (var d in devices)
        {
            result.Add(new AudioDeviceInfo
            {
                Id = d.ID,
                Name = d.FriendlyName,
                IsDefault = d.ID == enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID
            });
        }
        return result;
    }
}
