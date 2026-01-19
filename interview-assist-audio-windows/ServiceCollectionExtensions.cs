using InterviewAssist.Library.Audio;
using Microsoft.Extensions.DependencyInjection;

namespace InterviewAssist.Audio.Windows;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInterviewAssistWindowsAudio(this IServiceCollection services, int sampleRate = 24000, AudioInputSource initialSource = AudioInputSource.Microphone)
    {
        services.AddSingleton<IAudioDeviceEnumerator, WindowsAudioDeviceEnumerator>();
        services.AddSingleton<IAudioCaptureService>(sp => new WindowsAudioCaptureService(sampleRate, initialSource));
        return services;
    }
}
