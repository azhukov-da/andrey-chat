import { useEffect, useRef, useState } from 'react'
import WaveSurfer from 'wavesurfer.js'

interface Props {
  /** Object URL (or any playable URL) for the audio clip. */
  url: string
  /** Optional fallback duration in seconds, shown until the waveform finishes decoding. */
  className?: string
}

/**
 * WaveSurfer paints onto a canvas, where `hsl(var(--p))` and `currentColor` do not resolve.
 * The player sits inside a chat bubble whose background varies, so we read the *inherited*
 * text color at the container and derive both wave colors from it — that keeps the waveform
 * legible on any bubble/theme combination.
 */
function inheritedColor(el: HTMLElement, alpha: number, fallback: string): string {
  const color = getComputedStyle(el).color
  const rgb = /^rgba?\(([^)]+)\)$/.exec(color)
  if (!rgb?.[1]) return fallback
  const [r, g, b] = rgb[1].split(',').map((p) => p.trim())
  if (r === undefined || g === undefined || b === undefined) return fallback
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}

export function formatDuration(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return '0:00'
  const total = Math.floor(seconds)
  const mins = Math.floor(total / 60)
  const secs = total % 60
  return `${mins}:${secs.toString().padStart(2, '0')}`
}

export default function VoiceMessagePlayer({ url, className }: Props) {
  const containerRef = useRef<HTMLDivElement>(null)
  const waveSurferRef = useRef<WaveSurfer | null>(null)
  const [isPlaying, setIsPlaying] = useState(false)
  const [duration, setDuration] = useState(0)
  const [currentTime, setCurrentTime] = useState(0)
  const [ready, setReady] = useState(false)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    const container = containerRef.current
    if (!container) return

    const ws = WaveSurfer.create({
      container,
      url,
      height: 32,
      waveColor: inheritedColor(container, 0.35, 'rgba(156, 163, 175, 0.5)'),
      progressColor: inheritedColor(container, 1, '#9ca3af'),
      cursorWidth: 0,
      barWidth: 2,
      barGap: 2,
      barRadius: 2,
      normalize: true,
    })
    waveSurferRef.current = ws

    ws.on('ready', () => {
      setDuration(ws.getDuration())
      setReady(true)
    })
    ws.on('timeupdate', (time: number) => setCurrentTime(time))
    ws.on('play', () => setIsPlaying(true))
    ws.on('pause', () => setIsPlaying(false))
    ws.on('finish', () => {
      setIsPlaying(false)
      setCurrentTime(0)
    })
    ws.on('error', () => setFailed(true))

    return () => {
      waveSurferRef.current = null
      ws.destroy()
    }
  }, [url])

  const togglePlay = () => {
    void waveSurferRef.current?.playPause()
  }

  if (failed) {
    return (
      <audio src={url} controls className="w-full" data-testid="attachment-audio" />
    )
  }

  return (
    <div
      className={`flex items-center gap-2 ${className ?? ''}`}
      data-testid="voice-player"
    >
      <button
        type="button"
        className="btn btn-circle btn-sm btn-primary shrink-0"
        aria-label={isPlaying ? 'Pause voice message' : 'Play voice message'}
        onClick={togglePlay}
        disabled={!ready}
        data-testid="voice-player-toggle"
      >
        {isPlaying ? '❚❚' : '▶'}
      </button>
      <div ref={containerRef} className="flex-1 min-w-0" />
      <span
        className="text-xs opacity-60 tabular-nums shrink-0 w-9 text-right"
        data-testid="voice-player-time"
      >
        {formatDuration(ready ? (isPlaying || currentTime > 0 ? currentTime : duration) : 0)}
      </span>
    </div>
  )
}
