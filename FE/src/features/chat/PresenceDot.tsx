import { usePresence } from '@/hooks/usePresence'
import { cn } from '@/lib/classNames'

interface Props {
  userId: string
}

export default function PresenceDot({ userId }: Props) {
  const status = usePresence(userId)
  return (
    <span
      className={cn(
        'badge badge-xs',
        status === 'online' && 'badge-success',
        status === 'afk' && 'badge-warning',
        status === 'offline' && 'badge-ghost'
      )}
      title={status}
    />
  )
}
