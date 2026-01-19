namespace InterviewAssist.Library.Audio;

public interface IAudioCaptureService: IDisposable
{
	event Action<byte[]>? OnAudioChunk;
	void SetSource(AudioInputSource source);
	void Start();
	void Stop();
	AudioInputSource GetSource();
}
