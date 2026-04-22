import { apiFetch, ApiError } from './client'
import type { Message } from '@/types'

export async function uploadAttachment(roomId: string, file: File, comment?: string): Promise<Message> {
  const form = new FormData()
  form.append('roomId', roomId)
  form.append('file', file)
  if (comment) form.append('comment', comment)

  const res = await apiFetch('/api/attachments/upload', {
    method: 'POST',
    body: form,
  })
  if (!res.ok) {
    let errorText = res.statusText
    try {
      const body = (await res.json()) as { title?: string; detail?: string; message?: string; code?: string }
      errorText = body.message ?? body.title ?? body.detail ?? errorText
    } catch { /* ignore */ }
    throw new ApiError(res.status, errorText)
  }
  return (await res.json()) as Message
}

export function attachmentDownloadUrl(attachmentId: string): string {
  return `/api/attachments/${attachmentId}`
}

export async function downloadAttachment(attachmentId: string, fileName: string): Promise<void> {
  const res = await apiFetch(attachmentDownloadUrl(attachmentId))
  if (!res.ok) throw new ApiError(res.status, res.statusText)
  const blob = await res.blob()
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

export async function loadAttachmentObjectUrl(attachmentId: string): Promise<string> {
  const res = await apiFetch(attachmentDownloadUrl(attachmentId))
  if (!res.ok) throw new ApiError(res.status, res.statusText)
  const blob = await res.blob()
  return URL.createObjectURL(blob)
}
