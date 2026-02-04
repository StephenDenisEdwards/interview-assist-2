using Interview_assist_library.Audio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Interview_assist_audio_android;

/// <summary>
/// Extension methods for registering Android audio services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Android audio capture services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="sampleRate">Audio sample rate in Hz. Default is 16000.</param>
    /// <param name="initialSource">Initial audio source. Loopback will fall back to Microphone on Android.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInterviewAssistAndroidAudio(
        this IServiceCollection services,
        int sampleRate = 16000,
        AudioInputSource initialSource = AudioInputSource.Microphone)
    {
        services.AddSingleton<IAudioDeviceEnumerator, AndroidAudioDeviceEnumerator>();
        services.AddSingleton<IAudioCaptureService>(sp =>
            new AndroidAudioCaptureService(
                sampleRate,
                initialSource,
                sp.GetService<ILogger<AndroidAudioCaptureService>>()));

        return services;
    }
}
