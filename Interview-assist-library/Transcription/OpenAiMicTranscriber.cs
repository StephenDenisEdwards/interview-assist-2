using InterviewAssist.Library.Audio;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Channels;

namespace InterviewAssist.Library.Transcription;

public sealed class OpenAiMicTranscriber : IAsyncDisposable
{
	private readonly IAudioCaptureService _audio;
	private readonly string _apiKey;
	private readonly int _sampleRate;
	private CancellationTokenSource? _cts;
	private Channel<byte[]>? _audioChannel;
	private Action<byte[]>? _audioHandler;
	private Task? _transcribeTask;
	private int _started; // 0 = stopped, 1 = started

	public event Action<string>? OnTranscript;
	public event Action<string>? OnInfo;
	public event Action<string>? OnWarning;
	public event Action<Exception>? OnError;

	public OpenAiMicTranscriber(IAudioCaptureService audioCaptureService, string openAiApiKey, int sampleRate = 24000)
	{
		_audio = audioCaptureService ?? throw new ArgumentNullException(nameof(audioCaptureService));
		_apiKey = openAiApiKey ?? throw new ArgumentNullException(nameof(openAiApiKey));
		_sampleRate = sampleRate;
	}

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (Interlocked.Exchange(ref _started, 1) == 1)
		{
			throw new InvalidOperationException("Transcriber already started.");
		}

		_cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		_audioChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(16)
		{
			SingleReader = true,
			SingleWriter = false,
			FullMode = BoundedChannelFullMode.DropOldest
		});

		_audioHandler = bytes =>
		{
			if (_audioChannel == null) return;
			if (!_audioChannel.Writer.TryWrite(bytes))
			{
				OnWarning?.Invoke("Audio queue full; dropping audio chunk.");
			}
		};

		_audio.OnAudioChunk += _audioHandler;
		_audio.Start();
		OnInfo?.Invoke($"AudioCaptureService active ({_audio.GetSource()})");

		_transcribeTask = TranscriptionLoop(_cts.Token);
		try
		{
			await _transcribeTask.ConfigureAwait(false);
		}
		catch (OperationCanceledException) { }
	}

	public async Task StopAsync()
	{
		if (Interlocked.Exchange(ref _started, 0) == 0)
		{
			return;
		}

		try { _audio?.Stop(); } catch { }
		if (_audioHandler != null)
		{
			try { _audio.OnAudioChunk -= _audioHandler; } catch { }
			_audioHandler = null;
		}
		try { _audioChannel?.Writer.TryComplete(); } catch { }
		try { _cts?.Cancel(); } catch { }

		if (_transcribeTask != null)
		{
			try { await _transcribeTask.ConfigureAwait(false); } catch { }
			_transcribeTask = null;
		}

		try { _cts?.Dispose(); } catch { }
		_cts = null;
	}

	public async ValueTask DisposeAsync()
	{
		await StopAsync().ConfigureAwait(false);
	}

	private async Task TranscriptionLoop(CancellationToken ct)
	{
		if (_audioChannel == null) return;
		var buffer = new MemoryStream();
		var windowMs = 3000; // batch window
		var lastFlush = Environment.TickCount64;

		try
		{
			await foreach (var chunk in _audioChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
			{
				await buffer.WriteAsync(chunk, 0, chunk.Length, ct).ConfigureAwait(false);
				var elapsed = Environment.TickCount64 - lastFlush;
				if (elapsed >= windowMs)
				{
					await FlushAndTranscribeAsync(buffer, ct).ConfigureAwait(false);
					lastFlush = Environment.TickCount64;
				}
			}

			// final flush
			await FlushAndTranscribeAsync(buffer, ct).ConfigureAwait(false);
		}
		catch (OperationCanceledException) { }
		catch (Exception ex)
		{
			OnError?.Invoke(ex);
		}
		finally
		{
			buffer.Dispose();
		}
	}

	private async Task FlushAndTranscribeAsync(MemoryStream pcmBuffer, CancellationToken ct)
	{
		if (pcmBuffer.Length == 0) return;
		var wav = BuildWav(pcmBuffer.ToArray(), _sampleRate, 1, 16);
		pcmBuffer.SetLength(0);
		try
		{
			var text = await TranscribeAsync(wav, ct).ConfigureAwait(false);
			if (!string.IsNullOrWhiteSpace(text))
			{
				OnTranscript?.Invoke(text);
			}
		}
		catch (OperationCanceledException) { }
		catch (Exception ex)
		{
			OnError?.Invoke(ex);
		}
	}

	private static byte[] BuildWav(byte[] pcm, int sampleRate, short channels, short bitsPerSample)
	{
		using var ms = new MemoryStream();
		using var bw = new BinaryWriter(ms);
		int byteRate = sampleRate * channels * (bitsPerSample / 8);
		short blockAlign = (short)(channels * (bitsPerSample / 8));
		bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
		bw.Write(36 + pcm.Length);
		bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
		bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
		bw.Write(16);
		bw.Write((short)1);
		bw.Write(channels);
		bw.Write(sampleRate);
		bw.Write(byteRate);
		bw.Write(blockAlign);
		bw.Write(bitsPerSample);
		bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
		bw.Write(pcm.Length);
		bw.Write(pcm);
		bw.Flush();
		return ms.ToArray();
	}

	private async Task<string> TranscribeAsync(byte[] wavData, CancellationToken ct)
	{
		using var http = new HttpClient();
		http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

		using var content = new MultipartFormDataContent();
		var audioContent = new ByteArrayContent(wavData);
		audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
		content.Add(audioContent, "file", "audio.wav");
		content.Add(new StringContent("whisper-1"), "model");
		content.Add(new StringContent("json"), "response_format");

		using var resp = await http.PostAsync("https://api.openai.com/v1/audio/transcriptions", content, ct).ConfigureAwait(false);
		var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
		if (!resp.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Transcription failed: {(int)resp.StatusCode} {resp.ReasonPhrase} - {body}");
		}
		using var doc = JsonDocument.Parse(body);
		if (doc.RootElement.TryGetProperty("text", out var textProp))
		{
			return textProp.GetString() ?? string.Empty;
		}
		return string.Empty;
	}
}
