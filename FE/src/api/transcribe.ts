import { apiFetch, ApiError } from './client'

export interface TranscribeResult {
  text: string
  language?: string | null
}

export async function transcribeAudio(blob: Blob): Promise<TranscribeResult> {
  const form = new FormData()
  form.append('file', blob, 'dictation.webm')

  const res = await apiFetch('/api/transcribe', {
    method: 'POST',
    body: form,
  })
  if (!res.ok) {
    let errorText = res.statusText
    try {
      const body = (await res.json()) as { title?: string; detail?: string; message?: string; errors?: Record<string, string[]> }
      errorText = (body.title ?? body.detail ?? body.message ?? Object.values(body.errors ?? {}).flat().join(', ')) || errorText
    } catch { /* ignore */ }
    throw new ApiError(res.status, errorText)
  }
  return (await res.json()) as TranscribeResult
}
