import { useRef, useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { sendMessage } from '@/api/messages'
import { uploadAttachment } from '@/api/attachments'
import { transcribeAudio } from '@/api/transcribe'
import { useUiStore } from '@/stores/uiStore'
import { MAX_MESSAGE_BYTES } from '@/types'
import { getHubConnection } from '@/realtime/hubClient'
import type { Message } from '@/types'

const MAX_DICTATION_SECONDS = 120
const MAX_VOICE_MESSAGE_SECONDS = 600

/** Maps a MediaRecorder mimeType onto a file extension the backend can store and the STT sidecar can decode. */
function extensionForMimeType(mimeType: string): string {
  const base = mimeType.split(';')[0]?.trim().toLowerCase() ?? ''
  if (base === 'audio/mp4' || base === 'audio/aac') return 'm4a'
  if (base === 'audio/mpeg') return 'mp3'
  if (base === 'audio/wav' || base === 'audio/wave') return 'wav'
  if (base === 'audio/ogg') return 'ogg'
  return 'webm'
}

function formatElapsed(seconds: number): string {
  const mins = Math.floor(seconds / 60)
  const secs = seconds % 60
  return `${mins}:${secs.toString().padStart(2, '0')}`
}

interface Props {
  roomId: string
}

export default function MessageComposer({ roomId }: Props) {
  const textareaRef = useRef<HTMLTextAreaElement>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const queryClient = useQueryClient()
  const { getDraft, setDraft, setReplyTo } = useUiStore()
  const [text, setText] = useState(() => getDraft(roomId))
  const [uploadError, setUploadError] = useState<string | null>(null)
  const [emojiOpen, setEmojiOpen] = useState(false)
  const [recordingMode, setRecordingMode] = useState<'dictate' | 'voice' | null>(null)
  const [elapsedSeconds, setElapsedSeconds] = useState(0)
  const [micUnavailable, setMicUnavailable] = useState(false)
  const [dictateError, setDictateError] = useState<string | null>(null)
  const mediaRecorderRef = useRef<MediaRecorder | null>(null)
  const recordedChunksRef = useRef<Blob[]>([])
  const autoStopTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const elapsedTimerRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const isRecording = recordingMode !== null
  const replyToId = useUiStore((s) => s.replyTo.get(roomId) ?? null)

  const byteCount = new TextEncoder().encode(text).length
  const overLimit = byteCount > MAX_MESSAGE_BYTES

  type Pages = { pages: { items: Message[]; nextCursor?: string | null }[] }

  const insertMessage = (msg: Message) => {
    queryClient.setQueryData<Pages>(['messages', roomId], (old) => {
      if (!old) return old
      if (old.pages.some((page) => page.items.some((m) => m.id === msg.id))) return old
      const [first, ...rest] = old.pages
      if (!first) return old
      return { ...old, pages: [{ ...first, items: [msg, ...first.items] }, ...rest] }
    })
  }

  const mutation = useMutation({
    mutationFn: (t: string) => sendMessage(roomId, t, replyToId ?? undefined),
    onSuccess: insertMessage,
  })

  const uploadMutation = useMutation({
    mutationFn: ({ file, comment }: { file: File; comment: string | undefined }) =>
      uploadAttachment(roomId, file, comment),
    onSuccess: (msg) => {
      insertMessage(msg)
      setUploadError(null)
    },
    onError: (e: Error) => setUploadError(e.message),
  })

  useEffect(() => {
    setDraft(roomId, text)
  }, [text, roomId, setDraft])

  const submit = () => {
    const trimmed = text.trim()
    if (!trimmed || overLimit || mutation.isPending) return
    mutation.mutate(trimmed)
    setText('')
    setReplyTo(roomId, null)

    getHubConnection().invoke('StopTyping', roomId).catch(() => {})
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      submit()
    }
    if (text.length > 0) {
      getHubConnection().invoke('StartTyping', roomId).catch(() => {})
    }
  }

  const uploadFiles = (files: File[], comment?: string) => {
    setUploadError(null)
    for (const file of files) {
      uploadMutation.mutate({ file, comment })
    }
  }

  const handlePaste = (e: React.ClipboardEvent<HTMLTextAreaElement>) => {
    const files = Array.from(e.clipboardData.files)
    if (files.length > 0) {
      e.preventDefault()
      uploadFiles(files, text.trim() || undefined)
      setText('')
    }
  }

  const dictateMutation = useMutation({
    mutationFn: (blob: Blob) => transcribeAudio(blob),
    onSuccess: ({ text }) => {
      setDictateError(null)
      if (!text) return
      setText((prev) => (prev.trim() ? `${prev} ${text}` : text))
    },
    onError: (e: Error) => setDictateError(e.message),
  })

  const clearRecordingTimers = () => {
    if (autoStopTimerRef.current) {
      clearTimeout(autoStopTimerRef.current)
      autoStopTimerRef.current = null
    }
    if (elapsedTimerRef.current) {
      clearInterval(elapsedTimerRef.current)
      elapsedTimerRef.current = null
    }
  }

  const stopRecording = () => {
    clearRecordingTimers()
    const recorder = mediaRecorderRef.current
    if (recorder && recorder.state !== 'inactive') {
      recorder.stop()
    }
  }

  const startRecording = async (mode: 'dictate' | 'voice') => {
    setDictateError(null)
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
      const recorder = new MediaRecorder(stream)
      mediaRecorderRef.current = recorder
      recordedChunksRef.current = []

      recorder.ondataavailable = (e) => {
        if (e.data.size > 0) recordedChunksRef.current.push(e.data)
      }
      recorder.onstop = () => {
        stream.getTracks().forEach((t) => t.stop())
        setRecordingMode(null)
        setElapsedSeconds(0)
        const mimeType = recorder.mimeType || 'audio/webm'
        const blob = new Blob(recordedChunksRef.current, { type: mimeType })
        recordedChunksRef.current = []
        if (blob.size === 0) return
        if (mode === 'dictate') {
          dictateMutation.mutate(blob)
        } else {
          const fileName = `voice-message.${extensionForMimeType(mimeType)}`
          uploadFiles([new File([blob], fileName, { type: mimeType })])
        }
      }

      recorder.start()
      setRecordingMode(mode)
      setElapsedSeconds(0)
      const limit = mode === 'dictate' ? MAX_DICTATION_SECONDS : MAX_VOICE_MESSAGE_SECONDS
      autoStopTimerRef.current = setTimeout(stopRecording, limit * 1000)
      elapsedTimerRef.current = setInterval(() => setElapsedSeconds((s) => s + 1), 1000)
    } catch {
      setMicUnavailable(true)
    }
  }

  const toggleRecording = (mode: 'dictate' | 'voice') => {
    if (recordingMode === mode) {
      stopRecording()
    } else if (recordingMode === null) {
      void startRecording(mode)
    }
  }

  useEffect(() => {
    return () => {
      clearRecordingTimers()
      const recorder = mediaRecorderRef.current
      if (recorder && recorder.state !== 'inactive') recorder.stop()
    }
  }, [])

  const EMOJIS = ['😀','😁','😂','🤣','😃','😄','😅','😆','😉','😊','😍','😘','😎','🤔','😐','😴','😢','😡','👍','👎','👏','🙏','💪','🔥','🎉','❤️','💔','✨','⭐','☑️']

  const insertEmoji = (emoji: string) => {
    const ta = textareaRef.current
    if (ta && typeof ta.selectionStart === 'number') {
      const start = ta.selectionStart
      const end = ta.selectionEnd ?? start
      const next = text.slice(0, start) + emoji + text.slice(end)
      setText(next)
      requestAnimationFrame(() => {
        ta.focus()
        const pos = start + emoji.length
        ta.setSelectionRange(pos, pos)
      })
    } else {
      setText(text + emoji)
    }
    setEmojiOpen(false)
  }

  const handleFilesPicked = (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files ?? [])
    if (files.length > 0) {
      uploadFiles(files, text.trim() || undefined)
      setText('')
    }
    if (fileInputRef.current) fileInputRef.current.value = ''
  }

  return (
    <div className="border-t border-base-300 bg-base-100 p-3">
      {replyToId && (
        <div className="border-l-4 border-primary pl-2 text-sm opacity-70 mb-2 flex items-center justify-between">
          <span>Replying to message</span>
          <button className="btn btn-xs btn-ghost" onClick={() => setReplyTo(roomId, null)}>✕</button>
        </div>
      )}
      <div className="flex gap-2 items-end relative">
        <input
          ref={fileInputRef}
          type="file"
          multiple
          className="hidden"
          onChange={handleFilesPicked}
          data-testid="attachment-input"
        />
        <div className="relative self-end">
          <button
            type="button"
            className="btn btn-ghost btn-sm"
            aria-label="Insert emoji"
            title="Insert emoji"
            onClick={() => setEmojiOpen((v) => !v)}
            data-testid="emoji-button"
          >
            😊
          </button>
          {emojiOpen && (
            <div
              className="absolute bottom-full left-0 mb-2 z-50 bg-base-100 border border-base-300 rounded shadow p-2 grid grid-cols-6 gap-1 w-64"
              data-testid="emoji-picker"
            >
              {EMOJIS.map((e) => (
                <button
                  key={e}
                  type="button"
                  className="btn btn-ghost btn-xs text-lg"
                  onClick={() => insertEmoji(e)}
                >
                  {e}
                </button>
              ))}
            </div>
          )}
        </div>
        <button
          type="button"
          className="btn btn-ghost btn-sm self-end"
          aria-label="Attach file"
          title="Attach file"
          disabled={uploadMutation.isPending}
          onClick={() => fileInputRef.current?.click()}
          data-testid="attachment-button"
        >
          📎
        </button>
        <button
          type="button"
          className={`btn btn-ghost btn-sm self-end ${recordingMode === 'dictate' ? 'text-error' : ''}`}
          aria-label={recordingMode === 'dictate' ? 'Stop dictation' : 'Dictate message'}
          title={
            micUnavailable
              ? 'Microphone unavailable'
              : recordingMode === 'dictate'
              ? 'Stop dictation'
              : 'Dictate message (speech to text)'
          }
          disabled={micUnavailable || dictateMutation.isPending || recordingMode === 'voice'}
          onClick={() => toggleRecording('dictate')}
          data-testid="dictate-button"
        >
          {recordingMode === 'dictate' ? '⏹' : '🎤'}
        </button>
        <button
          type="button"
          className={`btn btn-sm self-end ${recordingMode === 'voice' ? 'btn-error' : 'btn-ghost'}`}
          aria-label={recordingMode === 'voice' ? 'Stop and send voice message' : 'Record voice message'}
          title={
            micUnavailable
              ? 'Microphone unavailable'
              : recordingMode === 'voice'
              ? 'Stop and send voice message'
              : 'Record voice message'
          }
          disabled={micUnavailable || uploadMutation.isPending || recordingMode === 'dictate'}
          onClick={() => toggleRecording('voice')}
          data-testid="record-voice-button"
        >
          {recordingMode === 'voice' ? (
            <span className="w-3 h-3 rounded-[2px] bg-error-content" />
          ) : (
            <span className="w-3 h-3 rounded-full bg-error" />
          )}
        </button>
        <textarea
          ref={textareaRef}
          className="textarea textarea-bordered flex-1 resize-none min-h-[44px] max-h-32"
          placeholder="Type a message… (Enter to send, Shift+Enter for newline)"
          value={text}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={handleKeyDown}
          onPaste={handlePaste}
          maxLength={MAX_MESSAGE_BYTES * 2}
          rows={1}
        />
        <button
          className="btn btn-primary btn-sm self-end"
          disabled={!text.trim() || overLimit || mutation.isPending}
          onClick={submit}
        >
          Send
        </button>
      </div>
      {overLimit && (
        <p className="text-xs text-error mt-1">{byteCount}/{MAX_MESSAGE_BYTES} bytes — message too long</p>
      )}
      {uploadMutation.isPending && (
        <p className="text-xs opacity-60 mt-1">Uploading…</p>
      )}
      {uploadError && (
        <p className="text-xs text-error mt-1">{uploadError}</p>
      )}
      {isRecording && (
        <p className="text-xs mt-1 flex items-center gap-2" data-testid="recording-indicator">
          <span className="inline-block w-2 h-2 rounded-full bg-error animate-pulse" />
          <span className="text-error font-medium">
            {recordingMode === 'voice' ? 'Recording voice message' : 'Listening'} {formatElapsed(elapsedSeconds)}
          </span>
          <span className="opacity-60">
            {recordingMode === 'voice'
              ? '— press stop to send'
              : `— auto-stops after ${MAX_DICTATION_SECONDS}s`}
          </span>
        </p>
      )}
      {dictateMutation.isPending && (
        <p className="text-xs opacity-60 mt-1">Transcribing…</p>
      )}
      {micUnavailable && (
        <p className="text-xs text-error mt-1">Microphone access is unavailable.</p>
      )}
      {dictateError && (
        <p className="text-xs text-error mt-1">{dictateError}</p>
      )}
    </div>
  )
}
