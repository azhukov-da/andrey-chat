using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions;
using Application.Common;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class FasterWhisperTranscriptionService : ITranscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FasterWhisperTranscriptionService> _logger;

    public FasterWhisperTranscriptionService(HttpClient httpClient, ILogger<FasterWhisperTranscriptionService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Result<TranscriptionResult>> TranscribeAsync(
        Stream audio,
        string fileName,
        string contentType,
        TimeSpan? maxDuration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            using var streamContent = new StreamContent(audio);
            streamContent.Headers.ContentType = ParseContentType(contentType);
            content.Add(streamContent, "file", string.IsNullOrWhiteSpace(fileName) ? "audio" : fileName);

            if (maxDuration.HasValue)
                content.Add(new StringContent(maxDuration.Value.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)), "max_duration_seconds");

            using var response = await _httpClient.PostAsync("/transcribe", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadFromJsonAsync<TranscribeResponse>(cancellationToken: cancellationToken);
                if (payload == null)
                    return Errors.Transcription.Unavailable;

                return new TranscriptionResult(payload.Text, payload.Language);
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var errorCode = TryReadErrorCode(errorBody);

            if (errorCode == "audio_too_long")
                return Errors.Transcription.TooLong((int)(maxDuration?.TotalSeconds ?? 0));

            if (errorCode is "empty_audio" or "undecodable_audio")
                return Errors.Transcription.EmptyOrUndecodable;

            _logger.LogWarning("Transcription sidecar returned {StatusCode}: {Body}", response.StatusCode, errorBody);
            return Errors.Transcription.Unavailable;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transcription request to sidecar failed");
            return Errors.Transcription.Unavailable;
        }
    }

    // Browser MediaRecorder sends parameterised types such as "audio/webm;codecs=opus",
    // which the MediaTypeHeaderValue constructor rejects — parse instead, and fall back
    // to a generic type rather than failing the whole request.
    private static System.Net.Http.Headers.MediaTypeHeaderValue ParseContentType(string contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var parsed) &&
            parsed != null)
        {
            return parsed;
        }

        return new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
    }

    private static string? TryReadErrorCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("detail", out var detail) &&
                detail.TryGetProperty("code", out var code))
            {
                return code.GetString();
            }
        }
        catch (JsonException)
        {
            // not a JSON body we recognize
        }
        return null;
    }

    private class TranscribeResponse
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("language")]
        public string? Language { get; set; }
    }
}
