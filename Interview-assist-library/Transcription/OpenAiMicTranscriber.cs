using InterviewAssist.Library.Audio;
using InterviewAssist.Library.Constants;
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
	private readonly double _silenceThreshold;
	private readonly string? _language;
	private readonly string? _prompt;
	private readonly int _windowMs;
	private readonly int _maxWindowMs;
	private CancellationTokenSource? _cts;
	private Channel<byte[]>? _audioChannel;
	private Action<byte[]>? _audioHandler;
	private Task? _transcribeTask;
	private int _started; // 0 = stopped, 1 = started

	public event Action<string>? OnTranscript;
	public event Action<string>? OnInfo;
	public event Action<string>? OnWarning;
	public event Action<Exception>? OnError;

	/// <summary>
	/// Creates a new transcriber instance.
	/// </summary>
	/// <param name="audioCaptureService">Audio capture service for input.</param>
	/// <param name="openAiApiKey">OpenAI API key.</param>
	/// <param name="sampleRate">Audio sample rate in Hz. Default: 24000.</param>
	/// <param name="silenceThreshold">RMS threshold for silence detection (0.0-1.0). Default: 0.01. Set to 0 to disable.</param>
	/// <param name="language">Language code for transcription (e.g., "en"). Null for auto-detection.</param>
	/// <param name="prompt">Optional vocabulary prompt to guide transcription.</param>
	/// <param name="windowMs">Minimum batch window in milliseconds. Default: 3000.</param>
	/// <param name="maxWindowMs">Maximum batch window in milliseconds. Default: 6000. Forces flush even without silence.</param>
	public OpenAiMicTranscriber(
		IAudioCaptureService audioCaptureService,
		string openAiApiKey,
		int sampleRate = 24000,
		double silenceThreshold = TranscriptionConstants.DefaultSilenceThreshold,
		string? language = null,
		string? prompt = null,
		int windowMs = 3000,
		int maxWindowMs = 6000)
	{
		_audio = audioCaptureService ?? throw new ArgumentNullException(nameof(audioCaptureService));
		_apiKey = openAiApiKey ?? throw new ArgumentNullException(nameof(openAiApiKey));
		_sampleRate = sampleRate;
		_silenceThreshold = silenceThreshold;
		_language = language;
		_prompt = prompt;
		_windowMs = windowMs;
		_maxWindowMs = maxWindowMs;
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
		var lastFlush = Environment.TickCount64;

		try
		{
			await foreach (var chunk in _audioChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
			{
				await buffer.WriteAsync(chunk, 0, chunk.Length, ct).ConfigureAwait(false);
				var elapsed = Environment.TickCount64 - lastFlush;

				// Adaptive batching: flush on speech boundary (silence after min window) or max window
				bool minWindowReached = elapsed >= _windowMs;
				bool maxWindowReached = elapsed >= _maxWindowMs;
				bool recentChunkIsSilence = _silenceThreshold > 0 && IsSilence(chunk, _silenceThreshold);
				bool speechBoundary = minWindowReached && recentChunkIsSilence;

				if (speechBoundary || maxWindowReached)
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

		var pcmData = pcmBuffer.ToArray();
		pcmBuffer.SetLength(0);

		// Check minimum duration (Whisper requires >= 0.1 seconds)
		double durationSeconds = (double)pcmData.Length / (_sampleRate * 2); // 16-bit = 2 bytes per sample
		if (durationSeconds < TranscriptionConstants.MinAudioDurationSeconds)
		{
			OnWarning?.Invoke($"Audio too short ({durationSeconds:F3}s), skipping transcription");
			return;
		}

		// Check for silence (skip if below threshold)
		if (_silenceThreshold > 0 && IsSilence(pcmData, _silenceThreshold))
		{
			OnInfo?.Invoke("Silence detected, skipping transcription");
			return;
		}

		var wav = BuildWav(pcmData, _sampleRate, 1, 16);
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

	/// <summary>
	/// Determines if audio data is silence based on RMS energy.
	/// </summary>
	/// <param name="pcmData">16-bit PCM audio data.</param>
	/// <param name="threshold">RMS threshold (0.0-1.0).</param>
	/// <returns>True if audio is considered silence.</returns>
	private static bool IsSilence(byte[] pcmData, double threshold)
	{
		if (pcmData.Length < 2) return true;

		double sumSquares = 0;
		int sampleCount = pcmData.Length / 2;

		for (int i = 0; i < pcmData.Length - 1; i += 2)
		{
			short sample = BitConverter.ToInt16(pcmData, i);
			double normalized = sample / 32768.0;
			sumSquares += normalized * normalized;
		}

		double rms = Math.Sqrt(sumSquares / sampleCount);
		return rms < threshold;
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

		// Add language hint for improved accuracy
		if (!string.IsNullOrWhiteSpace(_language))
		{
			content.Add(new StringContent(_language), "language");
		}

		// Add vocabulary prompt for domain-specific terms
		if (!string.IsNullOrWhiteSpace(_prompt))
		{
			content.Add(new StringContent(_prompt), "prompt");
		}

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
